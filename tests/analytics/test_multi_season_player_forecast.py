from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import unittest
from contextlib import contextmanager, redirect_stderr
from io import StringIO
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.multi_season_player_forecast import (  # noqa: E402
    ARTIFACT_TYPE,
    EVALUATION_RUN_IDENTITY,
    EXPECTED_ARCHIVES,
    MODEL_KEY,
    STATUS,
    build_multi_season_player_forecast,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics import test_multi_season_evaluation as helpers  # noqa: E402


class MultiSeasonPlayerForecastTests(unittest.TestCase):
    def test_fixed_shadow_is_deterministic_read_only_and_separate(self) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            artifact = build_multi_season_player_forecast(database)
            repeated = build_multi_season_player_forecast(database)
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(artifact, repeated)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, artifact["artifactType"])
        self.assertEqual(MODEL_KEY, artifact["modelKey"])
        self.assertEqual(STATUS, artifact["status"])
        self.assertFalse(artifact["isPromoted"])
        self.assertFalse(artifact["influencesAdvice"])
        self.assertTrue(
            artifact["comparison"]["baselineStillDrivesAdvice"]
        )
        self.assertEqual(
            EVALUATION_RUN_IDENTITY,
            artifact["training"]["evaluationRunIdentitySha256"],
        )
        self.assertEqual(10, artifact["training"]["trainingOriginCount"])
        self.assertEqual(60, artifact["training"]["trainingRowCount"])
        self.assertEqual(6, artifact["playerCount"])
        self.assertEqual(7, artifact["officialPlayerCount"])
        self.assertEqual(1, artifact["ineligiblePlayerCount"])
        self.assertEqual(
            {
                "both-historical-seasons": 2,
                "latest-historical-season-only": 2,
                "no-historical-season-match": 1,
                "older-historical-season-only": 1,
            },
            artifact["historicalIdentityCounts"],
        )
        for player in artifact["players"]:
            self.assertIsInstance(player["expectedPoints"], float)
            self.assertEqual(
                round(
                    player["expectedPoints"]
                    - player["baselineV0ExpectedPoints"],
                    6,
                ),
                player["differenceFromBaselineV0"],
            )
        injured = self._player(artifact, 3)
        self.assertEqual("i", injured["officialStatus"])
        self.assertEqual(
            "authoritative-current-official-not-modelled",
            injured["availabilityStatus"],
        )
        self.assertEqual(
            "older-historical-season-only",
            injured["historicalIdentityStatus"],
        )
        new_player = self._player(artifact, 4)
        self.assertEqual(
            "no-historical-season-match",
            new_player["historicalIdentityStatus"],
        )

    def test_exact_archives_must_be_available_at_target_cutoff(self) -> None:
        with self._database() as database:
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE official_fpl_captures
                    SET available_at_utc = '2026-06-30T00:00:00+00:00';
                    """
                )
            with self.assertRaises(TemporalRidgeError) as context:
                build_multi_season_player_forecast(database)
        self.assertEqual(
            "data.evaluated-archive-unavailable-at-cutoff",
            context.exception.code,
        )

        with self._database() as database:
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE historical_fpl_season_captures
                    SET players_sha256 = ?
                    WHERE season_code = '2024-25';
                    """,
                    ("f" * 64,),
                )
            with self.assertRaises(TemporalRidgeError) as context:
                build_multi_season_player_forecast(database)
        self.assertEqual("data.archive-not-evaluated", context.exception.code)

    def test_fixed_target_and_cli_overwrite_fail_closed(self) -> None:
        with self._database() as database:
            with self.assertRaises(TemporalRidgeError) as context:
                build_multi_season_player_forecast(
                    database,
                    season_code="2026-27",
                    gameweek=2,
                )
            self.assertEqual("configuration.target", context.exception.code)

            output = database.parent / "two-season-shadow.json"
            arguments = ["--database", str(database), "--output", str(output)]
            self.assertEqual(0, main(arguments))
            written = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                repeated = main(arguments)
        self.assertEqual(STATUS, written["status"])
        self.assertEqual(1, repeated)
        self.assertIn("output.already-exists", errors.getvalue())

    @staticmethod
    def _player(artifact: dict, player_id: int) -> dict:
        return next(
            player
            for player in artifact["players"]
            if player["playerId"] == player_id
        )

    @contextmanager
    def _database(self):
        helper = helpers.MultiSeasonEvaluationTests()
        with helper._database() as path:
            baseline_players = [
                {
                    "playerId": player_id,
                    "expectedPoints": 2.0 + player_id / 10.0,
                }
                for player_id in range(1, 7)
            ]
            with sqlite3.connect(path) as connection:
                for season_code, expected in EXPECTED_ARCHIVES.items():
                    connection.execute(
                        """
                        UPDATE historical_fpl_season_captures
                        SET source_revision = ?,
                            players_sha256 = ?,
                            gameweeks_sha256 = ?
                        WHERE season_code = ?;
                        """,
                        (
                            expected["sourceRevision"],
                            expected["playersSha256"],
                            expected["gameweeksSha256"],
                            season_code,
                        ),
                    )
                connection.executescript(
                    """
                    CREATE TABLE official_fpl_captures (
                        capture_id INTEGER PRIMARY KEY,
                        season_code TEXT NOT NULL,
                        next_gameweek_number INTEGER,
                        next_deadline_utc TEXT,
                        available_at_utc TEXT NOT NULL,
                        bootstrap_sha256 TEXT NOT NULL,
                        fixtures_sha256 TEXT NOT NULL,
                        player_count INTEGER NOT NULL
                    );
                    CREATE TABLE official_fpl_teams (
                        capture_id INTEGER NOT NULL,
                        team_id INTEGER NOT NULL,
                        name TEXT NOT NULL
                    );
                    CREATE TABLE official_fpl_players (
                        capture_id INTEGER NOT NULL,
                        player_id INTEGER NOT NULL,
                        code INTEGER NOT NULL,
                        web_name TEXT NOT NULL,
                        position TEXT NOT NULL,
                        team_id INTEGER NOT NULL,
                        status TEXT NOT NULL,
                        chance_next_round INTEGER,
                        news TEXT NOT NULL,
                        news_added_utc TEXT,
                        minutes INTEGER NOT NULL,
                        starts INTEGER NOT NULL,
                        total_points INTEGER NOT NULL
                    );
                    CREATE TABLE official_fpl_fixtures (
                        capture_id INTEGER NOT NULL,
                        fixture_id INTEGER NOT NULL,
                        event_id INTEGER,
                        kickoff_utc TEXT,
                        home_team_id INTEGER NOT NULL,
                        away_team_id INTEGER NOT NULL
                    );
                    CREATE TABLE player_gameweek_forecast_artifacts (
                        forecast_artifact_id INTEGER PRIMARY KEY,
                        official_capture_id INTEGER NOT NULL,
                        model_key TEXT NOT NULL,
                        document_json TEXT NOT NULL
                    );
                    """
                )
                connection.execute(
                    """
                    INSERT INTO official_fpl_captures VALUES (
                        50, '2026-27', 1,
                        '2026-08-21T17:30:00+00:00',
                        '2026-07-20T08:00:00+00:00',
                        ?, ?, 7
                    );
                    """,
                    ("c" * 64, "d" * 64),
                )
                connection.executemany(
                    "INSERT INTO official_fpl_teams VALUES (50, ?, ?);",
                    [(1, "One"), (2, "Two")],
                )
                player_codes = (1001, 2001, 1005, 3001, 1002, 2002, 3002)
                positions = (
                    "goalkeeper",
                    "defender",
                    "midfielder",
                    "forward",
                )
                connection.executemany(
                    """
                    INSERT INTO official_fpl_players VALUES (
                        50, ?, ?, ?, ?, ?, ?, ?, '', NULL, 0, 0, 0
                    );
                    """,
                    [
                        (
                            player_id,
                            player_codes[player_id - 1],
                            f"Player {player_id}",
                            positions[(player_id - 1) % len(positions)],
                            1 if player_id % 2 else 2,
                            (
                                "i"
                                if player_id == 3
                                else "u"
                                if player_id == 7
                                else "a"
                            ),
                            0 if player_id == 3 else None,
                        )
                        for player_id in range(1, 8)
                    ],
                )
                connection.execute(
                    """
                    INSERT INTO official_fpl_fixtures VALUES (
                        50, 900, 1, '2026-08-21T19:00:00+00:00', 1, 2
                    );
                    """
                )
                connection.execute(
                    """
                    INSERT INTO player_gameweek_forecast_artifacts
                    VALUES (1, 50, ?, ?);
                    """,
                    (
                        "official-market-baseline-v0-player-table",
                        json.dumps({"players": baseline_players}),
                    ),
                )
            yield path


if __name__ == "__main__":
    unittest.main()
