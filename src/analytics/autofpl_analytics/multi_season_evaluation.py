from __future__ import annotations

import argparse
import json
import math
import sqlite3
import sys
from collections import defaultdict
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, DefaultDict, Dict, List, Mapping, Optional, Sequence, Tuple

from .historical_preseason_evaluation import HistoricalCapture, _load_capture
from .temporal_ridge import (
    Prediction,
    Sample,
    TemporalRidgeError,
    _predict_fold,
    _round,
    _sha256,
    _summarise_model,
    _write_report,
)
from .temporal_tree import _predict_tree

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "multi-season-expanding-origin-v1"
DEFAULT_SEASONS = ("2024-25", "2025-26")
DEFAULT_EVALUATION_START_GAMEWEEK = 31
MINIMUM_TRAINING_ORIGINS = 5
RIDGE_PENALTY = 10.0
BASELINE_MODEL = "multi-season-player-rolling3-points"
CURRENT_RIDGE_MODEL = "current-season-only-ridge"
CURRENT_TREE_MODEL = "current-season-only-histogram-tree"
RIDGE_MODEL = "multi-season-ridge"
TREE_MODEL = "multi-season-histogram-tree"
MODEL_NAMES = (
    BASELINE_MODEL,
    CURRENT_RIDGE_MODEL,
    CURRENT_TREE_MODEL,
    RIDGE_MODEL,
    TREE_MODEL,
)
EWMA_ALPHA = 0.25

SUMMARY_METRICS = (
    "AppearanceRate",
    "StartRate",
    "Played60Rate",
    "MinutesMean",
    "TotalPointsMean",
    "ExpectedGoalsMean",
    "ExpectedAssistsMean",
    "ExpectedGoalInvolvementsMean",
    "DefensiveContributionMean",
    "DefensiveContributionObservedCount",
)
FEATURES = (
    "targetGameweek",
    "targetFixtureCount",
    "targetHomeFixtureRate",
    "priorGameweekCount",
    "priorSeasonGameweekCount",
    "currentSeasonPriorGameweekCount",
    "priorSeasonIdentity",
    "priorPositionChanged",
    "historySeasonCount",
    "daysSinceLastGameweek",
    "priorTrailingZeroMinuteGameweeks",
    "priorAppearanceRate",
    "priorStartRate",
    "priorPlayed60Rate",
    "priorMinutesMean",
    "priorTotalPointsMean",
    "priorExpectedGoalsMean",
    "priorExpectedAssistsMean",
    "priorExpectedGoalInvolvementsMean",
    "priorExpectedGoalsConcededMean",
    "priorDefensiveContributionMean",
    "priorDefensiveContributionObservedCount",
    "rolling3AppearanceRate",
    "rolling3StartRate",
    "rolling3Played60Rate",
    "rolling3MinutesMean",
    "rolling3TotalPointsMean",
    "rolling3ExpectedGoalsMean",
    "rolling3ExpectedAssistsMean",
    "rolling3ExpectedGoalInvolvementsMean",
    "rolling3DefensiveContributionMean",
    "rolling3DefensiveContributionObservedCount",
    "rolling5AppearanceRate",
    "rolling5StartRate",
    "rolling5Played60Rate",
    "rolling5MinutesMean",
    "rolling5TotalPointsMean",
    "rolling5ExpectedGoalsMean",
    "rolling5ExpectedAssistsMean",
    "rolling5ExpectedGoalInvolvementsMean",
    "rolling5DefensiveContributionMean",
    "rolling5DefensiveContributionObservedCount",
    "ewmaAppearanceRate",
    "ewmaStartRate",
    "ewmaPlayed60Rate",
    "ewmaMinutesMean",
    "ewmaTotalPointsMean",
    "ewmaExpectedGoalsMean",
    "ewmaExpectedAssistsMean",
    "ewmaExpectedGoalInvolvementsMean",
    "ewmaDefensiveContributionMean",
    "ewmaDefensiveContributionObservedCount",
)


@dataclass(frozen=True, order=True)
class Origin:
    season_index: int
    gameweek: int
    season_code: str


@dataclass(frozen=True)
class Observation:
    season_code: str
    season_index: int
    gameweek: int
    player_code: int
    position: str
    earliest_kickoff_utc: str
    latest_kickoff_utc: str
    fixture_count: int
    home_fixture_count: int
    minutes: int
    starts: int
    total_points: int
    expected_goals: float
    expected_assists: float
    expected_goal_involvements: float
    expected_goals_conceded: float
    defensive_contribution: Optional[float]


def evaluate_multi_season(
    database_path: Path,
    season_codes: Sequence[str] = DEFAULT_SEASONS,
    evaluation_start_gameweek: int = DEFAULT_EVALUATION_START_GAMEWEEK,
    minimum_training_origins: int = MINIMUM_TRAINING_ORIGINS,
    ridge_penalty: float = RIDGE_PENALTY,
) -> Dict[str, Any]:
    path = Path(database_path)
    seasons = tuple(season_codes)
    _validate(
        path,
        seasons,
        evaluation_start_gameweek,
        minimum_training_origins,
        ridge_penalty,
    )
    connection = _open_connection(path)
    try:
        captures = [_load_capture(connection, season) for season in seasons]
        missing = [
            season
            for season, capture in zip(seasons, captures)
            if capture is None
        ]
        base = _base(
            seasons,
            evaluation_start_gameweek,
            minimum_training_origins,
            ridge_penalty,
            [capture for capture in captures if capture is not None],
        )
        if missing:
            return _finish(
                base,
                "insufficient-data",
                "historical-season-archive-not-found",
                [],
                [],
                missing,
            )

        exact_captures = [capture for capture in captures if capture is not None]
        samples_by_origin = _build_feature_table(connection, exact_captures)
        current_only_samples = _build_feature_table(
            connection,
            exact_captures,
            carry_prior_seasons=False,
        )
        ordered_origins = sorted(samples_by_origin)
        evaluation_season = seasons[-1]
        evaluation_origins = [
            origin
            for origin in ordered_origins
            if origin.season_code == evaluation_season
            and origin.gameweek >= evaluation_start_gameweek
        ]
        predictions: List[Prediction] = []
        folds: List[Dict[str, Any]] = []
        for target_origin in evaluation_origins:
            training_origins = [
                origin for origin in ordered_origins if origin < target_origin
            ]
            if len(training_origins) < minimum_training_origins:
                continue
            training = [
                sample
                for origin in training_origins
                for sample in samples_by_origin[origin]
            ]
            target = samples_by_origin[target_origin]
            current_training_origins = [
                origin
                for origin in training_origins
                if origin.season_code == target_origin.season_code
            ]
            if not current_training_origins:
                continue
            current_training = [
                sample
                for origin in current_training_origins
                for sample in current_only_samples[origin]
            ]
            current_target = current_only_samples[target_origin]
            fold_predictions, diagnostics = _predict_models(
                training,
                target,
                current_training,
                current_target,
                ridge_penalty,
            )
            predictions.extend(fold_predictions)
            fold_models = [
                _summarise_model(name, fold_predictions)
                for name in MODEL_NAMES
            ]
            fold_models.sort(
                key=lambda item: (item["metrics"]["mae"], item["name"])
            )
            folds.append(
                _fold(
                    target_origin,
                    training_origins,
                    training,
                    target,
                    current_training_origins,
                    current_training,
                    fold_predictions,
                    diagnostics,
                )
            )
        if not folds:
            return _finish(
                base,
                "insufficient-data",
                "no-eligible-multi-season-folds",
                [],
                [],
                [],
            )
        models = [_summarise_model(name, predictions) for name in MODEL_NAMES]
        models.sort(key=lambda item: (item["metrics"]["mae"], item["name"]))
        return _finish(
            base,
            "complete",
            None,
            folds,
            models,
            [],
            _comparison(models, folds),
        )
    finally:
        connection.close()


def _build_feature_table(
    connection: sqlite3.Connection,
    captures: Sequence[HistoricalCapture],
    carry_prior_seasons: bool = True,
) -> Dict[Origin, List[Sample]]:
    observations: DefaultDict[Origin, List[Observation]] = defaultdict(list)
    for season_index, capture in enumerate(captures):
        for observation in _load_observations(
            connection,
            capture,
            season_index,
        ):
            observations[
                Origin(season_index, observation.gameweek, capture.season_code)
            ].append(observation)

    history: DefaultDict[int, List[Observation]] = defaultdict(list)
    samples: Dict[Origin, List[Sample]] = {}
    active_season_index: Optional[int] = None
    for origin in sorted(observations):
        if (
            not carry_prior_seasons
            and active_season_index is not None
            and origin.season_index != active_season_index
        ):
            history.clear()
        active_season_index = origin.season_index
        target_rows = sorted(
            observations[origin],
            key=lambda item: item.player_code,
        )
        samples[origin] = [
            Sample(
                season_code=row.season_code,
                gameweek=row.gameweek,
                player_id=row.player_code,
                position=row.position,
                features=_features(row, history[row.player_code]),
                actual=row.total_points,
            )
            for row in target_rows
        ]
        for row in target_rows:
            history[row.player_code].append(row)
    return samples


def _load_observations(
    connection: sqlite3.Connection,
    capture: HistoricalCapture,
    season_index: int,
) -> List[Observation]:
    players = connection.execute(
        """
        SELECT player_code, position
        FROM historical_fpl_players
        WHERE capture_id = :capture_id
        ORDER BY player_code;
        """,
        {"capture_id": capture.capture_id},
    ).fetchall()
    positions = {
        int(row["player_code"]): str(row["position"]) for row in players
    }
    if (
        len(positions) != capture.player_count
        or len(positions) != capture.stable_code_count
    ):
        raise TemporalRidgeError(
            "data.incomplete-historical-player-coverage",
            "Historical player rows do not match declared stable-code coverage.",
        )
    rows = connection.execute(
        """
        SELECT
            player_code,
            gameweek,
            fixture_id,
            kickoff_utc,
            was_home,
            minutes,
            starts,
            total_points,
            expected_goals,
            expected_assists,
            expected_goal_involvements,
            expected_goals_conceded,
            defensive_contribution
        FROM historical_fpl_player_gameweeks
        WHERE capture_id = :capture_id
        ORDER BY player_code, gameweek, kickoff_utc, fixture_id;
        """,
        {"capture_id": capture.capture_id},
    ).fetchall()
    if len(rows) != capture.player_gameweek_count:
        raise TemporalRidgeError(
            "data.incomplete-historical-gameweek-coverage",
            "Historical Gameweek rows do not match declared coverage.",
        )

    aggregates: Dict[Tuple[int, int], Dict[str, Any]] = {}
    for row in rows:
        player_code = int(row["player_code"])
        if player_code not in positions:
            raise TemporalRidgeError(
                "data.unknown-historical-player",
                "A historical Gameweek row has no stable player identity.",
            )
        gameweek = int(row["gameweek"])
        key = (player_code, gameweek)
        aggregate = aggregates.setdefault(
            key,
            {
                "kickoffs": [],
                "fixture_count": 0,
                "home_fixture_count": 0,
                "minutes": 0,
                "starts": 0,
                "total_points": 0,
                "expected_goals": 0.0,
                "expected_assists": 0.0,
                "expected_goal_involvements": 0.0,
                "expected_goals_conceded": 0.0,
                "defensive_contribution": 0.0,
                "defensive_observed": 0,
            },
        )
        aggregate["kickoffs"].append(str(row["kickoff_utc"]))
        aggregate["fixture_count"] += 1
        aggregate["home_fixture_count"] += int(row["was_home"])
        aggregate["minutes"] += int(row["minutes"])
        aggregate["starts"] += int(row["starts"])
        aggregate["total_points"] += int(row["total_points"])
        aggregate["expected_goals"] += _finite(row["expected_goals"])
        aggregate["expected_assists"] += _finite(row["expected_assists"])
        aggregate["expected_goal_involvements"] += _finite(
            row["expected_goal_involvements"]
        )
        aggregate["expected_goals_conceded"] += _finite(
            row["expected_goals_conceded"]
        )
        if row["defensive_contribution"] is not None:
            aggregate["defensive_contribution"] += _finite(
                row["defensive_contribution"]
            )
            aggregate["defensive_observed"] += 1

    return [
        Observation(
            season_code=capture.season_code,
            season_index=season_index,
            gameweek=gameweek,
            player_code=player_code,
            position=positions[player_code],
            earliest_kickoff_utc=min(aggregate["kickoffs"]),
            latest_kickoff_utc=max(aggregate["kickoffs"]),
            fixture_count=int(aggregate["fixture_count"]),
            home_fixture_count=int(aggregate["home_fixture_count"]),
            minutes=int(aggregate["minutes"]),
            starts=int(aggregate["starts"]),
            total_points=int(aggregate["total_points"]),
            expected_goals=float(aggregate["expected_goals"]),
            expected_assists=float(aggregate["expected_assists"]),
            expected_goal_involvements=float(
                aggregate["expected_goal_involvements"]
            ),
            expected_goals_conceded=float(
                aggregate["expected_goals_conceded"]
            ),
            defensive_contribution=(
                float(aggregate["defensive_contribution"])
                if aggregate["defensive_observed"]
                else None
            ),
        )
        for (player_code, gameweek), aggregate in sorted(aggregates.items())
    ]


def _features(
    target: Observation,
    history: Sequence[Observation],
) -> Dict[str, Optional[float]]:
    current = [
        row for row in history if row.season_index == target.season_index
    ]
    previous = [
        row for row in history if row.season_index < target.season_index
    ]
    prior_season_identity = bool(previous)
    prior_position = previous[-1].position if previous else None
    values: Dict[str, Optional[float]] = {
        "targetGameweek": float(target.gameweek),
        "targetFixtureCount": float(target.fixture_count),
        "targetHomeFixtureRate": (
            target.home_fixture_count / target.fixture_count
        ),
        "priorGameweekCount": float(len(history)),
        "priorSeasonGameweekCount": float(len(previous)),
        "currentSeasonPriorGameweekCount": float(len(current)),
        "priorSeasonIdentity": float(prior_season_identity),
        "priorPositionChanged": (
            None
            if prior_position is None
            else float(prior_position != target.position)
        ),
        "historySeasonCount": float(
            len({row.season_index for row in history})
        ),
        "daysSinceLastGameweek": (
            None
            if not history
            else _days_between(
                history[-1].latest_kickoff_utc,
                target.earliest_kickoff_utc,
            )
        ),
        "priorTrailingZeroMinuteGameweeks": float(
            _trailing_zero_minutes(history)
        ),
    }
    _copy_summary(values, "prior", _summary(history), include_xgc=True)
    _copy_summary(values, "rolling3", _summary(history[-3:]))
    _copy_summary(values, "rolling5", _summary(history[-5:]))
    _copy_summary(values, "ewma", _ewma(history))
    if tuple(values) != FEATURES:
        raise TemporalRidgeError(
            "evaluation.feature-contract",
            "Multi-season features do not match their fixed contract.",
        )
    return values


def _summary(
    rows: Sequence[Observation],
) -> Mapping[str, Optional[float]]:
    if not rows:
        return {
            "appearanceRate": None,
            "startRate": None,
            "played60Rate": None,
            "minutesMean": None,
            "totalPointsMean": None,
            "expectedGoalsMean": None,
            "expectedAssistsMean": None,
            "expectedGoalInvolvementsMean": None,
            "expectedGoalsConcededMean": None,
            "defensiveContributionMean": None,
            "defensiveContributionObservedCount": 0.0,
        }
    defensive = [
        row.defensive_contribution
        for row in rows
        if row.defensive_contribution is not None
    ]
    count = len(rows)
    return {
        "appearanceRate": sum(row.minutes > 0 for row in rows) / count,
        "startRate": sum(row.starts > 0 for row in rows) / count,
        "played60Rate": sum(row.minutes >= 60 for row in rows) / count,
        "minutesMean": sum(row.minutes for row in rows) / count,
        "totalPointsMean": sum(row.total_points for row in rows) / count,
        "expectedGoalsMean": sum(row.expected_goals for row in rows) / count,
        "expectedAssistsMean": (
            sum(row.expected_assists for row in rows) / count
        ),
        "expectedGoalInvolvementsMean": (
            sum(row.expected_goal_involvements for row in rows) / count
        ),
        "expectedGoalsConcededMean": (
            sum(row.expected_goals_conceded for row in rows) / count
        ),
        "defensiveContributionMean": (
            sum(defensive) / len(defensive) if defensive else None
        ),
        "defensiveContributionObservedCount": float(len(defensive)),
    }


def _ewma(
    rows: Sequence[Observation],
) -> Mapping[str, Optional[float]]:
    if not rows:
        return _summary(rows)
    summaries = [_summary((row,)) for row in rows]
    keys = tuple(summaries[0])
    values: Dict[str, Optional[float]] = {}
    for key in keys:
        if key == "defensiveContributionObservedCount":
            values[key] = float(
                sum(
                    int(summary[key] or 0)
                    for summary in summaries
                )
            )
            continue
        observed = [
            float(summary[key])
            for summary in summaries
            if summary[key] is not None
        ]
        if not observed:
            values[key] = None
            continue
        value = observed[0]
        for current in observed[1:]:
            value = EWMA_ALPHA * current + (1.0 - EWMA_ALPHA) * value
        values[key] = value
    return values


def _copy_summary(
    target: Dict[str, Optional[float]],
    prefix: str,
    source: Mapping[str, Optional[float]],
    include_xgc: bool = False,
) -> None:
    metrics = list(SUMMARY_METRICS)
    if include_xgc:
        metrics.insert(-2, "ExpectedGoalsConcededMean")
    for metric in metrics:
        key = metric[0].lower() + metric[1:]
        target[f"{prefix}{metric}"] = source[key]


def _predict_models(
    training: Sequence[Sample],
    target: Sequence[Sample],
    current_training: Sequence[Sample],
    current_target: Sequence[Sample],
    ridge_penalty: float,
) -> Tuple[List[Prediction], Dict[str, Any]]:
    if {
        (sample.player_id, sample.actual) for sample in target
    } != {
        (sample.player_id, sample.actual) for sample in current_target
    }:
        raise TemporalRidgeError(
            "data.inconsistent-ablation-cohort",
            "Current-only and multi-season target cohorts differ.",
        )
    baseline = _baseline_predictions(current_training, current_target)
    current_ridge, current_ridge_diagnostic = _predict_fold(
        current_training,
        current_target,
        ridge_penalty,
        continuous_features=FEATURES,
        model_name=CURRENT_RIDGE_MODEL,
        include_baselines=False,
    )
    current_tree, current_tree_diagnostic = _predict_tree(
        current_training,
        current_target,
        continuous_features=FEATURES,
        model_name=CURRENT_TREE_MODEL,
    )
    ridge, ridge_diagnostic = _predict_fold(
        training,
        target,
        ridge_penalty,
        continuous_features=FEATURES,
        model_name=RIDGE_MODEL,
        include_baselines=False,
    )
    tree, tree_diagnostic = _predict_tree(
        training,
        target,
        continuous_features=FEATURES,
        model_name=TREE_MODEL,
    )
    return [
        *baseline,
        *current_ridge,
        *current_tree,
        *ridge,
        *tree,
    ], {
        CURRENT_RIDGE_MODEL: current_ridge_diagnostic,
        CURRENT_TREE_MODEL: current_tree_diagnostic,
        RIDGE_MODEL: ridge_diagnostic,
        TREE_MODEL: tree_diagnostic,
    }


def _baseline_predictions(
    training: Sequence[Sample],
    target: Sequence[Sample],
) -> List[Prediction]:
    if not training or not target:
        raise TemporalRidgeError(
            "evaluation.empty-fold",
            "An eligible multi-season fold has no training or target rows.",
        )
    global_mean = sum(sample.actual for sample in training) / len(training)
    positions: DefaultDict[str, List[int]] = defaultdict(list)
    players: DefaultDict[int, List[int]] = defaultdict(list)
    for sample in training:
        positions[sample.position].append(sample.actual)
        players[sample.player_id].append(sample.actual)
    predictions: List[Prediction] = []
    for sample in target:
        position_values = positions[sample.position]
        fallback = (
            sum(position_values) / len(position_values)
            if position_values
            else global_mean
        )
        history = players[sample.player_id]
        predicted = (
            sum(history[-3:]) / len(history[-3:])
            if history
            else fallback
        )
        predictions.append(
            Prediction(
                model=BASELINE_MODEL,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=predicted,
                actual=sample.actual,
            )
        )
    return predictions


def _fold(
    target_origin: Origin,
    training_origins: Sequence[Origin],
    training: Sequence[Sample],
    target: Sequence[Sample],
    current_training_origins: Sequence[Origin],
    current_training: Sequence[Sample],
    predictions: Sequence[Prediction],
    diagnostics: Mapping[str, Any],
) -> Dict[str, Any]:
    models = [_summarise_model(name, predictions) for name in MODEL_NAMES]
    models.sort(key=lambda item: (item["metrics"]["mae"], item["name"]))
    training_by_season: DefaultDict[str, int] = defaultdict(int)
    for sample in training:
        training_by_season[sample.season_code] += 1
    prior_identity_count = sum(
        int(sample.features["priorSeasonIdentity"] or 0)
        for sample in target
    )
    legacy_defensive_missing = sum(
        sample.features["priorDefensiveContributionMean"] is None
        for sample in target
    )
    return {
        "seasonCode": target_origin.season_code,
        "gameweek": target_origin.gameweek,
        "trainingOriginCount": len(training_origins),
        "currentSeasonTrainingOriginCount": len(current_training_origins),
        "firstTrainingOrigin": _origin_document(training_origins[0]),
        "lastTrainingOrigin": _origin_document(training_origins[-1]),
        "trainingRows": len(training),
        "currentSeasonTrainingRows": len(current_training),
        "trainingRowsBySeason": dict(sorted(training_by_season.items())),
        "targetPlayerCount": len(target),
        "targetPriorSeasonIdentityCount": prior_identity_count,
        "targetPriorSeasonIdentityCoverage": _round(
            prior_identity_count / len(target)
        ),
        "targetMissingPriorDefensiveCount": legacy_defensive_missing,
        "models": models,
        "diagnostics": dict(diagnostics),
    }


def _comparison(
    models: Sequence[Mapping[str, Any]],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    indexed = {str(model["name"]): model for model in models}
    baseline = indexed[BASELINE_MODEL]["metrics"]
    comparisons = []
    for name in (
        CURRENT_RIDGE_MODEL,
        CURRENT_TREE_MODEL,
        RIDGE_MODEL,
        TREE_MODEL,
    ):
        metrics = indexed[name]["metrics"]
        improvement = (
            (baseline["mae"] - metrics["mae"]) / baseline["mae"]
            if baseline["mae"] > 0
            else 0.0
        )
        comparisons.append(
            {
                "name": name,
                "maeImprovementOverBaselineFraction": _round(improvement),
                "rmseDeltaFromBaseline": _round(
                    metrics["rmse"] - baseline["rmse"]
                ),
            }
        )
    cross_season_deltas = []
    for current_name, multi_name in (
        (CURRENT_RIDGE_MODEL, RIDGE_MODEL),
        (CURRENT_TREE_MODEL, TREE_MODEL),
    ):
        current = indexed[current_name]["metrics"]
        multi = indexed[multi_name]["metrics"]
        current_positions = indexed[current_name]["slices"]["position"]
        multi_positions = indexed[multi_name]["slices"]["position"]
        position_mae_deltas = {
            position: _round(
                multi_positions[position]["mae"]
                - current_positions[position]["mae"]
            )
            for position in sorted(current_positions)
        }
        fold_wins = 0
        for fold in folds:
            fold_models = {
                str(model["name"]): model for model in fold["models"]
            }
            if (
                fold_models[multi_name]["metrics"]["mae"]
                < fold_models[current_name]["metrics"]["mae"]
            ):
                fold_wins += 1
        cross_season_deltas.append(
            {
                "currentSeasonOnlyModel": current_name,
                "multiSeasonModel": multi_name,
                "maeImprovementFraction": _round(
                    (current["mae"] - multi["mae"]) / current["mae"]
                    if current["mae"] > 0
                    else 0.0
                ),
                "rmseDelta": _round(multi["rmse"] - current["rmse"]),
                "foldWins": fold_wins,
                "foldCount": len(folds),
                "positionMaeDeltas": position_mae_deltas,
                "allPositionMaeNonWorse": all(
                    delta <= 0 for delta in position_mae_deltas.values()
                ),
            }
        )
    return {
        "leadingModel": str(models[0]["name"]),
        "baseline": BASELINE_MODEL,
        "challengers": comparisons,
        "crossSeasonAblation": cross_season_deltas,
        "multiSeasonRecommendation": (
            "retain-as-prospective-shadow-challenger"
        ),
        "decision": "retrospective-screen-only",
        "isPromoted": False,
        "interpretation": (
            "The same rows and chronological folds compare all models. "
            "Because both archived seasons were already observed when this "
            "screen was designed, the result may choose a prospective "
            "challenger but cannot promote it."
        ),
    }


def _base(
    seasons: Sequence[str],
    evaluation_start_gameweek: int,
    minimum_training_origins: int,
    ridge_penalty: float,
    captures: Sequence[HistoricalCapture],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": "retrospective-screen-not-promoted",
        "isPromoted": False,
        "target": "historical-player-gameweek-total-points",
        "split": "cross-season-expanding-origin",
        "configuration": {
            "seasonCodes": list(seasons),
            "evaluationSeasonCode": seasons[-1],
            "evaluationStartGameweek": evaluation_start_gameweek,
            "minimumTrainingOrigins": minimum_training_origins,
            "stableIdentity": "exact-official-player-code",
            "nameFallback": False,
            "ridgePenalty": ridge_penalty,
            "penaltySelection": "fixed-before-evaluation",
            "treeConfiguration": "fixed-temporal-tree-v1",
            "imputation": "training-fold-median-with-missing-indicators",
            "scaling": "training-fold-z-score",
            "features": list(FEATURES),
            "models": list(MODEL_NAMES),
        },
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
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
    missing_seasons: Sequence[str],
    comparison: Optional[Mapping[str, Any]] = None,
) -> Dict[str, Any]:
    report = {
        **base,
        "status": status,
        "reason": reason,
        "eligibleFoldCount": len(folds),
        "folds": list(folds),
        "models": list(models),
        "missingSeasonCodes": list(missing_seasons),
        "comparison": comparison,
    }
    report["runIdentitySha256"] = _sha256(report)
    return report


def _origin_document(origin: Origin) -> Dict[str, Any]:
    return {
        "seasonCode": origin.season_code,
        "gameweek": origin.gameweek,
    }


def _validate(
    path: Path,
    seasons: Sequence[str],
    evaluation_start_gameweek: int,
    minimum_training_origins: int,
    ridge_penalty: float,
) -> None:
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    if len(seasons) < 2 or len(set(seasons)) != len(seasons):
        raise TemporalRidgeError(
            "configuration.season-codes",
            "At least two distinct historical season codes are required.",
        )
    starts = [_season_start(season) for season in seasons]
    if starts != sorted(starts):
        raise TemporalRidgeError(
            "configuration.season-order",
            "Historical seasons must be supplied in chronological order.",
        )
    if evaluation_start_gameweek < 1 or evaluation_start_gameweek > 38:
        raise TemporalRidgeError(
            "configuration.evaluation-start-gameweek",
            "The evaluation start Gameweek must be between 1 and 38.",
        )
    if minimum_training_origins < 1:
        raise TemporalRidgeError(
            "configuration.minimum-training-origins",
            "At least one training origin is required.",
        )
    if not math.isfinite(ridge_penalty) or ridge_penalty <= 0:
        raise TemporalRidgeError(
            "configuration.ridge-penalty",
            "The ridge penalty must be a positive finite number.",
        )


def _season_start(season_code: str) -> int:
    parts = season_code.split("-")
    if (
        len(parts) != 2
        or len(parts[0]) != 4
        or len(parts[1]) != 2
        or not all(part.isdigit() for part in parts)
    ):
        raise TemporalRidgeError(
            "configuration.season-code",
            f"Unsupported season code: {season_code}",
        )
    start = int(parts[0])
    if int(parts[1]) != (start + 1) % 100:
        raise TemporalRidgeError(
            "configuration.season-code",
            f"Unsupported season code: {season_code}",
        )
    return start


def _open_connection(path: Path) -> sqlite3.Connection:
    try:
        connection = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        connection.execute("PRAGMA foreign_keys = ON;")
        row = connection.execute(
            "SELECT MAX(version) AS version FROM schema_migrations;"
        ).fetchone()
        if row is None or row["version"] is None or int(row["version"]) < 24:
            raise TemporalRidgeError(
                "database.schema-version",
                "Multi-season evaluation requires database version 24 or newer.",
            )
        return connection
    except TemporalRidgeError:
        raise
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "database.open-failed",
            "The SQLite database could not be opened read-only.",
        ) from exception


def _days_between(earlier: str, later: str) -> float:
    try:
        earlier_time = datetime.fromisoformat(earlier.replace("Z", "+00:00"))
        later_time = datetime.fromisoformat(later.replace("Z", "+00:00"))
    except ValueError as exception:
        raise TemporalRidgeError(
            "data.invalid-kickoff-time",
            "A historical kickoff is not a valid ISO-8601 timestamp.",
        ) from exception
    difference = (later_time - earlier_time).total_seconds() / 86_400
    if difference < 0 or not math.isfinite(difference):
        raise TemporalRidgeError(
            "data.non-chronological-kickoff",
            "Historical kickoff order is not chronological.",
        )
    return difference


def _trailing_zero_minutes(rows: Sequence[Observation]) -> int:
    count = 0
    for row in reversed(rows):
        if row.minutes > 0:
            break
        count += 1
    return count


def _finite(value: Any) -> float:
    number = float(value)
    if not math.isfinite(number):
        raise TemporalRidgeError(
            "data.non-finite-historical-value",
            "A historical archive metric is not finite.",
        )
    return number


def _parse_args(argv: Optional[Sequence[str]]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Compare a fixed baseline, ridge and histogram tree on exact-code "
            "multi-season expanding-origin folds."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument(
        "--season",
        action="append",
        dest="seasons",
        help=(
            "Historical season in chronological order; repeat for each season. "
            "Defaults to 2024-25 then 2025-26."
        ),
    )
    parser.add_argument(
        "--evaluation-start-gameweek",
        type=int,
        default=DEFAULT_EVALUATION_START_GAMEWEEK,
    )
    parser.add_argument(
        "--minimum-training-origins",
        type=int,
        default=MINIMUM_TRAINING_ORIGINS,
    )
    parser.add_argument(
        "--ridge-penalty",
        type=float,
        default=RIDGE_PENALTY,
    )
    parser.add_argument("--output", type=Path)
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    arguments = _parse_args(argv)
    try:
        report = evaluate_multi_season(
            arguments.database,
            season_codes=arguments.seasons or DEFAULT_SEASONS,
            evaluation_start_gameweek=arguments.evaluation_start_gameweek,
            minimum_training_origins=arguments.minimum_training_origins,
            ridge_penalty=arguments.ridge_penalty,
        )
        _write_report(report, arguments.output)
        return 0
    except TemporalRidgeError as exception:
        sys.stderr.write(
            json.dumps(
                {
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
