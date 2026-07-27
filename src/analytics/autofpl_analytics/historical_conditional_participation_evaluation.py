from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

from .historical_joint_participation_evaluation import (
    _load_raw_report,
    _provenance,
    _raw_fold,
    _raw_fold_target,
    _raw_target,
    _validate_raw_identity,
)
from .historical_participation_coherence_evaluation import (
    MAXIMUM_CALIBRATION_REGRESSION,
    RAW_MODEL_PREFIX,
    _model_name,
    _model_summary,
    _require_aligned_samples,
)
from .historical_participation_evaluation import (
    BINARY_TARGETS,
    MAXIMUM_POSITION_BRIER_REGRESSION,
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
    _write_report,
)

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-conditional-participation-evaluation-v1"
MODEL_PREFIX = "appearance-conditional"
CHILD_TARGETS = ("start", "played-60")
FACTORIZATION = (
    "raw-appearance-times-child-probability-conditional-on-appearance"
)


def evaluate_historical_conditional_participation(
    database_path: Path,
    raw_report_path: Path,
    season_code: str = DEFAULT_SEASON,
    minimum_training_gameweeks: int = MINIMUM_TRAINING_GAMEWEEKS,
    holdout_start_gameweek: int = HOLDOUT_START_GAMEWEEK,
) -> Dict[str, Any]:
    database = Path(database_path)
    raw_path = Path(raw_report_path)
    _validate(
        database,
        season_code,
        minimum_training_gameweeks,
        holdout_start_gameweek,
    )
    raw_report = _load_raw_report(raw_path)
    connection = _open_connection(database)
    try:
        capture = _load_capture(connection, season_code)
        base = _base(
            season_code,
            minimum_training_gameweeks,
            holdout_start_gameweek,
            capture,
            raw_path,
            raw_report,
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
        eligible = [
            gameweek
            for gameweek in gameweeks
            if gameweek >= holdout_start_gameweek
            and len([prior for prior in gameweeks if prior < gameweek])
            >= minimum_training_gameweeks
        ]
        _validate_raw_identity(
            raw_report,
            capture,
            season_code,
            minimum_training_gameweeks,
            holdout_start_gameweek,
            eligible,
        )
        folds: List[Dict[str, Any]] = []
        predictions: Dict[str, List[Prediction]] = {
            target: [] for target in BINARY_TARGETS
        }
        for target_gameweek in eligible:
            training_gameweeks = [
                gameweek
                for gameweek in gameweeks
                if gameweek < target_gameweek
            ]
            fold, fold_predictions = _evaluate_fold(
                samples,
                target_gameweek,
                training_gameweeks,
                _raw_fold(raw_report, target_gameweek),
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
            _target_summary(
                target,
                predictions[target],
                folds,
                _raw_target(raw_report, target)["raw"],
            )
            for target in BINARY_TARGETS
        ]
        return _finish(base, "complete", None, folds, targets)
    finally:
        connection.close()


def _evaluate_fold(
    samples: Mapping[str, Mapping[int, Sequence[Sample]]],
    target_gameweek: int,
    training_gameweeks: Sequence[int],
    raw_fold: Mapping[str, Any],
) -> Tuple[Dict[str, Any], Dict[str, List[Prediction]]]:
    appearance_training = [
        sample
        for gameweek in training_gameweeks
        for sample in samples["appearance"][gameweek]
    ]
    appearance_target = samples["appearance"][target_gameweek]
    raw_predictions, appearance_diagnostics = _predict_classifier(
        appearance_training,
        appearance_target,
        _candidate_name("appearance"),
    )
    raw_appearance = [
        _renamed_prediction(
            prediction,
            _model_name(RAW_MODEL_PREFIX, "appearance"),
            prediction.predicted,
        )
        for prediction in raw_predictions
    ]
    regenerated = _model_summary(
        _model_name(RAW_MODEL_PREFIX, "appearance"),
        raw_appearance,
    )
    retained = _raw_fold_target(raw_fold, "appearance")["raw"]
    if regenerated != retained:
        raise TemporalRidgeError(
            "evaluation.raw-appearance-reproduction-mismatch",
            "The fixed raw appearance refit does not reproduce the retained "
            "fold comparator.",
        )

    appearance_by_player = {
        prediction.player_id: prediction for prediction in raw_appearance
    }
    conditional_predictions: Dict[str, Dict[int, Prediction]] = {}
    conditional_diagnostics: Dict[str, Any] = {}
    for child in CHILD_TARGETS:
        training = _conditional_training_samples(
            samples,
            child,
            training_gameweeks,
        )
        target = samples[child][target_gameweek]
        child_predictions, diagnostics = _predict_classifier(
            training,
            target,
            _model_name(MODEL_PREFIX, f"{child}-given-appearance"),
        )
        conditional_predictions[child] = {
            prediction.player_id: prediction
            for prediction in child_predictions
        }
        conditional_diagnostics[child] = diagnostics

    player_ids = sorted(appearance_by_player)
    if any(
        set(conditional_predictions[child]) != set(player_ids)
        for child in CHILD_TARGETS
    ):
        raise TemporalRidgeError(
            "evaluation.unaligned-conditional-predictions",
            "Conditional participation targets do not contain identical "
            "holdout players.",
        )

    fold_predictions: Dict[str, List[Prediction]] = {
        target: [] for target in BINARY_TARGETS
    }
    violations = 0
    for player_id in player_ids:
        appearance = appearance_by_player[player_id]
        values = {"appearance": appearance.predicted}
        for child in CHILD_TARGETS:
            conditional = conditional_predictions[child][player_id]
            values[child] = _factorized_probability(
                appearance.predicted,
                conditional.predicted,
            )
        if (
            values["start"] > values["appearance"]
            or values["played-60"] > values["appearance"]
        ):
            violations += 1
        for target in BINARY_TARGETS:
            source = (
                appearance
                if target == "appearance"
                else conditional_predictions[target][player_id]
            )
            fold_predictions[target].append(
                _renamed_prediction(
                    source,
                    _model_name(MODEL_PREFIX, target),
                    values[target],
                )
            )

    return (
        {
            "gameweek": target_gameweek,
            "trainingGameweeks": list(training_gameweeks),
            "targetPlayerCount": len(player_ids),
            "coherenceViolationCount": violations,
            "rawAppearanceReproduced": True,
            "targets": [
                _fold_target_summary(
                    target,
                    fold_predictions[target],
                    _raw_fold_target(raw_fold, target)["raw"],
                )
                for target in BINARY_TARGETS
            ],
            "diagnostics": {
                "appearance": appearance_diagnostics,
                "conditional": conditional_diagnostics,
            },
        },
        fold_predictions,
    )


def _conditional_training_samples(
    samples: Mapping[str, Mapping[int, Sequence[Sample]]],
    child: str,
    gameweeks: Sequence[int],
) -> List[Sample]:
    if child not in CHILD_TARGETS:
        raise TemporalRidgeError(
            "evaluation.invalid-conditional-participation-target",
            "Conditional participation fitting requires a child target.",
        )
    selected: List[Sample] = []
    for gameweek in gameweeks:
        appearances = samples["appearance"][gameweek]
        children = samples[child][gameweek]
        for appearance, child_sample in zip(appearances, children):
            if appearance.actual == 1:
                selected.append(child_sample)
    if not selected:
        raise TemporalRidgeError(
            "evaluation.empty-conditional-participation-training-set",
            "No appearance-positive rows are available for conditional "
            "participation fitting.",
        )
    return selected


def _factorized_probability(
    appearance_probability: float,
    conditional_probability: float,
) -> float:
    appearance = _probability(appearance_probability)
    conditional = _probability(conditional_probability)
    return appearance * conditional


def _probability(value: float) -> float:
    number = float(value)
    if not math.isfinite(number) or number < 0.0 or number > 1.0:
        raise TemporalRidgeError(
            "evaluation.invalid-conditional-participation-probability",
            "Conditional factorization requires probabilities between zero "
            "and one.",
        )
    return number


def _renamed_prediction(
    source: Prediction,
    model: str,
    predicted: float,
) -> Prediction:
    return Prediction(
        model=model,
        season_code=source.season_code,
        gameweek=source.gameweek,
        player_id=source.player_id,
        position=source.position,
        predicted=predicted,
        actual=source.actual,
    )


def _fold_target_summary(
    target: str,
    predictions: Sequence[Prediction],
    raw: Mapping[str, Any],
) -> Dict[str, Any]:
    return {
        "target": target,
        "raw": dict(raw),
        "factorized": _model_summary(
            _model_name(MODEL_PREFIX, target),
            predictions,
        ),
    }


def _target_summary(
    target: str,
    predictions: Sequence[Prediction],
    folds: Sequence[Mapping[str, Any]],
    raw: Mapping[str, Any],
) -> Dict[str, Any]:
    candidate = _model_summary(
        _model_name(MODEL_PREFIX, target),
        predictions,
    )
    raw_metrics = raw["metrics"]
    candidate_metrics = candidate["metrics"]
    raw_positions = raw["slices"]["position"]
    candidate_positions = candidate["slices"]["position"]
    checks = {
        "noBrierRegression": (
            candidate_metrics["brierScore"]
            <= raw_metrics["brierScore"]
        ),
        "noLogLossRegression": (
            candidate_metrics["logLoss"] <= raw_metrics["logLoss"]
        ),
        "calibrationWithinTolerance": (
            candidate_metrics["calibrationError10"]
            <= raw_metrics["calibrationError10"]
            + MAXIMUM_CALIBRATION_REGRESSION
        ),
        "majorityFoldNonRegression": (
            sum(
                _factorized_fold_metrics(fold, target)["brierScore"]
                <= _raw_fold_metrics(fold, target)["brierScore"]
                for fold in folds
            )
            > len(folds) / 2
        ),
        "noMaterialPositionBrierRegression": all(
            candidate_positions[position]["brierScore"]
            <= raw_positions[position]["brierScore"]
            + MAXIMUM_POSITION_BRIER_REGRESSION
            for position in candidate_positions
            if position in raw_positions
        ),
    }
    return {
        "target": target,
        "raw": dict(raw),
        "factorized": candidate,
        "brierChange": _round(
            candidate_metrics["brierScore"] - raw_metrics["brierScore"]
        ),
        "recommendation": {
            "status": (
                "supports-exploratory-current-season-registration"
                if all(checks.values())
                else "not-supported"
            ),
            "isPromoted": False,
            "checks": checks,
        },
    }


def _raw_fold_metrics(
    fold: Mapping[str, Any],
    target: str,
) -> Mapping[str, Any]:
    document = next(
        item for item in fold["targets"] if item["target"] == target
    )
    return document["raw"]["metrics"]


def _factorized_fold_metrics(
    fold: Mapping[str, Any],
    target: str,
) -> Mapping[str, Any]:
    document = next(
        item for item in fold["targets"] if item["target"] == target
    )
    return document["factorized"]["metrics"]


def _base(
    season_code: str,
    minimum_training_gameweeks: int,
    holdout_start_gameweek: int,
    capture: Optional[HistoricalCapture],
    raw_path: Path,
    raw_report: Mapping[str, Any],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": (
            "exploratory-reused-holdout-candidate-screen-not-promotion"
        ),
        "isPromoted": False,
        "canReplaceCurrentRawProbabilities": False,
        "configuration": {
            "seasonCode": season_code,
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "holdoutStartGameweek": holdout_start_gameweek,
            "targets": list(BINARY_TARGETS),
            "childTargets": list(CHILD_TARGETS),
            "features": list(FEATURES),
            "factorization": FACTORIZATION,
            "conditionalTrainingCohort": "appearance-outcome-equals-one",
            "maximumCalibrationRegression": (
                MAXIMUM_CALIBRATION_REGRESSION
            ),
            "maximumPositionBrierRegression": (
                MAXIMUM_POSITION_BRIER_REGRESSION
            ),
        },
        "rawComparator": {
            "pathName": raw_path.name,
            "fileSha256": hashlib.sha256(raw_path.read_bytes()).hexdigest(),
            "evaluatorVersion": raw_report.get("evaluatorVersion"),
            "dataIdentitySha256": raw_report.get("dataIdentitySha256"),
            "runIdentitySha256": raw_report.get("runIdentitySha256"),
        },
        "provenance": None if capture is None else _provenance(capture),
        "limitations": [
            "This factorization was selected after the underlying Gameweek "
            "31-38 holdout and two coherence challengers had been opened.",
            "The result is an exploratory candidate screen, not a new "
            "promotion holdout.",
            "Separate start and 60-minute conditional classifiers provide "
            "coherent marginals but do not define their joint dependence.",
            "The archive lacks historical decision-time injury state and "
            "cannot evaluate current-official-availability fusion.",
            "Genuinely new 2026/27 temporal folds are required before any "
            "product import.",
        ],
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    targets: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    violations = sum(
        int(fold["coherenceViolationCount"]) for fold in folds
    )
    appearance_reproduced = bool(folds) and all(
        bool(fold["rawAppearanceReproduced"]) for fold in folds
    )
    supported = (
        bool(targets)
        and violations == 0
        and appearance_reproduced
        and all(
            target["recommendation"]["status"]
            == "supports-exploratory-current-season-registration"
            for target in targets
        )
    )
    report: Dict[str, Any] = {
        **base,
        "status": status,
        "reason": reason,
        "holdout": {
            "foldCount": len(folds),
            "coherenceViolationCount": violations,
            "rawAppearanceReproduced": appearance_reproduced,
            "folds": list(folds),
        },
        "targets": list(targets),
        "historicalScreenSupportsCurrentSeasonRegistration": supported,
        "productImportReady": False,
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "provenance": report["provenance"],
            "rawComparatorDataIdentity": (
                report["rawComparator"]["dataIdentitySha256"]
            ),
            "holdoutGameweeks": [
                fold["gameweek"] for fold in folds
            ],
            "factorization": FACTORIZATION,
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Screen a fixed raw-appearance and conditional-child "
            "participation factorization against an immutable comparator."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--raw-report", required=True, type=Path)
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
        report = evaluate_historical_conditional_participation(
            parsed.database,
            parsed.raw_report,
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
