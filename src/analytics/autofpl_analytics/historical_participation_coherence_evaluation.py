from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

from .historical_participation_evaluation import (
    BINARY_TARGETS,
    MAXIMUM_POSITION_BRIER_REGRESSION,
    _build_samples,
    _candidate_name,
    _group_by_position,
    _load_capture,
    _predict_classifier,
    _probability_metrics,
    _validate,
)
from .historical_preseason_evaluation import (
    DEFAULT_SEASON,
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
    _write_report,
)

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-participation-coherence-evaluation-v1"
RAW_MODEL_PREFIX = "raw-independent"
PROJECTED_MODEL_PREFIX = "euclidean-coherent"
MAXIMUM_CALIBRATION_REGRESSION = 0.01


def evaluate_historical_participation_coherence(
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
            )
        samples = {
            target: _build_samples(
                connection,
                capture,
                target_name=target,
            )
            for target in BINARY_TARGETS
        }
        gameweeks = _require_aligned_samples(samples)
        folds: List[Dict[str, Any]] = []
        predictions: Dict[str, List[Prediction]] = {
            target: [] for target in BINARY_TARGETS
        }
        for target_gameweek in gameweeks:
            training_gameweeks = [
                gameweek
                for gameweek in gameweeks
                if gameweek < target_gameweek
            ]
            if (
                target_gameweek < holdout_start_gameweek
                or len(training_gameweeks) < minimum_training_gameweeks
            ):
                continue
            fold, fold_predictions = _evaluate_fold(
                samples,
                target_gameweek,
                training_gameweeks,
            )
            folds.append(fold)
            for target in BINARY_TARGETS:
                predictions[target].extend(fold_predictions[target])
        if not folds:
            return _finish(
                base,
                "insufficient-data",
                "no-eligible-locked-holdout-folds",
                [],
                [],
            )
        targets = [
            _target_summary(target, predictions[target], folds)
            for target in BINARY_TARGETS
        ]
        return _finish(base, "complete", None, folds, targets)
    finally:
        connection.close()


def _evaluate_fold(
    samples: Mapping[str, Mapping[int, Sequence[Sample]]],
    target_gameweek: int,
    training_gameweeks: Sequence[int],
) -> Tuple[Dict[str, Any], Dict[str, List[Prediction]]]:
    raw: Dict[str, Dict[int, Prediction]] = {}
    diagnostics: Dict[str, Any] = {}
    for target in BINARY_TARGETS:
        training = [
            sample
            for gameweek in training_gameweeks
            for sample in samples[target][gameweek]
        ]
        target_samples = samples[target][target_gameweek]
        target_predictions, target_diagnostics = _predict_classifier(
            training,
            target_samples,
            _candidate_name(target),
        )
        raw[target] = {
            prediction.player_id: prediction
            for prediction in target_predictions
        }
        diagnostics[target] = target_diagnostics

    player_ids = sorted(raw["appearance"])
    if any(set(raw[target]) != set(player_ids) for target in BINARY_TARGETS):
        raise TemporalRidgeError(
            "evaluation.unaligned-coherence-predictions",
            "Participation targets do not contain identical holdout players.",
        )
    fold_predictions: Dict[str, List[Prediction]] = {
        target: [] for target in BINARY_TARGETS
    }
    raw_violations = 0
    projected_violations = 0
    maximum_raw_violation = 0.0
    maximum_adjustment = 0.0
    for player_id in player_ids:
        values = {
            target: raw[target][player_id].predicted
            for target in BINARY_TARGETS
        }
        raw_violation = max(
            0.0,
            values["start"] - values["appearance"],
            values["played-60"] - values["appearance"],
        )
        if raw_violation > 0:
            raw_violations += 1
            maximum_raw_violation = max(
                maximum_raw_violation,
                raw_violation,
            )
        projected = _project_probabilities(
            values["appearance"],
            values["start"],
            values["played-60"],
        )
        if (
            projected["start"] > projected["appearance"]
            or projected["played-60"] > projected["appearance"]
        ):
            projected_violations += 1
        maximum_adjustment = max(
            maximum_adjustment,
            *(
                abs(projected[target] - values[target])
                for target in BINARY_TARGETS
            ),
        )
        for target in BINARY_TARGETS:
            source = raw[target][player_id]
            fold_predictions[target].extend(
                (
                    Prediction(
                        model=_model_name(RAW_MODEL_PREFIX, target),
                        season_code=source.season_code,
                        gameweek=source.gameweek,
                        player_id=source.player_id,
                        position=source.position,
                        predicted=source.predicted,
                        actual=source.actual,
                    ),
                    Prediction(
                        model=_model_name(PROJECTED_MODEL_PREFIX, target),
                        season_code=source.season_code,
                        gameweek=source.gameweek,
                        player_id=source.player_id,
                        position=source.position,
                        predicted=projected[target],
                        actual=source.actual,
                    ),
                )
            )
    fold = {
        "gameweek": target_gameweek,
        "trainingGameweeks": list(training_gameweeks),
        "targetPlayerCount": len(player_ids),
        "rawViolationCount": raw_violations,
        "projectedViolationCount": projected_violations,
        "maximumRawViolation": _round(maximum_raw_violation),
        "maximumAbsoluteAdjustment": _round(maximum_adjustment),
        "targets": [
            _fold_target_summary(target, fold_predictions[target])
            for target in BINARY_TARGETS
        ],
        "diagnostics": diagnostics,
    }
    return fold, fold_predictions


def _project_probabilities(
    appearance: float,
    start: float,
    played_60: float,
) -> Dict[str, float]:
    values = {
        "appearance": _probability(appearance),
        "start": _probability(start),
        "played-60": _probability(played_60),
    }
    pooled = ["appearance"]
    pooled_total = values["appearance"]
    pooled_mean = pooled_total
    for target in sorted(
        ("start", "played-60"),
        key=lambda item: (-values[item], item),
    ):
        if values[target] <= pooled_mean:
            continue
        pooled.append(target)
        pooled_total += values[target]
        pooled_mean = pooled_total / len(pooled)
    projected = dict(values)
    for target in pooled:
        projected[target] = pooled_mean
    return projected


def _probability(value: float) -> float:
    number = float(value)
    if not math.isfinite(number) or number < 0.0 or number > 1.0:
        raise TemporalRidgeError(
            "evaluation.invalid-participation-probability",
            "Probability projection requires values between zero and one.",
        )
    return number


def _require_aligned_samples(
    samples: Mapping[str, Mapping[int, Sequence[Sample]]],
) -> List[int]:
    gameweeks = sorted(samples["appearance"])
    for target in BINARY_TARGETS:
        if sorted(samples[target]) != gameweeks:
            raise TemporalRidgeError(
                "evaluation.unaligned-coherence-gameweeks",
                "Participation targets do not share identical Gameweeks.",
            )
    for gameweek in gameweeks:
        reference = samples["appearance"][gameweek]
        reference_identity = [
            (
                sample.player_id,
                sample.position,
                sample.features,
            )
            for sample in reference
        ]
        for target in ("start", "played-60"):
            target_samples = samples[target][gameweek]
            identity = [
                (
                    sample.player_id,
                    sample.position,
                    sample.features,
                )
                for sample in target_samples
            ]
            if identity != reference_identity:
                raise TemporalRidgeError(
                    "evaluation.unaligned-coherence-cohort",
                    "Participation targets do not share an identical player "
                    "and feature cohort.",
                )
            for appearance_sample, target_sample in zip(
                reference,
                target_samples,
            ):
                if target_sample.actual > appearance_sample.actual:
                    raise TemporalRidgeError(
                        "evaluation.incoherent-participation-outcome",
                        "Historical child outcomes must imply appearance.",
                    )
    return gameweeks


def _fold_target_summary(
    target: str,
    predictions: Sequence[Prediction],
) -> Dict[str, Any]:
    return {
        "target": target,
        "raw": _model_summary(
            _model_name(RAW_MODEL_PREFIX, target),
            predictions,
        ),
        "projected": _model_summary(
            _model_name(PROJECTED_MODEL_PREFIX, target),
            predictions,
        ),
    }


def _target_summary(
    target: str,
    predictions: Sequence[Prediction],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    raw = _model_summary(
        _model_name(RAW_MODEL_PREFIX, target),
        predictions,
    )
    projected = _model_summary(
        _model_name(PROJECTED_MODEL_PREFIX, target),
        predictions,
    )
    raw_metrics = raw["metrics"]
    projected_metrics = projected["metrics"]
    raw_positions = raw["slices"]["position"]
    projected_positions = projected["slices"]["position"]
    checks = {
        "noBrierRegression": (
            projected_metrics["brierScore"]
            <= raw_metrics["brierScore"]
        ),
        "noLogLossRegression": (
            projected_metrics["logLoss"] <= raw_metrics["logLoss"]
        ),
        "calibrationWithinTolerance": (
            projected_metrics["calibrationError10"]
            <= raw_metrics["calibrationError10"]
            + MAXIMUM_CALIBRATION_REGRESSION
        ),
        "majorityFoldNonRegression": (
            sum(
                _fold_metrics(fold, target, "projected")["brierScore"]
                <= _fold_metrics(fold, target, "raw")["brierScore"]
                for fold in folds
            )
            > len(folds) / 2
        ),
        "noMaterialPositionBrierRegression": all(
            projected_positions[position]["brierScore"]
            <= raw_positions[position]["brierScore"]
            + MAXIMUM_POSITION_BRIER_REGRESSION
            for position in projected_positions
            if position in raw_positions
        ),
    }
    return {
        "target": target,
        "raw": raw,
        "projected": projected,
        "brierChange": _round(
            projected_metrics["brierScore"] - raw_metrics["brierScore"]
        ),
        "recommendation": {
            "status": (
                "supports-secondary-coherence-diagnostic"
                if all(checks.values())
                else "not-supported"
            ),
            "isPromoted": False,
            "checks": checks,
        },
    }


def _fold_metrics(
    fold: Mapping[str, Any],
    target: str,
    model: str,
) -> Mapping[str, Any]:
    target_document = next(
        item for item in fold["targets"] if item["target"] == target
    )
    return target_document[model]["metrics"]


def _model_summary(
    name: str,
    predictions: Sequence[Prediction],
) -> Dict[str, Any]:
    selected = [
        prediction
        for prediction in predictions
        if prediction.model == name
    ]
    if not selected:
        raise TemporalRidgeError(
            "evaluation.missing-coherence-model",
            f"No predictions were available for {name}.",
        )
    return {
        "name": name,
        "metrics": _probability_metrics(selected),
        "slices": {
            "position": {
                position: _probability_metrics(items)
                for position, items in sorted(
                    _group_by_position(selected).items()
                )
            }
        },
    }


def _model_name(prefix: str, target: str) -> str:
    return f"{prefix}-{target}"


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
            "secondary-holdout-diagnostic-not-a-new-promotion-test"
        ),
        "isPromoted": False,
        "canReplaceCurrentRawProbabilities": False,
        "configuration": {
            "seasonCode": season_code,
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "holdoutStartGameweek": holdout_start_gameweek,
            "targets": list(BINARY_TARGETS),
            "projection": (
                "equal-weight-euclidean-projection-onto-appearance-greater-"
                "than-or-equal-to-start-and-played-60"
            ),
            "maximumCalibrationRegression": (
                MAXIMUM_CALIBRATION_REGRESSION
            ),
            "maximumPositionBrierRegression": (
                MAXIMUM_POSITION_BRIER_REGRESSION
            ),
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
            "The underlying classifiers' locked holdout was already opened "
            "before current-player incoherence motivated this diagnostic.",
            "The fixed projection is outcome-independent, but this result "
            "cannot act as a fresh promotion holdout.",
            "The archive lacks historical decision-time injury state and "
            "cannot evaluate current-official-availability fusion.",
            "Current-season temporal folds remain required before product "
            "import.",
        ],
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    targets: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    raw_violations = sum(
        int(fold["rawViolationCount"]) for fold in folds
    )
    projected_violations = sum(
        int(fold["projectedViolationCount"]) for fold in folds
    )
    diagnostic_supported = (
        bool(targets)
        and raw_violations > 0
        and projected_violations == 0
        and all(
            target["recommendation"]["status"]
            == "supports-secondary-coherence-diagnostic"
            for target in targets
        )
    )
    report: Dict[str, Any] = {
        **base,
        "status": status,
        "reason": reason,
        "lockedHoldout": {
            "foldCount": len(folds),
            "rawViolationCount": raw_violations,
            "projectedViolationCount": projected_violations,
            "folds": list(folds),
        },
        "targets": list(targets),
        "historicalDiagnosticSupportsProjection": diagnostic_supported,
        "productImportReady": False,
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "provenance": report["provenance"],
            "holdoutGameweeks": [
                fold["gameweek"] for fold in folds
            ],
            "targets": list(BINARY_TARGETS),
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare a fixed Euclidean coherence projection with the raw "
            "historical participation classifiers on identical holdout folds."
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
    parser.add_argument("--output", required=True, type=Path)
    parsed = parser.parse_args(arguments)
    try:
        report = evaluate_historical_participation_coherence(
            parsed.database,
            season_code=parsed.season,
            minimum_training_gameweeks=parsed.minimum_training_gameweeks,
            holdout_start_gameweek=parsed.holdout_start_gameweek,
        )
        _write_report(report, parsed.output)
        return 0 if report["status"] == "complete" else 2
    except (TemporalRidgeError, OSError, ValueError) as exception:
        code = getattr(exception, "code", "evaluation.failed")
        print(
            json.dumps(
                {"error": {"code": code, "message": str(exception)}},
                sort_keys=True,
            ),
            file=sys.stderr,
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
