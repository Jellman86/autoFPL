from __future__ import annotations

import argparse
import json
import math
import sqlite3
import sys
import unicodedata
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence
from zoneinfo import ZoneInfo

from .feature_table import FeatureTableError, build_feature_table
from .temporal_ridge import TemporalRidgeError, _open_connection, _sha256

SCHEMA_VERSION = "1.0"
FEATURE_SET = "fpl-form-temporal-feature-v1"
LONDON = ZoneInfo("Europe/London")


class FplFormFeatureError(Exception):
    """Raised when scraped forecast features cannot be trusted."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


def build_fpl_form_feature_table(
    database_path: Path,
    season_code: str,
    gameweek: int,
) -> Dict[str, Any]:
    """Join one forecast captured by the exact official replay cutoff."""
    path = Path(database_path)
    try:
        official = build_feature_table(path, season_code, gameweek)
    except FeatureTableError as exception:
        raise FplFormFeatureError(exception.code, str(exception)) from exception
    try:
        connection = _open_connection(path)
    except TemporalRidgeError as exception:
        raise FplFormFeatureError(exception.code, str(exception)) from exception
    try:
        capture = _load_capture(connection, official)
        base = {
            "schemaVersion": SCHEMA_VERSION,
            "featureSet": FEATURE_SET,
            "status": "complete" if capture is not None else "unavailable",
            "reason": None
            if capture is not None
            else "no-forecast-available-by-decision-cutoff",
            "researchStatus": "exploratory-source-feature-not-promoted",
            "isPromoted": False,
            "seasonCode": official["seasonCode"],
            "gameweek": official["gameweek"],
            "deadlineUtc": official["deadlineUtc"],
            "decisionCutoffUtc": official["decisionCutoffUtc"],
            "officialFeatureRunIdentitySha256": official["runIdentitySha256"],
            "officialPlayerCount": int(official["playerCount"]),
            "identityRule": (
                "strict direct source player/fixture IDs plus normalized "
                "name, team, position, participation and London-local kickoff"
            ),
        }
        if capture is None:
            return _finish(base, None, [])
        features = _load_and_validate_predictions(
            connection,
            official,
            int(capture["captureId"]),
        )
        rows = [
            {
                "playerId": int(player["playerId"]),
                **features.get(
                    int(player["playerId"]),
                    {
                        "hasForecast": False,
                        "fixturePredictionCount": 0,
                        "publishedConditionalPoints": None,
                        "appearanceAdjustedPoints": None,
                        "hasCompleteAppearanceProbabilities": False,
                    },
                ),
            }
            for player in official["players"]
        ]
        return _finish(base, capture, rows)
    finally:
        connection.close()


def _load_capture(
    connection: sqlite3.Connection,
    official: Mapping[str, Any],
) -> Optional[Dict[str, Any]]:
    try:
        row = connection.execute(
            """
            SELECT
                capture_id,
                available_at_utc,
                content_sha256,
                transport,
                extraction_version,
                provider_payload_sha256,
                player_count,
                fixture_prediction_count,
                appearance_probability_count
            FROM fpl_form_forecast_captures
            WHERE season_code = :season_code
              AND gameweek = :gameweek
              AND julianday(available_at_utc)
                  <= julianday(:decision_cutoff_utc)
            ORDER BY julianday(available_at_utc) DESC, capture_id DESC
            LIMIT 1;
            """,
            {
                "season_code": official["seasonCode"],
                "gameweek": official["gameweek"],
                "decision_cutoff_utc": official["decisionCutoffUtc"],
            },
        ).fetchone()
    except sqlite3.Error as exception:
        raise FplFormFeatureError(
            "database.fpl-form-schema-missing",
            "The database does not contain the FPL Form forecast tables.",
        ) from exception
    if row is None:
        return None
    return {
        "captureId": int(row["capture_id"]),
        "availableAtUtc": str(row["available_at_utc"]),
        "contentSha256": str(row["content_sha256"]),
        "transport": str(row["transport"]),
        "extractionVersion": str(row["extraction_version"]),
        "providerPayloadSha256": row["provider_payload_sha256"],
        "playerCount": int(row["player_count"]),
        "fixturePredictionCount": int(row["fixture_prediction_count"]),
        "appearanceProbabilityCount": int(
            row["appearance_probability_count"]
        ),
    }


def _load_and_validate_predictions(
    connection: sqlite3.Connection,
    official: Mapping[str, Any],
    capture_id: int,
) -> Dict[int, Dict[str, Any]]:
    replay_capture_id = int(official["provenance"]["replayCaptureId"])
    player_rows = connection.execute(
        """
        SELECT
            player.player_id,
            player.first_name || ' ' || player.second_name AS full_name,
            player.web_name,
            player.team_id,
            team.name AS team_name,
            team.short_name AS team_short_name,
            player.position
        FROM official_fpl_players AS player
        INNER JOIN official_fpl_teams AS team
            ON team.capture_id = player.capture_id
           AND team.team_id = player.team_id
        WHERE player.capture_id = :capture_id;
        """,
        {"capture_id": replay_capture_id},
    ).fetchall()
    players = {int(row["player_id"]): row for row in player_rows}
    fixture_rows = connection.execute(
        """
        SELECT
            fixture_id,
            home_team_id,
            away_team_id,
            kickoff_utc
        FROM official_fpl_fixtures
        WHERE capture_id = :capture_id
          AND event_id = :gameweek;
        """,
        {
            "capture_id": replay_capture_id,
            "gameweek": official["gameweek"],
        },
    ).fetchall()
    fixtures = {int(row["fixture_id"]): row for row in fixture_rows}
    rows = connection.execute(
        """
        SELECT
            source_player_id,
            fixture_id,
            player_name,
            team_name,
            position,
            kickoff_local,
            predicted_points,
            appearance_probability
        FROM fpl_form_fixture_predictions
        WHERE capture_id = :capture_id
        ORDER BY source_player_id, fixture_id;
        """,
        {"capture_id": capture_id},
    ).fetchall()
    header = connection.execute(
        """
        SELECT
            player_count,
            fixture_prediction_count,
            appearance_probability_count
        FROM fpl_form_forecast_captures
        WHERE capture_id = :capture_id;
        """,
        {"capture_id": capture_id},
    ).fetchone()
    assert header is not None
    if (
        len(rows) != int(header["fixture_prediction_count"])
        or len({int(row["source_player_id"]) for row in rows})
        != int(header["player_count"])
        or sum(row["appearance_probability"] is not None for row in rows)
        != int(header["appearance_probability_count"])
    ):
        raise FplFormFeatureError(
            "data.fpl-form-declared-coverage",
            "The forecast rows do not match their declared coverage.",
        )

    values: DefaultDict[int, list[tuple[float, Optional[float]]]] = (
        defaultdict(list)
    )
    for row in rows:
        player_id = int(row["source_player_id"])
        fixture_id = int(row["fixture_id"])
        player = players.get(player_id)
        fixture = fixtures.get(fixture_id)
        if player is None or fixture is None:
            _identity_failure(player_id, fixture_id, "direct-id-not-found")
        assert player is not None and fixture is not None
        if (
            str(row["position"]) != str(player["position"])
            or _normalize(str(row["player_name"]))
            not in {
                _normalize(str(player["full_name"])),
                _normalize(str(player["web_name"])),
            }
            or _normalize(str(row["team_name"]))
            not in {
                _normalize(str(player["team_name"])),
                _normalize(str(player["team_short_name"])),
            }
        ):
            _identity_failure(
                player_id,
                fixture_id,
                "player-attributes-disagree",
            )
        team_id = int(player["team_id"])
        if team_id not in {
            int(fixture["home_team_id"]),
            int(fixture["away_team_id"]),
        }:
            _identity_failure(
                player_id,
                fixture_id,
                "team-not-in-fixture",
            )
        local_kickoff = _parse_london_local(str(row["kickoff_local"]))
        official_kickoff = _parse_utc(str(fixture["kickoff_utc"]))
        if local_kickoff != official_kickoff:
            _identity_failure(
                player_id,
                fixture_id,
                "kickoff-disagrees",
            )
        points = _bounded_number(
            row["predicted_points"],
            -20.0,
            100.0,
            "predicted-points",
        )
        probability = (
            None
            if row["appearance_probability"] is None
            else _bounded_number(
                row["appearance_probability"],
                0.0,
                1.0,
                "appearance-probability",
            )
        )
        values[player_id].append((points, probability))

    return {
        player_id: {
            "hasForecast": True,
            "fixturePredictionCount": len(items),
            "publishedConditionalPoints": round(
                sum(points for points, _ in items),
                6,
            ),
            "appearanceAdjustedPoints": (
                round(
                    sum(
                        points * probability
                        for points, probability in items
                        if probability is not None
                    ),
                    6,
                )
                if all(probability is not None for _, probability in items)
                else None
            ),
            "hasCompleteAppearanceProbabilities": all(
                probability is not None for _, probability in items
            ),
        }
        for player_id, items in sorted(values.items())
    }


def _identity_failure(
    player_id: int,
    fixture_id: int,
    reason: str,
) -> None:
    raise FplFormFeatureError(
        "data.fpl-form-identity-incomplete",
        "FPL Form direct identity validation failed for "
        f"player {player_id}, fixture {fixture_id}: {reason}.",
    )


def _normalize(value: str) -> str:
    decomposed = unicodedata.normalize("NFD", value)
    parts = []
    pending_space = False
    for character in decomposed:
        if unicodedata.category(character) == "Mn":
            continue
        if character.isalnum():
            if pending_space and parts:
                parts.append(" ")
            parts.append(character.lower())
            pending_space = False
        else:
            pending_space = True
    return "".join(parts)


def _parse_london_local(value: str) -> datetime:
    try:
        naive = datetime.strptime(value, "%Y-%m-%d %H:%M:%S")
    except ValueError as exception:
        raise FplFormFeatureError(
            "data.fpl-form-kickoff-invalid",
            "FPL Form kickoff_local is not valid.",
        ) from exception
    candidates = {
        aware.astimezone(timezone.utc)
        for fold in (0, 1)
        for aware in [naive.replace(tzinfo=LONDON, fold=fold)]
        if aware.astimezone(timezone.utc)
        .astimezone(LONDON)
        .replace(tzinfo=None)
        == naive
    }
    if len(candidates) != 1:
        raise FplFormFeatureError(
            "data.fpl-form-kickoff-invalid",
            "FPL Form kickoff_local is ambiguous or nonexistent in London.",
        )
    return next(iter(candidates))


def _parse_utc(value: str) -> datetime:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exception:
        raise FplFormFeatureError(
            "data.official-kickoff-invalid",
            "Official kickoff_utc is not valid ISO-8601.",
        ) from exception
    if parsed.tzinfo is None:
        raise FplFormFeatureError(
            "data.official-kickoff-invalid",
            "Official kickoff_utc must include an offset.",
        )
    return parsed.astimezone(timezone.utc)


def _bounded_number(
    value: Any,
    minimum: float,
    maximum: float,
    label: str,
) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError) as exception:
        raise FplFormFeatureError(
            f"data.fpl-form-{label}",
            f"FPL Form {label} is not numeric.",
        ) from exception
    if not math.isfinite(number) or not minimum <= number <= maximum:
        raise FplFormFeatureError(
            f"data.fpl-form-{label}",
            f"FPL Form {label} is outside its admitted range.",
        )
    return number


def _finish(
    base: Mapping[str, Any],
    capture: Optional[Mapping[str, Any]],
    rows: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    report = {
        **base,
        "forecast": dict(capture) if capture is not None else None,
        "forecastPlayerCount": sum(
            int(bool(row["hasForecast"])) for row in rows
        ),
        "players": list(rows),
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "officialFeatureRunIdentitySha256": report[
                "officialFeatureRunIdentitySha256"
            ],
            "forecast": report["forecast"],
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def _write_output(path: Path, report: Mapping[str, Any]) -> None:
    try:
        with Path(path).open("x", encoding="utf-8", newline="\n") as stream:
            json.dump(report, stream, indent=2, sort_keys=True)
            stream.write("\n")
    except FileExistsError as exception:
        raise FplFormFeatureError(
            "output.already-exists",
            "The output path already exists; refusing to overwrite it.",
        ) from exception
    except OSError as exception:
        raise FplFormFeatureError(
            "output.write-failed",
            "The FPL Form feature table could not be written.",
        ) from exception


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Build cutoff-safe FPL Form player features for one official replay."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", required=True)
    parser.add_argument("--gameweek", required=True, type=int)
    parser.add_argument("--output", required=True, type=Path)
    options = parser.parse_args(arguments)
    try:
        report = build_fpl_form_feature_table(
            options.database,
            options.season,
            options.gameweek,
        )
        _write_output(options.output, report)
        return 0 if report["status"] == "complete" else 2
    except FplFormFeatureError as exception:
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
