from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import replace
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

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
EVALUATOR_VERSION = "official-underlying-feature-ablation-v1"
UNDERLYING_RIDGE = "temporal-ridge+official-underlying"
UNDERLYING_TREE = "hist-gradient-boosting+official-underlying"
UNDERLYING_METRICS = (
    "ExpectedGoals",
    "ExpectedAssists",
    "ExpectedGoalsConceded",
    "IctIndex",
    "Bps",
    "DefensiveContribution",
)
UNDERLYING_FEATURES = tuple(
    f"history{summary}{metric}{suffix}"
    for summary in ("Rolling3", "Ewma")
    for metric in UNDERLYING_METRICS
    for suffix in ("Mean", "SampleCount")
)
CANDIDATE_FEATURES = CONTINUOUS_FEATURES + UNDERLYING_FEATURES
MODEL_NAMES = (
    RIDGE_MODEL_NAME,
    TREE_MODEL_NAME,
    UNDERLYING_RIDGE,
    UNDERLYING_TREE,
    *BASELINE_NAMES,
)


def evaluate_official_underlying_ablation(
    database_path: Path,
    season_code: Optional[str] = None,
    minimum_training_gameweeks: int = 3,
    ridge_penalty: float = RIDGE_PENALTY,
) -> Dict[str, Any]:
    """Ablate official underlying features on identical temporal folds."""
    if not math.isfinite(ridge_penalty) or ridge_penalty <= 0:
        raise TemporalRidgeError(
            "configuration.ridge-penalty",
            "The ridge penalty must be a positive finite number.",
        )

    path = Path(database_path)
    official_report = evaluate_temporal_ridge(
        path,
        season_code=season_code,
        minimum_training_gameweeks=minimum_training_gameweeks,
        ridge_penalty=ridge_penalty,
    )
    base = _base_report(
        season_code,
        minimum_training_gameweeks,
        ridge_penalty,
        official_report,
    )
    if official_report["status"] != "complete":
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
    try:
        for official_fold in official_report["folds"]:
            season = str(official_fold["seasonCode"])
            training_samples: List[Sample] = []
            for training in official_fold["training"]:
                table = _build_table(
                    path,
                    season,
                    int(training["gameweek"]),
                )
                samples = _samples_for_table(
                    connection,
                    table,
                    int(training["outcomeCaptureId"]),
                )
                training_samples.extend(_augment_samples(samples, table))

            target_table = _build_table(
                path,
                season,
                int(official_fold["gameweek"]),
            )
            target_samples = _augment_samples(
                _samples_for_table(
                    connection,
                    target_table,
                    int(official_fold["target"]["outcomeCaptureId"]),
                ),
                target_table,
            )
            fold_predictions, diagnostics = _predict_variants(
                training_samples,
                target_samples,
                ridge_penalty,
            )
            predictions.extend(fold_predictions)
            fold_models = [
                _summarise_model(name, fold_predictions)
                for name in MODEL_NAMES
            ]
            fold_models.sort(
                key=lambda model: (model["metrics"]["mae"], model["name"])
            )
            folds.append(
                {
                    "seasonCode": season,
                    "gameweek": official_fold["gameweek"],
                    "deadlineUtc": official_fold["deadlineUtc"],
                    "decisionCutoffUtc": official_fold[
                        "decisionCutoffUtc"
                    ],
                    "target": official_fold["target"],
                    "training": official_fold["training"],
                    "trainingGameweeks": official_fold[
                        "trainingGameweeks"
                    ],
                    "trainingRows": len(training_samples),
                    "models": fold_models,
                    "diagnostics": diagnostics,
                }
            )
    finally:
        connection.close()

    models = [
        _summarise_model(name, predictions)
        for name in MODEL_NAMES
    ]
    models.sort(key=lambda model: (model["metrics"]["mae"], model["name"]))
    return _finish(base, "complete", None, folds, models)


def _augment_samples(
    samples: Sequence[Sample],
    table: Mapping[str, Any],
) -> List[Sample]:
    players = {
        int(player["playerId"]): player
        for player in table["players"]
    }
    if {sample.player_id for sample in samples} != set(players):
        raise TemporalRidgeError(
            "data.incomplete-player-coverage",
            "Underlying feature and labelled player coverage differ.",
        )

    augmented: List[Sample] = []
    for sample in samples:
        history = players[sample.player_id]["history"]
        rolling = history["rolling"]["3"]
        ewma = history["exponentiallyWeighted"] or {}
        values = dict(sample.features)
        _copy_underlying(values, "historyRolling3", rolling)
        _copy_underlying(values, "historyEwma", ewma)
        if tuple(values) != CANDIDATE_FEATURES:
            raise TemporalRidgeError(
                "evaluation.feature-contract",
                "The official underlying feature contract is inconsistent.",
            )
        augmented.append(replace(sample, features=values))
    return augmented


def _copy_underlying(
    target: Dict[str, Optional[float]],
    prefix: str,
    source: Mapping[str, Any],
) -> None:
    for metric in UNDERLYING_METRICS:
        key = metric[0].lower() + metric[1:]
        target[f"{prefix}{metric}Mean"] = _optional_float(
            source.get(f"{key}Mean")
        )
        target[f"{prefix}{metric}SampleCount"] = _optional_float(
            source.get(f"{key}SampleCount")
        )


def _optional_float(value: Any) -> Optional[float]:
    return None if value is None else float(value)


def _predict_variants(
    training: Sequence[Sample],
    target: Sequence[Sample],
    ridge_penalty: float,
) -> Tuple[List[Prediction], Dict[str, Any]]:
    official_ridge, official_ridge_diagnostic = _predict_fold(
        training,
        target,
        ridge_penalty,
    )
    official_tree, official_tree_diagnostic = _predict_tree(
        training,
        target,
    )
    underlying_ridge, underlying_ridge_diagnostic = _predict_fold(
        training,
        target,
        ridge_penalty,
        continuous_features=CANDIDATE_FEATURES,
        model_name=UNDERLYING_RIDGE,
        include_baselines=False,
    )
    underlying_tree, underlying_tree_diagnostic = _predict_tree(
        training,
        target,
        continuous_features=CANDIDATE_FEATURES,
        model_name=UNDERLYING_TREE,
    )
    predictions = [
        *official_ridge,
        *official_tree,
        *underlying_ridge,
        *underlying_tree,
    ]
    return predictions, {
        RIDGE_MODEL_NAME: official_ridge_diagnostic,
        TREE_MODEL_NAME: official_tree_diagnostic,
        UNDERLYING_RIDGE: underlying_ridge_diagnostic,
        UNDERLYING_TREE: underlying_tree_diagnostic,
    }


def _base_report(
    season_code: Optional[str],
    minimum_training_gameweeks: int,
    ridge_penalty: float,
    official_report: Mapping[str, Any],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": "exploratory-feature-ablation-not-promoted",
        "isPromoted": False,
        "target": "official-gameweek-total-points",
        "split": "identical-expanding-gameweek-origin",
        "candidateOfficialFoldCount": len(official_report["folds"]),
        "configuration": {
            "seasonCode": season_code,
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "ridgePenalty": ridge_penalty,
            "featureTable": "official-temporal-v2",
            "officialContinuousFeatures": list(CONTINUOUS_FEATURES),
            "underlyingCandidateFeatures": list(UNDERLYING_FEATURES),
            "ridgePreprocessing": (
                "training-fold median, missing indicators and z-score"
            ),
            "tree": dict(TREE_CONFIGURATION),
            "cohortRule": (
                "all variants use identical folds and labelled players"
            ),
            "featureSelection": (
                "fixed official xG, xA, xGC, ICT, BPS and defensive "
                "contribution rolling-3/EWMA means and observed counts"
            ),
        },
        "availabilityRule": (
            "each target and training feature table uses its own latest "
            "pre-deadline replay and only prior outcomes available at that "
            "replay capture time"
        ),
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    report = {
        **base,
        "status": status,
        "reason": reason,
        "eligibleFoldCount": len(folds),
        "folds": list(folds),
        "models": list(models),
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "candidateOfficialFoldCount": report[
                "candidateOfficialFoldCount"
            ],
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
        description=(
            "Ablate official underlying outcomes on identical temporal folds."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season")
    parser.add_argument(
        "--minimum-training-gameweeks",
        type=int,
        default=3,
    )
    parser.add_argument(
        "--ridge-penalty",
        type=float,
        default=RIDGE_PENALTY,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_official_underlying_ablation(
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
