from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

from .historical_participation_evaluation import (
    MAXIMUM_POSITION_MAE_REGRESSION,
    MINIMUM_SCORE_IMPROVEMENT,
    _baseline_predictions,
    _build_samples,
    _candidate_name,
    _load_capture,
    _predict_classifier,
    _validate,
)
from .historical_preseason_evaluation import (
    DEFAULT_SEASON,
    FEATURES,
    HOLDOUT_START_GAMEWEEK,
    MINIMUM_TRAINING_GAMEWEEKS,
    HistoricalCapture,
)
from .temporal_ridge import (
    Prediction,
    Sample,
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _summarise_model,
    _write_report,
)
from .temporal_tree import TREE_CONFIGURATION, _predict_tree

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-conditional-minutes-evaluation-v1"
CANDIDATE_MODEL = "appearance-hurdle-conditional-minutes-tree"
UNCONDITIONAL_MODEL = "historical-minutes-histogram-tree"
BASELINE_MODEL = "minutes-player-last"
REFERENCE_MODELS = (BASELINE_MODEL, UNCONDITIONAL_MODEL)
MINUTES_PER_FIXTURE = 90.0


def evaluate_historical_conditional_minutes(
    database_path: Path,
    season_code: str = DEFAULT_SEASON,
    minimum_training_gameweeks: int = MINIMUM_TRAINING_GAMEWEEKS,
    holdout_start_gameweek: int = HOLDOUT_START_GAMEWEEK,
) -> Dict[str, Any]:
    path = Path(database_path)
    _validate(
        path,
        season_code,
        minimum_training_gameweeks,
        holdout_start_gameweek,
    )
    connection = _open_connection(path)
    try:
        capture = _load_capture(connection, season_code)
        base = _base(
            season_code,
            minimum_training_gameweeks,
            holdout_start_gameweek,
            capture,
        )
        if capture is None:
            return _finish(
                base,
                "insufficient-data",
                "historical-season-archive-not-found",
                [],
                [],
                None,
            )
        samples = {
            "appearance": _build_samples(
                connection, capture, target_name="appearance"
            ),
            "minutes": _build_samples(
                connection, capture, target_name="minutes"
            ),
        }
    finally:
        connection.close()

    gameweeks = _aligned_gameweeks(samples)
    targets = [
        gameweek
        for gameweek in gameweeks
        if gameweek >= holdout_start_gameweek
        and len([prior for prior in gameweeks if prior < gameweek])
        >= minimum_training_gameweeks
    ]
    folds: List[Dict[str, Any]] = []
    predictions: List[Prediction] = []
    for target_gameweek in targets:
        training_gameweeks = [
            gameweek for gameweek in gameweeks if gameweek < target_gameweek
        ]
        fold, fold_predictions = _evaluate_fold(
            samples,
            target_gameweek,
            training_gameweeks,
        )
        folds.append(fold)
        predictions.extend(fold_predictions)
    if not folds:
        return _finish(
            base,
            "insufficient-data",
            "no-eligible-locked-holdout-folds",
            [],
            [],
            None,
        )

    models = [
        _summarise_model(name, predictions)
        for name in (CANDIDATE_MODEL, *REFERENCE_MODELS)
    ]
    models.sort(key=lambda item: (item["metrics"]["mae"], item["name"]))
    return _finish(
        base,
        "complete",
        None,
        folds,
        models,
        _comparison(models, folds),
    )


def _aligned_gameweeks(
    samples: Mapping[str, Mapping[int, Sequence[Sample]]],
) -> List[int]:
    gameweeks = sorted(samples["appearance"])
    if gameweeks != sorted(samples["minutes"]):
        raise TemporalRidgeError(
            "evaluation.unaligned-conditional-minutes-gameweeks",
            "Appearance and minutes targets do not contain identical "
            "Gameweeks.",
        )
    for gameweek in gameweeks:
        appearance = samples["appearance"][gameweek]
        minutes = samples["minutes"][gameweek]
        if len(appearance) != len(minutes) or any(
            (
                left.player_id,
                left.position,
                left.features,
            )
            != (
                right.player_id,
                right.position,
                right.features,
            )
            for left, right in zip(appearance, minutes)
        ):
            raise TemporalRidgeError(
                "evaluation.unaligned-conditional-minutes-players",
                "Appearance and minutes targets do not contain identical "
                "players and features.",
            )
    return gameweeks


def _evaluate_fold(
    samples: Mapping[str, Mapping[int, Sequence[Sample]]],
    target_gameweek: int,
    training_gameweeks: Sequence[int],
) -> Tuple[Dict[str, Any], List[Prediction]]:
    appearance_training = [
        sample
        for gameweek in training_gameweeks
        for sample in samples["appearance"][gameweek]
    ]
    minutes_training = [
        sample
        for gameweek in training_gameweeks
        for sample in samples["minutes"][gameweek]
    ]
    appearance_target = samples["appearance"][target_gameweek]
    minutes_target = samples["minutes"][target_gameweek]

    appearance_predictions, appearance_diagnostics = _predict_classifier(
        appearance_training,
        appearance_target,
        _candidate_name("appearance"),
    )
    conditional_training = [
        minutes
        for appearance, minutes in zip(
            appearance_training, minutes_training
        )
        if appearance.actual == 1
    ]
    if not conditional_training:
        raise TemporalRidgeError(
            "evaluation.empty-conditional-minutes-training-set",
            "No appearance-positive rows are available for conditional "
            "minutes fitting.",
        )
    conditional_predictions, conditional_diagnostics = _predict_tree(
        conditional_training,
        minutes_target,
        continuous_features=FEATURES,
        model_name="minutes-given-appearance-tree",
    )
    unconditional_predictions, unconditional_diagnostics = _predict_tree(
        minutes_training,
        minutes_target,
        continuous_features=FEATURES,
        model_name=UNCONDITIONAL_MODEL,
    )
    baseline_predictions = [
        prediction
        for prediction in _baseline_predictions(
            "minutes", minutes_training, minutes_target
        )
        if prediction.model == BASELINE_MODEL
    ]
    appearance_by_player = {
        prediction.player_id: prediction
        for prediction in appearance_predictions
    }
    conditional_by_player = {
        prediction.player_id: prediction
        for prediction in conditional_predictions
    }
    target_by_player = {
        sample.player_id: sample for sample in minutes_target
    }
    if not (
        set(appearance_by_player)
        == set(conditional_by_player)
        == set(target_by_player)
    ):
        raise TemporalRidgeError(
            "evaluation.unaligned-conditional-minutes-predictions",
            "The appearance and conditional-minutes predictions do not "
            "contain identical target players.",
        )
    candidate_predictions = [
        Prediction(
            model=CANDIDATE_MODEL,
            season_code=target_by_player[player_id].season_code,
            gameweek=target_by_player[player_id].gameweek,
            player_id=player_id,
            position=target_by_player[player_id].position,
            predicted=_expected_minutes(
                appearance_by_player[player_id].predicted,
                conditional_by_player[player_id].predicted,
                target_by_player[player_id].features["targetFixtureCount"],
            ),
            actual=target_by_player[player_id].actual,
        )
        for player_id in sorted(target_by_player)
    ]
    predictions = [
        *candidate_predictions,
        *unconditional_predictions,
        *baseline_predictions,
    ]
    return (
        {
            "gameweek": target_gameweek,
            "trainingGameweeks": list(training_gameweeks),
            "trainingRowCount": len(minutes_training),
            "conditionalTrainingRowCount": len(conditional_training),
            "targetPlayerCount": len(minutes_target),
            "models": [
                _summarise_model(name, predictions)
                for name in (CANDIDATE_MODEL, *REFERENCE_MODELS)
            ],
            "diagnostics": {
                "appearance": appearance_diagnostics,
                "conditionalMinutes": conditional_diagnostics,
                "unconditionalMinutes": unconditional_diagnostics,
            },
        },
        predictions,
    )


def _expected_minutes(
    appearance_probability: float,
    conditional_minutes: float,
    fixture_count: Optional[float],
) -> float:
    probability = float(appearance_probability)
    conditional = float(conditional_minutes)
    fixtures = float(fixture_count) if fixture_count is not None else math.nan
    if (
        not math.isfinite(probability)
        or probability < 0.0
        or probability > 1.0
    ):
        raise TemporalRidgeError(
            "evaluation.invalid-appearance-probability",
            "Expected minutes requires an appearance probability between "
            "zero and one.",
        )
    if not math.isfinite(conditional):
        raise TemporalRidgeError(
            "evaluation.invalid-conditional-minutes",
            "Conditional minutes must be finite.",
        )
    if not math.isfinite(fixtures) or fixtures < 1 or not fixtures.is_integer():
        raise TemporalRidgeError(
            "evaluation.invalid-target-fixture-count",
            "Expected minutes requires a positive integer fixture count.",
        )
    bounded = min(MINUTES_PER_FIXTURE * fixtures, max(0.0, conditional))
    return probability * bounded


def _comparison(
    models: Sequence[Mapping[str, Any]],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    indexed = {str(model["name"]): model for model in models}
    candidate = indexed[CANDIDATE_MODEL]
    references: Dict[str, Any] = {}
    passes = True
    for reference_name in REFERENCE_MODELS:
        reference = indexed[reference_name]
        candidate_metrics = candidate["metrics"]
        reference_metrics = reference["metrics"]
        improvement = (
            reference_metrics["mae"] - candidate_metrics["mae"]
        ) / reference_metrics["mae"]
        fold_wins = sum(
            _fold_metric(fold, CANDIDATE_MODEL, "mae")
            < _fold_metric(fold, reference_name, "mae")
            for fold in folds
        )
        candidate_positions = candidate["slices"]["position"]
        reference_positions = reference["slices"]["position"]
        position_regressions = {
            position: (
                candidate_positions[position]["mae"]
                - reference_positions[position]["mae"]
            )
            / reference_positions[position]["mae"]
            for position in candidate_positions
            if position in reference_positions
            and reference_positions[position]["mae"] > 0
        }
        checks = {
            "minimumMaeImprovement": (
                improvement >= MINIMUM_SCORE_IMPROVEMENT
            ),
            "noRmseRegression": (
                candidate_metrics["rmse"] <= reference_metrics["rmse"]
            ),
            "majorityFoldWins": fold_wins > len(folds) / 2,
            "noMaterialPositionMaeRegression": all(
                regression <= MAXIMUM_POSITION_MAE_REGRESSION
                for regression in position_regressions.values()
            ),
        }
        reference_passes = all(checks.values())
        passes = passes and reference_passes
        references[reference_name] = {
            "maeImprovementFraction": _round(improvement),
            "rmseDelta": _round(
                candidate_metrics["rmse"] - reference_metrics["rmse"]
            ),
            "foldWins": fold_wins,
            "foldCount": len(folds),
            "positionMaeRegressionFractions": {
                position: _round(regression)
                for position, regression in sorted(
                    position_regressions.items()
                )
            },
            "checks": checks,
            "passes": reference_passes,
        }
    return {
        "challenger": CANDIDATE_MODEL,
        "references": references,
        "fixedScreenPassed": passes,
        "decision": (
            "retain-for-prospective-player-distribution-shadow"
            if passes
            else "retain-existing-minutes-baseline"
        ),
        "isPromoted": False,
    }


def _fold_metric(
    fold: Mapping[str, Any], model_name: str, metric: str
) -> float:
    model = next(
        item for item in fold["models"] if item["name"] == model_name
    )
    return float(model["metrics"][metric])


def _base(
    season_code: str,
    minimum_training_gameweeks: int,
    holdout_start_gameweek: int,
    capture: Optional[HistoricalCapture],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": (
            "exploratory-reused-holdout-candidate-screen-not-promotion"
        ),
        "isPromoted": False,
        "productImportReady": False,
        "target": "unconditional-player-gameweek-minutes",
        "split": "expanding-gameweek-origin-reused-locked-holdout",
        "configuration": {
            "seasonCode": season_code,
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "holdoutStartGameweek": holdout_start_gameweek,
            "appearanceModel": _candidate_name("appearance"),
            "conditionalMinutesModel": dict(TREE_CONFIGURATION),
            "unconditionalMinutesModel": dict(TREE_CONFIGURATION),
            "conditionalTrainingCohort": "appearance-outcome-equals-one",
            "factorization": (
                "appearance-probability-times-expected-minutes-given-"
                "appearance"
            ),
            "minutesPerFixtureUpperBound": MINUTES_PER_FIXTURE,
            "references": list(REFERENCE_MODELS),
            "retentionGate": {
                "minimumMaeImprovementFraction": (
                    MINIMUM_SCORE_IMPROVEMENT
                ),
                "maximumRmseDelta": 0.0,
                "minimumFoldWins": "strict-majority-against-each-reference",
                "maximumPositionMaeRegressionFraction": (
                    MAXIMUM_POSITION_MAE_REGRESSION
                ),
                "promotionAllowed": False,
            },
        },
        "provenance": (
            None
            if capture is None
            else {
                "historicalCaptureId": capture.capture_id,
                "sourceRevision": capture.source_revision,
                "availableAtUtc": capture.available_at_utc,
                "playersSha256": capture.players_sha256,
                "gameweeksSha256": capture.gameweeks_sha256,
                "playerCount": capture.player_count,
                "playerGameweekRowCount": capture.player_gameweek_count,
                "stableCodeCount": capture.stable_code_count,
            }
        ),
        "limitations": [
            "The 2025/26 Gameweek 31-38 holdout was opened by earlier "
            "participation experiments, so this is not a promotion test.",
            "The settled archive lacks historical decision-time injury state.",
            "The model predicts a minutes mean, not a complete calibrated "
            "minutes distribution.",
            "Genuinely new 2026/27 folds are required before product import.",
        ],
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
    comparison: Optional[Mapping[str, Any]],
) -> Dict[str, Any]:
    report: Dict[str, Any] = {
        **base,
        "status": status,
        "reason": reason,
        "eligibleFoldCount": len(folds),
        "folds": list(folds),
        "models": list(models),
        "comparison": comparison,
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "provenance": report["provenance"],
            "foldGameweeks": [fold["gameweek"] for fold in folds],
            "factorization": report["configuration"]["factorization"],
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare a fixed appearance-hurdle conditional-minutes model "
            "with unconditional and player-last references."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=DEFAULT_SEASON)
    parser.add_argument(
        "--minimum-training-gameweeks",
        type=int,
        default=MINIMUM_TRAINING_GAMEWEEKS,
    )
    parser.add_argument(
        "--holdout-start-gameweek",
        type=int,
        default=HOLDOUT_START_GAMEWEEK,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_historical_conditional_minutes(
            options.database,
            season_code=options.season,
            minimum_training_gameweeks=options.minimum_training_gameweeks,
            holdout_start_gameweek=options.holdout_start_gameweek,
        )
        _write_report(report, options.output)
        return 0 if report["status"] == "complete" else 2
    except (TemporalRidgeError, OSError, ValueError) as exception:
        code = getattr(exception, "code", "evaluation.failed")
        sys.stderr.write(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "error",
                    "errorCode": code,
                    "message": str(exception),
                },
                sort_keys=True,
            )
            + "\n"
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
