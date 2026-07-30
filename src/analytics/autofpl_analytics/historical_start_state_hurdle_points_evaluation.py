from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

from .historical_appearance_hurdle_points_evaluation import (
    APPEARANCE_MODEL,
    CONDITIONAL_MODEL,
    EVALUATION_START_GAMEWEEK,
    HURDLE_MODEL,
    TARGET_SEASON,
    _appearance_sample,
    _fractional_regression,
    _observations_by_origin,
)
from .historical_participation_evaluation import (
    _predict_classifier,
    _probability_metrics,
)
from .historical_preseason_evaluation import _load_capture
from .multi_season_evaluation import (
    DEFAULT_SEASONS,
    FEATURES,
    MINIMUM_TRAINING_ORIGINS,
    Origin,
    _build_feature_table,
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
EVALUATOR_VERSION = "historical-start-state-hurdle-points-evaluation-v1"
START_MODEL = "multi-season-start-histogram-classifier"
COHERENT_START_MODEL = "multi-season-start-clipped-to-appearance"
STARTER_CONDITIONAL_MODEL = "multi-season-starter-conditional-points-tree"
SUBSTITUTE_CONDITIONAL_MODEL = (
    "multi-season-substitute-conditional-points-tree"
)
START_STATE_MODEL = "multi-season-start-state-hurdle-points"

INCUMBENT_DATA_IDENTITY = (
    "1ef395d722844b1833fb059d606606e0bf1f1d42732377474d671f1a3f17b16e"
)
INCUMBENT_RUN_IDENTITY = (
    "852b3728150cba9ad3bdb46ba24d66e9ac77262b2cf4dc9aae38f0df5cdc7e71"
)
MINIMUM_MAE_IMPROVEMENT_FRACTION = 0.01
MAXIMUM_RMSE_DELTA = 0.0
MAXIMUM_POSITION_MAE_REGRESSION_FRACTION = 0.05


def evaluate_historical_start_state_hurdle_points(
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
            "start-state.archive",
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
        "start-state.origin-alignment",
        "Point samples and participation observations have different origins.",
    )
    target_origins = [
        origin
        for origin in ordered_origins
        if origin.season_code == TARGET_SEASON
        and origin.gameweek >= EVALUATION_START_GAMEWEEK
    ]
    all_points: List[Prediction] = []
    all_appearance: List[Prediction] = []
    all_raw_start: List[Prediction] = []
    all_coherent_start: List[Prediction] = []
    all_starter_conditional: List[Prediction] = []
    all_substitute_conditional: List[Prediction] = []
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
        appearance_training = [
            _appearance_sample(sample, observations_by_origin[origin])
            for origin in training_origins
            for sample in samples_by_origin[origin]
        ]
        appearance_target = [
            _appearance_sample(
                sample,
                observations_by_origin[target_origin],
            )
            for sample in target
        ]
        start_training = [
            _start_sample(sample, observations_by_origin[origin])
            for origin in training_origins
            for sample in samples_by_origin[origin]
        ]
        start_target = [
            _start_sample(
                sample,
                observations_by_origin[target_origin],
            )
            for sample in target
        ]
        appeared_training = [
            sample
            for origin in training_origins
            for sample in samples_by_origin[origin]
            if _appeared(sample, observations_by_origin[origin])
        ]
        starter_training = [
            sample
            for origin in training_origins
            for sample in samples_by_origin[origin]
            if _started(sample, observations_by_origin[origin])
        ]
        substitute_training = [
            sample
            for origin in training_origins
            for sample in samples_by_origin[origin]
            if _substitute_appeared(
                sample,
                observations_by_origin[origin],
            )
        ]
        _require(
            appeared_training
            and starter_training
            and substitute_training,
            "start-state.training-cohorts",
            "A fixed conditional point training cohort is empty.",
        )

        appearance, appearance_diagnostics = _predict_classifier(
            appearance_training,
            appearance_target,
            APPEARANCE_MODEL,
            continuous_features=FEATURES,
        )
        raw_start, start_diagnostics = _predict_classifier(
            start_training,
            start_target,
            START_MODEL,
            continuous_features=FEATURES,
        )
        coherent_start, coherence = _coherent_start_predictions(
            appearance,
            raw_start,
        )
        incumbent_conditional, incumbent_diagnostics = _predict_tree(
            appeared_training,
            target,
            continuous_features=FEATURES,
            model_name=CONDITIONAL_MODEL,
        )
        starter_conditional, starter_diagnostics = _predict_tree(
            starter_training,
            target,
            continuous_features=FEATURES,
            model_name=STARTER_CONDITIONAL_MODEL,
        )
        substitute_conditional, substitute_diagnostics = _predict_tree(
            substitute_training,
            target,
            continuous_features=FEATURES,
            model_name=SUBSTITUTE_CONDITIONAL_MODEL,
        )
        incumbent = _combine_appearance_hurdle(
            target,
            appearance,
            incumbent_conditional,
        )
        challenger = _combine_start_state_hurdle(
            target,
            appearance,
            coherent_start,
            starter_conditional,
            substitute_conditional,
        )
        point_predictions = [*incumbent, *challenger]
        all_points.extend(point_predictions)
        all_appearance.extend(appearance)
        all_raw_start.extend(raw_start)
        all_coherent_start.extend(coherent_start)
        actual_observations = observations_by_origin[target_origin]
        starter_actual_predictions = [
            prediction
            for prediction, sample in zip(starter_conditional, target)
            if _started(sample, actual_observations)
        ]
        substitute_actual_predictions = [
            prediction
            for prediction, sample in zip(substitute_conditional, target)
            if _substitute_appeared(sample, actual_observations)
        ]
        all_starter_conditional.extend(starter_actual_predictions)
        all_substitute_conditional.extend(
            substitute_actual_predictions
        )
        models = [
            _summarise_model(name, point_predictions)
            for name in (HURDLE_MODEL, START_STATE_MODEL)
        ]
        models.sort(key=lambda row: (row["metrics"]["mae"], row["name"]))
        folds.append(
            {
                "seasonCode": target_origin.season_code,
                "gameweek": target_origin.gameweek,
                "trainingOriginCount": len(training_origins),
                "trainingRows": len(training),
                "appearedTrainingRows": len(appeared_training),
                "starterTrainingRows": len(starter_training),
                "substituteTrainingRows": len(substitute_training),
                "targetPlayerCount": len(target),
                "targetAppearanceCount": sum(
                    sample.actual for sample in appearance_target
                ),
                "targetStartCount": sum(
                    sample.actual for sample in start_target
                ),
                "models": models,
                "appearanceMetrics": _probability_metrics(appearance),
                "rawStartMetrics": _probability_metrics(raw_start),
                "coherentStartMetrics": _probability_metrics(
                    coherent_start
                ),
                "startCoherence": coherence,
                "starterConditionalPointMetrics": _summarise_model(
                    STARTER_CONDITIONAL_MODEL,
                    starter_actual_predictions,
                )["metrics"],
                "substituteConditionalPointMetrics": _summarise_model(
                    SUBSTITUTE_CONDITIONAL_MODEL,
                    substitute_actual_predictions,
                )["metrics"],
                "diagnostics": {
                    APPEARANCE_MODEL: appearance_diagnostics,
                    START_MODEL: start_diagnostics,
                    CONDITIONAL_MODEL: incumbent_diagnostics,
                    STARTER_CONDITIONAL_MODEL: starter_diagnostics,
                    SUBSTITUTE_CONDITIONAL_MODEL: (
                        substitute_diagnostics
                    ),
                },
            }
        )

    _require(
        folds,
        "start-state.folds",
        "No fixed start-state point evaluation folds are available.",
    )
    models = [
        _summarise_model(name, all_points)
        for name in (HURDLE_MODEL, START_STATE_MODEL)
    ]
    models.sort(key=lambda row: (row["metrics"]["mae"], row["name"]))
    by_name = {str(row["name"]): row for row in models}
    incumbent = by_name[HURDLE_MODEL]
    challenger = by_name[START_STATE_MODEL]
    incumbent_mae = float(incumbent["metrics"]["mae"])
    challenger_mae = float(challenger["metrics"]["mae"])
    mae_improvement = (
        (incumbent_mae - challenger_mae) / incumbent_mae
        if incumbent_mae > 0.0
        else 0.0
    )
    rmse_delta = (
        float(challenger["metrics"]["rmse"])
        - float(incumbent["metrics"]["rmse"])
    )
    fold_wins = sum(
        _fold_metric(fold, START_STATE_MODEL, "mae")
        < _fold_metric(fold, HURDLE_MODEL, "mae")
        for fold in folds
    )
    position_rows, position_regressions = _position_comparison(
        incumbent,
        challenger,
    )
    raw_start_metrics = _probability_metrics(all_raw_start)
    coherent_start_metrics = _probability_metrics(all_coherent_start)
    start_brier_delta = (
        float(coherent_start_metrics["brierScore"])
        - float(raw_start_metrics["brierScore"])
    )
    start_log_loss_delta = (
        float(coherent_start_metrics["logLoss"])
        - float(raw_start_metrics["logLoss"])
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
        "coherentStartBrierNonRegression": start_brier_delta <= 0.0,
        "coherentStartLogLossNonRegression": (
            start_log_loss_delta <= 0.0
        ),
    }
    retained = all(gates.values())
    captures_document = [
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
    ]
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
            "start-probability-times-starter-conditional-points-plus-"
            "appearance-minus-start-probability-times-substitute-"
            "conditional-points"
        ),
        "incumbentSource": {
            "evaluatorVersion": (
                "historical-appearance-hurdle-points-evaluation-v1"
            ),
            "dataIdentitySha256": INCUMBENT_DATA_IDENTITY,
            "runIdentitySha256": INCUMBENT_RUN_IDENTITY,
        },
        "models": models,
        "appearanceComponent": {
            "model": APPEARANCE_MODEL,
            "metrics": _probability_metrics(all_appearance),
        },
        "startComponent": {
            "rawModel": START_MODEL,
            "coherenceRule": "minimum-of-start-and-appearance-probability",
            "rawMetrics": raw_start_metrics,
            "coherentMetrics": coherent_start_metrics,
            "brierDelta": _round(start_brier_delta),
            "logLossDelta": _round(start_log_loss_delta),
            "adjustmentCount": sum(
                int(fold["startCoherence"]["adjustmentCount"])
                for fold in folds
            ),
            "maximumAdjustment": max(
                float(fold["startCoherence"]["maximumAdjustment"])
                for fold in folds
            ),
        },
        "conditionalPointComponents": {
            "starter": {
                "model": STARTER_CONDITIONAL_MODEL,
                "evaluationCohort": "target-players-with-starts-greater-than-zero",
                "metrics": _summarise_model(
                    STARTER_CONDITIONAL_MODEL,
                    all_starter_conditional,
                )["metrics"],
            },
            "substitute": {
                "model": SUBSTITUTE_CONDITIONAL_MODEL,
                "evaluationCohort": (
                    "target-players-with-minutes-greater-than-zero-and-"
                    "starts-equal-zero"
                ),
                "metrics": _summarise_model(
                    SUBSTITUTE_CONDITIONAL_MODEL,
                    all_substitute_conditional,
                )["metrics"],
            },
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
            "coherentStartProperScoreNonRegression": True,
        },
        "gates": gates,
        "decision": (
            "retain-start-state-hurdle-for-distribution-policy-screen"
            if retained
            else "do-not-retain-start-state-hurdle"
        ),
        "captures": captures_document,
        "limitations": [
            (
                "The 2025/26 target folds were opened by earlier experiments. "
                "This screen can reject or retain a prospective shadow but "
                "cannot promote it."
            ),
            (
                "The archive has no historical decision-time specialist "
                "predicted-lineup or injury state."
            ),
            (
                "A player with at least one start in a double Gameweek is "
                "classified in the starter state for the aggregated row."
            ),
            (
                "A retained point mean still requires full distribution and "
                "opening-squad policy evaluation before serving."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "captures": captures_document,
            "seasonCodes": artifact["seasonCodes"],
            "targetSeasonCode": artifact["targetSeasonCode"],
            "evaluationStartGameweek": artifact[
                "evaluationStartGameweek"
            ],
            "factorization": artifact["factorization"],
            "incumbentSource": artifact["incumbentSource"],
            "retentionRule": artifact["retentionRule"],
            "targetFolds": [
                {
                    "gameweek": fold["gameweek"],
                    "targetPlayerCount": fold["targetPlayerCount"],
                    "targetStartCount": fold["targetStartCount"],
                }
                for fold in folds
            ],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _start_sample(
    sample: Sample,
    observations: Mapping[int, Any],
) -> Sample:
    observation = _aligned_observation(sample, observations)
    return Sample(
        season_code=sample.season_code,
        gameweek=sample.gameweek,
        player_id=sample.player_id,
        position=sample.position,
        features=sample.features,
        actual=int(observation.starts > 0),
    )


def _aligned_observation(
    sample: Sample,
    observations: Mapping[int, Any],
) -> Any:
    observation = observations.get(sample.player_id)
    _require(
        observation is not None
        and int(observation.total_points) == int(sample.actual)
        and str(observation.position) == str(sample.position),
        "start-state.player-alignment",
        "A point sample does not match its participation observation.",
    )
    _require(
        int(observation.starts) >= 0
        and int(observation.minutes) >= 0
        and not (
            int(observation.starts) > 0
            and int(observation.minutes) <= 0
        ),
        "start-state.incoherent-observation",
        "A historical start requires positive minutes.",
    )
    return observation


def _appeared(
    sample: Sample,
    observations: Mapping[int, Any],
) -> bool:
    return int(_aligned_observation(sample, observations).minutes) > 0


def _started(
    sample: Sample,
    observations: Mapping[int, Any],
) -> bool:
    return int(_aligned_observation(sample, observations).starts) > 0


def _substitute_appeared(
    sample: Sample,
    observations: Mapping[int, Any],
) -> bool:
    observation = _aligned_observation(sample, observations)
    return int(observation.minutes) > 0 and int(observation.starts) == 0


def _coherent_start_predictions(
    appearance: Sequence[Prediction],
    raw_start: Sequence[Prediction],
) -> Tuple[List[Prediction], Dict[str, Any]]:
    _require(
        len(appearance) == len(raw_start),
        "start-state.probability-count",
        "Appearance and start predictions cover different players.",
    )
    predictions = []
    adjustments = []
    for appearance_prediction, start_prediction in zip(
        appearance,
        raw_start,
    ):
        _require_prediction_alignment(
            appearance_prediction,
            start_prediction,
        )
        _require(
            int(start_prediction.actual)
            <= int(appearance_prediction.actual),
            "start-state.target-incoherence",
            "A start outcome exists without an appearance outcome.",
        )
        appearance_probability = float(
            appearance_prediction.predicted
        )
        raw_probability = float(start_prediction.predicted)
        _require(
            0.0 <= appearance_probability <= 1.0
            and 0.0 <= raw_probability <= 1.0,
            "start-state.invalid-probability",
            "Participation probabilities must be between zero and one.",
        )
        probability = min(appearance_probability, raw_probability)
        adjustments.append(raw_probability - probability)
        predictions.append(
            Prediction(
                model=COHERENT_START_MODEL,
                season_code=start_prediction.season_code,
                gameweek=start_prediction.gameweek,
                player_id=start_prediction.player_id,
                position=start_prediction.position,
                predicted=probability,
                actual=start_prediction.actual,
            )
        )
    return predictions, {
        "rule": "minimum-of-start-and-appearance-probability",
        "adjustmentCount": sum(value > 0.0 for value in adjustments),
        "maximumAdjustment": _round(max(adjustments, default=0.0)),
    }


def _combine_appearance_hurdle(
    target: Sequence[Sample],
    appearance: Sequence[Prediction],
    conditional: Sequence[Prediction],
) -> List[Prediction]:
    _require(
        len(target) == len(appearance) == len(conditional),
        "start-state.incumbent-count",
        "The incumbent hurdle components cover different players.",
    )
    result = []
    for sample, probability, mean in zip(
        target,
        appearance,
        conditional,
    ):
        _require_sample_prediction_alignment(sample, probability, mean)
        value = float(probability.predicted) * float(mean.predicted)
        _require(
            math.isfinite(value),
            "start-state.non-finite-incumbent",
            "The incumbent hurdle produced a non-finite point mean.",
        )
        result.append(
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
    return result


def _combine_start_state_hurdle(
    target: Sequence[Sample],
    appearance: Sequence[Prediction],
    start: Sequence[Prediction],
    starter_conditional: Sequence[Prediction],
    substitute_conditional: Sequence[Prediction],
) -> List[Prediction]:
    _require(
        len(target)
        == len(appearance)
        == len(start)
        == len(starter_conditional)
        == len(substitute_conditional),
        "start-state.challenger-count",
        "The start-state hurdle components cover different players.",
    )
    result = []
    for sample, appearance_prediction, start_prediction, starter, substitute in zip(
        target,
        appearance,
        start,
        starter_conditional,
        substitute_conditional,
    ):
        _require_sample_prediction_alignment(
            sample,
            appearance_prediction,
            start_prediction,
            starter,
            substitute,
        )
        appearance_probability = float(
            appearance_prediction.predicted
        )
        start_probability = float(start_prediction.predicted)
        _require(
            0.0 <= start_probability <= appearance_probability <= 1.0,
            "start-state.incoherent-probability",
            "Start probability must not exceed appearance probability.",
        )
        substitute_probability = (
            appearance_probability - start_probability
        )
        value = (
            start_probability * float(starter.predicted)
            + substitute_probability * float(substitute.predicted)
        )
        _require(
            math.isfinite(value),
            "start-state.non-finite-challenger",
            "The start-state hurdle produced a non-finite point mean.",
        )
        result.append(
            Prediction(
                model=START_STATE_MODEL,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=value,
                actual=sample.actual,
            )
        )
    return result


def _require_sample_prediction_alignment(
    sample: Sample,
    *predictions: Prediction,
) -> None:
    identity = (
        sample.season_code,
        sample.gameweek,
        sample.player_id,
        sample.position,
    )
    _require(
        all(
            identity
            == (
                prediction.season_code,
                prediction.gameweek,
                prediction.player_id,
                prediction.position,
            )
            for prediction in predictions
        ),
        "start-state.prediction-alignment",
        "The start-state component predictions are not player-aligned.",
    )


def _require_prediction_alignment(
    left: Prediction,
    right: Prediction,
) -> None:
    _require(
        (
            left.season_code,
            left.gameweek,
            left.player_id,
            left.position,
        )
        == (
            right.season_code,
            right.gameweek,
            right.player_id,
            right.position,
        ),
        "start-state.prediction-alignment",
        "The participation component predictions are not player-aligned.",
    )


def _fold_metric(
    fold: Mapping[str, Any],
    model_name: str,
    metric: str,
) -> float:
    matches = [
        row for row in fold["models"] if row["name"] == model_name
    ]
    _require(
        len(matches) == 1,
        "start-state.fold-model",
        "A fold does not contain the fixed model exactly once.",
    )
    return float(matches[0]["metrics"][metric])


def _position_comparison(
    incumbent: Mapping[str, Any],
    challenger: Mapping[str, Any],
) -> Tuple[List[Dict[str, Any]], List[float]]:
    incumbent_positions = incumbent["slices"]["position"]
    challenger_positions = challenger["slices"]["position"]
    _require(
        set(incumbent_positions) == set(challenger_positions),
        "start-state.position-slices",
        "The fixed models have different position slices.",
    )
    rows = []
    regressions = []
    for position in sorted(incumbent_positions):
        incumbent_mae = float(incumbent_positions[position]["mae"])
        challenger_mae = float(challenger_positions[position]["mae"])
        regression = _fractional_regression(
            incumbent_mae,
            challenger_mae,
        )
        regressions.append(regression)
        rows.append(
            {
                "position": position,
                "incumbentMae": _round(incumbent_mae),
                "challengerMae": _round(challenger_mae),
                "maeRegressionFraction": _round(regression),
            }
        )
    return rows, regressions


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare the retained appearance hurdle with a fixed coherent "
            "start/substitute/zero-minutes point mixture."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = evaluate_historical_start_state_hurdle_points(
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
