from __future__ import annotations

import argparse
import json
import os
import sqlite3
import sys
import time
import uuid
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Dict, Optional, Sequence

from .current_joint_scenario_forecast import (
    build_current_joint_scenario_forecast,
)
from .multi_season_player_forecast import (
    CURRENT_GAMEWEEK,
    CURRENT_SEASON,
    build_multi_season_player_forecast,
)
from .temporal_ridge import TemporalRidgeError

SCHEMA_VERSION = "1.0"
DEFAULT_DATABASE = Path("/data/autofpl.db")
DEFAULT_INBOX = Path("/analytics-inbox")
DEFAULT_POLL_SECONDS = 15 * 60
MINIMUM_POLL_SECONDS = 60
MAXIMUM_POLL_SECONDS = 24 * 60 * 60
MAXIMUM_ARTIFACT_BYTES = 2 * 1024 * 1024


@dataclass(frozen=True)
class ShadowWorkerResult:
    schemaVersion: str
    status: str
    officialCaptureId: Optional[int]
    outputFile: Optional[str]
    errorCode: Optional[str]


def inspect_target(database_path: Path) -> Dict[str, Any]:
    path = Path(database_path)
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    try:
        connection = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        row = connection.execute(
            """
            SELECT
                capture_id,
                season_code,
                next_gameweek_number,
                EXISTS (
                    SELECT 1
                    FROM multi_season_player_forecast_artifacts AS artifact
                    WHERE artifact.official_capture_id =
                        official_fpl_captures.capture_id
                ) AS has_exact_point_shadow,
                EXISTS (
                    SELECT 1
                    FROM joint_scenario_shadow_artifacts AS scenario
                    WHERE scenario.official_capture_id =
                        official_fpl_captures.capture_id
                ) AS has_exact_scenario_shadow
            FROM official_fpl_captures
            ORDER BY available_at_utc DESC, capture_id DESC
            LIMIT 1;
            """
        ).fetchone()
        return (
            {
                "officialCaptureId": int(row["capture_id"]),
                "seasonCode": str(row["season_code"]),
                "gameweek": (
                    None
                    if row["next_gameweek_number"] is None
                    else int(row["next_gameweek_number"])
                ),
                "hasExactPointShadow": bool(
                    row["has_exact_point_shadow"]
                ),
                "hasExactScenarioShadow": bool(
                    row["has_exact_scenario_shadow"]
                ),
            }
            if row is not None
            else {
                "officialCaptureId": None,
                "seasonCode": None,
                "gameweek": None,
                "hasExactPointShadow": False,
                "hasExactScenarioShadow": False,
            }
        )
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "database.readiness-read-failed",
            "Shadow readiness could not be read from SQLite.",
        ) from exception
    finally:
        if "connection" in locals():
            connection.close()


def generate_once(
    database_path: Path,
    inbox_path: Path,
) -> ShadowWorkerResult:
    target = inspect_target(database_path)
    capture_id = target["officialCaptureId"]
    if capture_id is None:
        return _result("waiting", None, error_code="no-official-capture")
    if (
        target["hasExactPointShadow"]
        and target["hasExactScenarioShadow"]
    ):
        return _result("current", capture_id)
    if (
        target["seasonCode"] != CURRENT_SEASON
        or target["gameweek"] != CURRENT_GAMEWEEK
    ):
        return _result("waiting", capture_id, error_code="unsupported-target")

    inbox = Path(inbox_path)
    inbox.mkdir(mode=0o700, parents=True, exist_ok=True)
    if target["hasExactPointShadow"]:
        stem = f"joint-scenario-shadow-capture-{capture_id}"
        build = build_current_joint_scenario_forecast
    else:
        stem = f"multi-season-shadow-capture-{capture_id}"
        build = build_multi_season_player_forecast
    if any(inbox.glob(f"{stem}.*")):
        return _result("handoff-pending", capture_id)

    artifact = build(
        Path(database_path),
        CURRENT_SEASON,
        CURRENT_GAMEWEEK,
    )
    if int(artifact["officialCaptureId"]) != capture_id:
        return _result("waiting", capture_id, error_code="capture-changed")
    encoded = (
        json.dumps(
            artifact,
            ensure_ascii=False,
            separators=(",", ":"),
            sort_keys=True,
        )
        + "\n"
    ).encode("utf-8")
    if len(encoded) > MAXIMUM_ARTIFACT_BYTES:
        raise TemporalRidgeError(
            "output.too-large",
            "The shadow artifact exceeds the product import boundary.",
        )

    output = inbox / f"{stem}.json"
    temporary = inbox / f".shadow-worker-{uuid.uuid4().hex}.tmp"
    old_umask = os.umask(0o077)
    try:
        with temporary.open("xb") as stream:
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, output)
    finally:
        os.umask(old_umask)
        if temporary.exists():
            temporary.unlink()
    return _result("generated", capture_id, output.name)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Watch the read-only autoFPL database and hand one exact-capture "
            "two-season shadow artifact to the private product inbox."
        )
    )
    parser.add_argument("--database", type=Path, default=DEFAULT_DATABASE)
    parser.add_argument("--inbox", type=Path, default=DEFAULT_INBOX)
    parser.add_argument(
        "--poll-seconds",
        type=int,
        default=DEFAULT_POLL_SECONDS,
    )
    parser.add_argument("--once", action="store_true")
    options = parser.parse_args(arguments)
    if not (
        MINIMUM_POLL_SECONDS
        <= options.poll_seconds
        <= MAXIMUM_POLL_SECONDS
    ):
        parser.error(
            f"--poll-seconds must be from {MINIMUM_POLL_SECONDS} through "
            f"{MAXIMUM_POLL_SECONDS}"
        )

    os.umask(0o077)
    while True:
        try:
            result = generate_once(options.database, options.inbox)
        except TemporalRidgeError as exception:
            result = _result("error", None, error_code=exception.code)
        except (OSError, ValueError, KeyError, TypeError):
            result = _result(
                "error",
                None,
                error_code="worker-unexpected-input",
            )
        sys.stdout.write(json.dumps(asdict(result), sort_keys=True) + "\n")
        sys.stdout.flush()
        if options.once:
            return 0 if result.status != "error" else 1
        time.sleep(options.poll_seconds)


def _result(
    status: str,
    capture_id: Optional[int],
    output_file: Optional[str] = None,
    error_code: Optional[str] = None,
) -> ShadowWorkerResult:
    return ShadowWorkerResult(
        SCHEMA_VERSION,
        status,
        capture_id,
        output_file,
        error_code,
    )


if __name__ == "__main__":
    raise SystemExit(main())
