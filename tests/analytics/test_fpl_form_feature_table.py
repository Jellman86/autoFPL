from __future__ import annotations

import hashlib
import sqlite3
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.fpl_form_feature_table import (  # noqa: E402
    FplFormFeatureError,
    build_fpl_form_feature_table,
)
from tests.analytics import test_temporal_ridge as ridge_test_helpers  # noqa: E402


class FplFormFeatureTableTests(unittest.TestCase):
    def test_latest_forecast_before_exact_replay_cutoff_is_joined(self) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            first = build_fpl_form_feature_table(
                database,
                "2026-27",
                4,
            )
            second = build_fpl_form_feature_table(
                database,
                "2026-27",
                4,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual("complete", first["status"])
        self.assertEqual(50, first["forecast"]["captureId"])
        self.assertEqual(2, first["forecastPlayerCount"])
        self.assertEqual(2, first["officialPlayerCount"])
        player_one = self._player(first, 1)
        self.assertEqual(7.0, player_one["publishedConditionalPoints"])
        self.assertEqual(5.9, player_one["appearanceAdjustedPoints"])
        self.assertTrue(player_one["hasCompleteAppearanceProbabilities"])
        player_two = self._player(first, 2)
        self.assertEqual(3.0, player_two["publishedConditionalPoints"])
        self.assertIsNone(player_two["appearanceAdjustedPoints"])
        self.assertFalse(player_two["hasCompleteAppearanceProbabilities"])
        self.assertEqual(64, len(first["dataIdentitySha256"]))
        self.assertEqual(64, len(first["runIdentitySha256"]))

    def test_no_cutoff_eligible_forecast_is_explicitly_unavailable(self) -> None:
        with self._database() as database:
            with sqlite3.connect(database) as connection:
                connection.execute(
                    "DELETE FROM fpl_form_fixture_predictions WHERE capture_id = 50;"
                )
                connection.execute(
                    "DELETE FROM fpl_form_forecast_captures WHERE capture_id = 50;"
                )

            report = build_fpl_form_feature_table(
                database,
                "2026-27",
                4,
            )

        self.assertEqual("unavailable", report["status"])
        self.assertEqual(
            "no-forecast-available-by-decision-cutoff",
            report["reason"],
        )
        self.assertEqual([], report["players"])

    def test_direct_identity_disagreement_fails_closed(self) -> None:
        with self._database() as database:
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE fpl_form_fixture_predictions
                    SET team_name = 'Wrong Team'
                    WHERE capture_id = 50
                      AND source_player_id = 1
                      AND fixture_id = 404;
                    """
                )

            with self.assertRaises(FplFormFeatureError) as raised:
                build_fpl_form_feature_table(
                    database,
                    "2026-27",
                    4,
                )

        self.assertEqual(
            "data.fpl-form-identity-incomplete",
            raised.exception.code,
        )

    @staticmethod
    def _player(table: dict, player_id: int) -> dict:
        return next(
            player
            for player in table["players"]
            if player["playerId"] == player_id
        )

    class _database:
        def __init__(self) -> None:
            self._inner = ridge_test_helpers.TemporalRidgeTests._database()

        def __enter__(self) -> Path:
            path = self._inner.__enter__()
            FplFormFeatureTableTests._add_forecasts(path)
            return path

        def __exit__(self, *args: object) -> None:
            self._inner.__exit__(*args)

    @staticmethod
    def _add_forecasts(path: Path) -> None:
        with sqlite3.connect(path) as connection:
            connection.executescript(
                """
                ALTER TABLE official_fpl_teams ADD COLUMN name TEXT;
                UPDATE official_fpl_teams
                SET name = CASE short_name
                    WHEN 'AAA' THEN 'Alpha'
                    ELSE 'Beta'
                END;
                ALTER TABLE official_fpl_players ADD COLUMN web_name TEXT;
                UPDATE official_fpl_players SET web_name = second_name;

                CREATE TABLE fpl_form_forecast_captures (
                    capture_id INTEGER PRIMARY KEY,
                    season_code TEXT NOT NULL,
                    gameweek INTEGER NOT NULL,
                    available_at_utc TEXT NOT NULL,
                    content_sha256 TEXT NOT NULL,
                    transport TEXT NOT NULL,
                    extraction_version TEXT NOT NULL,
                    provider_payload_sha256 TEXT,
                    player_count INTEGER NOT NULL,
                    fixture_prediction_count INTEGER NOT NULL,
                    appearance_probability_count INTEGER NOT NULL
                );
                CREATE TABLE fpl_form_fixture_predictions (
                    capture_id INTEGER NOT NULL,
                    source_player_id INTEGER NOT NULL,
                    fixture_id INTEGER NOT NULL,
                    player_name TEXT NOT NULL,
                    team_name TEXT NOT NULL,
                    position TEXT NOT NULL,
                    kickoff_local TEXT NOT NULL,
                    predicted_points TEXT NOT NULL,
                    appearance_probability TEXT,
                    PRIMARY KEY (capture_id, source_player_id, fixture_id)
                );
                """
            )
            connection.executemany(
                """
                INSERT INTO fpl_form_forecast_captures VALUES (
                    ?, '2026-27', 4, ?, ?, 'playwright-mcp/v1',
                    'fpl-form-page-data/v2', ?, 2, 4, 3
                );
                """,
                [
                    (
                        50,
                        "2026-09-11T10:00:00+00:00",
                        FplFormFeatureTableTests._digest("content", 50),
                        FplFormFeatureTableTests._digest("payload", 50),
                    ),
                    (
                        51,
                        "2026-09-11T13:00:00+00:00",
                        FplFormFeatureTableTests._digest("content", 51),
                        FplFormFeatureTableTests._digest("payload", 51),
                    ),
                ],
            )
            rows = [
                (1, 404, "Ada Example", "Beta", "midfielder",
                 "2026-09-14 16:00:00", "4", "0.8"),
                (1, 405, "Ada Example", "Beta", "midfielder",
                 "2026-09-17 16:00:00", "3", "0.9"),
                (2, 404, "New Player", "Alpha", "forward",
                 "2026-09-14 16:00:00", "2", "0.7"),
                (2, 405, "New Player", "Alpha", "forward",
                 "2026-09-17 16:00:00", "1", None),
            ]
            for capture_id in (50, 51):
                connection.executemany(
                    """
                    INSERT INTO fpl_form_fixture_predictions VALUES (
                        ?, ?, ?, ?, ?, ?, ?, ?, ?
                    );
                    """,
                    [(capture_id, *row) for row in rows],
                )

    @staticmethod
    def _digest(prefix: str, value: int) -> str:
        return hashlib.sha256(f"{prefix}-{value}".encode()).hexdigest()


if __name__ == "__main__":
    unittest.main()
