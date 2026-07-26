from __future__ import annotations

import argparse
import hashlib
import json
import math
import sqlite3
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, DefaultDict, Dict, Iterable, List, Mapping, Optional, Sequence, Tuple

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "baseline-evaluation-v1"
REQUIRED_DATABASE_VERSION = 5
BASELINE_NAMES = (
    "zero-points",
    "position-expanding-mean",
    "player-expanding-mean",
    "player-last-points",
    "official-running-mean",
)


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
    total_points: int


@dataclass(frozen=True)
class Prediction:
    model: str
    season_code: str
    gameweek: int
    player_id: int
    position: str
    predicted: float
    actual: int


def evaluate_database(
    database_path: Path,
    season_code: Optional[str] = None,
    minimum_training_gameweeks: int = 1,
) -> Dict[str, Any]:
    """Evaluate deterministic point baselines with expanding Gameweek origins."""
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
            )

        predictions: List[Prediction] = []
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
            predictions.extend(
                _predict_fold(target_pair, target_rows, training_rows)
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
            )

        models = [
            _summarise_model(name, predictions)
            for name in BASELINE_NAMES
        ]
        models.sort(key=lambda model: (model["metrics"]["mae"], model["name"]))
        return _finish_report(
            base_report,
            status="complete",
            reason=None,
            folds=folds,
            models=models,
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
            outcome.total_points
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
            total_points=row["total_points"],
        )
        for row in rows
    ]


def _predict_fold(
    target_pair: PairedGameweek,
    target_rows: Sequence[PlayerOutcome],
    training: Sequence[Tuple[PairedGameweek, Sequence[PlayerOutcome]]],
) -> List[Prediction]:
    all_training_points: List[int] = []
    position_points: DefaultDict[str, List[int]] = defaultdict(list)
    player_points: DefaultDict[int, List[Tuple[int, int]]] = defaultdict(list)
    for pair, rows in training:
        for row in rows:
            all_training_points.append(row.total_points)
            position_points[row.position].append(row.total_points)
            player_points[row.player_id].append((pair.gameweek, row.total_points))

    if not all_training_points:
        raise EvaluationError(
            "data.empty-training-outcomes",
            "An eligible fold has no player outcomes in its training window.",
        )
    global_mean = _mean(all_training_points)
    predictions: List[Prediction] = []
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
    return predictions


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
            "split": "expanding-window-by-gameweek",
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "trainingOutcomeAvailabilityRule": (
                "outcome.availableAtUtc <= evaluation.deadlineUtc"
            ),
            "baselines": list(BASELINE_NAMES),
        },
        "completePairCount": complete_pair_count,
    }


def _finish_report(
    report: Dict[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    report["status"] = status
    report["reason"] = reason
    report["eligibleFoldCount"] = len(folds)
    report["folds"] = list(folds)
    report["models"] = list(models)
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
            "Evaluate leakage-safe autoFPL point baselines from complete "
            "SQLite replay/outcome pairs."
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
