from __future__ import annotations

import hashlib
import sqlite3
import sys
import tempfile
import unittest
from contextlib import contextmanager, redirect_stderr
from io import StringIO
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.cross_season_player_state import (  # noqa: E402
    CrossSeasonPlayerStateError,
    build_cross_season_player_state,
    main,
)


class CrossSeasonPlayerStateTests(unittest.TestCase):
    def test_stable_code_join_builds_temporal_state_and_current_health_wins(
        self,
    ) -> None:
        with self._database() as database_path:
            state = build_cross_season_player_state(
                database_path,
                "2026-27",
                1,
            )

        raya = self._player(state, 101)
        self.assertTrue(raya["hasPriorSeasonIdentity"])
        self.assertEqual("stable-official-code", raya["identityMatch"])
        self.assertTrue(raya["currentAvailability"]["isAuthoritative"])
        self.assertEqual("d", raya["currentAvailability"]["status"])
        self.assertFalse(raya["archivedFinalHealth"]["isAuthoritative"])
        self.assertEqual("a", raya["archivedFinalHealth"]["status"])
        self.assertEqual("durability-prior-only", raya["archivedFinalHealth"]["use"])
        self.assertEqual(3, raya["priorSeason"]["gameweekSampleCount"])
        self.assertEqual(2, raya["priorSeason"]["appearanceGameweekCount"])
        self.assertEqual(2, raya["priorSeason"]["lastAppearanceGameweek"])
        self.assertEqual(1, raya["priorSeason"]["trailingZeroMinuteGameweeks"])
        self.assertEqual(
            45.0,
            raya["priorSeason"]["rolling"]["3"]["minutesMean"],
        )
        self.assertEqual(
            0.666667,
            raya["priorSeason"]["rolling"]["3"]["appearanceRate"],
        )
        self.assertEqual(
            6.0,
            raya["priorSeason"]["rolling"]["1"]["totalPointsMean"],
        )
        self.assertEqual(0.25, raya["priorSeason"]["exponentiallyWeighted"]["alpha"])
        self.assertEqual(10, raya["currentSeasonObserved"]["minutes"])
        self.assertTrue(raya["teamNameChanged"])
        self.assertFalse(raya["positionChanged"])

        new_player = self._player(state, 102)
        self.assertFalse(new_player["hasPriorSeasonIdentity"])
        self.assertIsNone(new_player["archivedFinalHealth"])
        self.assertEqual(0, new_player["priorSeason"]["gameweekSampleCount"])
        self.assertIsNone(new_player["priorSeason"]["season"]["minutesMean"])
        self.assertEqual(1, state["priorSeasonIdentityMatchCount"])
        self.assertEqual(1, state["priorSeasonIdentityMissingCount"])
        self.assertFalse(state["influencesForecast"])
        self.assertFalse(state["isPromoted"])
        self.assertTrue(state["configuration"]["xPExcluded"])

    def test_artifact_is_deterministic_read_only_and_refuses_overwrite(
        self,
    ) -> None:
        with self._database() as database_path:
            before = self._hash(database_path)
            first = build_cross_season_player_state(
                database_path,
                "2026-27",
                1,
            )
            second = build_cross_season_player_state(
                database_path,
                "2026-27",
                1,
            )
            after = self._hash(database_path)
            output = database_path.parent / "cross-season.json"

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
                        "1",
                        "--output",
                        str(output),
                    ]
                ),
            )
            stderr = StringIO()
            with redirect_stderr(stderr):
                result = main(
                    [
                        "--database",
                        str(database_path),
                        "--season",
                        "2026-27",
                        "--gameweek",
                        "1",
                        "--output",
                        str(output),
                    ]
                )
            self.assertEqual(2, result)
            self.assertIn('"code": "output.exists"', stderr.getvalue())

    def test_archive_must_be_available_by_current_target_cutoff(self) -> None:
        with self._database(
            archive_available_at="2026-08-02T00:00:00+00:00"
        ) as database_path:
            with self.assertRaises(CrossSeasonPlayerStateError) as context:
                build_cross_season_player_state(
                    database_path,
                    "2026-27",
                    1,
                )
        self.assertEqual(
            "data.prior-season-archive-not-found",
            context.exception.code,
        )

    @staticmethod
    def _player(state: dict, player_id: int) -> dict:
        return next(
            player
            for player in state["players"]
            if player["playerId"] == player_id
        )

    @staticmethod
    def _hash(path: Path) -> str:
        return hashlib.sha256(path.read_bytes()).hexdigest()

    @contextmanager
    def _database(
        self,
        archive_available_at: str = "2026-07-20T00:00:00+00:00",
    ):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "autofpl.db"
            connection = sqlite3.connect(path)
            connection.executescript(
                """
                CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY);
                INSERT INTO schema_migrations VALUES (19);

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
                    name TEXT NOT NULL,
                    PRIMARY KEY (capture_id, team_id)
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
                    total_points INTEGER NOT NULL,
                    PRIMARY KEY (capture_id, player_id)
                );
                CREATE TABLE historical_fpl_season_captures (
                    capture_id INTEGER PRIMARY KEY,
                    source_key TEXT NOT NULL,
                    season_code TEXT NOT NULL,
                    source_revision TEXT NOT NULL,
                    available_at_utc TEXT NOT NULL,
                    players_sha256 TEXT NOT NULL,
                    gameweeks_sha256 TEXT NOT NULL,
                    player_count INTEGER NOT NULL,
                    player_gameweek_count INTEGER NOT NULL,
                    stable_code_count INTEGER NOT NULL
                );
                CREATE TABLE historical_fpl_players (
                    capture_id INTEGER NOT NULL,
                    player_code INTEGER NOT NULL,
                    position TEXT NOT NULL,
                    final_team_id INTEGER NOT NULL,
                    final_status TEXT NOT NULL,
                    final_chance_next_round INTEGER,
                    final_news_sha256 TEXT NOT NULL,
                    final_news_added_utc TEXT
                );
                CREATE TABLE historical_fpl_player_gameweeks (
                    capture_id INTEGER NOT NULL,
                    player_code INTEGER NOT NULL,
                    gameweek INTEGER NOT NULL,
                    fixture_id INTEGER NOT NULL,
                    kickoff_utc TEXT NOT NULL,
                    team_name TEXT NOT NULL,
                    minutes INTEGER NOT NULL,
                    starts INTEGER NOT NULL,
                    total_points INTEGER NOT NULL,
                    expected_goals TEXT NOT NULL,
                    expected_assists TEXT NOT NULL,
                    expected_goal_involvements TEXT NOT NULL,
                    expected_goals_conceded TEXT NOT NULL,
                    defensive_contribution INTEGER NOT NULL,
                    recoveries INTEGER NOT NULL,
                    tackles INTEGER NOT NULL
                );
                """
            )
            connection.execute(
                """
                INSERT INTO official_fpl_captures VALUES (
                    10, '2026-27', 1, '2026-08-01T12:00:00+00:00',
                    '2026-07-26T10:00:00+00:00', ?, ?, 2
                );
                """,
                ("a" * 64, "b" * 64),
            )
            connection.executemany(
                "INSERT INTO official_fpl_teams VALUES (?, ?, ?);",
                [(10, 1, "New Club"), (10, 2, "Second Club")],
            )
            connection.executemany(
                """
                INSERT INTO official_fpl_players VALUES (
                    10, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
                );
                """,
                [
                    (
                        101,
                        154561,
                        "Raya",
                        "goalkeeper",
                        1,
                        "d",
                        75,
                        "Current doubt",
                        "2026-07-25T10:00:00+00:00",
                        10,
                        0,
                        1,
                    ),
                    (
                        102,
                        999999,
                        "New",
                        "midfielder",
                        2,
                        "a",
                        None,
                        "",
                        None,
                        0,
                        0,
                        0,
                    ),
                ],
            )
            connection.execute(
                """
                INSERT INTO historical_fpl_season_captures VALUES (
                    20, 'vaastav-fpl-historical/v1', '2025-26',
                    'f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a',
                    ?, ?, ?, 1, 3, 1
                );
                """,
                (archive_available_at, "c" * 64, "d" * 64),
            )
            connection.execute(
                """
                INSERT INTO historical_fpl_players VALUES (
                    20, 154561, 'goalkeeper', 1, 'a', NULL, ?, NULL
                );
                """,
                ("e" * 64,),
            )
            rows = [
                (1, 90, 1, 3, "0.0", "0.0", "0.0", "0.4", 6, 5, 1),
                (2, 45, 1, 0, "0.0", "0.0", "0.0", "0.2", 3, 2, 0),
                (3, 0, 0, 6, "0.0", "0.0", "0.0", "0.0", 0, 0, 0),
            ]
            connection.executemany(
                """
                INSERT INTO historical_fpl_player_gameweeks VALUES (
                    20, 154561, ?, ?, ?, 'Old Club', ?, ?, ?, ?, ?, ?,
                    ?, ?, ?, ?
                );
                """,
                [
                    (
                        gameweek,
                        gameweek,
                        f"2026-05-{gameweek:02d}T15:00:00+00:00",
                        minutes,
                        starts,
                        points,
                        xg,
                        xa,
                        xgi,
                        xgc,
                        defensive,
                        recoveries,
                        tackles,
                    )
                    for (
                        gameweek,
                        minutes,
                        starts,
                        points,
                        xg,
                        xa,
                        xgi,
                        xgc,
                        defensive,
                        recoveries,
                        tackles,
                    ) in rows
                ],
            )
            connection.commit()
            connection.close()
            yield path


if __name__ == "__main__":
    unittest.main()
