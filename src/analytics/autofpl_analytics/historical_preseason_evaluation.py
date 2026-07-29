from __future__ import annotations

import argparse
import json
import math
import sqlite3
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import (
    Any,
    DefaultDict,
    Dict,
    Iterable,
    List,
    Mapping,
    Optional,
    Sequence,
    Tuple,
)

import numpy as np

from .temporal_ridge import (
    Prediction,
    Sample,
    TemporalRidgeError,
    _apply_transform,
    _fit_transform,
    _open_connection,
    _round,
    _sha256,
    _summarise_model,
    _write_report,
)
from .temporal_tree import _predict_tree

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-preseason-evaluation-v1"
DEFAULT_SEASON = "2025-26"
RIDGE_MODEL = "historical-preseason-ridge"
TREE_MODEL = "historical-preseason-histogram-tree"
BASELINE_MODELS = (
    "zero-points",
    "position-expanding-mean",
    "player-last-points",
    "player-rolling3-points",
    "player-expanding-mean",
)
CANDIDATE_MODELS = (RIDGE_MODEL, TREE_MODEL)
FEATURES = (
    "targetFixtureCount",
    "targetHomeFixtureRate",
    "priorGameweekCount",
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
    "rolling3AppearanceRate",
    "rolling3StartRate",
    "rolling3Played60Rate",
    "rolling3MinutesMean",
    "rolling3TotalPointsMean",
    "rolling3ExpectedGoalsMean",
    "rolling3ExpectedAssistsMean",
    "rolling3ExpectedGoalInvolvementsMean",
    "rolling3DefensiveContributionMean",
    "rolling5AppearanceRate",
    "rolling5StartRate",
    "rolling5Played60Rate",
    "rolling5MinutesMean",
    "rolling5TotalPointsMean",
    "rolling5ExpectedGoalsMean",
    "rolling5ExpectedAssistsMean",
    "rolling5ExpectedGoalInvolvementsMean",
    "rolling5DefensiveContributionMean",
    "ewmaAppearanceRate",
    "ewmaStartRate",
    "ewmaPlayed60Rate",
    "ewmaMinutesMean",
    "ewmaTotalPointsMean",
    "ewmaExpectedGoalsMean",
    "ewmaExpectedAssistsMean",
    "ewmaExpectedGoalInvolvementsMean",
    "ewmaDefensiveContributionMean",
)
RIDGE_PENALTY = 10.0
EWMA_ALPHA = 0.25
MINIMUM_TRAINING_GAMEWEEKS = 5
HOLDOUT_START_GAMEWEEK = 31
MINIMUM_HOLDOUT_MAE_IMPROVEMENT = 0.01
MAXIMUM_POSITION_MAE_REGRESSION = 0.05


@dataclass(frozen=True)
class HistoricalCapture:
    capture_id: int
    season_code: str
    source_revision: str
    available_at_utc: str
    players_sha256: str
    gameweeks_sha256: str
    player_count: int
    player_gameweek_count: int
    stable_code_count: int


@dataclass(frozen=True)
class HistoricalGameweek:
    gameweek: int
    fixture_count: int
    home_fixture_count: int
    minutes: int
    starts: int
    total_points: int
    expected_goals: float
    expected_assists: float
    expected_goal_involvements: float
    expected_goals_conceded: float
    defensive_contribution: Optional[int]


def evaluate_historical_preseason(
    database_path: Path,
    season_code: str = DEFAULT_SEASON,
    minimum_training_gameweeks: int = MINIMUM_TRAINING_GAMEWEEKS,
    holdout_start_gameweek: int = HOLDOUT_START_GAMEWEEK,
    ridge_penalty: float = RIDGE_PENALTY,
) -> Dict[str, Any]:
    path = Path(database_path)
    _validate(
        path,
        season_code,
        minimum_training_gameweeks,
        holdout_start_gameweek,
        ridge_penalty,
    )
    connection = _open_connection(path)
    try:
        capture = _load_capture(connection, season_code)
        base = _base(
            season_code,
            minimum_training_gameweeks,
            holdout_start_gameweek,
            ridge_penalty,
            capture,
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
        samples_by_gameweek = _build_samples(connection, capture)
        eligible_gameweeks = sorted(samples_by_gameweek)
        development_folds: List[Dict[str, Any]] = []
        development_predictions: List[Prediction] = []
        holdout_folds: List[Dict[str, Any]] = []
        holdout_predictions: List[Prediction] = []
        selected_candidate: Optional[str] = None
        selected_baseline: Optional[str] = None

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
            if target_gameweek < holdout_start_gameweek:
                predictions, diagnostics = _predict_models(
                    training,
                    target,
                    CANDIDATE_MODELS,
                    ridge_penalty,
                )
                development_predictions.extend(predictions)
                development_folds.append(
                    _fold(
                        target_gameweek,
                        training_gameweeks,
                        training,
                        target,
                        predictions,
                        diagnostics,
                    )
                )
                continue

            if selected_candidate is None or selected_baseline is None:
                if not development_folds:
                    continue
                selected_candidate = _select_model(
                    development_predictions,
                    CANDIDATE_MODELS,
                )
                selected_baseline = _select_model(
                    development_predictions,
                    BASELINE_MODELS,
                )
            predictions, diagnostics = _predict_models(
                training,
                target,
                (selected_candidate,),
                ridge_penalty,
            )
            holdout_predictions.extend(predictions)
            holdout_folds.append(
                _fold(
                    target_gameweek,
                    training_gameweeks,
                    training,
                    target,
                    predictions,
                    diagnostics,
                )
            )

        if not development_folds:
            return _finish(
                base,
                "insufficient-data",
                "no-eligible-development-folds",
                [],
                [],
                [],
                None,
            )
        development_models = _summaries(
            development_predictions,
            (*CANDIDATE_MODELS, *BASELINE_MODELS),
        )
        if not holdout_folds:
            return _finish(
                base,
                "insufficient-data",
                "no-eligible-locked-holdout-folds",
                development_folds,
                development_models,
                [],
                None,
            )
        assert selected_candidate is not None
        assert selected_baseline is not None
        holdout_models = _summaries(
            holdout_predictions,
            (selected_candidate, *BASELINE_MODELS),
        )
        recommendation = _recommendation(
            selected_candidate,
            selected_baseline,
            holdout_predictions,
            holdout_folds,
        )
        return _finish(
            base,
            "complete",
            None,
            development_folds,
            development_models,
            holdout_folds,
            holdout_models,
            recommendation,
        )
    finally:
        connection.close()


def _validate(
    path: Path,
    season_code: str,
    minimum_training_gameweeks: int,
    holdout_start_gameweek: int,
    ridge_penalty: float,
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
    if not math.isfinite(ridge_penalty) or ridge_penalty <= 0:
        raise TemporalRidgeError(
            "configuration.ridge-penalty",
            "ridge_penalty must be a positive finite number.",
        )


def _load_capture(
    connection: sqlite3.Connection,
    season_code: str,
) -> Optional[HistoricalCapture]:
    row = connection.execute(
        """
        SELECT
            capture_id,
            season_code,
            source_revision,
            available_at_utc,
            players_sha256,
            gameweeks_sha256,
            player_count,
            player_gameweek_count,
            stable_code_count
        FROM historical_fpl_season_captures
        WHERE season_code = :season_code
        ORDER BY julianday(available_at_utc) DESC, capture_id DESC
        LIMIT 1;
        """,
        {"season_code": season_code},
    ).fetchone()
    if row is None:
        return None
    return HistoricalCapture(
        capture_id=int(row["capture_id"]),
        season_code=str(row["season_code"]),
        source_revision=str(row["source_revision"]),
        available_at_utc=str(row["available_at_utc"]),
        players_sha256=str(row["players_sha256"]),
        gameweeks_sha256=str(row["gameweeks_sha256"]),
        player_count=int(row["player_count"]),
        player_gameweek_count=int(row["player_gameweek_count"]),
        stable_code_count=int(row["stable_code_count"]),
    )


def _build_samples(
    connection: sqlite3.Connection,
    capture: HistoricalCapture,
    target_name: str = "total-points",
) -> Dict[int, List[Sample]]:
    if target_name not in {
        "total-points",
        "appearance",
        "start",
        "played-60",
        "minutes",
    }:
        raise TemporalRidgeError(
            "configuration.historical-target",
            f"Unsupported historical target: {target_name}",
        )
    positions, histories = _load_historical_gameweeks(connection, capture)
    samples: DefaultDict[int, List[Sample]] = defaultdict(list)
    for player_code in sorted(histories):
        history = histories[player_code]
        for gameweek in sorted(history):
            target = history[gameweek]
            prior = [history[key] for key in sorted(history) if key < gameweek]
            samples[gameweek].append(
                Sample(
                    season_code=capture.season_code,
                    gameweek=gameweek,
                    player_id=player_code,
                    position=positions[player_code],
                    features=_features(target, prior),
                    actual=_target_value(target_name, target),
                )
            )
    return {
        gameweek: sorted(items, key=lambda item: item.player_id)
        for gameweek, items in sorted(samples.items())
    }


def _load_historical_gameweeks(
    connection: sqlite3.Connection,
    capture: HistoricalCapture,
) -> Tuple[Dict[int, str], Dict[int, Dict[int, HistoricalGameweek]]]:
    player_rows = connection.execute(
        """
        SELECT player_code, position
        FROM historical_fpl_players
        WHERE capture_id = :capture_id
        ORDER BY player_code;
        """,
        {"capture_id": capture.capture_id},
    ).fetchall()
    positions = {
        int(row["player_code"]): str(row["position"]) for row in player_rows
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
    histories: DefaultDict[int, Dict[int, HistoricalGameweek]] = defaultdict(dict)
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
                "fixture_count": 0,
                "home_fixture_count": 0,
                "minutes": 0,
                "starts": 0,
                "total_points": 0,
                "expected_goals": 0.0,
                "expected_assists": 0.0,
                "expected_goal_involvements": 0.0,
                "expected_goals_conceded": 0.0,
                "defensive_contribution": None,
            },
        )
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
            if aggregate["defensive_contribution"] is None:
                aggregate["defensive_contribution"] = 0
            aggregate["defensive_contribution"] += int(
                row["defensive_contribution"]
            )
    for (player_code, gameweek), aggregate in aggregates.items():
        histories[player_code][gameweek] = HistoricalGameweek(
            gameweek=gameweek,
            **aggregate,
        )

    return positions, dict(histories)


def _target_value(target_name: str, row: HistoricalGameweek) -> int:
    if target_name == "total-points":
        return row.total_points
    if target_name == "appearance":
        return int(row.minutes > 0)
    if target_name == "start":
        return int(row.starts > 0)
    if target_name == "played-60":
        return int(row.minutes >= 60)
    if target_name == "minutes":
        return row.minutes
    raise AssertionError(f"Unexpected historical target: {target_name}")


def _features(
    target: HistoricalGameweek,
    prior: Sequence[HistoricalGameweek],
) -> Dict[str, Optional[float]]:
    full = _summary(prior)
    rolling3 = _summary(prior[-3:])
    rolling5 = _summary(prior[-5:])
    ewma = _ewma(prior)
    values: Dict[str, Optional[float]] = {
        "targetFixtureCount": float(target.fixture_count),
        "targetHomeFixtureRate": (
            target.home_fixture_count / target.fixture_count
            if target.fixture_count > 0
            else 0.0
        ),
        "priorGameweekCount": float(len(prior)),
        "priorTrailingZeroMinuteGameweeks": float(
            _trailing_zero_minutes(prior)
        ),
    }
    _copy_summary(values, "prior", full)
    _copy_summary(values, "rolling3", rolling3)
    _copy_summary(values, "rolling5", rolling5)
    _copy_summary(values, "ewma", ewma)
    if tuple(values) != FEATURES:
        raise TemporalRidgeError(
            "evaluation.feature-contract",
            "Historical preseason features do not match their fixed contract.",
        )
    return values


def _summary(
    rows: Sequence[HistoricalGameweek],
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
        }
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
        "defensiveContributionMean": _optional_mean(
            row.defensive_contribution for row in rows
        ),
    }


def _ewma(
    rows: Sequence[HistoricalGameweek],
) -> Mapping[str, Optional[float]]:
    if not rows:
        return _summary(rows)
    per_row = [_summary((row,)) for row in rows]
    values: Dict[str, Optional[float]] = dict(per_row[0])
    for current in per_row[1:]:
        values = {
            key: _ewma_value(values[key], current[key])
            for key in values
        }
    return values


def _optional_mean(values: Iterable[Optional[int]]) -> Optional[float]:
    observed = [float(value) for value in values if value is not None]
    return sum(observed) / len(observed) if observed else None


def _ewma_value(
    previous: Optional[float],
    current: Optional[float],
) -> Optional[float]:
    if current is None:
        return previous
    if previous is None:
        return current
    return EWMA_ALPHA * current + (1.0 - EWMA_ALPHA) * previous


def _copy_summary(
    target: Dict[str, Optional[float]],
    prefix: str,
    source: Mapping[str, Optional[float]],
) -> None:
    metrics = (
        "AppearanceRate",
        "StartRate",
        "Played60Rate",
        "MinutesMean",
        "TotalPointsMean",
        "ExpectedGoalsMean",
        "ExpectedAssistsMean",
        "ExpectedGoalInvolvementsMean",
        "DefensiveContributionMean",
    )
    if prefix == "prior":
        metrics = (
            *metrics[:8],
            "ExpectedGoalsConcededMean",
            "DefensiveContributionMean",
        )
    for metric in metrics:
        key = metric[0].lower() + metric[1:]
        target[f"{prefix}{metric}"] = source[key]


def _trailing_zero_minutes(rows: Sequence[HistoricalGameweek]) -> int:
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


def _predict_models(
    training: Sequence[Sample],
    target: Sequence[Sample],
    candidates: Sequence[str],
    ridge_penalty: float,
) -> Tuple[List[Prediction], Dict[str, Any]]:
    predictions = _baseline_predictions(training, target)
    diagnostics: Dict[str, Any] = {}
    if RIDGE_MODEL in candidates:
        ridge, diagnostic = _predict_ridge(
            training,
            target,
            ridge_penalty,
        )
        predictions.extend(ridge)
        diagnostics[RIDGE_MODEL] = diagnostic
    if TREE_MODEL in candidates:
        tree, diagnostic = _predict_tree(
            training,
            target,
            continuous_features=FEATURES,
            model_name=TREE_MODEL,
        )
        predictions.extend(tree)
        diagnostics[TREE_MODEL] = diagnostic
    return predictions, diagnostics


def _predict_ridge(
    training: Sequence[Sample],
    target: Sequence[Sample],
    ridge_penalty: float,
) -> Tuple[List[Prediction], Dict[str, Any]]:
    transform = _fit_transform(training, FEATURES)
    training_matrix = np.asarray(
        [
            _apply_transform(sample, transform, FEATURES)
            for sample in training
        ],
        dtype=float,
    )
    target_matrix = np.asarray(
        [
            _apply_transform(sample, transform, FEATURES)
            for sample in target
        ],
        dtype=float,
    )
    targets = np.asarray(
        [sample.actual for sample in training],
        dtype=float,
    )
    intercept = float(targets.mean())
    gram = training_matrix.T @ training_matrix
    regularised = gram + ridge_penalty * np.eye(gram.shape[0])
    coefficients = np.linalg.solve(
        regularised,
        training_matrix.T @ (targets - intercept),
    )
    predicted = intercept + target_matrix @ coefficients
    if not np.isfinite(predicted).all():
        raise TemporalRidgeError(
            "evaluation.non-finite-ridge-prediction",
            "The historical ridge challenger produced a non-finite prediction.",
        )
    predictions = [
        Prediction(
            model=RIDGE_MODEL,
            season_code=sample.season_code,
            gameweek=sample.gameweek,
            player_id=sample.player_id,
            position=sample.position,
            predicted=float(value),
            actual=sample.actual,
        )
        for sample, value in zip(target, predicted)
    ]
    ranked = sorted(
        zip(transform.feature_names, coefficients),
        key=lambda item: (-abs(item[1]), item[0]),
    )
    return predictions, {
        "implementation": "numpy-normal-equation-ridge",
        "libraryVersion": str(np.__version__),
        "continuousFeatureCount": len(FEATURES),
        "modelFeatureCount": len(transform.feature_names),
        "ridgePenalty": ridge_penalty,
        "trainingTargetMean": _round(intercept),
        "imputation": {
            name: count
            for name, count in zip(FEATURES, transform.missing_counts)
            if count
        },
        "zeroVarianceFeatures": list(transform.zero_variance_features),
        "coefficientL2Norm": _round(float(np.linalg.norm(coefficients))),
        "largestStandardisedCoefficients": [
            {"feature": name, "coefficient": _round(float(value))}
            for name, value in ranked[:10]
        ],
    }


def _baseline_predictions(
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
    for sample in target:
        position_values = positions[sample.position]
        position_mean = (
            sum(position_values) / len(position_values)
            if position_values
            else global_mean
        )
        history = sorted(players[sample.player_id])
        player_values = [value for _, value in history]
        values = {
            "zero-points": 0.0,
            "position-expanding-mean": position_mean,
            "player-last-points": (
                float(player_values[-1]) if player_values else position_mean
            ),
            "player-rolling3-points": (
                sum(player_values[-3:]) / len(player_values[-3:])
                if player_values
                else position_mean
            ),
            "player-expanding-mean": (
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
                predicted=value,
                actual=sample.actual,
            )
            for name, value in values.items()
        )
    return predictions


def _fold(
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
        "models": _summaries(predictions, names),
        "diagnostics": dict(diagnostics),
    }


def _summaries(
    predictions: Sequence[Prediction],
    names: Sequence[str],
) -> List[Dict[str, Any]]:
    results = [_summarise_model(name, predictions) for name in names]
    results.sort(key=lambda item: (item["metrics"]["mae"], item["name"]))
    return results


def _select_model(
    predictions: Sequence[Prediction],
    names: Sequence[str],
) -> str:
    summaries = _summaries(predictions, names)
    return str(summaries[0]["name"])


def _recommendation(
    candidate_name: str,
    baseline_name: str,
    predictions: Sequence[Prediction],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    candidate = _summarise_model(candidate_name, predictions)
    baseline = _summarise_model(baseline_name, predictions)
    candidate_metrics = candidate["metrics"]
    baseline_metrics = baseline["metrics"]
    mae_improvement = (
        (baseline_metrics["mae"] - candidate_metrics["mae"])
        / baseline_metrics["mae"]
        if baseline_metrics["mae"] > 0
        else 0.0
    )
    candidate_positions = candidate["slices"]["position"]
    baseline_positions = baseline["slices"]["position"]
    position_regressions = {
        position: _round(
            (
                candidate_positions[position]["mae"]
                - baseline_positions[position]["mae"]
            )
            / baseline_positions[position]["mae"]
        )
        for position in candidate_positions
        if position in baseline_positions
        and baseline_positions[position]["mae"] > 0
    }
    fold_wins = 0
    for fold in folds:
        models = {item["name"]: item for item in fold["models"]}
        if (
            models[candidate_name]["metrics"]["mae"]
            < models[baseline_name]["metrics"]["mae"]
        ):
            fold_wins += 1
    checks = {
        "minimumMaeImprovement": (
            mae_improvement >= MINIMUM_HOLDOUT_MAE_IMPROVEMENT
        ),
        "noRmseRegression": (
            candidate_metrics["rmse"] <= baseline_metrics["rmse"]
        ),
        "majorityFoldWins": fold_wins > len(folds) / 2,
        "noMaterialPositionMaeRegression": (
            all(
                regression <= MAXIMUM_POSITION_MAE_REGRESSION
                for regression in position_regressions.values()
            )
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
        "holdoutMaeImprovementFraction": _round(mae_improvement),
        "holdoutFoldWins": fold_wins,
        "holdoutFoldCount": len(folds),
        "maximumPositionMaeRegressionFraction": (
            max(position_regressions.values())
            if position_regressions
            else 0.0
        ),
        "checks": checks,
        "interpretation": (
            "Support permits a separately labelled preseason challenger; "
            "it does not promote or replace Baseline v0 and must be superseded "
            "by current-season rolling-origin evidence."
        ),
    }


def _base(
    season_code: str,
    minimum_training_gameweeks: int,
    holdout_start_gameweek: int,
    ridge_penalty: float,
    capture: Optional[HistoricalCapture],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": "exploratory-preseason-bridge-not-promoted",
        "isPromoted": False,
        "target": "historical-gameweek-total-points",
        "split": "expanding-gameweek-origin-with-locked-late-season-holdout",
        "configuration": {
            "seasonCode": season_code,
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "holdoutStartGameweek": holdout_start_gameweek,
            "ridgePenalty": ridge_penalty,
            "ridgePenaltySelection": "fixed-before-real-data-evaluation",
            "ewmaAlpha": EWMA_ALPHA,
            "candidateModels": list(CANDIDATE_MODELS),
            "baselineModels": list(BASELINE_MODELS),
            "features": list(FEATURES),
            "minimumHoldoutMaeImprovementFraction": (
                MINIMUM_HOLDOUT_MAE_IMPROVEMENT
            ),
            "maximumPositionMaeRegressionFraction": (
                MAXIMUM_POSITION_MAE_REGRESSION
            ),
        },
        "availabilityRule": (
            "Every target feature uses fixture facts for that Gameweek and "
            "only player outcomes from numerically earlier Gameweeks. Final "
            "archived health, source xP and target outcomes are excluded."
        ),
        "limitations": [
            "The archive is a settled end-of-season export, not a sequence of "
            "decision-time captures.",
            "Historical injury chronology, price, ownership and publication "
            "time are unavailable and excluded.",
            "Within-season validation does not measure cross-season concept "
            "drift into the 2026/27 preseason.",
        ],
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
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    development_folds: Sequence[Mapping[str, Any]],
    development_models: Sequence[Mapping[str, Any]],
    holdout_folds: Sequence[Mapping[str, Any]],
    holdout_models: Optional[Sequence[Mapping[str, Any]]],
    recommendation: Optional[Mapping[str, Any]] = None,
) -> Dict[str, Any]:
    report = {
        **base,
        "status": status,
        "reason": reason,
        "development": {
            "foldCount": len(development_folds),
            "folds": list(development_folds),
            "models": list(development_models),
        },
        "lockedHoldout": {
            "opened": bool(holdout_folds),
            "foldCount": len(holdout_folds),
            "folds": list(holdout_folds),
            "models": list(holdout_models or []),
        },
        "preseasonBridgeRecommendation": recommendation,
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "provenance": report["provenance"],
            "developmentGameweeks": [
                fold["gameweek"] for fold in development_folds
            ],
            "holdoutGameweeks": [fold["gameweek"] for fold in holdout_folds],
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate fixed preseason point challengers on a historical "
            "season with a locked late-season holdout."
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
    parser.add_argument(
        "--ridge-penalty",
        type=float,
        default=RIDGE_PENALTY,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_historical_preseason(
            options.database,
            season_code=options.season,
            minimum_training_gameweeks=options.minimum_training_gameweeks,
            holdout_start_gameweek=options.holdout_start_gameweek,
            ridge_penalty=options.ridge_penalty,
        )
        _write_report(report, options.output)
        return 0 if report["status"] == "complete" else 2
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
