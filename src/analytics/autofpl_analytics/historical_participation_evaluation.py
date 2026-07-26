from __future__ import annotations

import argparse
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, List, Mapping, Optional, Sequence, Tuple

import numpy as np
from sklearn.ensemble import HistGradientBoostingClassifier

from .historical_preseason_evaluation import (
    DEFAULT_SEASON,
    FEATURES,
    HOLDOUT_START_GAMEWEEK,
    MINIMUM_TRAINING_GAMEWEEKS,
    HistoricalCapture,
    _build_samples,
    _load_capture,
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
from .temporal_tree import (
    RANDOM_SEED,
    TREE_CONFIGURATION,
    _can_split,
    _matrix,
    _predict_tree,
    _scikit_learn_version,
)

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-participation-evaluation-v1"
BINARY_TARGETS = ("appearance", "start", "played-60")
MINUTES_TARGET = "minutes"
TARGETS = (*BINARY_TARGETS, MINUTES_TARGET)
BINARY_BASELINES = (
    "global-smoothed-rate",
    "position-smoothed-rate",
    "player-last-event",
    "player-rolling3-smoothed-rate",
    "player-expanding-smoothed-rate",
)
MINUTES_BASELINES = (
    "minutes-zero",
    "minutes-position-expanding-mean",
    "minutes-player-last",
    "minutes-player-rolling3",
    "minutes-player-expanding-mean",
)
MINIMUM_SCORE_IMPROVEMENT = 0.01
MAXIMUM_CALIBRATION_REGRESSION = 0.02
MAXIMUM_POSITION_BRIER_REGRESSION = 0.02
MAXIMUM_POSITION_MAE_REGRESSION = 0.05


def evaluate_historical_participation(
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
            )
        tasks = [
            _evaluate_target(
                connection,
                capture,
                target_name,
                minimum_training_gameweeks,
                holdout_start_gameweek,
            )
            for target_name in TARGETS
        ]
        if any(task["status"] != "complete" for task in tasks):
            return _finish(
                base,
                "insufficient-data",
                "one-or-more-targets-have-no-eligible-locked-holdout",
                tasks,
            )
        return _finish(base, "complete", None, tasks)
    finally:
        connection.close()


def _evaluate_target(
    connection: Any,
    capture: HistoricalCapture,
    target_name: str,
    minimum_training_gameweeks: int,
    holdout_start_gameweek: int,
) -> Dict[str, Any]:
    samples_by_gameweek = _build_samples(
        connection,
        capture,
        target_name=target_name,
    )
    eligible_gameweeks = sorted(samples_by_gameweek)
    development_folds: List[Dict[str, Any]] = []
    development_predictions: List[Prediction] = []
    holdout_folds: List[Dict[str, Any]] = []
    holdout_predictions: List[Prediction] = []
    selected_baseline: Optional[str] = None
    candidate = _candidate_name(target_name)
    baselines = (
        BINARY_BASELINES
        if target_name in BINARY_TARGETS
        else MINUTES_BASELINES
    )

    for target_gameweek in eligible_gameweeks:
        training_gameweeks = [
            gameweek
            for gameweek in eligible_gameweeks
            if gameweek < target_gameweek
        ]
        if len(training_gameweeks) < minimum_training_gameweeks:
            continue
        training = [
            sample
            for gameweek in training_gameweeks
            for sample in samples_by_gameweek[gameweek]
        ]
        target = samples_by_gameweek[target_gameweek]
        predictions, diagnostics = _predict(
            target_name,
            training,
            target,
            candidate,
        )
        fold = _fold(
            target_name,
            target_gameweek,
            training_gameweeks,
            training,
            target,
            predictions,
            diagnostics,
        )
        if target_gameweek < holdout_start_gameweek:
            development_predictions.extend(predictions)
            development_folds.append(fold)
            continue
        if selected_baseline is None:
            if not development_folds:
                continue
            selected_baseline = _select_baseline(
                target_name,
                development_predictions,
                baselines,
            )
        holdout_predictions.extend(predictions)
        holdout_folds.append(fold)

    if not development_folds or not holdout_folds or selected_baseline is None:
        return {
            "target": target_name,
            "status": "insufficient-data",
            "reason": "no-eligible-development-or-holdout-folds",
            "development": {
                "foldCount": len(development_folds),
                "folds": development_folds,
                "models": [],
            },
            "lockedHoldout": {
                "opened": bool(holdout_folds),
                "foldCount": len(holdout_folds),
                "folds": holdout_folds,
                "models": [],
            },
            "recommendation": None,
        }

    development_models = _summaries(
        target_name,
        development_predictions,
        (candidate, *baselines),
    )
    holdout_models = _summaries(
        target_name,
        holdout_predictions,
        (candidate, *baselines),
    )
    recommendation = _recommendation(
        target_name,
        candidate,
        selected_baseline,
        holdout_predictions,
        holdout_folds,
    )
    return {
        "target": target_name,
        "status": "complete",
        "reason": None,
        "candidate": candidate,
        "selectedBaseline": selected_baseline,
        "development": {
            "foldCount": len(development_folds),
            "folds": development_folds,
            "models": development_models,
        },
        "lockedHoldout": {
            "opened": True,
            "foldCount": len(holdout_folds),
            "folds": holdout_folds,
            "models": holdout_models,
        },
        "recommendation": recommendation,
    }


def _predict(
    target_name: str,
    training: Sequence[Sample],
    target: Sequence[Sample],
    candidate: str,
) -> Tuple[List[Prediction], Dict[str, Any]]:
    predictions = _baseline_predictions(target_name, training, target)
    if target_name in BINARY_TARGETS:
        challenger, diagnostics = _predict_classifier(
            training,
            target,
            candidate,
        )
    else:
        challenger, diagnostics = _predict_tree(
            training,
            target,
            continuous_features=FEATURES,
            model_name=candidate,
        )
    predictions.extend(challenger)
    return predictions, diagnostics


def _predict_classifier(
    training: Sequence[Sample],
    target: Sequence[Sample],
    model_name: str,
) -> Tuple[List[Prediction], Dict[str, Any]]:
    raw_training, feature_names = _matrix(training, FEATURES)
    raw_target, _ = _matrix(target, FEATURES)
    selected = [
        index
        for index in range(raw_training.shape[1])
        if _can_split(raw_training[:, index])
    ]
    actuals = np.asarray([sample.actual for sample in training], dtype=int)
    if not set(actuals).issubset({0, 1}):
        raise TemporalRidgeError(
            "evaluation.non-binary-participation-target",
            "A participation classifier received a non-binary target.",
        )
    if len(set(actuals)) < 2 or not selected:
        probabilities = np.full(
            len(target),
            (float(actuals.sum()) + 1.0) / (len(actuals) + 2.0),
        )
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
        probabilities = estimator.predict_proba(raw_target[:, selected])[:, 1]
        iterations = int(estimator.n_iter_)
    if not np.isfinite(probabilities).all():
        raise TemporalRidgeError(
            "evaluation.non-finite-participation-probability",
            "The participation classifier produced a non-finite probability.",
        )
    probabilities = np.clip(probabilities, 0.0, 1.0)
    predictions = [
        Prediction(
            model=model_name,
            season_code=sample.season_code,
            gameweek=sample.gameweek,
            player_id=sample.player_id,
            position=sample.position,
            predicted=float(probability),
            actual=sample.actual,
        )
        for sample, probability in zip(target, probabilities)
    ]
    return predictions, {
        "implementation": "sklearn.ensemble.HistGradientBoostingClassifier",
        "libraryVersion": _scikit_learn_version(),
        "trainingRows": len(training),
        "eventCount": int(actuals.sum()),
        "candidateFeatureCount": len(feature_names),
        "modelFeatureCount": len(selected),
        "completedIterations": iterations,
        "droppedUnsplitableFeatures": [
            name
            for index, name in enumerate(feature_names)
            if index not in selected
        ],
    }


def _baseline_predictions(
    target_name: str,
    training: Sequence[Sample],
    target: Sequence[Sample],
) -> List[Prediction]:
    global_mean = sum(sample.actual for sample in training) / len(training)
    positions: DefaultDict[str, List[int]] = defaultdict(list)
    players: DefaultDict[int, List[Tuple[int, int]]] = defaultdict(list)
    for sample in training:
        positions[sample.position].append(sample.actual)
        players[sample.player_id].append((sample.gameweek, sample.actual))
    predictions: List[Prediction] = []
    global_smoothed = (
        sum(sample.actual for sample in training) + 1.0
    ) / (len(training) + 2.0)
    for sample in target:
        position_values = positions[sample.position]
        position_mean = (
            sum(position_values) / len(position_values)
            if position_values
            else global_mean
        )
        player_values = [
            value for _, value in sorted(players[sample.player_id])
        ]
        if target_name in BINARY_TARGETS:
            values = {
                "global-smoothed-rate": global_smoothed,
                "position-smoothed-rate": _smooth(
                    position_values,
                    global_smoothed,
                ),
                "player-last-event": (
                    float(player_values[-1])
                    if player_values
                    else position_mean
                ),
                "player-rolling3-smoothed-rate": _smooth(
                    player_values[-3:],
                    position_mean,
                ),
                "player-expanding-smoothed-rate": _smooth(
                    player_values,
                    position_mean,
                ),
            }
        else:
            values = {
                "minutes-zero": 0.0,
                "minutes-position-expanding-mean": position_mean,
                "minutes-player-last": (
                    float(player_values[-1])
                    if player_values
                    else position_mean
                ),
                "minutes-player-rolling3": (
                    sum(player_values[-3:]) / len(player_values[-3:])
                    if player_values
                    else position_mean
                ),
                "minutes-player-expanding-mean": (
                    sum(player_values) / len(player_values)
                    if player_values
                    else position_mean
                ),
            }
        predictions.extend(
            Prediction(
                model=name,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=float(value),
                actual=sample.actual,
            )
            for name, value in values.items()
        )
    return predictions


def _smooth(values: Sequence[int], prior: float) -> float:
    return (sum(values) + 2.0 * prior) / (len(values) + 2.0)


def _fold(
    target_name: str,
    gameweek: int,
    training_gameweeks: Sequence[int],
    training: Sequence[Sample],
    target: Sequence[Sample],
    predictions: Sequence[Prediction],
    diagnostics: Mapping[str, Any],
) -> Dict[str, Any]:
    names = sorted({prediction.model for prediction in predictions})
    return {
        "gameweek": gameweek,
        "trainingGameweeks": list(training_gameweeks),
        "trainingRowCount": len(training),
        "targetPlayerCount": len(target),
        "models": _summaries(target_name, predictions, names),
        "diagnostics": dict(diagnostics),
    }


def _summaries(
    target_name: str,
    predictions: Sequence[Prediction],
    names: Sequence[str],
) -> List[Dict[str, Any]]:
    if target_name in BINARY_TARGETS:
        results = [
            _summarise_probability_model(name, predictions)
            for name in names
        ]
        results.sort(
            key=lambda item: (
                item["metrics"]["brierScore"],
                item["name"],
            )
        )
        return results
    results = [_summarise_model(name, predictions) for name in names]
    results.sort(key=lambda item: (item["metrics"]["mae"], item["name"]))
    return results


def _summarise_probability_model(
    name: str,
    predictions: Sequence[Prediction],
) -> Dict[str, Any]:
    selected = [item for item in predictions if item.model == name]
    if not selected:
        raise TemporalRidgeError(
            "evaluation.missing-participation-model",
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


def _probability_metrics(
    predictions: Sequence[Prediction],
) -> Dict[str, Any]:
    count = len(predictions)
    brier = sum(
        (item.predicted - item.actual) ** 2 for item in predictions
    ) / count
    epsilon = 1e-15
    log_loss = -sum(
        item.actual
        * math.log(min(1.0 - epsilon, max(epsilon, item.predicted)))
        + (1 - item.actual)
        * math.log(
            min(1.0 - epsilon, max(epsilon, 1.0 - item.predicted))
        )
        for item in predictions
    ) / count
    return {
        "count": count,
        "brierScore": _round(brier),
        "logLoss": _round(log_loss),
        "calibrationError10": _round(_calibration_error(predictions)),
        "meanProbability": _round(
            sum(item.predicted for item in predictions) / count
        ),
        "eventRate": _round(
            sum(item.actual for item in predictions) / count
        ),
    }


def _calibration_error(predictions: Sequence[Prediction]) -> float:
    bins: DefaultDict[int, List[Prediction]] = defaultdict(list)
    for prediction in predictions:
        bins[min(9, int(prediction.predicted * 10))].append(prediction)
    total = len(predictions)
    return sum(
        len(items)
        / total
        * abs(
            sum(item.predicted for item in items) / len(items)
            - sum(item.actual for item in items) / len(items)
        )
        for items in bins.values()
    )


def _group_by_position(
    predictions: Sequence[Prediction],
) -> Dict[str, List[Prediction]]:
    grouped: DefaultDict[str, List[Prediction]] = defaultdict(list)
    for prediction in predictions:
        grouped[prediction.position].append(prediction)
    return dict(grouped)


def _select_baseline(
    target_name: str,
    predictions: Sequence[Prediction],
    baselines: Sequence[str],
) -> str:
    return str(_summaries(target_name, predictions, baselines)[0]["name"])


def _recommendation(
    target_name: str,
    candidate_name: str,
    baseline_name: str,
    predictions: Sequence[Prediction],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    models = {
        item["name"]: item
        for item in _summaries(
            target_name,
            predictions,
            (candidate_name, baseline_name),
        )
    }
    candidate = models[candidate_name]["metrics"]
    baseline = models[baseline_name]["metrics"]
    candidate_positions = models[candidate_name]["slices"]["position"]
    baseline_positions = models[baseline_name]["slices"]["position"]
    if target_name in BINARY_TARGETS:
        primary = "brierScore"
        secondary = "logLoss"
        improvement = _fractional_improvement(
            baseline[primary],
            candidate[primary],
        )
        checks = {
            "minimumBrierImprovement": (
                improvement >= MINIMUM_SCORE_IMPROVEMENT
            ),
            "noLogLossRegression": (
                candidate[secondary] <= baseline[secondary]
            ),
            "calibrationWithinTolerance": (
                candidate["calibrationError10"]
                <= baseline["calibrationError10"]
                + MAXIMUM_CALIBRATION_REGRESSION
            ),
            "majorityFoldWins": _fold_wins(
                folds,
                candidate_name,
                baseline_name,
                primary,
            )
            > len(folds) / 2,
            "noMaterialPositionBrierRegression": all(
                candidate_positions[position]["brierScore"]
                <= baseline_positions[position]["brierScore"]
                + MAXIMUM_POSITION_BRIER_REGRESSION
                for position in candidate_positions
                if position in baseline_positions
            ),
        }
    else:
        primary = "mae"
        improvement = _fractional_improvement(
            baseline[primary],
            candidate[primary],
        )
        checks = {
            "minimumMaeImprovement": (
                improvement >= MINIMUM_SCORE_IMPROVEMENT
            ),
            "noRmseRegression": candidate["rmse"] <= baseline["rmse"],
            "majorityFoldWins": _fold_wins(
                folds,
                candidate_name,
                baseline_name,
                primary,
            )
            > len(folds) / 2,
            "noMaterialPositionMaeRegression": all(
                _fractional_regression(
                    baseline_positions[position]["mae"],
                    candidate_positions[position]["mae"],
                )
                <= MAXIMUM_POSITION_MAE_REGRESSION
                for position in candidate_positions
                if position in baseline_positions
            ),
        }
    supported = all(checks.values())
    return {
        "status": (
            "supported-for-provisional-preseason-bridge"
            if supported
            else "not-supported"
        ),
        "isPromoted": False,
        "selectedCandidate": candidate_name,
        "selectedBaseline": baseline_name,
        "primaryMetric": primary,
        "holdoutImprovementFraction": _round(improvement),
        "checks": checks,
    }


def _fold_wins(
    folds: Sequence[Mapping[str, Any]],
    candidate: str,
    baseline: str,
    metric: str,
) -> int:
    return sum(
        _model(fold, candidate)["metrics"][metric]
        < _model(fold, baseline)["metrics"][metric]
        for fold in folds
    )


def _model(fold: Mapping[str, Any], name: str) -> Mapping[str, Any]:
    return next(item for item in fold["models"] if item["name"] == name)


def _fractional_improvement(baseline: float, candidate: float) -> float:
    return (baseline - candidate) / baseline if baseline > 0 else 0.0


def _fractional_regression(baseline: float, candidate: float) -> float:
    if baseline > 0:
        return (candidate - baseline) / baseline
    return 0.0 if candidate <= baseline else math.inf


def _candidate_name(target_name: str) -> str:
    return (
        f"historical-{target_name}-histogram-classifier"
        if target_name in BINARY_TARGETS
        else "historical-minutes-histogram-tree"
    )


def _validate(
    path: Path,
    season_code: str,
    minimum_training_gameweeks: int,
    holdout_start_gameweek: int,
) -> None:
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    if not season_code.strip() or len(season_code) > 16:
        raise TemporalRidgeError(
            "configuration.season-code",
            "season_code must contain 1 to 16 characters.",
        )
    if minimum_training_gameweeks < 1:
        raise TemporalRidgeError(
            "configuration.minimum-training-gameweeks",
            "minimum_training_gameweeks must be at least one.",
        )
    if holdout_start_gameweek < 3 or holdout_start_gameweek > 38:
        raise TemporalRidgeError(
            "configuration.holdout-start-gameweek",
            "holdout_start_gameweek must be between 3 and 38.",
        )


def _base(
    season_code: str,
    minimum_training_gameweeks: int,
    holdout_start_gameweek: int,
    capture: Optional[HistoricalCapture],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": "exploratory-preseason-bridge-not-promoted",
        "isPromoted": False,
        "targets": list(TARGETS),
        "split": "expanding-gameweek-origin-with-locked-late-season-holdout",
        "configuration": {
            "seasonCode": season_code,
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "holdoutStartGameweek": holdout_start_gameweek,
            "binaryCandidate": {
                **TREE_CONFIGURATION,
                "loss": "log_loss",
                "implementation": (
                    "sklearn.ensemble.HistGradientBoostingClassifier"
                ),
            },
            "minutesCandidate": dict(TREE_CONFIGURATION),
            "binaryBaselines": list(BINARY_BASELINES),
            "minutesBaselines": list(MINUTES_BASELINES),
            "features": list(FEATURES),
            "minimumScoreImprovementFraction": (
                MINIMUM_SCORE_IMPROVEMENT
            ),
            "maximumCalibrationRegression": (
                MAXIMUM_CALIBRATION_REGRESSION
            ),
            "maximumPositionBrierRegression": (
                MAXIMUM_POSITION_BRIER_REGRESSION
            ),
            "maximumPositionMaeRegressionFraction": (
                MAXIMUM_POSITION_MAE_REGRESSION
            ),
        },
        "availabilityRule": (
            "Target fixture facts are known before kickoff; every player "
            "feature uses only numerically earlier Gameweek outcomes."
        ),
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
            "The settled archive lacks historical decision-time injury state.",
            "Binary Gameweek targets mean at least one appearance or start "
            "when a player has multiple fixtures.",
            "Within-season evidence does not establish cross-season "
            "calibration for promoted clubs, transfers or tactical changes.",
        ],
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    tasks: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    report: Dict[str, Any] = {
        **base,
        "status": status,
        "reason": reason,
        "tasks": list(tasks),
        "allTargetsSupportedForProvisionalBridge": (
            bool(tasks)
            and all(
                (task.get("recommendation") or {}).get("status")
                == "supported-for-provisional-preseason-bridge"
                for task in tasks
            )
        ),
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "provenance": report["provenance"],
            "targets": [
                {
                    "target": task["target"],
                    "developmentGameweeks": [
                        fold["gameweek"]
                        for fold in task["development"]["folds"]
                    ],
                    "holdoutGameweeks": [
                        fold["gameweek"]
                        for fold in task["lockedHoldout"]["folds"]
                    ],
                }
                for task in tasks
            ],
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate fixed appearance, start, 60-minute and minutes "
            "challengers on the pinned historical season."
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
        report = evaluate_historical_participation(
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
