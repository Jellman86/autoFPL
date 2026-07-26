from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import tempfile
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.feature_table import (  # noqa: E402
    FeatureTableError,
    build_feature_table,
    main,
)


class FeatureTableTests(unittest.TestCase):
    def test_features_use_only_latest_correction_available_at_target_cutoff(
        self,
    ) -> None:
        with self._database() as database_path:
            gameweek_three = build_feature_table(
                database_path,
                "2026-27",
                3,
            )
            gameweek_four = build_feature_table(
                database_path,
                "2026-27",
                4,
            )

        gw3_player = self._player(gameweek_three, 1)
        self.assertEqual([1, 2], gw3_player["history"]["gameweeks"])
        self.assertEqual(4, gw3_player["history"]["latest"]["totalPoints"])
        self.assertEqual(
            3.0,
            gw3_player["history"]["rolling"]["3"]["totalPointsMean"],
        )
        self.assertEqual(
            3.0,
            gw3_player["history"]["exponentiallyWeighted"]["totalPointsMean"],
        )
        self.assertEqual(
            [10, 20],
            [
                item["outcomeCaptureId"]
                for item in gameweek_three["provenance"][
                    "historyOutcomeCaptures"
                ]
            ],
        )
        self.assertEqual(
            0.4,
            gw3_player["history"]["rolling"]["3"]["expectedGoalsMean"],
        )
        self.assertEqual(
            1,
            gw3_player["history"]["rolling"]["3"][
                "expectedGoalsSampleCount"
            ],
        )

        gw4_player = self._player(gameweek_four, 1)
        self.assertEqual([1, 2, 3], gw4_player["history"]["gameweeks"])
        self.assertEqual(8, gw4_player["history"]["latest"]["totalPoints"])
        self.assertEqual(
            6.0,
            gw4_player["history"]["rolling"]["3"]["totalPointsMean"],
        )
        self.assertEqual(
            6.5,
            gw4_player["history"]["exponentiallyWeighted"]["totalPointsMean"],
        )
        self.assertEqual(
            [11, 20, 30],
            [
                item["outcomeCaptureId"]
                for item in gameweek_four["provenance"][
                    "historyOutcomeCaptures"
                ]
            ],
        )
        self.assertNotEqual(
            99,
            gw4_player["history"]["rolling"]["5"]["totalPointsMean"],
        )
        self.assertEqual(
            0.6,
            gw4_player["history"]["rolling"]["3"]["expectedGoalsMean"],
        )
        self.assertEqual(
            3,
            gw4_player["history"]["rolling"]["3"][
                "expectedGoalsSampleCount"
            ],
        )
        self.assertEqual(
            0.65,
            gw4_player["history"]["exponentiallyWeighted"][
                "expectedGoalsMean"
            ],
        )
        self.assertNotEqual(
            9.9,
            gw4_player["history"]["rolling"]["5"]["expectedGoalsMean"],
        )

    def test_rows_expose_match_leading_context_and_honest_missingness(
        self,
    ) -> None:
        with self._database() as database_path:
            table = build_feature_table(database_path, "2026-27", 4)

        player = self._player(table, 1)
        self.assertEqual(2, player["targetFixtureCount"])
        self.assertEqual(
            ["AAA", "AAA"],
            [
                fixture["opponentShortName"]
                for fixture in player["targetFixtures"]
            ],
        )
        self.assertEqual(7.0, player["restDaysBeforeFirstKickoff"])
        self.assertEqual(3.0, player["minimumRestDaysWithinGameweek"])
        self.assertEqual(3, player["history"]["sampleCount"])
        self.assertEqual(1, player["history"]["rolling"]["1"]["sampleCount"])
        self.assertEqual(3, player["history"]["rolling"]["5"]["sampleCount"])
        self.assertEqual(
            45.0,
            player["history"]["rolling"]["3"]["minutesMean"],
        )
        self.assertEqual(
            0.666667,
            player["history"]["rolling"]["3"]["playedRate"],
        )
        team = next(item for item in table["teams"] if item["teamId"] == 2)
        self.assertEqual(2, team["history"]["matchCount"])
        self.assertEqual(
            2.5,
            team["history"]["rolling"]["3"]["goalsForMean"],
        )
        self.assertEqual(
            1.0,
            team["history"]["rolling"]["3"]["goalsAgainstMean"],
        )
        self.assertEqual(
            3.0,
            team["history"]["rolling"]["3"]["pointsPerMatch"],
        )
        self.assertEqual(
            1,
            team["history"]["rolling"]["3"]["home"]["sampleCount"],
        )
        self.assertEqual(
            1,
            team["history"]["rolling"]["3"]["away"]["sampleCount"],
        )

        new_player = self._player(table, 2)
        self.assertFalse(new_player["history"]["hasPriorOutcome"])
        self.assertEqual(0, new_player["history"]["sampleCount"])
        self.assertIsNone(new_player["history"]["latest"])
        self.assertIsNone(new_player["history"]["exponentiallyWeighted"])
        self.assertIsNone(
            new_player["history"]["rolling"]["5"]["totalPointsMean"]
        )
        self.assertIsNone(
            new_player["history"]["rolling"]["5"]["expectedGoalsMean"]
        )
        self.assertEqual(
            0,
            new_player["history"]["rolling"]["5"][
                "expectedGoalsSampleCount"
            ],
        )
        self.assertEqual("official-temporal-v2", table["featureSet"])
        self.assertEqual(
            "null-preserved-with-per-metric-sample-count",
            table["configuration"]["underlyingMetricMissingness"],
        )
        self.assertEqual(
            "replay.availableAtUtc <= target.deadlineUtc; "
            "history.gameweek < target.gameweek; "
            "history.availableAtUtc <= replay.availableAtUtc; "
            "latest eligible correction per history Gameweek",
            table["availabilityRule"],
        )
        self.assertEqual(
            "2026-09-11T12:00:00+00:00",
            table["decisionCutoffUtc"],
        )
        self.assertEqual("2026-09-12T12:00:00+00:00", table["deadlineUtc"])
        self.assertEqual(400, table["provenance"]["replayCaptureId"])
        self.assertFalse(table["isPromoted"])
        self.assertEqual("exploratory", table["status"])

    def test_output_is_deterministic_read_only_and_refuses_overwrite(
        self,
    ) -> None:
        with self._database() as database_path:
            before = self._hash(database_path)
            first = build_feature_table(database_path, "2026-27", 4)
            second = build_feature_table(database_path, "2026-27", 4)
            after = self._hash(database_path)
            output_path = database_path.parent / "features.json"

            self.assertEqual(first, second)
            self.assertEqual(before, after)
            self.assertEqual(64, len(first["dataIdentitySha256"]))
            self.assertEqual(64, len(first["runIdentitySha256"]))
            self.assertEqual(
                0,
                main(
                    [
                        "--database",
                        str(database_path),
                        "--season",
                        "2026-27",
                        "--gameweek",
                        "4",
                        "--output",
                        str(output_path),
                    ]
                ),
            )
            written = json.loads(output_path.read_text(encoding="utf-8"))
            self.assertEqual(first, written)
            errors = StringIO()
            with redirect_stderr(errors):
                exit_code = main(
                    [
                        "--database",
                        str(database_path),
                        "--season",
                        "2026-27",
                        "--gameweek",
                        "4",
                        "--output",
                        str(output_path),
                    ]
                )
            self.assertEqual(2, exit_code)
            self.assertEqual("output.exists", json.loads(errors.getvalue())["code"])

    def test_missing_target_replay_fails_closed(self) -> None:
        with self._database() as database_path:
            with self.assertRaises(FeatureTableError) as raised:
                build_feature_table(database_path, "2026-27", 5)
        self.assertEqual("data.target-replay-not-found", raised.exception.code)

    def test_additive_newer_database_schema_is_supported(self) -> None:
        with self._database() as database_path:
            with sqlite3.connect(database_path) as connection:
                connection.execute(
                    "INSERT INTO schema_migrations (version) VALUES (11);"
                )

            table = build_feature_table(database_path, "2026-27", 4)

        self.assertEqual(4, table["gameweek"])

    def test_pre_underlying_database_schema_is_rejected(self) -> None:
        with self._database() as database_path:
            with sqlite3.connect(database_path) as connection:
                connection.execute(
                    "UPDATE schema_migrations SET version = 9;"
                )
            with self.assertRaises(FeatureTableError) as raised:
                build_feature_table(database_path, "2026-27", 4)
        self.assertEqual("database.schema-version", raised.exception.code)

    def test_incomplete_history_capture_fails_closed(self) -> None:
        with self._database() as database_path:
            with sqlite3.connect(database_path) as connection:
                connection.execute(
                    """
                    DELETE FROM official_fpl_player_outcomes
                    WHERE outcome_capture_id = 30;
                    """
                )
                connection.commit()
            with self.assertRaises(FeatureTableError) as raised:
                build_feature_table(database_path, "2026-27", 4)
        self.assertEqual(
            "data.incomplete-history-player-coverage",
            raised.exception.code,
        )

    @staticmethod
    def _player(table: dict, player_id: int) -> dict:
        return next(
            player
            for player in table["players"]
            if player["playerId"] == player_id
        )

    @staticmethod
    def _hash(path: Path) -> str:
        return hashlib.sha256(path.read_bytes()).hexdigest()

    class _database:
        def __init__(self) -> None:
            self._temporary_directory: tempfile.TemporaryDirectory[str] | None = (
                None
            )

        def __enter__(self) -> Path:
            self._temporary_directory = tempfile.TemporaryDirectory(
                prefix="autofpl-feature-tests-"
            )
            path = Path(self._temporary_directory.name) / "autofpl.db"
            FeatureTableTests._create_database(path)
            return path

        def __exit__(self, *_: object) -> None:
            assert self._temporary_directory is not None
            self._temporary_directory.cleanup()

    @staticmethod
    def _create_database(path: Path) -> None:
        with sqlite3.connect(path) as connection:
            connection.executescript(
                """
                CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY);
                INSERT INTO schema_migrations (version) VALUES (10);

                CREATE TABLE official_fpl_captures (
                    capture_id INTEGER PRIMARY KEY,
                    season_code TEXT NOT NULL,
                    available_at_utc TEXT NOT NULL,
                    bootstrap_sha256 TEXT NOT NULL,
                    fixtures_sha256 TEXT NOT NULL,
                    player_count INTEGER NOT NULL
                );
                CREATE TABLE official_fpl_events (
                    capture_id INTEGER NOT NULL,
                    event_id INTEGER NOT NULL,
                    deadline_utc TEXT NOT NULL,
                    PRIMARY KEY (capture_id, event_id)
                );
                CREATE TABLE official_fpl_teams (
                    capture_id INTEGER NOT NULL,
                    team_id INTEGER NOT NULL,
                    short_name TEXT NOT NULL,
                    PRIMARY KEY (capture_id, team_id)
                );
                CREATE TABLE official_fpl_players (
                    capture_id INTEGER NOT NULL,
                    player_id INTEGER NOT NULL,
                    code INTEGER NOT NULL,
                    first_name TEXT NOT NULL,
                    second_name TEXT NOT NULL,
                    team_id INTEGER NOT NULL,
                    position TEXT NOT NULL,
                    price_tenths INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    chance_next_round INTEGER,
                    selected_by_percent TEXT NOT NULL,
                    total_points INTEGER NOT NULL,
                    minutes INTEGER NOT NULL,
                    starts INTEGER NOT NULL,
                    PRIMARY KEY (capture_id, player_id)
                );
                CREATE TABLE official_fpl_fixtures (
                    capture_id INTEGER NOT NULL,
                    fixture_id INTEGER NOT NULL,
                    event_id INTEGER,
                    home_team_id INTEGER NOT NULL,
                    away_team_id INTEGER NOT NULL,
                    kickoff_utc TEXT,
                    finished INTEGER NOT NULL,
                    home_score INTEGER,
                    away_score INTEGER,
                    PRIMARY KEY (capture_id, fixture_id)
                );
                CREATE TABLE official_fpl_outcome_captures (
                    outcome_capture_id INTEGER PRIMARY KEY,
                    season_code TEXT NOT NULL,
                    gameweek INTEGER NOT NULL,
                    available_at_utc TEXT NOT NULL,
                    live_sha256 TEXT NOT NULL,
                    player_count INTEGER NOT NULL
                );
                CREATE TABLE official_fpl_player_outcomes (
                    outcome_capture_id INTEGER NOT NULL,
                    player_id INTEGER NOT NULL,
                    minutes INTEGER NOT NULL,
                    starts INTEGER NOT NULL,
                    total_points INTEGER NOT NULL,
                    goals_scored INTEGER NOT NULL,
                    assists INTEGER NOT NULL,
                    clean_sheets INTEGER NOT NULL,
                    goals_conceded INTEGER NOT NULL,
                    saves INTEGER NOT NULL,
                    bonus INTEGER NOT NULL,
                    yellow_cards INTEGER NOT NULL,
                    red_cards INTEGER NOT NULL,
                    own_goals INTEGER,
                    penalties_saved INTEGER,
                    penalties_missed INTEGER,
                    bps INTEGER,
                    influence REAL,
                    creativity REAL,
                    threat REAL,
                    ict_index REAL,
                    clearances_blocks_interceptions INTEGER,
                    recoveries INTEGER,
                    tackles INTEGER,
                    defensive_contribution INTEGER,
                    expected_goals REAL,
                    expected_assists REAL,
                    expected_goal_involvements REAL,
                    expected_goals_conceded REAL,
                    PRIMARY KEY (outcome_capture_id, player_id)
                );
                """
            )
            captures = [
                (
                    300,
                    "2026-27",
                    "2026-09-04T12:00:00+00:00",
                    FeatureTableTests._digest("bootstrap", 300),
                    FeatureTableTests._digest("fixtures", 300),
                    2,
                ),
                (
                    400,
                    "2026-27",
                    "2026-09-11T12:00:00+00:00",
                    FeatureTableTests._digest("bootstrap", 400),
                    FeatureTableTests._digest("fixtures", 400),
                    2,
                ),
                (
                    401,
                    "2026-27",
                    "2026-09-12T12:00:01+00:00",
                    FeatureTableTests._digest("bootstrap", 401),
                    FeatureTableTests._digest("fixtures", 401),
                    999,
                ),
            ]
            connection.executemany(
                """
                INSERT INTO official_fpl_captures (
                    capture_id,
                    season_code,
                    available_at_utc,
                    bootstrap_sha256,
                    fixtures_sha256,
                    player_count
                ) VALUES (?, ?, ?, ?, ?, ?);
                """,
                captures,
            )
            connection.executemany(
                """
                INSERT INTO official_fpl_events (
                    capture_id,
                    event_id,
                    deadline_utc
                ) VALUES (?, ?, ?);
                """,
                [
                    (300, 3, "2026-09-05T12:00:00+00:00"),
                    (400, 4, "2026-09-12T12:00:00+00:00"),
                    (401, 4, "2026-09-12T12:00:00+00:00"),
                ],
            )
            for capture_id in (300, 400):
                connection.executemany(
                    """
                    INSERT INTO official_fpl_teams (
                        capture_id,
                        team_id,
                        short_name
                    ) VALUES (?, ?, ?);
                    """,
                    [(capture_id, 1, "AAA"), (capture_id, 2, "BBB")],
                )
                connection.executemany(
                    """
                    INSERT INTO official_fpl_players (
                        capture_id,
                        player_id,
                        code,
                        first_name,
                        second_name,
                        team_id,
                        position,
                        price_tenths,
                        status,
                        chance_next_round,
                        selected_by_percent,
                        total_points,
                        minutes,
                        starts
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                    """,
                    [
                        (
                            capture_id,
                            1,
                            101,
                            "Ada",
                            "Example",
                            2,
                            "midfielder",
                            75,
                            "a",
                            None,
                            "10.5",
                            14,
                            135,
                            2,
                        ),
                        (
                            capture_id,
                            2,
                            102,
                            "New",
                            "Player",
                            1,
                            "forward",
                            60,
                            "a",
                            None,
                            "1.0",
                            0,
                            0,
                            0,
                        ),
                    ],
                )
            connection.executemany(
                """
                INSERT INTO official_fpl_fixtures (
                    capture_id,
                    fixture_id,
                    event_id,
                    home_team_id,
                    away_team_id,
                    kickoff_utc,
                    finished,
                    home_score,
                    away_score
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                [
                    (
                        300,
                        302,
                        2,
                        1,
                        2,
                        "2026-08-31T15:00:00+00:00",
                        1,
                        1,
                        2,
                    ),
                    (
                        300,
                        303,
                        3,
                        1,
                        2,
                        "2026-09-07T15:00:00+00:00",
                        0,
                        None,
                        None,
                    ),
                    (
                        400,
                        402,
                        2,
                        1,
                        2,
                        "2026-08-31T15:00:00+00:00",
                        1,
                        1,
                        2,
                    ),
                    (
                        400,
                        403,
                        3,
                        2,
                        1,
                        "2026-09-07T15:00:00+00:00",
                        1,
                        3,
                        1,
                    ),
                    (
                        400,
                        404,
                        4,
                        1,
                        2,
                        "2026-09-14T15:00:00+00:00",
                        0,
                        None,
                        None,
                    ),
                    (
                        400,
                        405,
                        4,
                        1,
                        2,
                        "2026-09-17T15:00:00+00:00",
                        0,
                        None,
                        None,
                    ),
                ],
            )
            captures_and_values = [
                (10, 1, "2026-08-23T12:00:00+00:00", 2, 90, None),
                (11, 1, "2026-09-10T12:00:00+00:00", 6, 90, 0.6),
                (12, 1, "2026-09-20T12:00:00+00:00", 99, 90, 9.9),
                (20, 2, "2026-08-30T12:00:00+00:00", 4, 45, 0.4),
                (30, 3, "2026-09-08T12:00:00+00:00", 8, 0, 0.8),
            ]
            for (
                outcome_id,
                gameweek,
                available_at,
                points,
                minutes,
                expected_goals,
            ) in (
                captures_and_values
            ):
                connection.execute(
                    """
                    INSERT INTO official_fpl_outcome_captures (
                        outcome_capture_id,
                        season_code,
                        gameweek,
                        available_at_utc,
                        live_sha256,
                        player_count
                    ) VALUES (?, '2026-27', ?, ?, ?, 1);
                    """,
                    (
                        outcome_id,
                        gameweek,
                        available_at,
                        FeatureTableTests._digest("live", outcome_id),
                    ),
                )
                connection.execute(
                    """
                    INSERT INTO official_fpl_player_outcomes (
                        outcome_capture_id,
                        player_id,
                        minutes,
                        starts,
                        total_points,
                        goals_scored,
                        assists,
                        clean_sheets,
                        goals_conceded,
                        saves,
                        bonus,
                        yellow_cards,
                        red_cards,
                        bps,
                        ict_index,
                        defensive_contribution,
                        expected_goals,
                        expected_assists,
                        expected_goal_involvements,
                        expected_goals_conceded
                    ) VALUES (
                        ?, 1, ?, ?, ?, ?, ?, ?, ?, 0, ?, 0, 0,
                        ?, ?, ?, ?, ?, ?, ?
                    );
                    """,
                    (
                        outcome_id,
                        minutes,
                        int(minutes > 0),
                        points,
                        int(points >= 6),
                        int(points == 4),
                        int(minutes >= 60),
                        int(minutes > 0),
                        max(0, points - 5),
                        None if expected_goals is None else points + 20,
                        None if expected_goals is None else points / 2,
                        None if expected_goals is None else points + 5,
                        expected_goals,
                        (
                            None
                            if expected_goals is None
                            else expected_goals / 2
                        ),
                        (
                            None
                            if expected_goals is None
                            else expected_goals * 1.5
                        ),
                        (
                            None
                            if expected_goals is None
                            else expected_goals + 0.2
                        ),
                    ),
                )
            connection.commit()

    @staticmethod
    def _digest(prefix: str, value: int) -> str:
        return hashlib.sha256(f"{prefix}-{value}".encode()).hexdigest()


if __name__ == "__main__":
    unittest.main()
