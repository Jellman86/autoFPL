from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from collections import defaultdict
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, DefaultDict, Dict, List, Mapping, Optional, Sequence

SCHEMA_VERSION = "1.0"
FEATURE_SET = "official-temporal-v2"
REQUIRED_DATABASE_VERSION = 10
WINDOWS = (1, 3, 5)
EWMA_ALPHA = 0.5
METRICS = (
    "totalPoints",
    "minutes",
    "starts",
    "goalsScored",
    "assists",
    "cleanSheets",
    "goalsConceded",
    "saves",
    "bonus",
    "yellowCards",
    "redCards",
)
UNDERLYING_METRICS = (
    "ownGoals",
    "penaltiesSaved",
    "penaltiesMissed",
    "bps",
    "influence",
    "creativity",
    "threat",
    "ictIndex",
    "clearancesBlocksInterceptions",
    "recoveries",
    "tackles",
    "defensiveContribution",
    "expectedGoals",
    "expectedAssists",
    "expectedGoalInvolvements",
    "expectedGoalsConceded",
)


class FeatureTableError(Exception):
    """Raised when a trustworthy temporal feature table cannot be built."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


@dataclass(frozen=True)
class TargetReplay:
    capture_id: int
    season_code: str
    gameweek: int
    deadline_utc: str
    available_at_utc: str
    bootstrap_sha256: str
    fixtures_sha256: str
    player_count: int


@dataclass(frozen=True)
class OutcomeCapture:
    outcome_capture_id: int
    gameweek: int
    available_at_utc: str
    live_sha256: str
    player_count: int


@dataclass(frozen=True)
class HistoricalOutcome:
    gameweek: int
    values: Mapping[str, Optional[float]]


def build_feature_table(
    database_path: Path,
    season_code: str,
    gameweek: int,
) -> Dict[str, Any]:
    """Build deterministic player features available before one deadline."""
    path = Path(database_path)
    _validate_configuration(path, season_code, gameweek)
    connection = _open_read_only(path)
    try:
        _require_schema(connection)
        replay = _load_target_replay(connection, season_code, gameweek)
        if replay is None:
            raise FeatureTableError(
                "data.target-replay-not-found",
                "No qualifying pre-deadline official replay exists for "
                f"{season_code} Gameweek {gameweek}.",
            )

        outcome_captures = _load_history_captures(connection, replay)
        histories = _load_histories(connection, outcome_captures)
        players = _load_target_players(connection, replay)
        teams = _load_team_features(connection, replay)
        fixtures_by_team = _load_target_fixtures(connection, replay)
        prior_kickoffs_by_team = _load_prior_kickoffs(connection, replay)
        rows = [
            _build_player_row(
                player,
                histories.get(player["playerId"], []),
                fixtures_by_team.get(player["teamId"], []),
                prior_kickoffs_by_team.get(player["teamId"]),
            )
            for player in players
        ]
        if len(rows) != replay.player_count:
            raise FeatureTableError(
                "data.incomplete-target-player-coverage",
                "The selected replay does not contain its declared player count.",
            )

        provenance = {
            "replayCaptureId": replay.capture_id,
            "replayAvailableAtUtc": replay.available_at_utc,
            "bootstrapSha256": replay.bootstrap_sha256,
            "fixturesSha256": replay.fixtures_sha256,
            "historyOutcomeCaptures": [
                {
                    "gameweek": capture.gameweek,
                    "outcomeCaptureId": capture.outcome_capture_id,
                    "availableAtUtc": capture.available_at_utc,
                    "liveSha256": capture.live_sha256,
                    "playerCount": capture.player_count,
                }
                for capture in outcome_captures
            ],
        }
        data_identity = _sha256(
            {
                "seasonCode": replay.season_code,
                "gameweek": replay.gameweek,
                "deadlineUtc": replay.deadline_utc,
                "decisionCutoffUtc": replay.available_at_utc,
                "provenance": provenance,
            }
        )
        table: Dict[str, Any] = {
            "schemaVersion": SCHEMA_VERSION,
            "featureSet": FEATURE_SET,
            "status": "exploratory",
            "isPromoted": False,
            "seasonCode": replay.season_code,
            "gameweek": replay.gameweek,
            "deadlineUtc": replay.deadline_utc,
            "decisionCutoffUtc": replay.available_at_utc,
            "availabilityRule": (
                "replay.availableAtUtc <= target.deadlineUtc; "
                "history.gameweek < target.gameweek; "
                "history.availableAtUtc <= replay.availableAtUtc; "
                "latest eligible correction per history Gameweek"
            ),
            "configuration": {
                "rollingGameweeks": list(WINDOWS),
                "ewmaAlpha": EWMA_ALPHA,
                "historyUnit": "official-gameweek-total",
                "crossSeasonPlayerMatching": False,
                "nullableUnderlyingMetrics": list(UNDERLYING_METRICS),
                "underlyingMetricMissingness": (
                    "null-preserved-with-per-metric-sample-count"
                ),
            },
            "provenance": provenance,
            "dataIdentitySha256": data_identity,
            "teamCount": len(teams),
            "teams": teams,
            "playerCount": len(rows),
            "players": rows,
        }
        table["runIdentitySha256"] = _sha256(table)
        return table
    finally:
        connection.close()


def _validate_configuration(path: Path, season_code: str, gameweek: int) -> None:
    if not path.is_file():
        raise FeatureTableError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    if not season_code.strip() or len(season_code) > 16:
        raise FeatureTableError(
            "configuration.season-code",
            "season_code must be a non-empty value of at most 16 characters.",
        )
    if gameweek < 1 or gameweek > 38:
        raise FeatureTableError(
            "configuration.gameweek",
            "gameweek must be between 1 and 38.",
        )


def _open_read_only(path: Path) -> sqlite3.Connection:
    try:
        connection = sqlite3.connect(
            f"file:{path.resolve()}?mode=ro",
            uri=True,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        connection.execute("PRAGMA foreign_keys = ON;")
        return connection
    except sqlite3.Error as exception:
        raise FeatureTableError(
            "database.open-failed",
            "The SQLite database could not be opened read-only.",
        ) from exception


def _require_schema(connection: sqlite3.Connection) -> None:
    try:
        row = connection.execute(
            "SELECT MAX(version) AS version FROM schema_migrations;"
        ).fetchone()
    except sqlite3.Error as exception:
        raise FeatureTableError(
            "database.schema-missing",
            "The database does not contain autoFPL migrations.",
        ) from exception
    version = None if row is None else row["version"]
    if version is None or version < REQUIRED_DATABASE_VERSION:
        raise FeatureTableError(
            "database.schema-version",
            "Temporal features require autoFPL database version "
            f"{REQUIRED_DATABASE_VERSION} or newer.",
        )


def _load_target_replay(
    connection: sqlite3.Connection,
    season_code: str,
    gameweek: int,
) -> Optional[TargetReplay]:
    row = connection.execute(
        """
        SELECT
            capture.capture_id,
            capture.season_code,
            event.event_id AS gameweek,
            event.deadline_utc,
            capture.available_at_utc,
            capture.bootstrap_sha256,
            capture.fixtures_sha256,
            capture.player_count
        FROM official_fpl_captures AS capture
        INNER JOIN official_fpl_events AS event
            ON event.capture_id = capture.capture_id
           AND event.event_id = :gameweek
        WHERE capture.season_code = :season_code
          AND capture.available_at_utc <= event.deadline_utc
        ORDER BY capture.available_at_utc DESC, capture.capture_id DESC
        LIMIT 1;
        """,
        {"season_code": season_code, "gameweek": gameweek},
    ).fetchone()
    if row is None:
        return None
    return TargetReplay(
        capture_id=row["capture_id"],
        season_code=row["season_code"],
        gameweek=row["gameweek"],
        deadline_utc=row["deadline_utc"],
        available_at_utc=row["available_at_utc"],
        bootstrap_sha256=row["bootstrap_sha256"],
        fixtures_sha256=row["fixtures_sha256"],
        player_count=row["player_count"],
    )


def _load_history_captures(
    connection: sqlite3.Connection,
    replay: TargetReplay,
) -> List[OutcomeCapture]:
    rows = connection.execute(
        """
        SELECT
            outcome.outcome_capture_id,
            outcome.gameweek,
            outcome.available_at_utc,
            outcome.live_sha256,
            outcome.player_count
        FROM official_fpl_outcome_captures AS outcome
        WHERE outcome.season_code = :season_code
          AND outcome.gameweek < :gameweek
          AND outcome.available_at_utc <= :cutoff_utc
          AND NOT EXISTS (
              SELECT 1
              FROM official_fpl_outcome_captures AS newer
              WHERE newer.season_code = outcome.season_code
                AND newer.gameweek = outcome.gameweek
                AND newer.available_at_utc <= :cutoff_utc
                AND (
                    newer.available_at_utc > outcome.available_at_utc
                    OR (
                        newer.available_at_utc = outcome.available_at_utc
                        AND newer.outcome_capture_id > outcome.outcome_capture_id
                    )
                )
          )
        ORDER BY outcome.gameweek;
        """,
        {
            "season_code": replay.season_code,
            "gameweek": replay.gameweek,
            "cutoff_utc": replay.available_at_utc,
        },
    ).fetchall()
    return [
        OutcomeCapture(
            outcome_capture_id=row["outcome_capture_id"],
            gameweek=row["gameweek"],
            available_at_utc=row["available_at_utc"],
            live_sha256=row["live_sha256"],
            player_count=row["player_count"],
        )
        for row in rows
    ]


def _load_histories(
    connection: sqlite3.Connection,
    captures: Sequence[OutcomeCapture],
) -> Mapping[int, List[HistoricalOutcome]]:
    histories: DefaultDict[int, List[HistoricalOutcome]] = defaultdict(list)
    for capture in captures:
        rows = connection.execute(
            """
            SELECT
                player_id,
                total_points,
                minutes,
                starts,
                goals_scored,
                assists,
                clean_sheets,
                goals_conceded,
                saves,
                bonus,
                yellow_cards,
                red_cards,
                own_goals,
                penalties_saved,
                penalties_missed,
                bps,
                influence,
                creativity,
                threat,
                ict_index,
                clearances_blocks_interceptions,
                recoveries,
                tackles,
                defensive_contribution,
                expected_goals,
                expected_assists,
                expected_goal_involvements,
                expected_goals_conceded
            FROM official_fpl_player_outcomes
            WHERE outcome_capture_id = :outcome_capture_id
            ORDER BY player_id;
            """,
            {"outcome_capture_id": capture.outcome_capture_id},
        ).fetchall()
        if len(rows) != capture.player_count:
            raise FeatureTableError(
                "data.incomplete-history-player-coverage",
                "Outcome capture "
                f"{capture.outcome_capture_id} does not contain its declared "
                "player count.",
            )
        for row in rows:
            histories[row["player_id"]].append(
                HistoricalOutcome(
                    gameweek=capture.gameweek,
                    values={
                        "totalPoints": row["total_points"],
                        "minutes": row["minutes"],
                        "starts": row["starts"],
                        "goalsScored": row["goals_scored"],
                        "assists": row["assists"],
                        "cleanSheets": row["clean_sheets"],
                        "goalsConceded": row["goals_conceded"],
                        "saves": row["saves"],
                        "bonus": row["bonus"],
                        "yellowCards": row["yellow_cards"],
                        "redCards": row["red_cards"],
                        "ownGoals": row["own_goals"],
                        "penaltiesSaved": row["penalties_saved"],
                        "penaltiesMissed": row["penalties_missed"],
                        "bps": row["bps"],
                        "influence": row["influence"],
                        "creativity": row["creativity"],
                        "threat": row["threat"],
                        "ictIndex": row["ict_index"],
                        "clearancesBlocksInterceptions": row[
                            "clearances_blocks_interceptions"
                        ],
                        "recoveries": row["recoveries"],
                        "tackles": row["tackles"],
                        "defensiveContribution": row[
                            "defensive_contribution"
                        ],
                        "expectedGoals": row["expected_goals"],
                        "expectedAssists": row["expected_assists"],
                        "expectedGoalInvolvements": row[
                            "expected_goal_involvements"
                        ],
                        "expectedGoalsConceded": row[
                            "expected_goals_conceded"
                        ],
                    },
                )
            )
    return histories


def _load_target_players(
    connection: sqlite3.Connection,
    replay: TargetReplay,
) -> List[Dict[str, Any]]:
    rows = connection.execute(
        """
        SELECT
            player.player_id,
            player.code,
            player.first_name || ' ' || player.second_name AS full_name,
            player.team_id,
            team.short_name AS team_short_name,
            player.position,
            player.price_tenths,
            player.status,
            player.chance_next_round,
            player.selected_by_percent,
            player.total_points,
            player.minutes,
            player.starts
        FROM official_fpl_players AS player
        INNER JOIN official_fpl_teams AS team
            ON team.capture_id = player.capture_id
           AND team.team_id = player.team_id
        WHERE player.capture_id = :capture_id
        ORDER BY player.player_id;
        """,
        {"capture_id": replay.capture_id},
    ).fetchall()
    return [
        {
            "playerId": row["player_id"],
            "playerCode": row["code"],
            "name": row["full_name"],
            "teamId": row["team_id"],
            "teamShortName": row["team_short_name"],
            "position": row["position"],
            "priceTenths": row["price_tenths"],
            "status": row["status"],
            "chanceNextRound": row["chance_next_round"],
            "selectedByPercent": row["selected_by_percent"],
            "cumulativeTotalPoints": row["total_points"],
            "cumulativeMinutes": row["minutes"],
            "cumulativeStarts": row["starts"],
        }
        for row in rows
    ]


def _load_target_fixtures(
    connection: sqlite3.Connection,
    replay: TargetReplay,
) -> Mapping[int, List[Dict[str, Any]]]:
    rows = connection.execute(
        """
        SELECT
            fixture.fixture_id,
            fixture.home_team_id,
            fixture.away_team_id,
            home.short_name AS home_short_name,
            away.short_name AS away_short_name,
            fixture.kickoff_utc
        FROM official_fpl_fixtures AS fixture
        INNER JOIN official_fpl_teams AS home
            ON home.capture_id = fixture.capture_id
           AND home.team_id = fixture.home_team_id
        INNER JOIN official_fpl_teams AS away
            ON away.capture_id = fixture.capture_id
           AND away.team_id = fixture.away_team_id
        WHERE fixture.capture_id = :capture_id
          AND fixture.event_id = :gameweek
        ORDER BY fixture.kickoff_utc, fixture.fixture_id;
        """,
        {"capture_id": replay.capture_id, "gameweek": replay.gameweek},
    ).fetchall()
    fixtures: DefaultDict[int, List[Dict[str, Any]]] = defaultdict(list)
    for row in rows:
        fixtures[row["home_team_id"]].append(
            {
                "fixtureId": row["fixture_id"],
                "opponentTeamId": row["away_team_id"],
                "opponentShortName": row["away_short_name"],
                "isHome": True,
                "kickoffUtc": row["kickoff_utc"],
            }
        )
        fixtures[row["away_team_id"]].append(
            {
                "fixtureId": row["fixture_id"],
                "opponentTeamId": row["home_team_id"],
                "opponentShortName": row["home_short_name"],
                "isHome": False,
                "kickoffUtc": row["kickoff_utc"],
            }
        )
    return fixtures


def _load_team_features(
    connection: sqlite3.Connection,
    replay: TargetReplay,
) -> List[Dict[str, Any]]:
    team_rows = connection.execute(
        """
        SELECT team_id, short_name
        FROM official_fpl_teams
        WHERE capture_id = :capture_id
        ORDER BY team_id;
        """,
        {"capture_id": replay.capture_id},
    ).fetchall()
    fixture_rows = connection.execute(
        """
        SELECT
            fixture.fixture_id,
            fixture.event_id,
            fixture.home_team_id,
            fixture.away_team_id,
            fixture.kickoff_utc,
            fixture.home_score,
            fixture.away_score
        FROM official_fpl_fixtures AS fixture
        WHERE fixture.capture_id = :capture_id
          AND fixture.event_id < :gameweek
          AND fixture.finished = 1
          AND fixture.home_score IS NOT NULL
          AND fixture.away_score IS NOT NULL
        ORDER BY fixture.event_id, fixture.kickoff_utc, fixture.fixture_id;
        """,
        {"capture_id": replay.capture_id, "gameweek": replay.gameweek},
    ).fetchall()
    histories: DefaultDict[int, List[Dict[str, Any]]] = defaultdict(list)
    for row in fixture_rows:
        histories[row["home_team_id"]].append(
            _team_match(
                row,
                opponent_team_id=row["away_team_id"],
                is_home=True,
                goals_for=row["home_score"],
                goals_against=row["away_score"],
            )
        )
        histories[row["away_team_id"]].append(
            _team_match(
                row,
                opponent_team_id=row["home_team_id"],
                is_home=False,
                goals_for=row["away_score"],
                goals_against=row["home_score"],
            )
        )
    return [
        {
            "teamId": row["team_id"],
            "teamShortName": row["short_name"],
            "history": _summarise_team_history(
                histories.get(row["team_id"], [])
            ),
        }
        for row in team_rows
    ]


def _team_match(
    row: sqlite3.Row,
    opponent_team_id: int,
    is_home: bool,
    goals_for: int,
    goals_against: int,
) -> Dict[str, Any]:
    return {
        "gameweek": row["event_id"],
        "fixtureId": row["fixture_id"],
        "opponentTeamId": opponent_team_id,
        "isHome": is_home,
        "kickoffUtc": row["kickoff_utc"],
        "goalsFor": goals_for,
        "goalsAgainst": goals_against,
        "points": 3
        if goals_for > goals_against
        else 1
        if goals_for == goals_against
        else 0,
        "cleanSheet": int(goals_against == 0),
        "scored": int(goals_for > 0),
    }


def _summarise_team_history(
    matches: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    return {
        "matchCount": len(matches),
        "latest": dict(matches[-1]) if matches else None,
        "rolling": {
            str(window): _summarise_team_window(matches[-window:])
            for window in WINDOWS
        },
        "exponentiallyWeighted": _summarise_team_ewma(matches),
    }


def _summarise_team_window(
    matches: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    summary = _summarise_team_segment(matches)
    summary["home"] = _summarise_team_segment(
        [match for match in matches if match["isHome"]]
    )
    summary["away"] = _summarise_team_segment(
        [match for match in matches if not match["isHome"]]
    )
    return summary


def _summarise_team_segment(
    matches: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    return {
        "sampleCount": len(matches),
        "goalsForMean": _mean([match["goalsFor"] for match in matches]),
        "goalsAgainstMean": _mean(
            [match["goalsAgainst"] for match in matches]
        ),
        "pointsPerMatch": _mean([match["points"] for match in matches]),
        "cleanSheetRate": _mean(
            [match["cleanSheet"] for match in matches]
        ),
        "scoredRate": _mean([match["scored"] for match in matches]),
    }


def _summarise_team_ewma(
    matches: Sequence[Mapping[str, Any]],
) -> Optional[Dict[str, Any]]:
    if not matches:
        return None
    return {
        "sampleCount": len(matches),
        "alpha": EWMA_ALPHA,
        "goalsForMean": _ewma([match["goalsFor"] for match in matches]),
        "goalsAgainstMean": _ewma(
            [match["goalsAgainst"] for match in matches]
        ),
        "pointsPerMatch": _ewma([match["points"] for match in matches]),
        "cleanSheetRate": _ewma(
            [match["cleanSheet"] for match in matches]
        ),
        "scoredRate": _ewma([match["scored"] for match in matches]),
    }


def _load_prior_kickoffs(
    connection: sqlite3.Connection,
    replay: TargetReplay,
) -> Mapping[int, str]:
    rows = connection.execute(
        """
        SELECT team_id, MAX(kickoff_utc) AS latest_kickoff_utc
        FROM (
            SELECT home_team_id AS team_id, kickoff_utc
            FROM official_fpl_fixtures
            WHERE capture_id = :capture_id
              AND event_id < :gameweek
              AND kickoff_utc IS NOT NULL
            UNION ALL
            SELECT away_team_id AS team_id, kickoff_utc
            FROM official_fpl_fixtures
            WHERE capture_id = :capture_id
              AND event_id < :gameweek
              AND kickoff_utc IS NOT NULL
        )
        GROUP BY team_id;
        """,
        {"capture_id": replay.capture_id, "gameweek": replay.gameweek},
    ).fetchall()
    return {
        row["team_id"]: row["latest_kickoff_utc"]
        for row in rows
        if row["latest_kickoff_utc"] is not None
    }


def _build_player_row(
    player: Mapping[str, Any],
    history: Sequence[HistoricalOutcome],
    fixtures: Sequence[Mapping[str, Any]],
    prior_kickoff_utc: Optional[str],
) -> Dict[str, Any]:
    ordered_history = sorted(history, key=lambda item: item.gameweek)
    latest = ordered_history[-1] if ordered_history else None
    fixture_list = [dict(fixture) for fixture in fixtures]
    kickoff_values = [
        fixture["kickoffUtc"]
        for fixture in fixture_list
        if fixture["kickoffUtc"] is not None
    ]
    first_kickoff = min(kickoff_values) if kickoff_values else None
    last_kickoff = max(kickoff_values) if kickoff_values else None
    return {
        **player,
        "targetFixtureCount": len(fixture_list),
        "targetFixtures": fixture_list,
        "restDaysBeforeFirstKickoff": _days_between(
            prior_kickoff_utc,
            first_kickoff,
        ),
        "minimumRestDaysWithinGameweek": _days_between(
            first_kickoff,
            last_kickoff,
        )
        if len(kickoff_values) > 1
        else None,
        "history": {
            "sampleCount": len(ordered_history),
            "gameweeks": [item.gameweek for item in ordered_history],
            "hasPriorOutcome": bool(ordered_history),
            "latest": (
                {
                    "gameweek": latest.gameweek,
                    **latest.values,
                    "played": latest.values["minutes"] > 0,
                    "played60": latest.values["minutes"] >= 60,
                }
                if latest is not None
                else None
            ),
            "rolling": {
                str(window): _summarise_window(ordered_history[-window:])
                for window in WINDOWS
            },
            "exponentiallyWeighted": _summarise_ewma(ordered_history),
        },
    }


def _summarise_window(
    history: Sequence[HistoricalOutcome],
) -> Dict[str, Any]:
    summary: Dict[str, Any] = {
        "sampleCount": len(history),
        "playedRate": _mean(
            [int(item.values["minutes"] > 0) for item in history]
        ),
        "played60Rate": _mean(
            [int(item.values["minutes"] >= 60) for item in history]
        ),
    }
    for metric in METRICS:
        summary[f"{metric}Mean"] = _mean(
            [item.values[metric] for item in history]
        )
    for metric in UNDERLYING_METRICS:
        observed = [
            value
            for item in history
            if (value := item.values[metric]) is not None
        ]
        summary[f"{metric}Mean"] = _mean(observed)
        summary[f"{metric}SampleCount"] = len(observed)
    return summary


def _summarise_ewma(
    history: Sequence[HistoricalOutcome],
) -> Optional[Dict[str, Any]]:
    if not history:
        return None
    summary: Dict[str, Any] = {
        "sampleCount": len(history),
        "alpha": EWMA_ALPHA,
        "playedRate": _ewma(
            [int(item.values["minutes"] > 0) for item in history]
        ),
        "played60Rate": _ewma(
            [int(item.values["minutes"] >= 60) for item in history]
        ),
    }
    for metric in METRICS:
        summary[f"{metric}Mean"] = _ewma(
            [item.values[metric] for item in history]
        )
    for metric in UNDERLYING_METRICS:
        observed = [
            value
            for item in history
            if (value := item.values[metric]) is not None
        ]
        summary[f"{metric}Mean"] = _ewma(observed) if observed else None
        summary[f"{metric}SampleCount"] = len(observed)
    return summary


def _mean(values: Sequence[float]) -> Optional[float]:
    if not values:
        return None
    return _rounded(sum(values) / float(len(values)))


def _ewma(values: Sequence[float]) -> float:
    value = float(values[0])
    for current in values[1:]:
        value = (EWMA_ALPHA * current) + ((1.0 - EWMA_ALPHA) * value)
    return _rounded(value)


def _rounded(value: float) -> float:
    return round(value, 6)


def _days_between(
    earlier: Optional[str],
    later: Optional[str],
) -> Optional[float]:
    if earlier is None or later is None:
        return None
    try:
        delta = datetime.fromisoformat(later) - datetime.fromisoformat(earlier)
    except ValueError as exception:
        raise FeatureTableError(
            "data.invalid-kickoff-time",
            "Fixture kickoff times must be ISO-8601 values.",
        ) from exception
    return round(delta.total_seconds() / 86_400.0, 3)


def _sha256(value: Mapping[str, Any]) -> str:
    canonical = json.dumps(
        value,
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=False,
    ).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def write_feature_table(path: Path, table: Mapping[str, Any]) -> None:
    output = Path(path)
    if output.exists():
        raise FeatureTableError(
            "output.exists",
            "The output path already exists and will not be overwritten.",
        )
    output.parent.mkdir(parents=True, exist_ok=True)
    try:
        with output.open("x", encoding="utf-8") as stream:
            json.dump(
                table,
                stream,
                indent=2,
                sort_keys=True,
                ensure_ascii=False,
            )
            stream.write("\n")
    except FileExistsError as exception:
        raise FeatureTableError(
            "output.exists",
            "The output path already exists and will not be overwritten.",
        ) from exception


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Build an exploratory cutoff-safe temporal feature table from "
            "the autoFPL SQLite database."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", required=True)
    parser.add_argument("--gameweek", required=True, type=int)
    parser.add_argument("--output", required=True, type=Path)
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    arguments = _parser().parse_args(argv)
    try:
        table = build_feature_table(
            arguments.database,
            arguments.season,
            arguments.gameweek,
        )
        write_feature_table(arguments.output, table)
        return 0
    except FeatureTableError as exception:
        error = {
            "status": "error",
            "code": exception.code,
            "message": str(exception),
        }
        sys.stderr.write(json.dumps(error, sort_keys=True) + "\n")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
