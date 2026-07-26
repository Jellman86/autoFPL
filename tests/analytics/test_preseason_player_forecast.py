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

from autofpl_analytics.preseason_player_forecast import (  # noqa: E402
    EVALUATION_RUN_IDENTITY,
    EXPECTED_GAMEWEEKS_SHA256,
    EXPECTED_PLAYERS_SHA256,
    EXPECTED_SOURCE_REVISION,
    build_preseason_player_forecast,
    main,
)
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402
from tests.analytics import (  # noqa: E402
    test_historical_preseason_evaluation as historical_helpers,
)


class PreseasonPlayerForecastTests(unittest.TestCase):
    def test_fixed_evaluated_model_emits_current_comparison_read_only(
        self,
    ) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            artifact = build_preseason_player_forecast(database)
            repeated = build_preseason_player_forecast(database)
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(artifact, repeated)
        self.assertEqual(before, after)
        self.assertEqual(
            "historical-preseason-player-gameweek-forecast",
            artifact["artifactType"],
        )
        self.assertEqual(
            "provisional-preseason-challenger",
            artifact["status"],
        )
        self.assertFalse(artifact["isPromoted"])
        self.assertFalse(artifact["influencesAdvice"])
        self.assertTrue(
            artifact["comparison"]["baselineStillDrivesAdvice"]
        )
        self.assertEqual(5, artifact["playerCount"])
        self.assertEqual(6, artifact["officialPlayerCount"])
        self.assertEqual(1, artifact["ineligiblePlayerCount"])
        self.assertEqual(4, artifact["priorSeasonIdentityMatchCount"])
        self.assertEqual(1, artifact["priorSeasonIdentityMissingCount"])
        self.assertEqual(
            EVALUATION_RUN_IDENTITY,
            artifact["training"]["evaluationRunIdentitySha256"],
        )
        self.assertEqual(
            12,
            artifact["training"]["trainingGameweekCount"],
        )
        self.assertEqual(
            144,
            artifact["training"]["trainingRowCount"],
        )
        for player in artifact["players"]:
            self.assertIsInstance(player["expectedPoints"], float)
            self.assertIsInstance(player["baselineV0ExpectedPoints"], float)
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
        new_player = self._player(artifact, 5)
        self.assertEqual(
            "no-prior-season-match",
            new_player["priorSeasonIdentityStatus"],
        )

    def test_archive_must_match_the_retained_evaluation(self) -> None:
        with self._database() as database:
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE historical_fpl_season_captures
                    SET gameweeks_sha256 = ?;
                    """,
                    ("f" * 64,),
                )
            with self.assertRaises(TemporalRidgeError) as context:
                build_preseason_player_forecast(database)
        self.assertEqual("data.archive-not-evaluated", context.exception.code)

    def test_fixed_target_and_cli_overwrite_fail_closed(self) -> None:
        with self._database() as database:
            with self.assertRaises(TemporalRidgeError) as context:
                build_preseason_player_forecast(
                    database,
                    season_code="2026-27",
                    gameweek=2,
                )
            self.assertEqual("configuration.target", context.exception.code)

            output = database.parent / "forecast.json"
            arguments = ["--database", str(database), "--output", str(output)]
            self.assertEqual(0, main(arguments))
            written = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                repeated = main(arguments)
        self.assertEqual(
            "provisional-preseason-challenger",
            written["status"],
        )
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
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as path:
            baseline_players = [
                {
                    "playerId": player_id,
                    "expectedPoints": 2.0 + player_id / 10.0,
                }
                for player_id in range(1, 6)
            ]
            with sqlite3.connect(path) as connection:
                connection.executescript(
                    """
                    ALTER TABLE historical_fpl_season_captures
                        ADD COLUMN source_key TEXT NOT NULL
                        DEFAULT 'vaastav-fpl-historical/v1';
                    ALTER TABLE historical_fpl_players
                        ADD COLUMN final_team_id INTEGER NOT NULL DEFAULT 1;
                    ALTER TABLE historical_fpl_players
                        ADD COLUMN final_status TEXT NOT NULL DEFAULT 'a';
                    ALTER TABLE historical_fpl_players
                        ADD COLUMN final_chance_next_round INTEGER;
                    ALTER TABLE historical_fpl_players
                        ADD COLUMN final_news_sha256 TEXT NOT NULL
                        DEFAULT 'fixture-news-hash';
                    ALTER TABLE historical_fpl_players
                        ADD COLUMN final_news_added_utc TEXT;
                    ALTER TABLE historical_fpl_player_gameweeks
                        ADD COLUMN team_name TEXT NOT NULL DEFAULT 'Prior';
                    ALTER TABLE historical_fpl_player_gameweeks
                        ADD COLUMN recoveries INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE historical_fpl_player_gameweeks
                        ADD COLUMN tackles INTEGER NOT NULL DEFAULT 0;
                    """
                )
                connection.execute(
                    """
                    UPDATE historical_fpl_season_captures
                    SET source_revision = ?,
                        players_sha256 = ?,
                        gameweeks_sha256 = ?;
                    """,
                    (
                        EXPECTED_SOURCE_REVISION,
                        EXPECTED_PLAYERS_SHA256,
                        EXPECTED_GAMEWEEKS_SHA256,
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
                        '2026-07-27T08:00:00+00:00',
                        ?, ?, 6
                    );
                    """,
                    ("c" * 64, "d" * 64),
                )
                connection.executemany(
                    "INSERT INTO official_fpl_teams VALUES (50, ?, ?);",
                    [(1, "One"), (2, "Two")],
                )
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
                            (
                                1000 + player_id
                                if player_id <= 4
                                else 2000 + player_id
                            ),
                            f"Player {player_id}",
                            positions[(player_id - 1) % len(positions)],
                            1 if player_id % 2 else 2,
                            (
                                "i"
                                if player_id == 3
                                else "u"
                                if player_id == 6
                                else "a"
                            ),
                            0 if player_id == 3 else None,
                        )
                        for player_id in range(1, 7)
                    ],
                )
                connection.execute(
                    """
                    INSERT INTO official_fpl_fixtures
                    VALUES (50, 900, 1, 1, 2);
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
