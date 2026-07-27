from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

import numpy as np
from sklearn.ensemble import HistGradientBoostingClassifier

from .historical_participation_coherence_evaluation import (
    EVALUATOR_VERSION as RAW_EVALUATOR_VERSION,
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
    _load_capture,
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
from .temporal_tree import (
    RANDOM_SEED,
    _can_split,
    _matrix,
    _scikit_learn_version,
)

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-joint-participation-evaluation-v1"
MODEL_PREFIX = "five-state-joint"
STATE_COUNT = 5
STATE_LABELS = (
    "no-appearance",
    "appearance-no-start-under-60",
    "appearance-no-start-60-plus",
    "appearance-start-under-60",
    "appearance-start-60-plus",
)
MODEL_CONFIGURATION = {
    "implementation": "sklearn.ensemble.HistGradientBoostingClassifier",
    "loss": "log_loss",
    "learningRate": 0.05,
    "maximumIterations": 100,
    "maximumLeafNodes": 7,
    "minimumSamplesPerLeaf": 20,
    "l2Regularisation": 10.0,
    "maximumBins": 63,
    "earlyStopping": False,
    "randomSeed": RANDOM_SEED,
    "missingValues": "native-learned-branch",
}


def evaluate_historical_joint_participation(
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
                None,
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
        joint_actuals: List[int] = []
        joint_probabilities: List[Tuple[float, ...]] = []
        for target_gameweek in eligible:
            training_gameweeks = [
                gameweek
                for gameweek in gameweeks
                if gameweek < target_gameweek
            ]
            fold, fold_predictions, actuals, probabilities = _evaluate_fold(
                samples,
                target_gameweek,
                training_gameweeks,
                _raw_fold(raw_report, target_gameweek),
            )
            folds.append(fold)
            joint_actuals.extend(actuals)
            joint_probabilities.extend(probabilities)
            for target in BINARY_TARGETS:
                predictions[target].extend(fold_predictions[target])
        if not folds:
            return _finish(
                base,
                "insufficient-data",
                "no-eligible-locked-holdout-folds",
                [],
                [],
                None,
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
        return _finish(
            base,
            "complete",
            None,
            folds,
            targets,
            _joint_metrics(joint_actuals, joint_probabilities),
        )
    finally:
        connection.close()


def _evaluate_fold(
    samples: Mapping[str, Mapping[int, Sequence[Sample]]],
    target_gameweek: int,
    training_gameweeks: Sequence[int],
    raw_fold: Mapping[str, Any],
) -> Tuple[
    Dict[str, Any],
    Dict[str, List[Prediction]],
    List[int],
    List[Tuple[float, ...]],
]:
    training = _joint_samples(samples, training_gameweeks)
    target = _joint_samples(samples, (target_gameweek,))
    probabilities, diagnostics = _predict_joint(training, target)
    fold_predictions: Dict[str, List[Prediction]] = {
        name: [] for name in BINARY_TARGETS
    }
    actuals: List[int] = []
    coherent_violations = 0
    for sample, state_probabilities in zip(target, probabilities):
        marginals = _marginals(state_probabilities)
        if (
            marginals["start"] > marginals["appearance"] + 1e-15
            or marginals["played-60"]
            > marginals["appearance"] + 1e-15
        ):
            coherent_violations += 1
        actuals.append(sample.actual)
        actual_marginals = _state_marginals(sample.actual)
        for target_name in BINARY_TARGETS:
            fold_predictions[target_name].append(
                Prediction(
                    model=_model_name(MODEL_PREFIX, target_name),
                    season_code=sample.season_code,
                    gameweek=sample.gameweek,
                    player_id=sample.player_id,
                    position=sample.position,
                    predicted=marginals[target_name],
                    actual=actual_marginals[target_name],
                )
            )
    fold_targets = []
    for target_name in BINARY_TARGETS:
        raw_target = _raw_fold_target(raw_fold, target_name)["raw"]
        candidate = _model_summary(
            _model_name(MODEL_PREFIX, target_name),
            fold_predictions[target_name],
        )
        fold_targets.append(
            {
                "target": target_name,
                "raw": raw_target,
                "joint": candidate,
                "brierChange": _round(
                    candidate["metrics"]["brierScore"]
                    - raw_target["metrics"]["brierScore"]
                ),
            }
        )
    return (
        {
            "gameweek": target_gameweek,
            "trainingGameweeks": list(training_gameweeks),
            "targetPlayerCount": len(target),
            "coherenceViolationCount": coherent_violations,
            "jointStateMetrics": _joint_metrics(
                actuals,
                probabilities,
            ),
            "targets": fold_targets,
            "diagnostics": diagnostics,
        },
        fold_predictions,
        actuals,
        probabilities,
    )


def _joint_samples(
    samples: Mapping[str, Mapping[int, Sequence[Sample]]],
    gameweeks: Sequence[int],
) -> List[Sample]:
    joined: List[Sample] = []
    for gameweek in gameweeks:
        appearances = samples["appearance"][gameweek]
        starts = samples["start"][gameweek]
        played_60 = samples["played-60"][gameweek]
        for appearance, start, sixty in zip(
            appearances,
            starts,
            played_60,
        ):
            joined.append(
                Sample(
                    season_code=appearance.season_code,
                    gameweek=appearance.gameweek,
                    player_id=appearance.player_id,
                    position=appearance.position,
                    features=appearance.features,
                    actual=_state(
                        appearance.actual,
                        start.actual,
                        sixty.actual,
                    ),
                )
            )
    return joined


def _state(appearance: int, start: int, played_60: int) -> int:
    values = (int(appearance), int(start), int(played_60))
    if any(value not in (0, 1) for value in values):
        raise TemporalRidgeError(
            "evaluation.non-binary-joint-participation-outcome",
            "Joint participation outcomes must be binary.",
        )
    if not appearance:
        if start or played_60:
            raise TemporalRidgeError(
                "evaluation.incoherent-participation-outcome",
                "Start and 60-minute outcomes require an appearance.",
            )
        return 0
    return 1 + 2 * start + played_60


def _state_marginals(state: int) -> Dict[str, int]:
    if state < 0 or state >= STATE_COUNT:
        raise TemporalRidgeError(
            "evaluation.invalid-joint-participation-state",
            "Joint participation state is outside the fixed five states.",
        )
    return {
        "appearance": int(state != 0),
        "start": int(state in (3, 4)),
        "played-60": int(state in (2, 4)),
    }


def _marginals(probabilities: Sequence[float]) -> Dict[str, float]:
    if len(probabilities) != STATE_COUNT:
        raise TemporalRidgeError(
            "evaluation.invalid-joint-probability-count",
            "Joint participation prediction must contain five states.",
        )
    values = tuple(float(value) for value in probabilities)
    if (
        any(
            not math.isfinite(value) or value < 0.0 or value > 1.0
            for value in values
        )
        or not math.isclose(
            sum(values),
            1.0,
            rel_tol=0.0,
            abs_tol=1e-12,
        )
    ):
        raise TemporalRidgeError(
            "evaluation.invalid-joint-probabilities",
            "Joint participation probabilities must be finite and sum to one.",
        )
    return {
        "appearance": 1.0 - values[0],
        "start": values[3] + values[4],
        "played-60": values[2] + values[4],
    }


def _predict_joint(
    training: Sequence[Sample],
    target: Sequence[Sample],
) -> Tuple[List[Tuple[float, ...]], Dict[str, Any]]:
    if not training or not target:
        raise TemporalRidgeError(
            "evaluation.empty-fold",
            "An eligible joint participation fold has no rows.",
        )
    raw_training, feature_names = _matrix(training, FEATURES)
    raw_target, _ = _matrix(target, FEATURES)
    selected = [
        index
        for index in range(raw_training.shape[1])
        if _can_split(raw_training[:, index])
    ]
    actuals = np.asarray([sample.actual for sample in training], dtype=int)
    observed_states = sorted(set(int(value) for value in actuals))
    if any(state < 0 or state >= STATE_COUNT for state in observed_states):
        raise TemporalRidgeError(
            "evaluation.invalid-joint-participation-state",
            "Training rows contain an invalid joint participation state.",
        )
    if len(observed_states) < 2 or not selected:
        counts = np.bincount(actuals, minlength=STATE_COUNT)
        constant = (counts + 1.0) / (len(actuals) + STATE_COUNT)
        predicted = np.tile(constant, (len(target), 1))
        iterations = 0
    else:
        estimator = HistGradientBoostingClassifier(
            loss="log_loss",
            learning_rate=0.05,
            max_iter=100,
            max_leaf_nodes=7,
            min_samples_leaf=20,
            l2_regularization=10.0,
            max_bins=63,
            early_stopping=False,
            random_state=RANDOM_SEED,
        )
        estimator.fit(raw_training[:, selected], actuals)
        observed = estimator.predict_proba(raw_target[:, selected])
        predicted = np.zeros((len(target), STATE_COUNT), dtype=float)
        for index, state in enumerate(estimator.classes_):
            predicted[:, int(state)] = observed[:, index]
        iterations = int(estimator.n_iter_)
    if not np.isfinite(predicted).all():
        raise TemporalRidgeError(
            "evaluation.non-finite-joint-participation-probability",
            "The joint classifier produced a non-finite probability.",
        )
    row_totals = predicted.sum(axis=1)
    if (predicted < 0.0).any() or not np.allclose(
        row_totals,
        1.0,
        atol=1e-12,
    ):
        raise TemporalRidgeError(
            "evaluation.invalid-joint-probabilities",
            "The joint classifier produced an invalid probability vector.",
        )
    probabilities = [
        tuple(float(value) for value in row)
        for row in predicted
    ]
    return probabilities, {
        **MODEL_CONFIGURATION,
        "libraryVersion": _scikit_learn_version(),
        "trainingRows": len(training),
        "stateCounts": {
            STATE_LABELS[state]: int((actuals == state).sum())
            for state in range(STATE_COUNT)
        },
        "candidateFeatureCount": len(feature_names),
        "modelFeatureCount": len(selected),
        "completedIterations": iterations,
        "droppedUnsplitableFeatures": [
            name
            for index, name in enumerate(feature_names)
            if index not in selected
        ],
    }


def _joint_metrics(
    actuals: Sequence[int],
    probabilities: Sequence[Sequence[float]],
) -> Dict[str, Any]:
    if not actuals or len(actuals) != len(probabilities):
        raise TemporalRidgeError(
            "evaluation.invalid-joint-metric-cohort",
            "Joint metrics require aligned non-empty outcomes.",
        )
    epsilon = 1e-15
    log_loss = 0.0
    brier = 0.0
    for actual, row in zip(actuals, probabilities):
        values = tuple(float(value) for value in row)
        _marginals(values)
        log_loss -= math.log(max(epsilon, values[int(actual)]))
        brier += sum(
            (value - int(index == actual)) ** 2
            for index, value in enumerate(values)
        )
    count = len(actuals)
    return {
        "count": count,
        "multiclassBrierScore": _round(brier / count),
        "logLoss": _round(log_loss / count),
        "stateRates": {
            STATE_LABELS[state]: _round(
                sum(actual == state for actual in actuals) / count
            )
            for state in range(STATE_COUNT)
        },
        "meanProbabilities": {
            STATE_LABELS[state]: _round(
                sum(row[state] for row in probabilities) / count
            )
            for state in range(STATE_COUNT)
        },
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
                _joint_fold_metrics(fold, target)["brierScore"]
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
        "joint": candidate,
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


def _joint_fold_metrics(
    fold: Mapping[str, Any],
    target: str,
) -> Mapping[str, Any]:
    document = next(
        item for item in fold["targets"] if item["target"] == target
    )
    return document["joint"]["metrics"]


def _load_raw_report(path: Path) -> Mapping[str, Any]:
    if not path.is_file():
        raise TemporalRidgeError(
            "evaluation.raw-report-not-found",
            "The retained raw comparator report does not exist.",
        )
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise TemporalRidgeError(
            "evaluation.invalid-raw-report",
            "The retained raw comparator report is not valid JSON.",
        ) from exception
    if not isinstance(document, Mapping):
        raise TemporalRidgeError(
            "evaluation.invalid-raw-report",
            "The retained raw comparator report must be a JSON object.",
        )
    return document


def _validate_raw_identity(
    report: Mapping[str, Any],
    capture: HistoricalCapture,
    season_code: str,
    minimum_training_gameweeks: int,
    holdout_start_gameweek: int,
    gameweeks: Sequence[int],
) -> None:
    configuration = report.get("configuration")
    provenance = report.get("provenance")
    expected_provenance = _provenance(capture)
    if (
        report.get("status") != "complete"
        or report.get("evaluatorVersion") != RAW_EVALUATOR_VERSION
        or report.get("researchStatus")
        != "secondary-holdout-diagnostic-not-a-new-promotion-test"
        or not isinstance(configuration, Mapping)
        or configuration.get("seasonCode") != season_code
        or configuration.get("minimumTrainingGameweeks")
        != minimum_training_gameweeks
        or configuration.get("holdoutStartGameweek")
        != holdout_start_gameweek
        or configuration.get("targets") != list(BINARY_TARGETS)
        or provenance != expected_provenance
    ):
        raise TemporalRidgeError(
            "evaluation.raw-report-identity-mismatch",
            "The raw comparator report does not match this fixed evaluation.",
        )
    expected_run_identity = _sha256(
        {
            key: value
            for key, value in report.items()
            if key != "runIdentitySha256"
        }
    )
    if report.get("runIdentitySha256") != expected_run_identity:
        raise TemporalRidgeError(
            "evaluation.raw-report-integrity-mismatch",
            "The raw comparator report content does not match its run identity.",
        )
    holdout = report.get("lockedHoldout")
    if not isinstance(holdout, Mapping):
        raise TemporalRidgeError(
            "evaluation.invalid-raw-report",
            "The raw comparator report has no holdout document.",
        )
    report_gameweeks = [
        fold.get("gameweek")
        for fold in holdout.get("folds", [])
        if isinstance(fold, Mapping)
    ]
    expected_identity = _sha256(
        {
            "provenance": expected_provenance,
            "holdoutGameweeks": list(gameweeks),
            "targets": list(BINARY_TARGETS),
        }
    )
    if (
        report_gameweeks != list(gameweeks)
        or report.get("dataIdentitySha256") != expected_identity
    ):
        raise TemporalRidgeError(
            "evaluation.raw-report-identity-mismatch",
            "The raw comparator folds do not match the database evidence.",
        )
    for target in BINARY_TARGETS:
        raw = _raw_target(report, target).get("raw")
        if (
            not isinstance(raw, Mapping)
            or raw.get("name") != _model_name(RAW_MODEL_PREFIX, target)
        ):
            raise TemporalRidgeError(
                "evaluation.invalid-raw-report",
                "The raw comparator model identity is missing.",
            )


def _raw_fold(
    report: Mapping[str, Any],
    gameweek: int,
) -> Mapping[str, Any]:
    holdout = report.get("lockedHoldout")
    if not isinstance(holdout, Mapping):
        raise TemporalRidgeError(
            "evaluation.invalid-raw-report",
            "The raw comparator report has no holdout document.",
        )
    folds = holdout.get("folds")
    if not isinstance(folds, list):
        raise TemporalRidgeError(
            "evaluation.invalid-raw-report",
            "The raw comparator report has no fold list.",
        )
    for fold in folds:
        if not isinstance(fold, Mapping):
            continue
        if fold.get("gameweek") == gameweek:
            return fold
    raise TemporalRidgeError(
        "evaluation.invalid-raw-report",
        f"The raw comparator has no Gameweek {gameweek} fold.",
    )


def _raw_fold_target(
    fold: Mapping[str, Any],
    target: str,
) -> Mapping[str, Any]:
    targets = fold.get("targets")
    if not isinstance(targets, list):
        raise TemporalRidgeError(
            "evaluation.invalid-raw-report",
            "The raw comparator fold has no target list.",
        )
    for item in targets:
        if not isinstance(item, Mapping):
            continue
        if item.get("target") == target:
            return item
    raise TemporalRidgeError(
        "evaluation.invalid-raw-report",
        f"The raw comparator fold has no {target} target.",
    )


def _raw_target(
    report: Mapping[str, Any],
    target: str,
) -> Mapping[str, Any]:
    targets = report.get("targets")
    if not isinstance(targets, list):
        raise TemporalRidgeError(
            "evaluation.invalid-raw-report",
            "The raw comparator report has no target list.",
        )
    for item in targets:
        if not isinstance(item, Mapping):
            continue
        if item.get("target") == target:
            return item
    raise TemporalRidgeError(
        "evaluation.invalid-raw-report",
        f"The raw comparator report has no {target} target.",
    )


def _provenance(capture: HistoricalCapture) -> Dict[str, Any]:
    return {
        "historicalCaptureId": capture.capture_id,
        "sourceRevision": capture.source_revision,
        "availableAtUtc": capture.available_at_utc,
        "playersSha256": capture.players_sha256,
        "gameweeksSha256": capture.gameweeks_sha256,
        "playerCount": capture.player_count,
        "playerGameweekRowCount": capture.player_gameweek_count,
        "stableCodeCount": capture.stable_code_count,
    }


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
            "states": list(STATE_LABELS),
            "features": list(FEATURES),
            "model": dict(MODEL_CONFIGURATION),
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
            "This candidate was designed after the underlying Gameweek "
            "31-38 holdout and projection diagnostic had been opened.",
            "The result is an exploratory candidate screen, not a new "
            "promotion holdout.",
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
    joint_metrics: Optional[Mapping[str, Any]],
) -> Dict[str, Any]:
    violations = sum(
        int(fold["coherenceViolationCount"]) for fold in folds
    )
    supported = (
        bool(targets)
        and violations == 0
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
            "folds": list(folds),
        },
        "jointStateMetrics": (
            None if joint_metrics is None else dict(joint_metrics)
        ),
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
            "states": list(STATE_LABELS),
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Screen a fixed five-state coherent participation classifier "
            "against an immutable raw historical report."
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
        report = evaluate_historical_joint_participation(
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
