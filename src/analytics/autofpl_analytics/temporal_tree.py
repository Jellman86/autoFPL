from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

import numpy as np
from sklearn.ensemble import HistGradientBoostingRegressor

from .temporal_ridge import (
    BASELINE_NAMES,
    CONTINUOUS_FEATURES,
    MODEL_NAME as RIDGE_MODEL_NAME,
    POSITIONS,
    Prediction,
    Sample,
    TemporalRidgeError,
    _build_table,
    _open_connection,
    _samples_for_table,
    _sha256,
    _summarise_model,
    _write_report,
    evaluate_temporal_ridge,
)

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "temporal-tabular-v1"
MODEL_NAME = "hist-gradient-boosting"
RANDOM_SEED = 20_260_726
TREE_CONFIGURATION = {
    "loss": "squared_error",
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


def evaluate_temporal_tree(
    database_path: Path,
    season_code: Optional[str] = None,
    minimum_training_gameweeks: int = 3,
) -> Dict[str, Any]:
    """Compare a fixed nonlinear challenger on the ridge evaluator's folds."""
    ridge_report = evaluate_temporal_ridge(
        database_path,
        season_code=season_code,
        minimum_training_gameweeks=minimum_training_gameweeks,
    )
    report = {
        **ridge_report,
        "evaluatorVersion": EVALUATOR_VERSION,
        "configuration": {
            **ridge_report["configuration"],
            "tree": dict(TREE_CONFIGURATION),
        },
    }
    if ridge_report["status"] != "complete":
        report["runIdentitySha256"] = _sha256(
            {
                key: value
                for key, value in report.items()
                if key != "runIdentitySha256"
            }
        )
        return report

    connection = _open_connection(Path(database_path))
    try:
        tree_predictions: List[Prediction] = []
        updated_folds: List[Dict[str, Any]] = []
        for fold in ridge_report["folds"]:
            training_samples: List[Sample] = []
            for training in fold["training"]:
                table = _build_table(
                    Path(database_path),
                    str(fold["seasonCode"]),
                    int(training["gameweek"]),
                )
                training_samples.extend(
                    _samples_for_table(
                        connection,
                        table,
                        int(training["outcomeCaptureId"]),
                    )
                )
            target_table = _build_table(
                Path(database_path),
                str(fold["seasonCode"]),
                int(fold["gameweek"]),
            )
            target_samples = _samples_for_table(
                connection,
                target_table,
                int(fold["target"]["outcomeCaptureId"]),
            )
            predictions, diagnostics = _predict_tree(
                training_samples,
                target_samples,
            )
            tree_predictions.extend(predictions)
            models = [
                *fold["models"],
                _summarise_model(MODEL_NAME, predictions),
            ]
            models.sort(
                key=lambda model: (model["metrics"]["mae"], model["name"])
            )
            updated_folds.append(
                {
                    **fold,
                    "models": models,
                    "treeDiagnostics": diagnostics,
                }
            )
    finally:
        connection.close()

    models = [
        *ridge_report["models"],
        _summarise_model(MODEL_NAME, tree_predictions),
    ]
    models.sort(key=lambda model: (model["metrics"]["mae"], model["name"]))
    report["folds"] = updated_folds
    report["models"] = models
    report["runIdentitySha256"] = _sha256(
        {
            key: value
            for key, value in report.items()
            if key != "runIdentitySha256"
        }
    )
    return report


def _predict_tree(
    training: Sequence[Sample],
    target: Sequence[Sample],
) -> Tuple[List[Prediction], Dict[str, Any]]:
    if not training or not target:
        raise TemporalRidgeError(
            "evaluation.empty-fold",
            "An eligible tree fold has no training or target rows.",
        )
    raw_training_matrix, feature_names = _matrix(training)
    raw_target_matrix, _ = _matrix(target)
    selected_indices = [
        index
        for index in range(raw_training_matrix.shape[1])
        if _can_split(raw_training_matrix[:, index])
    ]
    dropped_features = [
        name
        for index, name in enumerate(feature_names)
        if index not in selected_indices
    ]
    training_matrix = raw_training_matrix[:, selected_indices]
    target_matrix = raw_target_matrix[:, selected_indices]
    targets = np.asarray([sample.actual for sample in training], dtype=float)
    if not selected_indices:
        mean = float(targets.mean())
        predictions = [
            Prediction(
                model=MODEL_NAME,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=mean,
                actual=sample.actual,
            )
            for sample in target
        ]
        return predictions, {
            "implementation": (
                "sklearn.ensemble.HistGradientBoostingRegressor"
            ),
            "libraryVersion": _scikit_learn_version(),
            "trainingRows": len(training),
            "candidateFeatureCount": len(feature_names),
            "modelFeatureCount": 0,
            "completedIterations": 0,
            "droppedUnsplitableFeatures": dropped_features,
            "missingTrainingValues": _missing_counts(
                raw_training_matrix,
                feature_names,
            ),
        }
    estimator = HistGradientBoostingRegressor(
        loss="squared_error",
        learning_rate=0.05,
        max_iter=100,
        max_leaf_nodes=7,
        min_samples_leaf=20,
        l2_regularization=10.0,
        max_bins=63,
        early_stopping=False,
        random_state=RANDOM_SEED,
    )
    estimator.fit(training_matrix, targets)
    predicted = estimator.predict(target_matrix)
    if not np.isfinite(predicted).all():
        raise TemporalRidgeError(
            "evaluation.non-finite-tree-prediction",
            "The tree challenger produced a non-finite prediction.",
        )
    predictions = [
        Prediction(
            model=MODEL_NAME,
            season_code=sample.season_code,
            gameweek=sample.gameweek,
            player_id=sample.player_id,
            position=sample.position,
            predicted=float(value),
            actual=sample.actual,
        )
        for sample, value in zip(target, predicted)
    ]
    return predictions, {
        "implementation": (
            "sklearn.ensemble.HistGradientBoostingRegressor"
        ),
        "libraryVersion": _scikit_learn_version(),
        "trainingRows": len(training),
        "candidateFeatureCount": len(feature_names),
        "modelFeatureCount": len(selected_indices),
        "completedIterations": int(estimator.n_iter_),
        "droppedUnsplitableFeatures": dropped_features,
        "missingTrainingValues": _missing_counts(
            raw_training_matrix,
            feature_names,
        ),
    }


def _matrix(
    samples: Sequence[Sample],
) -> Tuple[np.ndarray, Tuple[str, ...]]:
    feature_names = CONTINUOUS_FEATURES + tuple(
        f"position.{position}" for position in POSITIONS
    )
    rows = [
        [
            math.nan
            if sample.features[name] is None
            else float(sample.features[name])
            for name in CONTINUOUS_FEATURES
        ]
        + [
            float(sample.position == position)
            for position in POSITIONS
        ]
        for sample in samples
    ]
    return np.asarray(rows, dtype=float), feature_names


def _can_split(column: np.ndarray) -> bool:
    finite = column[np.isfinite(column)]
    distinct = np.unique(finite)
    return len(distinct) >= 2 or (
        len(distinct) == 1 and len(finite) < len(column)
    )


def _missing_counts(
    matrix: np.ndarray,
    feature_names: Sequence[str],
) -> Dict[str, int]:
    counts = np.isnan(matrix).sum(axis=0)
    return {
        name: int(count)
        for name, count in zip(feature_names, counts)
        if count
    }


def _scikit_learn_version() -> str:
    import sklearn

    return str(sklearn.__version__)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare fixed leakage-safe ridge and histogram-tree challengers."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season")
    parser.add_argument(
        "--minimum-training-gameweeks",
        type=int,
        default=3,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_temporal_tree(
            options.database,
            season_code=options.season,
            minimum_training_gameweeks=options.minimum_training_gameweeks,
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
