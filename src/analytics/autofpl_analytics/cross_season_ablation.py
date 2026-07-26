from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import replace
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

from .cross_season_player_state import (
    PRIOR_SEASON,
    _load_archive,
    _load_history,
    _summarise,
    _summarise_ewma,
    _trailing_zero_minutes,
)
from .temporal_ridge import (
    BASELINE_NAMES,
    CONTINUOUS_FEATURES,
    MODEL_NAME as RIDGE_MODEL_NAME,
    RIDGE_PENALTY,
    Prediction,
    Sample,
    TemporalRidgeError,
    _build_table,
    _open_connection,
    _predict_fold,
    _samples_for_table,
    _sha256,
    _summarise_model,
    _write_report,
    evaluate_temporal_ridge,
)
from .temporal_tree import (
    MODEL_NAME as TREE_MODEL_NAME,
    TREE_CONFIGURATION,
    _predict_tree,
)

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "cross-season-feature-ablation-v1"
CROSS_RIDGE = "temporal-ridge+cross-season-state"
CROSS_TREE = "hist-gradient-boosting+cross-season-state"
CROSS_FEATURES = (
    "priorHasIdentity",
    "priorPositionChanged",
    "priorGameweekSampleCount",
    "priorTrailingZeroMinuteGameweeks",
    "priorSeasonAppearanceRate",
    "priorSeasonStartRate",
    "priorSeasonPlayed60Rate",
    "priorSeasonMinutesMean",
    "priorSeasonTotalPointsMean",
    "priorSeasonPointsPer90",
    "priorSeasonExpectedGoalsPer90",
    "priorSeasonExpectedAssistsPer90",
    "priorRolling5AppearanceRate",
    "priorRolling5StartRate",
    "priorRolling5MinutesMean",
    "priorRolling5TotalPointsMean",
    "priorRolling5ExpectedGoalsMean",
    "priorRolling5ExpectedAssistsMean",
    "priorRolling5ExpectedGoalInvolvementsMean",
    "priorRolling5DefensiveContributionMean",
    "priorEwmaAppearanceRate",
    "priorEwmaStartRate",
    "priorEwmaMinutesMean",
    "priorEwmaTotalPointsMean",
    "priorEwmaExpectedGoalsMean",
    "priorEwmaExpectedAssistsMean",
    "priorEwmaExpectedGoalInvolvementsMean",
    "priorEwmaDefensiveContributionMean",
)
CANDIDATE_FEATURES = CONTINUOUS_FEATURES + CROSS_FEATURES
MODEL_NAMES = (
    RIDGE_MODEL_NAME,
    TREE_MODEL_NAME,
    CROSS_RIDGE,
    CROSS_TREE,
    *BASELINE_NAMES,
)


def evaluate_cross_season_ablation(
    database_path: Path,
    season_code: Optional[str] = None,
    minimum_training_gameweeks: int = 3,
    ridge_penalty: float = RIDGE_PENALTY,
) -> Dict[str, Any]:
    if not math.isfinite(ridge_penalty) or ridge_penalty <= 0:
        raise TemporalRidgeError(
            "configuration.ridge-penalty",
            "The ridge penalty must be a positive finite number.",
        )
    path = Path(database_path)
    official = evaluate_temporal_ridge(
        path,
        season_code=season_code,
        minimum_training_gameweeks=minimum_training_gameweeks,
        ridge_penalty=ridge_penalty,
    )
    base = _base(
        season_code,
        minimum_training_gameweeks,
        ridge_penalty,
        official,
    )
    if official["status"] != "complete":
        return _finish(
            base,
            "insufficient-data",
            "no-official-eligible-folds",
            [],
            [],
        )

    connection = _open_connection(path)
    predictions: List[Prediction] = []
    folds: List[Dict[str, Any]] = []
    excluded: List[Dict[str, Any]] = []
    try:
        for fold in official["folds"]:
            season = str(fold["seasonCode"])
            training_samples: List[Sample] = []
            training_identity: List[Dict[str, Any]] = []
            fold_available = True
            for training in fold["training"]:
                table = _build_table(path, season, int(training["gameweek"]))
                samples = _samples_for_table(
                    connection,
                    table,
                    int(training["outcomeCaptureId"]),
                )
                augmented = _augment_samples(connection, samples, table)
                if augmented is None:
                    fold_available = False
                    break
                training_samples.extend(augmented)
                training_identity.append(dict(training))
            target_table = _build_table(path, season, int(fold["gameweek"]))
            target_samples = _samples_for_table(
                connection,
                target_table,
                int(fold["target"]["outcomeCaptureId"]),
            )
            target_augmented = _augment_samples(
                connection,
                target_samples,
                target_table,
            )
            if not fold_available or target_augmented is None:
                excluded.append(
                    {
                        "seasonCode": season,
                        "gameweek": fold["gameweek"],
                        "reason": "prior-season-archive-unavailable-by-cutoff",
                    }
                )
                continue
            fold_predictions, diagnostics = _predict_variants(
                training_samples,
                target_augmented,
                ridge_penalty,
            )
            predictions.extend(fold_predictions)
            models = [
                _summarise_model(name, fold_predictions)
                for name in MODEL_NAMES
            ]
            models.sort(
                key=lambda item: (item["metrics"]["mae"], item["name"])
            )
            folds.append(
                {
                    "seasonCode": season,
                    "gameweek": fold["gameweek"],
                    "deadlineUtc": fold["deadlineUtc"],
                    "decisionCutoffUtc": fold["decisionCutoffUtc"],
                    "target": fold["target"],
                    "training": training_identity,
                    "trainingGameweeks": len(training_identity),
                    "trainingRows": len(training_samples),
                    "models": models,
                    "diagnostics": diagnostics,
                }
            )
    finally:
        connection.close()
    if not folds:
        return _finish(
            base,
            "insufficient-data",
            "no-cross-season-source-complete-folds",
            [],
            [],
            excluded,
        )
    models = [_summarise_model(name, predictions) for name in MODEL_NAMES]
    models.sort(key=lambda item: (item["metrics"]["mae"], item["name"]))
    return _finish(base, "complete", None, folds, models, excluded)


def _augment_samples(
    connection: Any,
    samples: Sequence[Sample],
    table: Mapping[str, Any],
) -> Optional[List[Sample]]:
    archive = _load_archive(
        connection,
        {
            "captureId": table["provenance"]["replayCaptureId"],
            "availableAtUtc": table["decisionCutoffUtc"],
        },
    )
    if archive is None:
        return None
    histories, historical_players, row_count = _load_history(
        connection,
        int(archive["captureId"]),
    )
    if (
        len(historical_players) != int(archive["playerCount"])
        or len(historical_players) != int(archive["stableCodeCount"])
        or row_count != int(archive["playerGameweekCount"])
    ):
        raise TemporalRidgeError(
            "data.incomplete-prior-season-coverage",
            "The historical archive rows do not match declared coverage.",
        )
    players = {int(item["playerId"]): item for item in table["players"]}
    if {sample.player_id for sample in samples} != set(players):
        raise TemporalRidgeError(
            "data.incomplete-player-coverage",
            "Cross-season feature and labelled player coverage differ.",
        )
    augmented: List[Sample] = []
    for sample in samples:
        player = players[sample.player_id]
        code = int(player["playerCode"])
        prior_player = historical_players.get(code)
        history = histories.get(code, {})
        ordered = [
            {"gameweek": gameweek, **history[gameweek]}
            for gameweek in sorted(history)
        ]
        values = dict(sample.features)
        values.update(
            {
                "priorHasIdentity": float(prior_player is not None),
                "priorPositionChanged": (
                    None
                    if prior_player is None
                    else float(prior_player["position"] != player["position"])
                ),
                "priorGameweekSampleCount": float(len(ordered)),
                "priorTrailingZeroMinuteGameweeks": float(
                    _trailing_zero_minutes(ordered)
                ),
                **_summary_features(
                    "priorSeason",
                    _summarise(ordered),
                ),
                **_summary_features(
                    "priorRolling5",
                    _summarise(ordered[-5:]),
                ),
                **_summary_features(
                    "priorEwma",
                    _summarise_ewma(ordered) or {},
                ),
            }
        )
        if tuple(values) != CANDIDATE_FEATURES:
            raise TemporalRidgeError(
                "evaluation.feature-contract",
                "The cross-season feature contract is inconsistent.",
            )
        augmented.append(replace(sample, features=values))
    return augmented


def _summary_features(
    prefix: str,
    summary: Mapping[str, Any],
) -> Dict[str, Optional[float]]:
    if prefix == "priorSeason":
        return {
            "priorSeasonAppearanceRate": _optional(
                summary.get("appearanceRate")
            ),
            "priorSeasonStartRate": _optional(summary.get("startRate")),
            "priorSeasonPlayed60Rate": _optional(
                summary.get("played60Rate")
            ),
            "priorSeasonMinutesMean": _optional(
                summary.get("minutesMean")
            ),
            "priorSeasonTotalPointsMean": _optional(
                summary.get("totalPointsMean")
            ),
            "priorSeasonPointsPer90": _optional(
                summary.get("pointsPer90")
            ),
            "priorSeasonExpectedGoalsPer90": _optional(
                summary.get("expectedGoalsPer90")
            ),
            "priorSeasonExpectedAssistsPer90": _optional(
                summary.get("expectedAssistsPer90")
            ),
        }
    names = (
        "AppearanceRate",
        "StartRate",
        "MinutesMean",
        "TotalPointsMean",
        "ExpectedGoalsMean",
        "ExpectedAssistsMean",
        "ExpectedGoalInvolvementsMean",
        "DefensiveContributionMean",
    )
    return {
        f"{prefix}{name}": _optional(
            summary.get(name[0].lower() + name[1:])
        )
        for name in names
    }


def _optional(value: Any) -> Optional[float]:
    return None if value is None else float(value)


def _predict_variants(
    training: Sequence[Sample],
    target: Sequence[Sample],
    penalty: float,
) -> Tuple[List[Prediction], Dict[str, Any]]:
    ridge, ridge_diag = _predict_fold(training, target, penalty)
    tree, tree_diag = _predict_tree(training, target)
    cross_ridge, cross_ridge_diag = _predict_fold(
        training,
        target,
        penalty,
        continuous_features=CANDIDATE_FEATURES,
        model_name=CROSS_RIDGE,
        include_baselines=False,
    )
    cross_tree, cross_tree_diag = _predict_tree(
        training,
        target,
        continuous_features=CANDIDATE_FEATURES,
        model_name=CROSS_TREE,
    )
    return [*ridge, *tree, *cross_ridge, *cross_tree], {
        RIDGE_MODEL_NAME: ridge_diag,
        TREE_MODEL_NAME: tree_diag,
        CROSS_RIDGE: cross_ridge_diag,
        CROSS_TREE: cross_tree_diag,
    }


def _base(
    season_code: Optional[str],
    minimum: int,
    penalty: float,
    official: Mapping[str, Any],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": "exploratory-feature-ablation-not-promoted",
        "isPromoted": False,
        "target": "official-gameweek-total-points",
        "split": "identical-expanding-gameweek-origin",
        "candidateOfficialFoldCount": len(official["folds"]),
        "configuration": {
            "seasonCode": season_code,
            "minimumTrainingGameweeks": minimum,
            "ridgePenalty": penalty,
            "priorSeason": PRIOR_SEASON,
            "officialContinuousFeatures": list(CONTINUOUS_FEATURES),
            "crossSeasonCandidateFeatures": list(CROSS_FEATURES),
            "tree": dict(TREE_CONFIGURATION),
            "cohortRule": (
                "all variants use identical source-complete folds and players"
            ),
            "healthRule": (
                "current official chance remains in incumbent; archived final "
                "health is excluded; participation is durability evidence"
            ),
            "xPExcluded": True,
        },
        "availabilityRule": (
            "the fixed prior-season archive must be available by every "
            "training and target replay cutoff"
        ),
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
    excluded: Sequence[Mapping[str, Any]] = (),
) -> Dict[str, Any]:
    report = {
        **base,
        "status": status,
        "reason": reason,
        "eligibleFoldCount": len(folds),
        "excludedFolds": list(excluded),
        "folds": list(folds),
        "models": list(models),
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "candidateOfficialFoldCount": report[
                "candidateOfficialFoldCount"
            ],
            "excludedFolds": report["excludedFolds"],
            "folds": [
                {
                    "seasonCode": fold["seasonCode"],
                    "gameweek": fold["gameweek"],
                    "target": fold["target"],
                    "training": fold["training"],
                }
                for fold in folds
            ],
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="Ablate cross-season state on identical temporal folds."
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season")
    parser.add_argument("--minimum-training-gameweeks", type=int, default=3)
    parser.add_argument("--ridge-penalty", type=float, default=RIDGE_PENALTY)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_cross_season_ablation(
            options.database,
            season_code=options.season,
            minimum_training_gameweeks=options.minimum_training_gameweeks,
            ridge_penalty=options.ridge_penalty,
        )
        _write_report(report, options.output)
        return 0 if report["status"] == "complete" else 2
    except TemporalRidgeError as exception:
        error = {
            "schemaVersion": SCHEMA_VERSION,
            "status": "error",
            "errorCode": exception.code,
            "message": str(exception),
        }
        sys.stderr.write(json.dumps(error, sort_keys=True) + "\n")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
