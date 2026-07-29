from __future__ import annotations

import argparse
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, List, Mapping, Optional, Sequence

from .historical_participation_evaluation import (
    _predict_classifier,
    _probability_metrics,
)
from .historical_preseason_evaluation import _load_capture
from .multi_season_evaluation import (
    DEFAULT_EVALUATION_START_GAMEWEEK,
    DEFAULT_SEASONS,
    FEATURES,
    MINIMUM_TRAINING_ORIGINS,
    TREE_MODEL,
    Origin,
    _build_feature_table,
    _load_observations,
    _open_connection,
)
from .temporal_ridge import (
    Prediction,
    Sample,
    TemporalRidgeError,
    _round,
    _sha256,
    _summarise_model,
    _write_report,
)
from .temporal_tree import _predict_tree

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-appearance-hurdle-points-evaluation-v1"
TARGET_SEASON = "2025-26"
EVALUATION_START_GAMEWEEK = DEFAULT_EVALUATION_START_GAMEWEEK
DIRECT_MODEL = TREE_MODEL
APPEARANCE_MODEL = "multi-season-appearance-histogram-classifier"
CONDITIONAL_MODEL = "multi-season-appearance-conditional-points-tree"
HURDLE_MODEL = "multi-season-appearance-hurdle-points"
MINIMUM_MAE_IMPROVEMENT_FRACTION = 0.01
MAXIMUM_RMSE_DELTA = 0.0
MAXIMUM_POSITION_MAE_REGRESSION_FRACTION = 0.05


def evaluate_historical_appearance_hurdle_points(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    connection = _open_connection(path)
    try:
        captures = [
            _load_capture(connection, season)
            for season in DEFAULT_SEASONS
        ]
        _require(
            all(capture is not None for capture in captures),
            "hurdle-points.archive",
            "Every fixed historical archive is required.",
        )
        exact_captures = [
            capture for capture in captures if capture is not None
        ]
        samples_by_origin = _build_feature_table(
            connection,
            exact_captures,
        )
        observations_by_origin = _observations_by_origin(
            connection,
            exact_captures,
        )
        return _evaluate(
            samples_by_origin,
            observations_by_origin,
            exact_captures,
        )
    finally:
        connection.close()


def _evaluate(
    samples_by_origin: Mapping[Origin, Sequence[Sample]],
    observations_by_origin: Mapping[Origin, Mapping[int, Any]],
    captures: Sequence[Any],
) -> Dict[str, Any]:
    ordered_origins = sorted(samples_by_origin)
    _require(
        set(ordered_origins) == set(observations_by_origin),
        "hurdle-points.origin-alignment",
        "Point samples and appearance observations have different origins.",
    )
    target_origins = [
        origin
        for origin in ordered_origins
        if origin.season_code == TARGET_SEASON
        and origin.gameweek >= EVALUATION_START_GAMEWEEK
    ]
    all_point_predictions: List[Prediction] = []
    all_appearance_predictions: List[Prediction] = []
    all_conditional_predictions: List[Prediction] = []
    folds = []
    for target_origin in target_origins:
        training_origins = [
            origin for origin in ordered_origins if origin < target_origin
        ]
        if len(training_origins) < MINIMUM_TRAINING_ORIGINS:
            continue
        training = [
            sample
            for origin in training_origins
            for sample in samples_by_origin[origin]
        ]
        target = list(samples_by_origin[target_origin])
        training_appearance = [
            _appearance_sample(
                sample,
                observations_by_origin[origin],
            )
            for origin in training_origins
            for sample in samples_by_origin[origin]
        ]
        target_appearance = [
            _appearance_sample(
                sample,
                observations_by_origin[target_origin],
            )
            for sample in target
        ]
        conditional_training = [
            sample
            for origin in training_origins
            for sample in samples_by_origin[origin]
            if _appeared(
                sample,
                observations_by_origin[origin],
            )
        ]
        _require(
            conditional_training,
            "hurdle-points.conditional-training",
            "No appearance-positive point rows are available for a fold.",
        )

        direct, direct_diagnostics = _predict_tree(
            training,
            target,
            continuous_features=FEATURES,
            model_name=DIRECT_MODEL,
        )
        appearance, appearance_diagnostics = _predict_classifier(
            training_appearance,
            target_appearance,
            APPEARANCE_MODEL,
            continuous_features=FEATURES,
        )
        conditional, conditional_diagnostics = _predict_tree(
            conditional_training,
            target,
            continuous_features=FEATURES,
            model_name=CONDITIONAL_MODEL,
        )
        hurdle = _combine_hurdle(
            target,
            appearance,
            conditional,
        )
        point_predictions = [*direct, *hurdle]
        all_point_predictions.extend(point_predictions)
        all_appearance_predictions.extend(appearance)
        conditional_appeared = [
            prediction
            for prediction, binary in zip(
                conditional,
                target_appearance,
            )
            if binary.actual == 1
        ]
        all_conditional_predictions.extend(conditional_appeared)
        models = [
            _summarise_model(name, point_predictions)
            for name in (DIRECT_MODEL, HURDLE_MODEL)
        ]
        models.sort(
            key=lambda row: (row["metrics"]["mae"], row["name"])
        )
        folds.append(
            {
                "seasonCode": target_origin.season_code,
                "gameweek": target_origin.gameweek,
                "trainingOriginCount": len(training_origins),
                "trainingRows": len(training),
                "conditionalTrainingRows": len(
                    conditional_training
                ),
                "targetPlayerCount": len(target),
                "targetAppearanceCount": sum(
                    sample.actual for sample in target_appearance
                ),
                "models": models,
                "appearanceMetrics": _probability_metrics(
                    appearance
                ),
                "conditionalPointMetricsOnAppearedPlayers": (
                    _summarise_model(
                        CONDITIONAL_MODEL,
                        conditional_appeared,
                    )["metrics"]
                ),
                "diagnostics": {
                    DIRECT_MODEL: direct_diagnostics,
                    APPEARANCE_MODEL: appearance_diagnostics,
                    CONDITIONAL_MODEL: conditional_diagnostics,
                },
            }
        )

    _require(
        folds,
        "hurdle-points.folds",
        "No fixed hurdle-point evaluation folds are available.",
    )
    models = [
        _summarise_model(name, all_point_predictions)
        for name in (DIRECT_MODEL, HURDLE_MODEL)
    ]
    models.sort(key=lambda row: (row["metrics"]["mae"], row["name"]))
    by_name = {str(row["name"]): row for row in models}
    incumbent = by_name[DIRECT_MODEL]
    challenger = by_name[HURDLE_MODEL]
    incumbent_metrics = incumbent["metrics"]
    challenger_metrics = challenger["metrics"]
    incumbent_mae = float(incumbent_metrics["mae"])
    challenger_mae = float(challenger_metrics["mae"])
    mae_improvement = (
        (incumbent_mae - challenger_mae) / incumbent_mae
        if incumbent_mae > 0.0
        else 0.0
    )
    rmse_delta = (
        float(challenger_metrics["rmse"])
        - float(incumbent_metrics["rmse"])
    )
    fold_wins = sum(
        _fold_model(fold, HURDLE_MODEL)["metrics"]["mae"]
        < _fold_model(fold, DIRECT_MODEL)["metrics"]["mae"]
        for fold in folds
    )
    incumbent_positions = incumbent["slices"]["position"]
    challenger_positions = challenger["slices"]["position"]
    _require(
        set(incumbent_positions) == set(challenger_positions),
        "hurdle-points.position-slices",
        "Direct and hurdle models have different position slices.",
    )
    position_rows = []
    position_regressions = []
    for position in sorted(incumbent_positions):
        incumbent_position_mae = float(
            incumbent_positions[position]["mae"]
        )
        challenger_position_mae = float(
            challenger_positions[position]["mae"]
        )
        regression = _fractional_regression(
            incumbent_position_mae,
            challenger_position_mae,
        )
        position_regressions.append(regression)
        position_rows.append(
            {
                "position": position,
                "incumbentMae": _round(incumbent_position_mae),
                "challengerMae": _round(challenger_position_mae),
                "maeRegressionFraction": _round(regression),
            }
        )
    gates = {
        "aggregateMaeImprovement": (
            mae_improvement >= MINIMUM_MAE_IMPROVEMENT_FRACTION
        ),
        "aggregateRmseNonRegression": rmse_delta <= MAXIMUM_RMSE_DELTA,
        "strictMajorityFoldWins": fold_wins > len(folds) / 2,
        "positionMaeStability": (
            max(position_regressions)
            <= MAXIMUM_POSITION_MAE_REGRESSION_FRACTION
        ),
    }
    retained = all(gates.values())
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "status": "complete",
        "researchStatus": "reused-holdout-screen-not-promoted",
        "isPromoted": False,
        "influencesAdvice": False,
        "target": "historical-player-gameweek-total-points",
        "seasonCodes": list(DEFAULT_SEASONS),
        "targetSeasonCode": TARGET_SEASON,
        "evaluationStartGameweek": EVALUATION_START_GAMEWEEK,
        "factorization": (
            "appearance-probability-times-expected-points-"
            "conditional-on-appearance"
        ),
        "models": models,
        "appearanceComponent": {
            "model": APPEARANCE_MODEL,
            "metrics": _probability_metrics(
                all_appearance_predictions
            ),
        },
        "conditionalPointComponent": {
            "model": CONDITIONAL_MODEL,
            "evaluationCohort": "target-players-with-minutes-greater-than-zero",
            "metrics": _summarise_model(
                CONDITIONAL_MODEL,
                all_conditional_predictions,
            )["metrics"],
        },
        "comparison": {
            "maeImprovementFraction": _round(mae_improvement),
            "rmseDelta": _round(rmse_delta),
            "foldWins": fold_wins,
            "foldCount": len(folds),
            "positionSlices": position_rows,
            "folds": folds,
        },
        "retentionRule": {
            "minimumMaeImprovementFraction": (
                MINIMUM_MAE_IMPROVEMENT_FRACTION
            ),
            "maximumRmseDelta": MAXIMUM_RMSE_DELTA,
            "strictMajorityFoldWins": True,
            "maximumPositionMaeRegressionFraction": (
                MAXIMUM_POSITION_MAE_REGRESSION_FRACTION
            ),
        },
        "gates": gates,
        "decision": (
            "retain-appearance-hurdle-prospective-shadow"
            if retained
            else "do-not-retain-appearance-hurdle"
        ),
        "captures": [
            {
                "captureId": capture.capture_id,
                "seasonCode": capture.season_code,
                "sourceRevision": capture.source_revision,
                "availableAtUtc": capture.available_at_utc,
                "playersSha256": capture.players_sha256,
                "gameweeksSha256": capture.gameweeks_sha256,
                "playerCount": capture.player_count,
                "playerGameweekCount": capture.player_gameweek_count,
                "stableCodeCount": capture.stable_code_count,
            }
            for capture in captures
        ],
        "limitations": [
            (
                "The 2025/26 target folds were opened by earlier experiments. "
                "This screen can reject or retain a prospective shadow but "
                "cannot promote it."
            ),
            (
                "The archive has no historical decision-time injury or "
                "specialist predicted-lineup state."
            ),
            (
                "The factorization models Gameweek appearance rather than "
                "fixture-level minutes and event rates."
            ),
            (
                "A retained point-mean factorization would still need "
                "distributional and opening-squad outcome evaluation."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "captures": artifact["captures"],
            "seasonCodes": artifact["seasonCodes"],
            "targetSeasonCode": artifact["targetSeasonCode"],
            "evaluationStartGameweek": (
                artifact["evaluationStartGameweek"]
            ),
            "factorization": artifact["factorization"],
            "retentionRule": artifact["retentionRule"],
            "targetFolds": [
                {
                    "gameweek": fold["gameweek"],
                    "targetPlayerCount": fold[
                        "targetPlayerCount"
                    ],
                }
                for fold in folds
            ],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _observations_by_origin(
    connection: Any,
    captures: Sequence[Any],
) -> Dict[Origin, Dict[int, Any]]:
    result: DefaultDict[Origin, Dict[int, Any]] = defaultdict(dict)
    for season_index, capture in enumerate(captures):
        for observation in _load_observations(
            connection,
            capture,
            season_index,
        ):
            origin = Origin(
                season_index,
                observation.gameweek,
                capture.season_code,
            )
            _require(
                observation.player_code not in result[origin],
                "hurdle-points.duplicate-player",
                "An origin contains a duplicate player observation.",
            )
            result[origin][observation.player_code] = observation
    return dict(result)


def _appearance_sample(
    sample: Sample,
    observations: Mapping[int, Any],
) -> Sample:
    observation = observations.get(sample.player_id)
    _require(
        observation is not None
        and int(observation.total_points) == int(sample.actual)
        and str(observation.position) == str(sample.position),
        "hurdle-points.player-alignment",
        "A point sample does not match its appearance observation.",
    )
    return Sample(
        season_code=sample.season_code,
        gameweek=sample.gameweek,
        player_id=sample.player_id,
        position=sample.position,
        features=sample.features,
        actual=int(observation.minutes > 0),
    )


def _appeared(
    sample: Sample,
    observations: Mapping[int, Any],
) -> bool:
    return _appearance_sample(sample, observations).actual == 1


def _combine_hurdle(
    target: Sequence[Sample],
    appearance: Sequence[Prediction],
    conditional: Sequence[Prediction],
) -> List[Prediction]:
    _require(
        len(target) == len(appearance) == len(conditional),
        "hurdle-points.prediction-count",
        "The hurdle components do not cover the same target players.",
    )
    combined = []
    for sample, probability, conditional_mean in zip(
        target,
        appearance,
        conditional,
    ):
        expected_identity = (
            sample.season_code,
            sample.gameweek,
            sample.player_id,
            sample.position,
        )
        _require(
            expected_identity
            == (
                probability.season_code,
                probability.gameweek,
                probability.player_id,
                probability.position,
            )
            == (
                conditional_mean.season_code,
                conditional_mean.gameweek,
                conditional_mean.player_id,
                conditional_mean.position,
            ),
            "hurdle-points.prediction-alignment",
            "The hurdle component predictions are not player-aligned.",
        )
        value = float(probability.predicted) * float(
            conditional_mean.predicted
        )
        _require(
            math.isfinite(value),
            "hurdle-points.non-finite",
            "The hurdle factorization produced a non-finite point mean.",
        )
        combined.append(
            Prediction(
                model=HURDLE_MODEL,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=value,
                actual=sample.actual,
            )
        )
    return combined


def _fold_model(
    fold: Mapping[str, Any],
    model_name: str,
) -> Mapping[str, Any]:
    matches = [
        row for row in fold["models"] if row["name"] == model_name
    ]
    _require(
        len(matches) == 1,
        "hurdle-points.fold-model",
        "A fold does not contain the fixed model exactly once.",
    )
    return matches[0]


def _fractional_regression(
    incumbent: float,
    challenger: float,
) -> float:
    if incumbent > 0.0:
        return (challenger - incumbent) / incumbent
    return 0.0 if challenger <= incumbent else math.inf


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare the direct two-season point tree with a fixed "
            "appearance-hurdle conditional-points factorization."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = evaluate_historical_appearance_hurdle_points(
            options.database
        )
        _write_report(artifact, options.output)
        return 0
    except TemporalRidgeError as exception:
        sys.stderr.write(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "error",
                    "errorCode": exception.code,
                    "message": str(exception),
                },
                sort_keys=True,
            )
            + "\n"
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
