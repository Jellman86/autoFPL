from __future__ import annotations

import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.shadow_worker import (  # noqa: E402
    generate_once,
    inspect_target,
)


class ShadowWorkerTests(unittest.TestCase):
    def test_missing_current_and_generated_states_are_explicit(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            database = root / "autofpl.db"
            inbox = root / "inbox"
            self._create_database(database)

            waiting = generate_once(database, inbox)
            self.assertEqual("waiting", waiting.status)
            self.assertEqual("no-official-capture", waiting.errorCode)

            self._insert_capture(database, 16)
            artifact = {
                "officialCaptureId": 16,
                "players": [],
            }
            with patch(
                "autofpl_analytics.shadow_worker."
                "build_multi_season_player_forecast",
                return_value=artifact,
            ):
                generated = generate_once(database, inbox)

            self.assertEqual("generated", generated.status)
            self.assertEqual(16, generated.officialCaptureId)
            output = inbox / str(generated.outputFile)
            self.assertEqual(artifact, json.loads(output.read_text()))
            self.assertEqual(0o600, output.stat().st_mode & 0o777)

            pending = generate_once(database, inbox)
            self.assertEqual("handoff-pending", pending.status)

    def test_exact_shadow_and_unsupported_target_do_not_generate(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            database = root / "autofpl.db"
            self._create_database(database)
            self._insert_capture(database, 16)
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    INSERT INTO multi_season_player_forecast_artifacts
                        (official_capture_id)
                    VALUES (16);
                    """
                )

            target = inspect_target(database)
            self.assertTrue(target["hasExactShadow"])
            self.assertEqual(
                "current",
                generate_once(database, root / "inbox").status,
            )

            with sqlite3.connect(database) as connection:
                connection.executescript(
                    """
                    UPDATE official_fpl_captures
                    SET next_gameweek_number = 2
                    WHERE capture_id = 16;
                    DELETE FROM multi_season_player_forecast_artifacts;
                    """
                )
            unsupported = generate_once(database, root / "inbox")
            self.assertEqual("waiting", unsupported.status)
            self.assertEqual("unsupported-target", unsupported.errorCode)

    @staticmethod
    def _create_database(path: Path) -> None:
        with sqlite3.connect(path) as connection:
            connection.executescript(
                """
                CREATE TABLE official_fpl_captures (
                    capture_id INTEGER PRIMARY KEY,
                    season_code TEXT NOT NULL,
                    next_gameweek_number INTEGER,
                    available_at_utc TEXT NOT NULL
                );
                CREATE TABLE multi_season_player_forecast_artifacts (
                    forecast_artifact_id INTEGER PRIMARY KEY,
                    official_capture_id INTEGER NOT NULL
                );
                """
            )

    @staticmethod
    def _insert_capture(path: Path, capture_id: int) -> None:
        with sqlite3.connect(path) as connection:
            connection.execute(
                """
                INSERT INTO official_fpl_captures
                    (capture_id, season_code, next_gameweek_number,
                     available_at_utc)
                VALUES (?, '2026-27', 1, '2026-07-29T04:38:41Z');
                """,
                (capture_id,),
            )
