from __future__ import annotations

import argparse
import json
import math
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, DefaultDict, Dict, List, Mapping, Optional, Sequence

import numpy as np

from .baseline import (
    DistributionPrediction,
    EvaluationError,
    ProbabilityPrediction,
    _empirical_distribution,
    _summarise_distribution_model,
    _summarise_probability_model,
)
from .historical_participation_evaluation import (
    _candidate_name,
    _predict_classifier,
)
from .historical_preseason_evaluation import (
    DEFAULT_SEASON,
    FEATURES,
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
    _write_report,
)
from .temporal_tree import _predict_tree

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-joint-scenario-evaluation-v1"
MODEL_NAME = "joint-gameweek-residual-bootstrap"
TREE_MEAN_MODEL = "historical-preseason-histogram-tree"
TREE_DEGENERATE_MODEL = "tree-mean-degenerate"
PLAYER_EMPIRICAL_MODEL = "player-empirical"
POSITION_EMPIRICAL_MODEL = "position-empirical"
APPEARANCE_MODEL = "historical-appearance-histogram-classifier"
QUANTISED_APPEARANCE_MODEL = "joint-scenario-appearance"
PLAYER_APPEARANCE_MODEL = "player-appearance-empirical"
DEFAULT_MINIMUM_TRAINING_GAMEWEEKS = 5
DEFAULT_EVALUATION_START_GAMEWEEK = 31
MINIMUM_CRPS_IMPROVEMENT = 0.01
MAXIMUM_POSITION_CRPS_REGRESSION = 0.05
MINIMUM_POINTS = -50
MAXIMUM_POINTS = 100


@dataclass(frozen=True)
class JointFold:
    player_ids: tuple[int, ...]
    positions: tuple[str, ...]
    source_gameweeks: tuple[int, ...]
    points: np.ndarray
    played: np.ndarray
    mean_predictions: tuple[float, ...]
    appearance_predictions: tuple[float, ...]
    self_donor_assignments: int
    fallback_donor_assignments: int


def evaluate_historical_joint_scenarios(
    database_path: Path,
    season_code: str = DEFAULT_SEASON,
    minimum_training_gameweeks: int = (
        DEFAULT_MINIMUM_TRAINING_GAMEWEEKS
    ),
    evaluation_start_gameweek: int = (
        DEFAULT_EVALUATION_START_GAMEWEEK
    ),
) -> Dict[str, Any]:
    path = Path(database_path)
    _validate(
        path,
        season_code,
        minimum_training_gameweeks,
        evaluation_start_gameweek,
    )
    connection = _open_connection(path)
    try:
        capture = _load_capture(connection, season_code)
        base = _base(
            capture,
            season_code,
            minimum_training_gameweeks,
            evaluation_start_gameweek,
        )
        if capture is None:
            return _finish(
                base,
                "insufficient-data",
                "historical-season-archive-not-found",
                [],
                [],
                [],
                None,
            )
        points_by_gameweek = _build_samples(
            connection,
            capture,
            target_name="total-points",
        )
        appearance_by_gameweek = _build_samples(
            connection,
            capture,
            target_name="appearance",
        )
        _require_aligned_samples(
            points_by_gameweek,
            appearance_by_gameweek,
        )

        distribution_predictions: List[DistributionPrediction] = []
        appearance_predictions: List[ProbabilityPrediction] = []
        folds: List[Dict[str, Any]] = []
        for target_gameweek in sorted(points_by_gameweek):
            if target_gameweek < evaluation_start_gameweek:
                continue
            training_gameweeks = tuple(
                gameweek
                for gameweek in sorted(points_by_gameweek)
                if gameweek < target_gameweek
            )
            if len(training_gameweeks) < minimum_training_gameweeks:
                continue
            training_points = [
                sample
                for gameweek in training_gameweeks
                for sample in points_by_gameweek[gameweek]
            ]
            training_appearance = [
                sample
                for gameweek in training_gameweeks
                for sample in appearance_by_gameweek[gameweek]
            ]
            target_points = points_by_gameweek[target_gameweek]
            target_appearance = appearance_by_gameweek[target_gameweek]
            mean_predictions, mean_diagnostics = _predict_tree(
                training_points,
                target_points,
                continuous_features=FEATURES,
                model_name=TREE_MEAN_MODEL,
            )
            raw_appearance, appearance_diagnostics = _predict_classifier(
                training_appearance,
                target_appearance,
                APPEARANCE_MODEL,
            )
            joint = generate_joint_fold(
                {
                    gameweek: points_by_gameweek[gameweek]
                    for gameweek in training_gameweeks
                },
                {
                    gameweek: appearance_by_gameweek[gameweek]
                    for gameweek in training_gameweeks
                },
                target_points,
                mean_predictions,
                raw_appearance,
            )
            fold_distributions = _distribution_predictions(
                joint,
                training_points,
                target_points,
            )
            fold_appearance = _appearance_predictions(
                joint,
                training_appearance,
                target_appearance,
                raw_appearance,
            )
            distribution_predictions.extend(fold_distributions)
            appearance_predictions.extend(fold_appearance)
            folds.append(
                _fold(
                    target_gameweek,
                    training_gameweeks,
                    joint,
                    fold_distributions,
                    fold_appearance,
                    mean_diagnostics,
                    appearance_diagnostics,
                )
            )

        if not folds:
            return _finish(
                base,
                "insufficient-data",
                "no-eligible-retrospective-folds",
                [],
                [],
                [],
                None,
            )
        distribution_models = [
            _summarise_distribution_model(
                name,
                distribution_predictions,
            )
            for name in (
                MODEL_NAME,
                TREE_DEGENERATE_MODEL,
                PLAYER_EMPIRICAL_MODEL,
                POSITION_EMPIRICAL_MODEL,
            )
        ]
        appearance_models = [
            _summarise_probability_model(name, appearance_predictions)
            for name in (
                QUANTISED_APPEARANCE_MODEL,
                APPEARANCE_MODEL,
                PLAYER_APPEARANCE_MODEL,
            )
        ]
        screen = _screen(distribution_models, folds)
        return _finish(
            base,
            "complete",
            None,
            folds,
            distribution_models,
            appearance_models,
            screen,
        )
    finally:
        connection.close()


def generate_joint_fold(
    training_points_by_gameweek: Mapping[int, Sequence[Sample]],
    training_appearance_by_gameweek: Mapping[int, Sequence[Sample]],
    target_points: Sequence[Sample],
    mean_predictions: Sequence[Prediction],
    appearance_predictions: Sequence[Prediction],
) -> JointFold:
    source_gameweeks = tuple(sorted(training_points_by_gameweek))
    if not source_gameweeks:
        raise TemporalRidgeError(
            "scenario.empty-training-window",
            "A joint scenario fold requires at least one source Gameweek.",
        )
    _require_aligned_samples(
        training_points_by_gameweek,
        training_appearance_by_gameweek,
    )
    target = tuple(sorted(target_points, key=lambda sample: sample.player_id))
    if not target:
        raise TemporalRidgeError(
            "scenario.empty-target",
            "A joint scenario fold requires target players.",
        )
    mean_by_player = _prediction_map(mean_predictions, target)
    appearance_by_player = _prediction_map(
        appearance_predictions,
        target,
    )
    point_rows = {
        gameweek: {
            sample.player_id: sample
            for sample in training_points_by_gameweek[gameweek]
        }
        for gameweek in source_gameweeks
    }
    appearance_rows = {
        gameweek: {
            sample.player_id: sample
            for sample in training_appearance_by_gameweek[gameweek]
        }
        for gameweek in source_gameweeks
    }
    player_histories: DefaultDict[int, List[int]] = defaultdict(list)
    position_histories: DefaultDict[str, List[int]] = defaultdict(list)
    for gameweek in source_gameweeks:
        for sample in point_rows[gameweek].values():
            player_histories[sample.player_id].append(sample.actual)
            position_histories[sample.position].append(sample.actual)

    scenario_points = np.zeros(
        (len(source_gameweeks), len(target)),
        dtype=np.int64,
    )
    scenario_played = np.zeros_like(scenario_points, dtype=np.bool_)
    self_assignments = 0
    fallback_assignments = 0
    for column, player in enumerate(target):
        donors: List[tuple[Sample, Sample, bool]] = []
        for source_gameweek in source_gameweeks:
            point = point_rows[source_gameweek].get(player.player_id)
            appearance = appearance_rows[source_gameweek].get(
                player.player_id
            )
            used_self = point is not None and appearance is not None
            if not used_self:
                point, appearance = _fallback_donor(
                    source_gameweek,
                    player,
                    point_rows[source_gameweek],
                    appearance_rows[source_gameweek],
                )
            assert point is not None
            assert appearance is not None
            donors.append((point, appearance, used_self))
            if used_self:
                self_assignments += 1
            else:
                fallback_assignments += 1

        probability = _probability(
            appearance_by_player[player.player_id].predicted
        )
        played_count = min(
            len(source_gameweeks),
            max(0, int(math.floor(probability * len(donors) + 0.5))),
        )
        ranked = sorted(
            range(len(donors)),
            key=lambda index: (
                -donors[index][1].actual,
                _tie_break(player.player_id, source_gameweeks[index]),
                source_gameweeks[index],
            ),
        )
        played_indices = set(ranked[:played_count])
        base_played_points: Dict[int, int] = {}
        for index in played_indices:
            point, appearance, _ = donors[index]
            if appearance.actual == 1:
                base_played_points[index] = point.actual
                continue
            alternate = _playing_position_donor(
                source_gameweeks[index],
                player,
                point_rows[source_gameweeks[index]],
                appearance_rows[source_gameweeks[index]],
            )
            if alternate is not None:
                base_played_points[index] = alternate.actual
            else:
                position_values = [
                    value
                    for value in position_histories[player.position]
                    if value != 0
                ]
                base_played_points[index] = (
                    _nearest_integer(
                        sum(position_values) / len(position_values)
                    )
                    if position_values
                    else 0
                )

        if played_indices:
            quantised_probability = played_count / len(source_gameweeks)
            conditional_target_mean = (
                mean_by_player[player.player_id].predicted
                / quantised_probability
            )
            base_mean = (
                sum(base_played_points.values())
                / len(base_played_points)
            )
            shift = conditional_target_mean - base_mean
            for index in played_indices:
                scenario_played[index, column] = True
                scenario_points[index, column] = _bounded_points(
                    base_played_points[index] + shift
                )

    return JointFold(
        player_ids=tuple(sample.player_id for sample in target),
        positions=tuple(sample.position for sample in target),
        source_gameweeks=source_gameweeks,
        points=scenario_points,
        played=scenario_played,
        mean_predictions=tuple(
            mean_by_player[sample.player_id].predicted
            for sample in target
        ),
        appearance_predictions=tuple(
            appearance_by_player[sample.player_id].predicted
            for sample in target
        ),
        self_donor_assignments=self_assignments,
        fallback_donor_assignments=fallback_assignments,
    )


def _distribution_predictions(
    joint: JointFold,
    training: Sequence[Sample],
    target: Sequence[Sample],
) -> List[DistributionPrediction]:
    target_by_player = {sample.player_id: sample for sample in target}
    players: DefaultDict[int, List[int]] = defaultdict(list)
    positions: DefaultDict[str, List[int]] = defaultdict(list)
    for sample in training:
        players[sample.player_id].append(sample.actual)
        positions[sample.position].append(sample.actual)
    predictions: List[DistributionPrediction] = []
    for column, player_id in enumerate(joint.player_ids):
        sample = target_by_player[player_id]
        position_values = positions[sample.position]
        player_values = players[player_id] or position_values
        values = {
            MODEL_NAME: joint.points[:, column].tolist(),
            TREE_DEGENERATE_MODEL: [
                _bounded_points(joint.mean_predictions[column])
            ],
            PLAYER_EMPIRICAL_MODEL: player_values,
            POSITION_EMPIRICAL_MODEL: position_values,
        }
        predictions.extend(
            DistributionPrediction(
                model=name,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=player_id,
                position=sample.position,
                distribution=_empirical_distribution(distribution),
                actual=sample.actual,
            )
            for name, distribution in values.items()
        )
    return predictions


def _appearance_predictions(
    joint: JointFold,
    training: Sequence[Sample],
    target: Sequence[Sample],
    raw_predictions: Sequence[Prediction],
) -> List[ProbabilityPrediction]:
    target_by_player = {sample.player_id: sample for sample in target}
    raw_by_player = {item.player_id: item for item in raw_predictions}
    player_history: DefaultDict[int, List[int]] = defaultdict(list)
    position_history: DefaultDict[str, List[int]] = defaultdict(list)
    for sample in training:
        player_history[sample.player_id].append(sample.actual)
        position_history[sample.position].append(sample.actual)
    predictions: List[ProbabilityPrediction] = []
    for column, player_id in enumerate(joint.player_ids):
        sample = target_by_player[player_id]
        history = player_history[player_id]
        fallback = position_history[sample.position]
        empirical = (
            (sum(history) + 1.0) / (len(history) + 2.0)
            if history
            else (sum(fallback) + 1.0) / (len(fallback) + 2.0)
        )
        values = {
            QUANTISED_APPEARANCE_MODEL: float(
                np.mean(joint.played[:, column])
            ),
            APPEARANCE_MODEL: raw_by_player[player_id].predicted,
            PLAYER_APPEARANCE_MODEL: empirical,
        }
        predictions.extend(
            ProbabilityPrediction(
                model=name,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=player_id,
                position=sample.position,
                probability=probability,
                actual=sample.actual,
            )
            for name, probability in values.items()
        )
    return predictions


def _fold(
    target_gameweek: int,
    training_gameweeks: Sequence[int],
    joint: JointFold,
    distributions: Sequence[DistributionPrediction],
    appearance: Sequence[ProbabilityPrediction],
    mean_diagnostics: Mapping[str, Any],
    appearance_diagnostics: Mapping[str, Any],
) -> Dict[str, Any]:
    return {
        "gameweek": target_gameweek,
        "trainingGameweeks": list(training_gameweeks),
        "scenarioCount": len(joint.source_gameweeks),
        "playerCount": len(joint.player_ids),
        "models": [
            _summarise_distribution_model(name, distributions)
            for name in (
                MODEL_NAME,
                TREE_DEGENERATE_MODEL,
                PLAYER_EMPIRICAL_MODEL,
                POSITION_EMPIRICAL_MODEL,
            )
        ],
        "appearanceModels": [
            _summarise_probability_model(name, appearance)
            for name in (
                QUANTISED_APPEARANCE_MODEL,
                APPEARANCE_MODEL,
                PLAYER_APPEARANCE_MODEL,
            )
        ],
        "diagnostics": {
            "selfDonorAssignments": joint.self_donor_assignments,
            "fallbackDonorAssignments": joint.fallback_donor_assignments,
            "nonPlayingNonZeroPointCount": int(
                np.sum((joint.points != 0) & ~joint.played)
            ),
            "tree": dict(mean_diagnostics),
            "appearance": dict(appearance_diagnostics),
        },
    }


def _screen(
    models: Sequence[Mapping[str, Any]],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    by_name = {str(model["name"]): model for model in models}
    candidate = by_name[MODEL_NAME]
    baselines = [
        by_name[PLAYER_EMPIRICAL_MODEL],
        by_name[POSITION_EMPIRICAL_MODEL],
    ]
    baseline = min(
        baselines,
        key=lambda model: (
            model["metrics"]["meanCrps"],
            model["name"],
        ),
    )
    candidate_crps = float(candidate["metrics"]["meanCrps"])
    baseline_crps = float(baseline["metrics"]["meanCrps"])
    improvement = (
        (baseline_crps - candidate_crps) / baseline_crps
        if baseline_crps > 0
        else 0.0
    )
    candidate_positions = candidate["slices"]["position"]
    baseline_positions = baseline["slices"]["position"]
    position_regressions = {
        position: _round(
            (
                candidate_positions[position]["meanCrps"]
                - baseline_positions[position]["meanCrps"]
            )
            / baseline_positions[position]["meanCrps"]
        )
        for position in candidate_positions
        if position in baseline_positions
        and baseline_positions[position]["meanCrps"] > 0
    }
    fold_wins = 0
    for fold in folds:
        fold_models = {
            str(model["name"]): model
            for model in fold["models"]
        }
        if (
            fold_models[MODEL_NAME]["metrics"]["meanCrps"]
            < fold_models[str(baseline["name"])]["metrics"]["meanCrps"]
        ):
            fold_wins += 1
    checks = {
        "minimumAggregateCrpsImprovement": (
            improvement >= MINIMUM_CRPS_IMPROVEMENT
        ),
        "majorityFoldWins": fold_wins > len(folds) / 2,
        "noMaterialPositionCrpsRegression": all(
            regression <= MAXIMUM_POSITION_CRPS_REGRESSION
            for regression in position_regressions.values()
        ),
    }
    return {
        "status": (
            "passes-retrospective-screen"
            if all(checks.values())
            else "does-not-pass-retrospective-screen"
        ),
        "isPromoted": False,
        "mayInfluenceAdvice": False,
        "candidate": MODEL_NAME,
        "selectedBaseline": baseline["name"],
        "aggregateCrpsImprovementFraction": _round(improvement),
        "foldWins": fold_wins,
        "foldCount": len(folds),
        "positionCrpsRegressions": position_regressions,
        "checks": checks,
        "prospectiveRequirement": (
            "A passing retrospective screen may create a frozen current "
            "shadow only. Promotion requires genuinely new 2026/27 folds."
        ),
    }


def _require_aligned_samples(
    points: Mapping[int, Sequence[Sample]],
    appearance: Mapping[int, Sequence[Sample]],
) -> None:
    if set(points) != set(appearance):
        raise TemporalRidgeError(
            "scenario.gameweek-alignment",
            "Point and appearance histories use different Gameweeks.",
        )
    for gameweek in points:
        point_identity = [
            (sample.player_id, sample.position)
            for sample in sorted(
                points[gameweek],
                key=lambda item: item.player_id,
            )
        ]
        appearance_identity = [
            (sample.player_id, sample.position)
            for sample in sorted(
                appearance[gameweek],
                key=lambda item: item.player_id,
            )
        ]
        if point_identity != appearance_identity:
            raise TemporalRidgeError(
                "scenario.player-alignment",
                "Point and appearance histories use different player rows.",
            )


def _prediction_map(
    predictions: Sequence[Prediction],
    target: Sequence[Sample],
) -> Dict[int, Prediction]:
    values = {prediction.player_id: prediction for prediction in predictions}
    if len(values) != len(predictions) or set(values) != {
        sample.player_id for sample in target
    }:
        raise TemporalRidgeError(
            "scenario.prediction-alignment",
            "Scenario predictions do not match the target player cohort.",
        )
    return values


def _fallback_donor(
    source_gameweek: int,
    target: Sample,
    point_rows: Mapping[int, Sample],
    appearance_rows: Mapping[int, Sample],
) -> tuple[Sample, Sample]:
    candidates = sorted(
        player_id
        for player_id, sample in point_rows.items()
        if sample.position == target.position
        and player_id in appearance_rows
    )
    if not candidates:
        raise TemporalRidgeError(
            "scenario.position-donor-unavailable",
            "A source Gameweek has no aligned position donor.",
        )
    selected = candidates[
        _tie_break(target.player_id, source_gameweek) % len(candidates)
    ]
    return point_rows[selected], appearance_rows[selected]


def _playing_position_donor(
    source_gameweek: int,
    target: Sample,
    point_rows: Mapping[int, Sample],
    appearance_rows: Mapping[int, Sample],
) -> Optional[Sample]:
    candidates = sorted(
        player_id
        for player_id, sample in point_rows.items()
        if sample.position == target.position
        and player_id in appearance_rows
        and appearance_rows[player_id].actual == 1
    )
    if not candidates:
        return None
    selected = candidates[
        _tie_break(target.player_id, source_gameweek) % len(candidates)
    ]
    return point_rows[selected]


def _probability(value: float) -> float:
    number = float(value)
    if not math.isfinite(number) or number < 0.0 or number > 1.0:
        raise TemporalRidgeError(
            "scenario.invalid-appearance-probability",
            "Scenario appearance probabilities must be finite and bounded.",
        )
    return number


def _bounded_points(value: float) -> int:
    number = float(value)
    if not math.isfinite(number):
        raise TemporalRidgeError(
            "scenario.non-finite-points",
            "A scenario point value is not finite.",
        )
    return min(
        MAXIMUM_POINTS,
        max(MINIMUM_POINTS, _nearest_integer(number)),
    )


def _nearest_integer(value: float) -> int:
    return (
        int(math.floor(value + 0.5))
        if value >= 0
        else int(math.ceil(value - 0.5))
    )


def _tie_break(player_id: int, gameweek: int) -> int:
    return (
        player_id * 1_103_515_245
        + gameweek * 12_345
        + 20_260_729
    ) % 2_147_483_647


def _validate(
    path: Path,
    season_code: str,
    minimum_training_gameweeks: int,
    evaluation_start_gameweek: int,
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
    if (
        minimum_training_gameweeks < 1
        or minimum_training_gameweeks > 37
    ):
        raise TemporalRidgeError(
            "configuration.minimum-training-gameweeks",
            "minimum_training_gameweeks must be between 1 and 37.",
        )
    if evaluation_start_gameweek < 2 or evaluation_start_gameweek > 38:
        raise TemporalRidgeError(
            "configuration.evaluation-start-gameweek",
            "evaluation_start_gameweek must be between 2 and 38.",
        )


def _base(
    capture: Optional[HistoricalCapture],
    season_code: str,
    minimum_training_gameweeks: int,
    evaluation_start_gameweek: int,
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "status": None,
        "reason": None,
        "seasonCode": season_code,
        "configuration": {
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "evaluationStartGameweek": evaluation_start_gameweek,
            "scenarioSource": (
                "one-complete-cutoff-safe-training-gameweek-per-row"
            ),
            "pointMeanModel": TREE_MEAN_MODEL,
            "appearanceModel": APPEARANCE_MODEL,
            "integerPointBounds": [MINIMUM_POINTS, MAXIMUM_POINTS],
            "minimumCrpsImprovementFraction": (
                MINIMUM_CRPS_IMPROVEMENT
            ),
            "maximumPositionCrpsRegressionFraction": (
                MAXIMUM_POSITION_CRPS_REGRESSION
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
                "playerGameweekRowCount": (
                    capture.player_gameweek_count
                ),
            }
        ),
        "isPromoted": False,
        "mayInfluenceAdvice": False,
        "limitations": [
            "This evaluator was created after the retained 2025/26 holdout "
            "was opened, so its result is a retrospective screen only.",
            "Whole-Gameweek resampling preserves common temporal shocks but "
            "does not yet model fixtures, team scorelines or bonus explicitly.",
            "Historical availability timestamps are unavailable; only "
            "numerically earlier settled outcomes enter each target fold.",
            "Current advice remains distribution-free until a frozen shadow "
            "accumulates genuinely new 2026/27 outcomes.",
        ],
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    distribution_models: Sequence[Mapping[str, Any]],
    appearance_models: Sequence[Mapping[str, Any]],
    screen: Optional[Mapping[str, Any]],
) -> Dict[str, Any]:
    report = {
        **base,
        "status": status,
        "reason": reason,
        "foldCount": len(folds),
        "folds": list(folds),
        "distributionModels": list(distribution_models),
        "appearanceModels": list(appearance_models),
        "retrospectiveScreen": screen,
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "provenance": report["provenance"],
            "gameweeks": [fold["gameweek"] for fold in folds],
            "configuration": report["configuration"],
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate the fixed whole-Gameweek joint residual bootstrap on "
            "historical expanding folds."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=DEFAULT_SEASON)
    parser.add_argument(
        "--minimum-training-gameweeks",
        type=int,
        default=DEFAULT_MINIMUM_TRAINING_GAMEWEEKS,
    )
    parser.add_argument(
        "--evaluation-start-gameweek",
        type=int,
        default=DEFAULT_EVALUATION_START_GAMEWEEK,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_historical_joint_scenarios(
            options.database,
            options.season,
            options.minimum_training_gameweeks,
            options.evaluation_start_gameweek,
        )
        _write_report(report, options.output)
        return 0 if report["status"] == "complete" else 2
    except (TemporalRidgeError, EvaluationError) as exception:
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
