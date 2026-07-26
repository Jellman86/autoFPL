from __future__ import annotations

import argparse
import hashlib
import json
import math
import sqlite3
import sys
from bisect import bisect_right
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, DefaultDict, Dict, Iterable, List, Mapping, Optional, Sequence, Tuple

SCHEMA_VERSION = "1.3"
EVALUATOR_VERSION = "baseline-evaluation-v4"
REQUIRED_DATABASE_VERSION = 5
BASELINE_NAMES = (
    "zero-points",
    "position-expanding-mean",
    "player-expanding-mean",
    "player-last-points",
    "official-running-mean",
)
PROBABILITY_BASELINE_NAMES = (
    "global-played60-rate",
    "position-played60-rate",
    "player-played60-rate",
    "official-start-rate",
)
EXPECTED_MINUTES_BASELINE_NAMES = (
    "minutes-zero",
    "minutes-global-expanding-mean",
    "minutes-position-expanding-mean",
    "minutes-player-expanding-mean",
    "minutes-player-last",
    "minutes-official-running-mean",
)
POINT_DISTRIBUTION_BASELINE_NAMES = (
    "points-zero-degenerate",
    "points-global-empirical",
    "points-position-empirical",
    "points-player-empirical",
)
MINUTES_DISTRIBUTION_BASELINE_NAMES = (
    "minutes-zero-degenerate",
    "minutes-global-empirical",
    "minutes-position-empirical",
    "minutes-player-empirical",
)
CALIBRATION_BIN_COUNT = 5
LOG_LOSS_EPSILON = 1e-15
DISTRIBUTION_QUANTILES = (0.1, 0.25, 0.5, 0.75, 0.9)
CENTRAL_INTERVAL_COVERAGES = (0.5, 0.8, 0.95)


class EvaluationError(Exception):
    """Raised when an input database cannot support a trustworthy evaluation."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


@dataclass(frozen=True)
class PairedGameweek:
    season_code: str
    gameweek: int
    deadline_utc: str
    replay_capture_id: int
    replay_available_at_utc: str
    bootstrap_sha256: str
    fixtures_sha256: str
    replay_player_count: int
    outcome_capture_id: int
    outcome_available_at_utc: str
    live_sha256: str
    outcome_player_count: int


@dataclass(frozen=True)
class PlayerOutcome:
    player_id: int
    position: str
    cumulative_points_at_deadline: int
    cumulative_minutes_at_deadline: int
    cumulative_starts_at_deadline: int
    total_points: int
    minutes: int


@dataclass(frozen=True)
class Prediction:
    model: str
    season_code: str
    gameweek: int
    player_id: int
    position: str
    predicted: float
    actual: int


@dataclass(frozen=True)
class ProbabilityPrediction:
    model: str
    season_code: str
    gameweek: int
    player_id: int
    position: str
    probability: float
    actual: int


@dataclass(frozen=True)
class EmpiricalDistribution:
    samples: Tuple[int, ...]
    prefix_sums: Tuple[int, ...]
    pairwise_half_mean: float


@dataclass(frozen=True)
class DistributionPrediction:
    model: str
    season_code: str
    gameweek: int
    player_id: int
    position: str
    distribution: EmpiricalDistribution
    actual: int


@dataclass
class FoldPredictions:
    points: List[Prediction]
    played_60: List[ProbabilityPrediction]
    expected_minutes: List[Prediction]
    point_distributions: List[DistributionPrediction]
    minutes_distributions: List[DistributionPrediction]


def evaluate_database(
    database_path: Path,
    season_code: Optional[str] = None,
    minimum_training_gameweeks: int = 1,
) -> Dict[str, Any]:
    """Evaluate point, minutes, and availability baselines by Gameweek."""
    path = Path(database_path)
    if minimum_training_gameweeks < 1:
        raise EvaluationError(
            "configuration.minimum-training-gameweeks",
            "minimum_training_gameweeks must be at least one.",
        )
    if season_code is not None and (
        not season_code.strip() or len(season_code) > 16
    ):
        raise EvaluationError(
            "configuration.season-code",
            "season_code must be a non-empty value of at most 16 characters.",
        )
    if not path.is_file():
        raise EvaluationError(
            "database.not-found",
            "The SQLite database does not exist.",
        )

    connection = _open_read_only(path)
    try:
        _require_schema(connection)
        target_pairs = _load_target_pairs(connection, season_code)
        base_report = _base_report(
            season_code,
            minimum_training_gameweeks,
            len(target_pairs),
        )
        if not target_pairs:
            return _finish_report(
                base_report,
                status="insufficient-data",
                reason="no-complete-replay-outcome-pairs",
                folds=[],
                models=[],
                probability_models=[],
                expected_minutes_models=[],
                point_distribution_models=[],
                minutes_distribution_models=[],
            )

        predictions: List[Prediction] = []
        probability_predictions: List[ProbabilityPrediction] = []
        expected_minutes_predictions: List[Prediction] = []
        point_distribution_predictions: List[DistributionPrediction] = []
        minutes_distribution_predictions: List[DistributionPrediction] = []
        folds: List[Dict[str, Any]] = []
        for target_pair in target_pairs:
            training_pairs = _load_training_pairs(
                connection,
                target_pair.season_code,
                target_pair.gameweek,
                target_pair.deadline_utc,
            )
            if len(training_pairs) < minimum_training_gameweeks:
                continue

            training_rows = [
                (pair, _load_player_rows(connection, pair))
                for pair in training_pairs
            ]
            target_rows = _load_player_rows(connection, target_pair)
            fold_predictions = _predict_fold(
                target_pair,
                target_rows,
                training_rows,
            )
            predictions.extend(fold_predictions.points)
            probability_predictions.extend(fold_predictions.played_60)
            expected_minutes_predictions.extend(
                fold_predictions.expected_minutes
            )
            point_distribution_predictions.extend(
                fold_predictions.point_distributions
            )
            minutes_distribution_predictions.extend(
                fold_predictions.minutes_distributions
            )
            folds.append(
                {
                    "seasonCode": target_pair.season_code,
                    "gameweek": target_pair.gameweek,
                    "deadlineUtc": target_pair.deadline_utc,
                    "target": _pair_identity(target_pair),
                    "training": [
                        _pair_identity(pair) for pair in training_pairs
                    ],
                    "trainingGameweeks": len(training_pairs),
                    "playerCount": len(target_rows),
                }
            )

        if not folds:
            return _finish_report(
                base_report,
                status="insufficient-data",
                reason="no-eligible-rolling-origin-folds",
                folds=[],
                models=[],
                probability_models=[],
                expected_minutes_models=[],
                point_distribution_models=[],
                minutes_distribution_models=[],
            )

        models = [
            _summarise_model(name, predictions)
            for name in BASELINE_NAMES
        ]
        models.sort(key=lambda model: (model["metrics"]["mae"], model["name"]))
        probability_models = [
            _summarise_probability_model(name, probability_predictions)
            for name in PROBABILITY_BASELINE_NAMES
        ]
        probability_models.sort(
            key=lambda model: (
                model["metrics"]["brierScore"],
                model["name"],
            )
        )
        expected_minutes_models = [
            _summarise_model(name, expected_minutes_predictions)
            for name in EXPECTED_MINUTES_BASELINE_NAMES
        ]
        expected_minutes_models.sort(
            key=lambda model: (model["metrics"]["mae"], model["name"])
        )
        point_distribution_models = [
            _summarise_distribution_model(
                name,
                point_distribution_predictions,
            )
            for name in POINT_DISTRIBUTION_BASELINE_NAMES
        ]
        point_distribution_models.sort(
            key=lambda model: (
                model["metrics"]["meanCrps"],
                model["name"],
            )
        )
        minutes_distribution_models = [
            _summarise_distribution_model(
                name,
                minutes_distribution_predictions,
            )
            for name in MINUTES_DISTRIBUTION_BASELINE_NAMES
        ]
        minutes_distribution_models.sort(
            key=lambda model: (
                model["metrics"]["meanCrps"],
                model["name"],
            )
        )
        return _finish_report(
            base_report,
            status="complete",
            reason=None,
            folds=folds,
            models=models,
            probability_models=probability_models,
            expected_minutes_models=expected_minutes_models,
            point_distribution_models=point_distribution_models,
            minutes_distribution_models=minutes_distribution_models,
        )
    finally:
        connection.close()


def _open_read_only(path: Path) -> sqlite3.Connection:
    uri = "{}?mode=ro".format(path.resolve().as_uri())
    try:
        connection = sqlite3.connect(uri, uri=True)
    except sqlite3.Error as exception:
        raise EvaluationError(
            "database.open-failed",
            "The SQLite database could not be opened read-only.",
        ) from exception
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA query_only = ON;")
    connection.execute("PRAGMA foreign_keys = ON;")
    return connection


def _require_schema(connection: sqlite3.Connection) -> None:
    try:
        row = connection.execute(
            "SELECT MAX(version) AS version FROM schema_migrations;"
        ).fetchone()
    except sqlite3.Error as exception:
        raise EvaluationError(
            "database.schema-missing",
            "The database does not contain autoFPL migrations.",
        ) from exception
    version = None if row is None else row["version"]
    if version != REQUIRED_DATABASE_VERSION:
        raise EvaluationError(
            "database.schema-version",
            "Baseline evaluation requires autoFPL database version {}.".format(
                REQUIRED_DATABASE_VERSION
            ),
        )


def _load_target_pairs(
    connection: sqlite3.Connection,
    season_code: Optional[str],
) -> List[PairedGameweek]:
    season_filter = ""
    parameters: Dict[str, Any] = {}
    if season_code is not None:
        season_filter = "AND outcome.season_code = :season_code"
        parameters["season_code"] = season_code
    query = _pair_query(
        outcome_filter=season_filter,
        latest_outcome_filter="",
    )
    return [
        _read_pair(row)
        for row in connection.execute(query, parameters).fetchall()
    ]


def _load_training_pairs(
    connection: sqlite3.Connection,
    season_code: str,
    before_gameweek: int,
    cutoff_utc: str,
) -> List[PairedGameweek]:
    query = _pair_query(
        outcome_filter=(
            "AND outcome.season_code = :season_code "
            "AND outcome.gameweek < :before_gameweek "
            "AND outcome.available_at_utc <= :cutoff_utc"
        ),
        latest_outcome_filter=(
            "AND newer.available_at_utc <= :cutoff_utc"
        ),
    )
    parameters = {
        "season_code": season_code,
        "before_gameweek": before_gameweek,
        "cutoff_utc": cutoff_utc,
    }
    return [
        _read_pair(row)
        for row in connection.execute(query, parameters).fetchall()
    ]


def _pair_query(outcome_filter: str, latest_outcome_filter: str) -> str:
    return """
        SELECT
            outcome.season_code,
            outcome.gameweek,
            event.deadline_utc,
            replay.capture_id AS replay_capture_id,
            replay.available_at_utc AS replay_available_at_utc,
            replay.bootstrap_sha256,
            replay.fixtures_sha256,
            replay.player_count AS replay_player_count,
            outcome.outcome_capture_id,
            outcome.available_at_utc AS outcome_available_at_utc,
            outcome.live_sha256,
            outcome.player_count AS outcome_player_count
        FROM official_fpl_outcome_captures AS outcome
        INNER JOIN official_fpl_captures AS replay
            ON replay.season_code = outcome.season_code
        INNER JOIN official_fpl_events AS event
            ON event.capture_id = replay.capture_id
           AND event.event_id = outcome.gameweek
        WHERE replay.available_at_utc <= event.deadline_utc
          {outcome_filter}
          AND NOT EXISTS (
              SELECT 1
              FROM official_fpl_outcome_captures AS newer
              WHERE newer.season_code = outcome.season_code
                AND newer.gameweek = outcome.gameweek
                {latest_outcome_filter}
                AND (
                    newer.available_at_utc > outcome.available_at_utc
                    OR (
                        newer.available_at_utc = outcome.available_at_utc
                        AND newer.outcome_capture_id > outcome.outcome_capture_id
                    )
                )
          )
          AND replay.capture_id = (
              SELECT candidate.capture_id
              FROM official_fpl_captures AS candidate
              INNER JOIN official_fpl_events AS candidate_event
                  ON candidate_event.capture_id = candidate.capture_id
                 AND candidate_event.event_id = outcome.gameweek
              WHERE candidate.season_code = outcome.season_code
                AND candidate.available_at_utc <= candidate_event.deadline_utc
              ORDER BY candidate.available_at_utc DESC, candidate.capture_id DESC
              LIMIT 1
          )
        ORDER BY outcome.season_code, outcome.gameweek;
        """.format(
        outcome_filter=outcome_filter,
        latest_outcome_filter=latest_outcome_filter,
    )


def _read_pair(row: sqlite3.Row) -> PairedGameweek:
    return PairedGameweek(
        season_code=row["season_code"],
        gameweek=row["gameweek"],
        deadline_utc=row["deadline_utc"],
        replay_capture_id=row["replay_capture_id"],
        replay_available_at_utc=row["replay_available_at_utc"],
        bootstrap_sha256=row["bootstrap_sha256"],
        fixtures_sha256=row["fixtures_sha256"],
        replay_player_count=row["replay_player_count"],
        outcome_capture_id=row["outcome_capture_id"],
        outcome_available_at_utc=row["outcome_available_at_utc"],
        live_sha256=row["live_sha256"],
        outcome_player_count=row["outcome_player_count"],
    )


def _load_player_rows(
    connection: sqlite3.Connection,
    pair: PairedGameweek,
) -> List[PlayerOutcome]:
    rows = connection.execute(
        """
        SELECT
            player.player_id,
            player.position,
            player.total_points AS cumulative_points_at_deadline,
            player.minutes AS cumulative_minutes_at_deadline,
            player.starts AS cumulative_starts_at_deadline,
            outcome.total_points,
            outcome.minutes
        FROM official_fpl_players AS player
        INNER JOIN official_fpl_player_outcomes AS outcome
            ON outcome.player_id = player.player_id
           AND outcome.outcome_capture_id = :outcome_capture_id
        WHERE player.capture_id = :replay_capture_id
        ORDER BY player.player_id;
        """,
        {
            "outcome_capture_id": pair.outcome_capture_id,
            "replay_capture_id": pair.replay_capture_id,
        },
    ).fetchall()
    expected = pair.replay_player_count
    if (
        pair.outcome_player_count < expected
        or len(rows) != expected
        or len({row["player_id"] for row in rows}) != expected
    ):
        raise EvaluationError(
            "data.incomplete-player-coverage",
            "Replay/outcome player coverage is incomplete for {} Gameweek {}.".format(
                pair.season_code,
                pair.gameweek,
            ),
        )
    return [
        PlayerOutcome(
            player_id=row["player_id"],
            position=row["position"],
            cumulative_points_at_deadline=row[
                "cumulative_points_at_deadline"
            ],
            cumulative_minutes_at_deadline=row[
                "cumulative_minutes_at_deadline"
            ],
            cumulative_starts_at_deadline=row[
                "cumulative_starts_at_deadline"
            ],
            total_points=row["total_points"],
            minutes=row["minutes"],
        )
        for row in rows
    ]


def _predict_fold(
    target_pair: PairedGameweek,
    target_rows: Sequence[PlayerOutcome],
    training: Sequence[Tuple[PairedGameweek, Sequence[PlayerOutcome]]],
) -> FoldPredictions:
    all_training_points: List[int] = []
    position_points: DefaultDict[str, List[int]] = defaultdict(list)
    player_points: DefaultDict[int, List[Tuple[int, int]]] = defaultdict(list)
    all_training_minutes: List[int] = []
    position_minutes: DefaultDict[str, List[int]] = defaultdict(list)
    player_minutes: DefaultDict[int, List[Tuple[int, int]]] = defaultdict(list)
    all_training_played_60: List[int] = []
    position_played_60: DefaultDict[str, List[int]] = defaultdict(list)
    player_played_60: DefaultDict[int, List[Tuple[int, int]]] = defaultdict(list)
    for pair, rows in training:
        for row in rows:
            all_training_points.append(row.total_points)
            position_points[row.position].append(row.total_points)
            player_points[row.player_id].append((pair.gameweek, row.total_points))
            all_training_minutes.append(row.minutes)
            position_minutes[row.position].append(row.minutes)
            player_minutes[row.player_id].append((pair.gameweek, row.minutes))
            played_60 = int(row.minutes >= 60)
            all_training_played_60.append(played_60)
            position_played_60[row.position].append(played_60)
            player_played_60[row.player_id].append(
                (pair.gameweek, played_60)
            )

    if not all_training_points:
        raise EvaluationError(
            "data.empty-training-outcomes",
            "An eligible fold has no player outcomes in its training window.",
        )
    global_mean = _mean(all_training_points)
    global_minutes_mean = _mean(all_training_minutes)
    global_played_60_rate = _smoothed_rate(all_training_played_60)
    zero_distribution = _empirical_distribution([0])
    global_points_distribution = _empirical_distribution(all_training_points)
    global_minutes_distribution = _empirical_distribution(
        all_training_minutes
    )
    position_points_distributions = {
        position: _empirical_distribution(values)
        for position, values in position_points.items()
    }
    position_minutes_distributions = {
        position: _empirical_distribution(values)
        for position, values in position_minutes.items()
    }
    predictions: List[Prediction] = []
    probability_predictions: List[ProbabilityPrediction] = []
    expected_minutes_predictions: List[Prediction] = []
    point_distribution_predictions: List[DistributionPrediction] = []
    minutes_distribution_predictions: List[DistributionPrediction] = []
    for row in target_rows:
        position_mean = _mean(position_points[row.position]) if position_points[
            row.position
        ] else global_mean
        history = sorted(player_points[row.player_id])
        player_mean = (
            _mean([points for _, points in history])
            if history
            else position_mean
        )
        last_points = history[-1][1] if history else position_mean
        official_running_mean = row.cumulative_points_at_deadline / float(
            target_pair.gameweek - 1
        )
        values = {
            "zero-points": 0.0,
            "position-expanding-mean": position_mean,
            "player-expanding-mean": player_mean,
            "player-last-points": float(last_points),
            "official-running-mean": official_running_mean,
        }
        predictions.extend(
            Prediction(
                model=name,
                season_code=target_pair.season_code,
                gameweek=target_pair.gameweek,
                player_id=row.player_id,
                position=row.position,
                predicted=value,
                actual=row.total_points,
            )
            for name, value in values.items()
        )
        position_played_60_rate = (
            _smoothed_rate(position_played_60[row.position])
            if position_played_60[row.position]
            else global_played_60_rate
        )
        played_60_history = sorted(player_played_60[row.player_id])
        player_played_60_rate = (
            _smoothed_rate([played_60 for _, played_60 in played_60_history])
            if played_60_history
            else position_played_60_rate
        )
        prior_gameweeks = target_pair.gameweek - 1
        official_start_rate = min(
            1.0,
            (row.cumulative_starts_at_deadline + 1.0)
            / (prior_gameweeks + 2.0),
        )
        probability_values = {
            "global-played60-rate": global_played_60_rate,
            "position-played60-rate": position_played_60_rate,
            "player-played60-rate": player_played_60_rate,
            "official-start-rate": official_start_rate,
        }
        probability_predictions.extend(
            ProbabilityPrediction(
                model=name,
                season_code=target_pair.season_code,
                gameweek=target_pair.gameweek,
                player_id=row.player_id,
                position=row.position,
                probability=value,
                actual=int(row.minutes >= 60),
            )
            for name, value in probability_values.items()
        )
        position_minutes_mean = (
            _mean(position_minutes[row.position])
            if position_minutes[row.position]
            else global_minutes_mean
        )
        minutes_history = sorted(player_minutes[row.player_id])
        player_minutes_mean = (
            _mean([minutes for _, minutes in minutes_history])
            if minutes_history
            else position_minutes_mean
        )
        last_minutes = (
            float(minutes_history[-1][1])
            if minutes_history
            else position_minutes_mean
        )
        official_running_minutes_mean = (
            row.cumulative_minutes_at_deadline
            / float(target_pair.gameweek - 1)
        )
        expected_minutes_values = {
            "minutes-zero": 0.0,
            "minutes-global-expanding-mean": global_minutes_mean,
            "minutes-position-expanding-mean": position_minutes_mean,
            "minutes-player-expanding-mean": player_minutes_mean,
            "minutes-player-last": last_minutes,
            "minutes-official-running-mean": (
                official_running_minutes_mean
            ),
        }
        expected_minutes_predictions.extend(
            Prediction(
                model=name,
                season_code=target_pair.season_code,
                gameweek=target_pair.gameweek,
                player_id=row.player_id,
                position=row.position,
                predicted=value,
                actual=row.minutes,
            )
            for name, value in expected_minutes_values.items()
        )
        position_points_distribution = position_points_distributions.get(
            row.position,
            global_points_distribution,
        )
        player_points_distribution = (
            _empirical_distribution(
                [points for _, points in history]
            )
            if history
            else position_points_distribution
        )
        point_distribution_values = {
            "points-zero-degenerate": zero_distribution,
            "points-global-empirical": global_points_distribution,
            "points-position-empirical": position_points_distribution,
            "points-player-empirical": player_points_distribution,
        }
        point_distribution_predictions.extend(
            DistributionPrediction(
                model=name,
                season_code=target_pair.season_code,
                gameweek=target_pair.gameweek,
                player_id=row.player_id,
                position=row.position,
                distribution=distribution,
                actual=row.total_points,
            )
            for name, distribution in point_distribution_values.items()
        )
        position_minutes_distribution = (
            position_minutes_distributions.get(
                row.position,
                global_minutes_distribution,
            )
        )
        player_minutes_distribution = (
            _empirical_distribution(
                [minutes for _, minutes in minutes_history]
            )
            if minutes_history
            else position_minutes_distribution
        )
        minutes_distribution_values = {
            "minutes-zero-degenerate": zero_distribution,
            "minutes-global-empirical": global_minutes_distribution,
            "minutes-position-empirical": position_minutes_distribution,
            "minutes-player-empirical": player_minutes_distribution,
        }
        minutes_distribution_predictions.extend(
            DistributionPrediction(
                model=name,
                season_code=target_pair.season_code,
                gameweek=target_pair.gameweek,
                player_id=row.player_id,
                position=row.position,
                distribution=distribution,
                actual=row.minutes,
            )
            for name, distribution in minutes_distribution_values.items()
        )
    return FoldPredictions(
        points=predictions,
        played_60=probability_predictions,
        expected_minutes=expected_minutes_predictions,
        point_distributions=point_distribution_predictions,
        minutes_distributions=minutes_distribution_predictions,
    )


def _summarise_model(
    name: str,
    predictions: Sequence[Prediction],
) -> Dict[str, Any]:
    selected = [prediction for prediction in predictions if prediction.model == name]
    by_position: DefaultDict[str, List[Prediction]] = defaultdict(list)
    for prediction in selected:
        by_position[prediction.position].append(prediction)
    return {
        "name": name,
        "metrics": _metrics(selected),
        "slices": {
            "position": {
                position: _metrics(items)
                for position, items in sorted(by_position.items())
            }
        },
    }


def _metrics(predictions: Sequence[Prediction]) -> Dict[str, Any]:
    if not predictions:
        raise EvaluationError(
            "evaluation.empty-predictions",
            "A baseline produced no predictions.",
        )
    errors = [
        prediction.predicted - prediction.actual
        for prediction in predictions
    ]
    return {
        "count": len(errors),
        "mae": _round(sum(abs(error) for error in errors) / len(errors)),
        "rmse": _round(
            math.sqrt(sum(error * error for error in errors) / len(errors))
        ),
        "meanError": _round(sum(errors) / len(errors)),
    }


def _summarise_distribution_model(
    name: str,
    predictions: Sequence[DistributionPrediction],
) -> Dict[str, Any]:
    selected = [
        prediction for prediction in predictions if prediction.model == name
    ]
    by_position: DefaultDict[str, List[DistributionPrediction]] = defaultdict(
        list
    )
    for prediction in selected:
        by_position[prediction.position].append(prediction)
    return {
        "name": name,
        "metrics": _distribution_metrics(selected),
        "slices": {
            "position": {
                position: _distribution_metrics(items)
                for position, items in sorted(by_position.items())
            }
        },
    }


def _distribution_metrics(
    predictions: Sequence[DistributionPrediction],
) -> Dict[str, Any]:
    if not predictions:
        raise EvaluationError(
            "evaluation.empty-distribution-predictions",
            "A distribution baseline produced no predictions.",
        )
    count = len(predictions)
    quantile_calibration: List[Dict[str, Any]] = []
    pinball_losses: List[float] = []
    for quantile in DISTRIBUTION_QUANTILES:
        predicted_quantiles = [
            _empirical_quantile(
                prediction.distribution,
                quantile,
            )
            for prediction in predictions
        ]
        observed_rate = sum(
            int(prediction.actual <= predicted)
            for prediction, predicted in zip(
                predictions,
                predicted_quantiles,
            )
        ) / count
        quantile_calibration.append(
            {
                "quantile": quantile,
                "observedAtOrBelowRate": _round(observed_rate),
                "absoluteGap": _round(abs(observed_rate - quantile)),
            }
        )
        pinball_losses.extend(
            _pinball_loss(prediction.actual, predicted, quantile)
            for prediction, predicted in zip(
                predictions,
                predicted_quantiles,
            )
        )

    central_intervals: List[Dict[str, Any]] = []
    for nominal_coverage in CENTRAL_INTERVAL_COVERAGES:
        lower_quantile = (1.0 - nominal_coverage) / 2.0
        upper_quantile = 1.0 - lower_quantile
        bounds = [
            (
                _empirical_quantile(
                    prediction.distribution,
                    lower_quantile,
                ),
                _empirical_quantile(
                    prediction.distribution,
                    upper_quantile,
                ),
            )
            for prediction in predictions
        ]
        observed_coverage = sum(
            int(lower <= prediction.actual <= upper)
            for prediction, (lower, upper) in zip(predictions, bounds)
        ) / count
        central_intervals.append(
            {
                "nominalCoverage": nominal_coverage,
                "observedCoverage": _round(observed_coverage),
                "meanWidth": _round(
                    sum(upper - lower for lower, upper in bounds) / count
                ),
            }
        )

    medians = [
        _empirical_quantile(prediction.distribution, 0.5)
        for prediction in predictions
    ]
    return {
        "count": count,
        "meanCrps": _round(
            sum(
                _empirical_crps(
                    prediction.distribution,
                    prediction.actual,
                )
                for prediction in predictions
            )
            / count
        ),
        "medianMae": _round(
            sum(
                abs(prediction.actual - median)
                for prediction, median in zip(predictions, medians)
            )
            / count
        ),
        "meanPinballLoss": _round(
            sum(pinball_losses) / len(pinball_losses)
        ),
        "meanDistributionSampleCount": _round(
            sum(
                len(prediction.distribution.samples)
                for prediction in predictions
            )
            / count
        ),
        "quantileCalibration": quantile_calibration,
        "centralIntervals": central_intervals,
    }


def _summarise_probability_model(
    name: str,
    predictions: Sequence[ProbabilityPrediction],
) -> Dict[str, Any]:
    selected = [
        prediction for prediction in predictions if prediction.model == name
    ]
    by_position: DefaultDict[str, List[ProbabilityPrediction]] = defaultdict(
        list
    )
    for prediction in selected:
        by_position[prediction.position].append(prediction)
    metrics, calibration_bins = _probability_metrics(
        selected,
        include_calibration_bins=True,
    )
    return {
        "name": name,
        "metrics": metrics,
        "calibrationBins": calibration_bins,
        "slices": {
            "position": {
                position: _probability_metrics(
                    items,
                    include_calibration_bins=False,
                )[0]
                for position, items in sorted(by_position.items())
            }
        },
    }


def _probability_metrics(
    predictions: Sequence[ProbabilityPrediction],
    include_calibration_bins: bool,
) -> Tuple[Dict[str, Any], List[Dict[str, Any]]]:
    if not predictions:
        raise EvaluationError(
            "evaluation.empty-probability-predictions",
            "A probability baseline produced no predictions.",
        )
    calibration_groups = _calibration_groups(predictions)
    calibration_bins = _calibration_bins(calibration_groups)
    count = len(predictions)
    brier_score = sum(
        (prediction.probability - prediction.actual) ** 2
        for prediction in predictions
    ) / count
    log_loss = -sum(
        prediction.actual * math.log(
            min(
                1.0 - LOG_LOSS_EPSILON,
                max(LOG_LOSS_EPSILON, prediction.probability),
            )
        )
        + (1 - prediction.actual)
        * math.log(
            min(
                1.0 - LOG_LOSS_EPSILON,
                max(LOG_LOSS_EPSILON, 1.0 - prediction.probability),
            )
        )
        for prediction in predictions
    ) / count
    expected_calibration_error = sum(
        len(items)
        / count
        * abs(
            sum(item.probability for item in items) / len(items)
            - sum(item.actual for item in items) / len(items)
        )
        for items in calibration_groups.values()
    )
    metrics = {
        "count": count,
        "brierScore": _round(brier_score),
        "logLoss": _round(log_loss),
        "observedRate": _round(
            sum(prediction.actual for prediction in predictions) / count
        ),
        "meanProbability": _round(
            sum(prediction.probability for prediction in predictions) / count
        ),
        "expectedCalibrationError": _round(expected_calibration_error),
    }
    return metrics, calibration_bins if include_calibration_bins else []


def _calibration_groups(
    predictions: Sequence[ProbabilityPrediction],
) -> Dict[int, List[ProbabilityPrediction]]:
    bins: DefaultDict[int, List[ProbabilityPrediction]] = defaultdict(list)
    for prediction in predictions:
        if not 0.0 <= prediction.probability <= 1.0:
            raise EvaluationError(
                "evaluation.invalid-probability",
                "A probability baseline produced a value outside [0, 1].",
            )
        index = min(
            int(prediction.probability * CALIBRATION_BIN_COUNT),
            CALIBRATION_BIN_COUNT - 1,
        )
        bins[index].append(prediction)
    return dict(sorted(bins.items()))


def _calibration_bins(
    groups: Mapping[int, Sequence[ProbabilityPrediction]],
) -> List[Dict[str, Any]]:
    output: List[Dict[str, Any]] = []
    for index, items in groups.items():
        count = len(items)
        mean_probability = sum(item.probability for item in items) / count
        observed_rate = sum(item.actual for item in items) / count
        output.append(
            {
                "lowerBoundInclusive": _round(
                    index / float(CALIBRATION_BIN_COUNT)
                ),
                "upperBound": _round(
                    (index + 1) / float(CALIBRATION_BIN_COUNT)
                ),
                "upperBoundInclusive": index == CALIBRATION_BIN_COUNT - 1,
                "count": count,
                "meanProbability": _round(mean_probability),
                "observedRate": _round(observed_rate),
                "absoluteGap": _round(
                    abs(mean_probability - observed_rate)
                ),
            }
        )
    return output


def _base_report(
    season_code: Optional[str],
    minimum_training_gameweeks: int,
    complete_pair_count: int,
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": "exploratory-baseline-not-promoted",
        "configuration": {
            "seasonCode": season_code,
            "target": "official-fpl-total-points",
            "probabilityTarget": (
                "official-fpl-played-at-least-60-minutes"
            ),
            "expectedMinutesTarget": "official-fpl-gameweek-total-minutes",
            "pointDistributionTarget": (
                "official-fpl-gameweek-total-points-distribution"
            ),
            "minutesDistributionTarget": (
                "official-fpl-gameweek-total-minutes-distribution"
            ),
            "split": "expanding-window-by-gameweek",
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "trainingOutcomeAvailabilityRule": (
                "outcome.availableAtUtc <= evaluation.deadlineUtc"
            ),
            "baselines": list(BASELINE_NAMES),
            "probabilityBaselines": list(PROBABILITY_BASELINE_NAMES),
            "expectedMinutesBaselines": list(
                EXPECTED_MINUTES_BASELINE_NAMES
            ),
            "pointDistributionBaselines": list(
                POINT_DISTRIBUTION_BASELINE_NAMES
            ),
            "minutesDistributionBaselines": list(
                MINUTES_DISTRIBUTION_BASELINE_NAMES
            ),
            "probabilitySmoothing": "beta-posterior-mean-alpha-1-beta-1",
            "calibrationBinCount": CALIBRATION_BIN_COUNT,
            "empiricalQuantileMethod": "linear-type-7",
            "distributionQuantiles": list(DISTRIBUTION_QUANTILES),
            "centralIntervalCoverages": list(
                CENTRAL_INTERVAL_COVERAGES
            ),
        },
        "completePairCount": complete_pair_count,
    }


def _finish_report(
    report: Dict[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
    probability_models: Sequence[Mapping[str, Any]],
    expected_minutes_models: Sequence[Mapping[str, Any]],
    point_distribution_models: Sequence[Mapping[str, Any]],
    minutes_distribution_models: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    report["status"] = status
    report["reason"] = reason
    report["eligibleFoldCount"] = len(folds)
    report["folds"] = list(folds)
    report["models"] = list(models)
    report["probabilityModels"] = list(probability_models)
    report["expectedMinutesModels"] = list(expected_minutes_models)
    report["pointDistributionModels"] = list(point_distribution_models)
    report["minutesDistributionModels"] = list(
        minutes_distribution_models
    )
    report["dataIdentitySha256"] = _sha256_json(
        [
            {
                "seasonCode": fold["seasonCode"],
                "gameweek": fold["gameweek"],
                "target": fold["target"],
                "training": fold["training"],
            }
            for fold in folds
        ]
    )
    report["runIdentitySha256"] = _sha256_json(report)
    return report


def _pair_identity(pair: PairedGameweek) -> Dict[str, Any]:
    return {
        "seasonCode": pair.season_code,
        "gameweek": pair.gameweek,
        "deadlineUtc": pair.deadline_utc,
        "replayCaptureId": pair.replay_capture_id,
        "replayAvailableAtUtc": pair.replay_available_at_utc,
        "bootstrapSha256": pair.bootstrap_sha256,
        "fixturesSha256": pair.fixtures_sha256,
        "outcomeCaptureId": pair.outcome_capture_id,
        "outcomeAvailableAtUtc": pair.outcome_available_at_utc,
        "liveSha256": pair.live_sha256,
    }


def _mean(values: Iterable[int]) -> float:
    materialised = list(values)
    return sum(materialised) / float(len(materialised))


def _smoothed_rate(values: Iterable[int]) -> float:
    materialised = list(values)
    return (sum(materialised) + 1.0) / (len(materialised) + 2.0)


def _empirical_distribution(values: Iterable[int]) -> EmpiricalDistribution:
    samples = tuple(sorted(values))
    if not samples:
        raise EvaluationError(
            "evaluation.empty-empirical-distribution",
            "An empirical distribution has no training samples.",
        )
    prefix_sums = [0]
    for sample in samples:
        prefix_sums.append(prefix_sums[-1] + sample)
    count = len(samples)
    pairwise_half_mean = sum(
        (2 * index - count - 1) * sample
        for index, sample in enumerate(samples, start=1)
    ) / float(count * count)
    return EmpiricalDistribution(
        samples=samples,
        prefix_sums=tuple(prefix_sums),
        pairwise_half_mean=pairwise_half_mean,
    )


def _empirical_crps(
    distribution: EmpiricalDistribution,
    actual: int,
) -> float:
    samples = distribution.samples
    count = len(samples)
    split = bisect_right(samples, actual)
    left_distance = (
        actual * split - distribution.prefix_sums[split]
    )
    right_distance = (
        distribution.prefix_sums[count]
        - distribution.prefix_sums[split]
        - actual * (count - split)
    )
    mean_absolute_distance = (
        left_distance + right_distance
    ) / float(count)
    return mean_absolute_distance - distribution.pairwise_half_mean


def _empirical_quantile(
    distribution: EmpiricalDistribution,
    quantile: float,
) -> float:
    samples = distribution.samples
    position = (len(samples) - 1) * quantile
    lower_index = int(math.floor(position))
    upper_index = int(math.ceil(position))
    fraction = position - lower_index
    return (
        samples[lower_index] * (1.0 - fraction)
        + samples[upper_index] * fraction
    )


def _pinball_loss(actual: int, predicted: float, quantile: float) -> float:
    error = actual - predicted
    return quantile * error if error >= 0 else (quantile - 1.0) * error


def _round(value: float) -> float:
    return round(value, 6)


def _sha256_json(value: Any) -> str:
    encoded = json.dumps(
        value,
        ensure_ascii=True,
        allow_nan=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _write_report(report: Mapping[str, Any], output_path: Optional[Path]) -> None:
    rendered = json.dumps(
        report,
        ensure_ascii=True,
        allow_nan=False,
        indent=2,
        sort_keys=True,
    )
    if output_path is None:
        sys.stdout.write(rendered)
        sys.stdout.write("\n")
        return
    path = Path(output_path)
    try:
        with path.open("x", encoding="utf-8", newline="\n") as stream:
            stream.write(rendered)
            stream.write("\n")
    except FileExistsError as exception:
        raise EvaluationError(
            "output.already-exists",
            "The output path already exists; refusing to overwrite it.",
        ) from exception
    except OSError as exception:
        raise EvaluationError(
            "output.write-failed",
            "The evaluation report could not be written.",
        ) from exception


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate leakage-safe autoFPL point, minutes, availability, "
            "and empirical distribution baselines from complete SQLite pairs."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season")
    parser.add_argument(
        "--minimum-training-gameweeks",
        type=int,
        default=1,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_database(
            options.database,
            season_code=options.season,
            minimum_training_gameweeks=options.minimum_training_gameweeks,
        )
        _write_report(report, options.output)
    except EvaluationError as exception:
        error = {
            "schemaVersion": SCHEMA_VERSION,
            "status": "error",
            "errorCode": exception.code,
            "message": str(exception),
        }
        sys.stderr.write(json.dumps(error, sort_keys=True))
        sys.stderr.write("\n")
        return 1
    return 0 if report["status"] == "complete" else 2


if __name__ == "__main__":
    raise SystemExit(main())
