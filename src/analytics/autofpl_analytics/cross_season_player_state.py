from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence

SCHEMA_VERSION = "1.0"
STATE_SET = "cross-season-player-state-v1"
REQUIRED_DATABASE_VERSION = 19
PRIOR_SEASON = "2025-26"
WINDOWS = (1, 3, 5, 10)
EWMA_ALPHA = 0.25
METRICS = (
    "minutes",
    "starts",
    "totalPoints",
    "expectedGoals",
    "expectedAssists",
    "expectedGoalInvolvements",
    "expectedGoalsConceded",
    "defensiveContribution",
    "recoveries",
    "tackles",
)


class CrossSeasonPlayerStateError(Exception):
    """Raised when a trustworthy cross-season state cannot be built."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


def build_cross_season_player_state(
    database_path: Path,
    season_code: str,
    gameweek: int,
) -> Dict[str, Any]:
    path = Path(database_path)
    _validate(path, season_code, gameweek)
    connection = _open_read_only(path)
    try:
        _require_schema(connection)
        target = _load_target(connection, season_code, gameweek)
        if target is None:
            raise CrossSeasonPlayerStateError(
                "data.target-capture-not-found",
                "No qualifying official target capture exists for "
                f"{season_code} Gameweek {gameweek}.",
            )
        archive = _load_archive(connection, target)
        if archive is None:
            raise CrossSeasonPlayerStateError(
                "data.prior-season-archive-not-found",
                f"No {PRIOR_SEASON} archive was available by the target cutoff.",
            )
        histories, historical_players, historical_row_count = _load_history(
            connection,
            int(archive["captureId"]),
        )
        if (
            len(historical_players) != int(archive["playerCount"])
            or len(historical_players) != int(archive["stableCodeCount"])
            or historical_row_count != int(archive["playerGameweekCount"])
        ):
            raise CrossSeasonPlayerStateError(
                "data.incomplete-prior-season-coverage",
                "The historical archive rows do not match declared coverage.",
            )
        current_players = _load_current_players(
            connection,
            int(target["captureId"]),
        )
        if len(current_players) != int(target["playerCount"]):
            raise CrossSeasonPlayerStateError(
                "data.incomplete-current-player-coverage",
                "The target capture does not contain its declared player count.",
            )
        current_codes = [
            int(player["playerCode"]) for player in current_players
        ]
        if len(current_codes) != len(set(current_codes)):
            raise CrossSeasonPlayerStateError(
                "data.ambiguous-current-player-code",
                "Current official stable player codes must be unique.",
            )

        players = [
            _build_player(
                current,
                historical_players.get(int(current["playerCode"])),
                histories.get(int(current["playerCode"]), {}),
            )
            for current in current_players
        ]
        matched = sum(player["hasPriorSeasonIdentity"] for player in players)
        base: Dict[str, Any] = {
            "schemaVersion": SCHEMA_VERSION,
            "stateSet": STATE_SET,
            "status": "exploratory-data-artifact",
            "isPromoted": False,
            "influencesForecast": False,
            "seasonCode": season_code,
            "gameweek": gameweek,
            "deadlineUtc": target["deadlineUtc"],
            "decisionCutoffUtc": target["availableAtUtc"],
            "identityRule": (
                "current official player.code = prior archive player_code"
            ),
            "healthPrecedenceRule": (
                "current official status/news/chance is authoritative; "
                "archived final health and prior participation are durability "
                "priors only"
            ),
            "configuration": {
                "priorSeason": PRIOR_SEASON,
                "historyUnit": "prior-season-gameweek-total",
                "rollingGameweeks": list(WINDOWS),
                "ewmaAlpha": EWMA_ALPHA,
                "ewmaStatus": "fixed-exploratory-not-fitted",
                "xPExcluded": True,
                "teamChangeSignal": "normalized-name-comparison-exploratory",
            },
            "provenance": {
                "officialCaptureId": target["captureId"],
                "officialBootstrapSha256": target["bootstrapSha256"],
                "officialFixturesSha256": target["fixturesSha256"],
                "historicalCaptureId": archive["captureId"],
                "historicalSourceKey": archive["sourceKey"],
                "historicalSourceRevision": archive["sourceRevision"],
                "historicalAvailableAtUtc": archive["availableAtUtc"],
                "historicalPlayersSha256": archive["playersSha256"],
                "historicalGameweeksSha256": archive["gameweeksSha256"],
            },
            "playerCount": len(players),
            "priorSeasonIdentityMatchCount": matched,
            "priorSeasonIdentityMissingCount": len(players) - matched,
            "players": players,
        }
        base["dataIdentitySha256"] = _sha256(
            {
                "seasonCode": season_code,
                "gameweek": gameweek,
                "deadlineUtc": target["deadlineUtc"],
                "decisionCutoffUtc": target["availableAtUtc"],
                "provenance": base["provenance"],
            }
        )
        base["runIdentitySha256"] = _sha256(base)
        return base
    finally:
        connection.close()


def _validate(path: Path, season_code: str, gameweek: int) -> None:
    if not path.is_file():
        raise CrossSeasonPlayerStateError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    if not season_code.strip() or len(season_code) > 16:
        raise CrossSeasonPlayerStateError(
            "configuration.season-code",
            "season_code must contain 1 to 16 characters.",
        )
    if gameweek < 1 or gameweek > 38:
        raise CrossSeasonPlayerStateError(
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
        raise CrossSeasonPlayerStateError(
            "database.open-failed",
            "The SQLite database could not be opened read-only.",
        ) from exception


def _require_schema(connection: sqlite3.Connection) -> None:
    try:
        row = connection.execute(
            "SELECT MAX(version) AS version FROM schema_migrations;"
        ).fetchone()
    except sqlite3.Error as exception:
        raise CrossSeasonPlayerStateError(
            "database.schema-missing",
            "The database does not contain autoFPL migrations.",
        ) from exception
    version = None if row is None else row["version"]
    if version is None or int(version) < REQUIRED_DATABASE_VERSION:
        raise CrossSeasonPlayerStateError(
            "database.schema-version",
            "Cross-season state requires autoFPL database version "
            f"{REQUIRED_DATABASE_VERSION} or newer.",
        )


def _load_target(
    connection: sqlite3.Connection,
    season_code: str,
    gameweek: int,
) -> Optional[Dict[str, Any]]:
    row = connection.execute(
        """
        SELECT
            capture_id,
            next_deadline_utc,
            available_at_utc,
            bootstrap_sha256,
            fixtures_sha256,
            player_count
        FROM official_fpl_captures
        WHERE season_code = :season_code
          AND next_gameweek_number = :gameweek
          AND next_deadline_utc IS NOT NULL
          AND julianday(available_at_utc) <= julianday(next_deadline_utc)
        ORDER BY julianday(available_at_utc) DESC, capture_id DESC
        LIMIT 1;
        """,
        {"season_code": season_code, "gameweek": gameweek},
    ).fetchone()
    if row is None:
        return None
    return {
        "captureId": int(row["capture_id"]),
        "deadlineUtc": str(row["next_deadline_utc"]),
        "availableAtUtc": str(row["available_at_utc"]),
        "bootstrapSha256": str(row["bootstrap_sha256"]),
        "fixturesSha256": str(row["fixtures_sha256"]),
        "playerCount": int(row["player_count"]),
    }


def _load_archive(
    connection: sqlite3.Connection,
    target: Mapping[str, Any],
) -> Optional[Dict[str, Any]]:
    row = connection.execute(
        """
        SELECT
            capture_id,
            source_key,
            source_revision,
            available_at_utc,
            players_sha256,
            gameweeks_sha256,
            player_count,
            player_gameweek_count,
            stable_code_count
        FROM historical_fpl_season_captures
        WHERE season_code = :season_code
          AND julianday(available_at_utc) <= julianday(:cutoff)
        ORDER BY julianday(available_at_utc) DESC, capture_id DESC
        LIMIT 1;
        """,
        {"season_code": PRIOR_SEASON, "cutoff": target["availableAtUtc"]},
    ).fetchone()
    if row is None:
        return None
    return {
        "captureId": int(row["capture_id"]),
        "sourceKey": str(row["source_key"]),
        "sourceRevision": str(row["source_revision"]),
        "availableAtUtc": str(row["available_at_utc"]),
        "playersSha256": str(row["players_sha256"]),
        "gameweeksSha256": str(row["gameweeks_sha256"]),
        "playerCount": int(row["player_count"]),
        "playerGameweekCount": int(row["player_gameweek_count"]),
        "stableCodeCount": int(row["stable_code_count"]),
    }


def _load_history(
    connection: sqlite3.Connection,
    capture_id: int,
) -> tuple[
    Dict[int, Dict[int, Dict[str, float]]],
    Dict[int, Dict[str, Any]],
    int,
]:
    player_rows = connection.execute(
        """
        SELECT
            player_code,
            position,
            final_team_id,
            final_status,
            final_chance_next_round,
            final_news_sha256,
            final_news_added_utc
        FROM historical_fpl_players
        WHERE capture_id = :capture_id
        ORDER BY player_code;
        """,
        {"capture_id": capture_id},
    ).fetchall()
    historical_players = {
        int(row["player_code"]): {
            "position": str(row["position"]),
            "finalTeamId": int(row["final_team_id"]),
            "finalStatus": str(row["final_status"]),
            "finalChanceNextRound": row["final_chance_next_round"],
            "finalNewsSha256": str(row["final_news_sha256"]),
            "finalNewsAddedUtc": row["final_news_added_utc"],
        }
        for row in player_rows
    }
    rows = connection.execute(
        """
        SELECT
            player_code,
            gameweek,
            team_name,
            minutes,
            starts,
            total_points,
            expected_goals,
            expected_assists,
            expected_goal_involvements,
            expected_goals_conceded,
            defensive_contribution,
            recoveries,
            tackles
        FROM historical_fpl_player_gameweeks
        WHERE capture_id = :capture_id
        ORDER BY player_code, gameweek, kickoff_utc, fixture_id;
        """,
        {"capture_id": capture_id},
    ).fetchall()
    histories: DefaultDict[int, Dict[int, Dict[str, float]]] = defaultdict(dict)
    last_team: Dict[int, tuple[int, str]] = {}
    for row in rows:
        code = int(row["player_code"])
        gameweek = int(row["gameweek"])
        values = histories[code].setdefault(
            gameweek,
            {
                "minutes": 0.0,
                "starts": 0.0,
                "totalPoints": 0.0,
                "expectedGoals": 0.0,
                "expectedAssists": 0.0,
                "expectedGoalInvolvements": 0.0,
                "expectedGoalsConceded": 0.0,
                "defensiveContribution": 0.0,
                "recoveries": 0.0,
                "tackles": 0.0,
            },
        )
        values["minutes"] += float(row["minutes"])
        values["starts"] += float(row["starts"])
        values["totalPoints"] += float(row["total_points"])
        values["expectedGoals"] += float(row["expected_goals"])
        values["expectedAssists"] += float(row["expected_assists"])
        values["expectedGoalInvolvements"] += float(
            row["expected_goal_involvements"]
        )
        values["expectedGoalsConceded"] += float(
            row["expected_goals_conceded"]
        )
        values["defensiveContribution"] += float(
            row["defensive_contribution"]
        )
        values["recoveries"] += float(row["recoveries"])
        values["tackles"] += float(row["tackles"])
        if code not in last_team or gameweek >= last_team[code][0]:
            last_team[code] = (gameweek, str(row["team_name"]))
    for code, (_, team_name) in last_team.items():
        historical_players[code]["lastTeamName"] = team_name
    return dict(histories), historical_players, len(rows)


def _load_current_players(
    connection: sqlite3.Connection,
    capture_id: int,
) -> list[Dict[str, Any]]:
    rows = connection.execute(
        """
        SELECT
            player.player_id,
            player.code,
            player.web_name,
            player.position,
            player.team_id,
            team.name AS team_name,
            player.status,
            player.chance_next_round,
            player.news,
            player.news_added_utc,
            player.minutes,
            player.starts,
            player.total_points
        FROM official_fpl_players AS player
        INNER JOIN official_fpl_teams AS team
            ON team.capture_id = player.capture_id
           AND team.team_id = player.team_id
        WHERE player.capture_id = :capture_id
        ORDER BY player.player_id;
        """,
        {"capture_id": capture_id},
    ).fetchall()
    return [
        {
            "playerId": int(row["player_id"]),
            "playerCode": int(row["code"]),
            "webName": str(row["web_name"]),
            "position": str(row["position"]),
            "teamId": int(row["team_id"]),
            "teamName": str(row["team_name"]),
            "status": str(row["status"]),
            "chanceNextRound": row["chance_next_round"],
            "newsSha256": hashlib.sha256(
                str(row["news"]).encode("utf-8")
            ).hexdigest(),
            "newsAddedUtc": row["news_added_utc"],
            "minutes": int(row["minutes"]),
            "starts": int(row["starts"]),
            "totalPoints": int(row["total_points"]),
        }
        for row in rows
    ]


def _build_player(
    current: Mapping[str, Any],
    historical_player: Optional[Mapping[str, Any]],
    history_by_gameweek: Mapping[int, Mapping[str, float]],
) -> Dict[str, Any]:
    ordered = [
        {"gameweek": gameweek, **history_by_gameweek[gameweek]}
        for gameweek in sorted(history_by_gameweek)
    ]
    last_appearance = next(
        (
            int(item["gameweek"])
            for item in reversed(ordered)
            if item["minutes"] > 0
        ),
        None,
    )
    trailing_zero_gameweeks = _trailing_zero_minutes(ordered)
    prior_team = (
        None
        if historical_player is None
        else historical_player.get("lastTeamName")
    )
    prior_position = (
        None if historical_player is None else historical_player["position"]
    )
    return {
        "playerId": current["playerId"],
        "playerCode": current["playerCode"],
        "webName": current["webName"],
        "position": current["position"],
        "teamId": current["teamId"],
        "teamName": current["teamName"],
        "hasPriorSeasonIdentity": historical_player is not None,
        "identityMatch": (
            "stable-official-code"
            if historical_player is not None
            else "no-prior-season-match"
        ),
        "positionChanged": (
            None
            if prior_position is None
            else prior_position != current["position"]
        ),
        "priorSeasonPosition": prior_position,
        "priorSeasonLastTeamName": prior_team,
        "teamNameChanged": (
            None
            if prior_team is None
            else _normalize(prior_team) != _normalize(str(current["teamName"]))
        ),
        "currentAvailability": {
            "isAuthoritative": True,
            "source": "current-official-capture",
            "status": current["status"],
            "chanceNextRound": current["chanceNextRound"],
            "newsSha256": current["newsSha256"],
            "newsAddedUtc": current["newsAddedUtc"],
        },
        "archivedFinalHealth": (
            None
            if historical_player is None
            else {
                "isAuthoritative": False,
                "use": "durability-prior-only",
                "status": historical_player["finalStatus"],
                "chanceNextRound": historical_player[
                    "finalChanceNextRound"
                ],
                "newsAddedUtc": historical_player["finalNewsAddedUtc"],
                "newsSha256": historical_player["finalNewsSha256"],
            }
        ),
        "currentSeasonObserved": {
            "minutes": current["minutes"],
            "starts": current["starts"],
            "totalPoints": current["totalPoints"],
        },
        "priorSeason": {
            "gameweekSampleCount": len(ordered),
            "appearanceGameweekCount": sum(
                item["minutes"] > 0 for item in ordered
            ),
            "startGameweekCount": sum(item["starts"] > 0 for item in ordered),
            "lastAppearanceGameweek": last_appearance,
            "trailingZeroMinuteGameweeks": trailing_zero_gameweeks,
            "season": _summarise(ordered),
            "rolling": {
                str(window): _summarise(ordered[-window:])
                for window in WINDOWS
            },
            "exponentiallyWeighted": _summarise_ewma(ordered),
        },
    }


def _summarise(history: Sequence[Mapping[str, Any]]) -> Dict[str, Any]:
    summary: Dict[str, Any] = {
        "sampleCount": len(history),
        "appearanceRate": _mean(
            [int(float(item["minutes"]) > 0) for item in history]
        ),
        "startRate": _mean(
            [int(float(item["starts"]) > 0) for item in history]
        ),
        "played60Rate": _mean(
            [int(float(item["minutes"]) >= 60) for item in history]
        ),
    }
    for metric in METRICS:
        summary[f"{metric}Mean"] = _mean(
            [float(item[metric]) for item in history]
        )
    total_minutes = sum(float(item["minutes"]) for item in history)
    summary["totalMinutes"] = int(total_minutes)
    summary["pointsPer90"] = _per90(history, "totalPoints", total_minutes)
    summary["expectedGoalsPer90"] = _per90(
        history,
        "expectedGoals",
        total_minutes,
    )
    summary["expectedAssistsPer90"] = _per90(
        history,
        "expectedAssists",
        total_minutes,
    )
    return summary


def _trailing_zero_minutes(
    history: Sequence[Mapping[str, Any]],
) -> int:
    count = 0
    for item in reversed(history):
        if float(item["minutes"]) > 0:
            break
        count += 1
    return count


def _summarise_ewma(
    history: Sequence[Mapping[str, Any]],
) -> Optional[Dict[str, Any]]:
    if not history:
        return None
    summary: Dict[str, Any] = {
        "sampleCount": len(history),
        "alpha": EWMA_ALPHA,
        "appearanceRate": _ewma(
            [int(float(item["minutes"]) > 0) for item in history]
        ),
        "startRate": _ewma(
            [int(float(item["starts"]) > 0) for item in history]
        ),
        "played60Rate": _ewma(
            [int(float(item["minutes"]) >= 60) for item in history]
        ),
    }
    for metric in METRICS:
        summary[f"{metric}Mean"] = _ewma(
            [float(item[metric]) for item in history]
        )
    return summary


def _mean(values: Sequence[float]) -> Optional[float]:
    if not values:
        return None
    return round(sum(values) / len(values), 6)


def _ewma(values: Sequence[float]) -> float:
    value = float(values[0])
    for current in values[1:]:
        value = (EWMA_ALPHA * float(current)) + (
            (1.0 - EWMA_ALPHA) * value
        )
    return round(value, 6)


def _per90(
    history: Sequence[Mapping[str, Any]],
    metric: str,
    total_minutes: float,
) -> Optional[float]:
    if total_minutes <= 0:
        return None
    return round(
        90.0 * sum(float(item[metric]) for item in history) / total_minutes,
        6,
    )


def _normalize(value: str) -> str:
    return " ".join(value.casefold().split())


def _sha256(value: Mapping[str, Any]) -> str:
    canonical = json.dumps(
        value,
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=False,
    ).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def write_cross_season_player_state(
    path: Path,
    state: Mapping[str, Any],
) -> None:
    output = Path(path)
    if output.exists():
        raise CrossSeasonPlayerStateError(
            "output.exists",
            "The output path already exists and will not be overwritten.",
        )
    output.parent.mkdir(parents=True, exist_ok=True)
    try:
        with output.open("x", encoding="utf-8") as stream:
            json.dump(
                state,
                stream,
                indent=2,
                sort_keys=True,
                ensure_ascii=False,
            )
            stream.write("\n")
    except FileExistsError as exception:
        raise CrossSeasonPlayerStateError(
            "output.exists",
            "The output path already exists and will not be overwritten.",
        ) from exception


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Build an exploratory cross-season player-state artifact from "
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
        state = build_cross_season_player_state(
            arguments.database,
            arguments.season,
            arguments.gameweek,
        )
        write_cross_season_player_state(arguments.output, state)
        return 0
    except CrossSeasonPlayerStateError as exception:
        error = {
            "status": "error",
            "code": exception.code,
            "message": str(exception),
        }
        sys.stderr.write(json.dumps(error, sort_keys=True) + "\n")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
