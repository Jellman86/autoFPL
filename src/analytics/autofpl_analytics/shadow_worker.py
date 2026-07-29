from __future__ import annotations

import argparse
import hashlib
import json
import os
import sqlite3
import subprocess
import sys
import tempfile
import time
import uuid
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Dict, Optional, Sequence

from .current_joint_scenario_forecast import (
    _build_from_artifacts,
    _retained_screen,
)
from .current_scenario_selection_score import (
    build_current_scenario_selection_score,
)
from .multi_season_player_forecast import (
    CURRENT_GAMEWEEK,
    CURRENT_SEASON,
    build_multi_season_player_forecast,
)
from .temporal_ridge import TemporalRidgeError

SCHEMA_VERSION = "1.0"
DEFAULT_DATABASE = Path("/analytics-snapshot/autofpl.db")
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
                ) AS has_exact_scenario_shadow,
                (
                    SELECT selection_revision_id
                    FROM selection_revisions
                    WHERE forecast_artifact_id = (
                        SELECT artifact_id
                        FROM baseline_forecast_artifacts
                        WHERE capture_id =
                            official_fpl_captures.capture_id
                        ORDER BY artifact_id DESC
                        LIMIT 1
                    )
                    ORDER BY revision DESC, selection_revision_id DESC
                    LIMIT 1
                ) AS selection_revision_id,
                EXISTS (
                    SELECT 1
                    FROM selection_scenario_score_shadow_artifacts AS score
                    WHERE score.scenario_artifact_id = (
                        SELECT scenario_artifact_id
                        FROM joint_scenario_shadow_artifacts
                        WHERE official_capture_id =
                            official_fpl_captures.capture_id
                        ORDER BY scenario_artifact_id DESC
                        LIMIT 1
                    )
                    AND score.forecast_artifact_id = (
                        SELECT artifact_id
                        FROM baseline_forecast_artifacts
                        WHERE capture_id =
                            official_fpl_captures.capture_id
                        ORDER BY artifact_id DESC
                        LIMIT 1
                    )
                    AND score.selection_revision_id IS (
                        SELECT selection_revision_id
                        FROM selection_revisions
                        WHERE forecast_artifact_id = (
                            SELECT artifact_id
                            FROM baseline_forecast_artifacts
                            WHERE capture_id =
                                official_fpl_captures.capture_id
                            ORDER BY artifact_id DESC
                            LIMIT 1
                        )
                        ORDER BY revision DESC, selection_revision_id DESC
                        LIMIT 1
                    )
                ) AS has_exact_selection_score
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
                "selectionRevisionId": (
                    None
                    if row["selection_revision_id"] is None
                    else int(row["selection_revision_id"])
                ),
                "hasExactSelectionScore": bool(
                    row["has_exact_selection_score"]
                ),
            }
            if row is not None
            else {
                "officialCaptureId": None,
                "seasonCode": None,
                "gameweek": None,
                "hasExactPointShadow": False,
                "hasExactScenarioShadow": False,
                "selectionRevisionId": None,
                "hasExactSelectionScore": False,
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
        and target["hasExactSelectionScore"]
    ):
        return _result("current", capture_id)
    if (
        target["seasonCode"] != CURRENT_SEASON
        or target["gameweek"] != CURRENT_GAMEWEEK
    ):
        return _result("waiting", capture_id, error_code="unsupported-target")

    inbox = Path(inbox_path)
    inbox.mkdir(mode=0o700, parents=True, exist_ok=True)
    if (
        target["hasExactPointShadow"]
        and target["hasExactScenarioShadow"]
    ):
        selection_key = (
            "none"
            if target["selectionRevisionId"] is None
            else str(target["selectionRevisionId"])
        )
        stem = (
            f"selection-scenario-score-capture-{capture_id}"
            f"-selection-{selection_key}"
        )
        build = build_current_scenario_selection_score
    elif target["hasExactPointShadow"]:
        stem = f"joint-scenario-shadow-capture-{capture_id}"
        build = _build_joint_scenario_for_worker
    else:
        stem = f"multi-season-shadow-capture-{capture_id}"
        build = build_multi_season_player_forecast
    if any(inbox.glob(f"{stem}.*")):
        return _result("handoff-pending", capture_id)

    artifact = (
        build(Path(database_path))
        if target["hasExactScenarioShadow"]
        else build(
            Path(database_path),
            CURRENT_SEASON,
            CURRENT_GAMEWEEK,
        )
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


def _build_joint_scenario_for_worker(
    database_path: Path,
    season_code: str,
    gameweek: int,
) -> Dict[str, Any]:
    point_forecast = _load_persisted_point_forecast(
        database_path,
    )
    with tempfile.TemporaryDirectory(
        prefix="autofpl-participation-",
    ) as directory:
        output = Path(directory) / "participation.json"
        command = [
            sys.executable,
            "-m",
            "autofpl_analytics.preseason_participation_forecast",
            "--database",
            str(database_path),
            "--season",
            season_code,
            "--gameweek",
            str(gameweek),
            "--output",
            str(output),
        ]
        try:
            subprocess.run(
                command,
                check=True,
                timeout=10 * 60,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
        except (subprocess.SubprocessError, OSError) as exception:
            raise TemporalRidgeError(
                "worker.participation-generation-failed",
                "The isolated participation generator failed.",
            ) from exception
        participation_forecast = json.loads(
            output.read_text(encoding="utf-8")
        )
    return _build_from_artifacts(
        database_path,
        point_forecast,
        participation_forecast,
        _retained_screen(),
    )


def _load_persisted_point_forecast(
    database_path: Path,
) -> Dict[str, Any]:
    connection: Optional[sqlite3.Connection] = None
    try:
        connection = sqlite3.connect(
            f"file:{Path(database_path)}?mode=ro",
            uri=True,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        row = connection.execute(
            """
            SELECT document_json, content_sha256
            FROM multi_season_player_forecast_artifacts
            ORDER BY
                julianday(decision_cutoff_utc) DESC,
                forecast_artifact_id DESC
            LIMIT 1;
            """
        ).fetchone()
        if row is None:
            raise TemporalRidgeError(
                "worker.point-forecast-not-found",
                "The exact persisted point forecast is unavailable.",
            )
        encoded = str(row["document_json"]).encode("utf-8")
        if (
            not encoded
            or len(encoded) > MAXIMUM_ARTIFACT_BYTES
            or hashlib.sha256(encoded).hexdigest()
            != str(row["content_sha256"])
        ):
            raise TemporalRidgeError(
                "worker.point-forecast-invalid",
                "The persisted point forecast failed its content boundary.",
            )
        return json.loads(encoded)
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "worker.point-forecast-read-failed",
            "The persisted point forecast could not be read.",
        ) from exception
    finally:
        if connection is not None:
            connection.close()


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
