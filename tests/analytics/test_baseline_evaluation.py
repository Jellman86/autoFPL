from __future__ import annotations

import hashlib
import io
import json
import sqlite3
import sys
import tempfile
import unittest
from contextlib import redirect_stderr
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.baseline import EvaluationError, evaluate_database, main


class BaselineEvaluationTests(unittest.TestCase):
    def test_rolling_origins_exclude_corrections_unavailable_at_deadline(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            database = Path(temporary_directory) / "autofpl.db"
            self._create_database(database)
            self._seed_complete_history(database)

            report = evaluate_database(database, season_code="2026-27")

        self.assertEqual("complete", report["status"])
        self.assertEqual("exploratory-baseline-not-promoted", report["researchStatus"])
        self.assertEqual(3, report["completePairCount"])
        self.assertEqual(2, report["eligibleFoldCount"])
        self.assertEqual(64, len(report["dataIdentitySha256"]))
        self.assertEqual(64, len(report["runIdentitySha256"]))

        gameweek_two, gameweek_three = report["folds"]
        self.assertEqual(2, gameweek_two["gameweek"])
        self.assertEqual(
            [1],
            [item["outcomeCaptureId"] for item in gameweek_two["training"]],
        )
        self.assertEqual(3, gameweek_three["gameweek"])
        self.assertEqual(
            [2, 3],
            [item["outcomeCaptureId"] for item in gameweek_three["training"]],
        )

        models = {model["name"]: model for model in report["models"]}
        self.assertEqual(
            {
                "zero-points",
                "position-expanding-mean",
                "player-expanding-mean",
                "player-last-points",
                "official-running-mean",
            },
            set(models),
        )
        self.assertEqual(4, models["official-running-mean"]["metrics"]["count"])
        self.assertEqual(4.25, models["official-running-mean"]["metrics"]["mae"])
        self.assertEqual(-0.25, models["official-running-mean"]["metrics"]["meanError"])
        self.assertEqual(
            2,
            models["official-running-mean"]["slices"]["position"]["goalkeeper"][
                "count"
            ],
        )

    def test_report_is_deterministic_and_database_remains_byte_identical(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            database = Path(temporary_directory) / "autofpl.db"
            self._create_database(database)
            self._seed_complete_history(database)
            before = hashlib.sha256(database.read_bytes()).hexdigest()

            first = evaluate_database(database)
            second = evaluate_database(database)
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)

    def test_no_pairs_returns_machine_readable_insufficient_data(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            database = Path(temporary_directory) / "autofpl.db"
            self._create_database(database)

            report = evaluate_database(database)

        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual("no-complete-replay-outcome-pairs", report["reason"])
        self.assertEqual(0, report["eligibleFoldCount"])
        self.assertEqual([], report["models"])

    def test_incomplete_player_pair_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            database = Path(temporary_directory) / "autofpl.db"
            self._create_database(database)
            self._seed_complete_history(database)
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    DELETE FROM official_fpl_player_outcomes
                    WHERE outcome_capture_id = 3 AND player_id = 2;
                    """
                )

            with self.assertRaises(EvaluationError) as raised:
                evaluate_database(database)

        self.assertEqual("data.incomplete-player-coverage", raised.exception.code)

    def test_cli_refuses_to_overwrite_a_report(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            database = directory / "autofpl.db"
            output = directory / "report.json"
            self._create_database(database)
            self._seed_complete_history(database)

            first_exit = main(
                [
                    "--database",
                    str(database),
                    "--season",
                    "2026-27",
                    "--output",
                    str(output),
                ]
            )
            first_report = json.loads(output.read_text(encoding="utf-8"))
            with redirect_stderr(io.StringIO()):
                second_exit = main(
                    [
                        "--database",
                        str(database),
                        "--output",
                        str(output),
                    ]
                )

        self.assertEqual(0, first_exit)
        self.assertEqual("complete", first_report["status"])
        self.assertEqual(1, second_exit)

    @staticmethod
    def _create_database(database: Path) -> None:
        with sqlite3.connect(database) as connection:
            connection.executescript(
                """
                CREATE TABLE schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at_utc TEXT NOT NULL
                );
                INSERT INTO schema_migrations
                    (version, name, applied_at_utc)
                VALUES
                    (5, 'official-fpl-gameweek-outcome', '2026-07-01T00:00:00.0000000Z');

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
                CREATE TABLE official_fpl_players (
                    capture_id INTEGER NOT NULL,
                    player_id INTEGER NOT NULL,
                    position TEXT NOT NULL,
                    total_points INTEGER NOT NULL,
                    PRIMARY KEY (capture_id, player_id)
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
                    total_points INTEGER NOT NULL,
                    PRIMARY KEY (outcome_capture_id, player_id)
                );
                """
            )

    @classmethod
    def _seed_complete_history(cls, database: Path) -> None:
        deadlines = {
            1: "2026-08-01T11:00:00.0000000Z",
            2: "2026-08-08T11:00:00.0000000Z",
            3: "2026-08-15T11:00:00.0000000Z",
        }
        capture_times = {
            1: "2026-07-31T18:00:00.0000000Z",
            2: "2026-08-07T18:00:00.0000000Z",
            3: "2026-08-14T18:00:00.0000000Z",
        }
        cumulative_points = {
            1: {1: 0, 2: 0},
            2: {1: 4, 2: 2},
            3: {1: 16, 2: 2},
        }
        outcome_rows = {
            1: (1, "2026-08-02T18:00:00.0000000Z", {1: 4, 2: 2}),
            2: (1, "2026-08-10T18:00:00.0000000Z", {1: 10, 2: 2}),
            3: (2, "2026-08-09T18:00:00.0000000Z", {1: 6, 2: 0}),
            4: (3, "2026-08-16T18:00:00.0000000Z", {1: 2, 2: 8}),
        }
        with sqlite3.connect(database) as connection:
            for gameweek in (1, 2, 3):
                connection.execute(
                    """
                    INSERT INTO official_fpl_captures (
                        capture_id,
                        season_code,
                        available_at_utc,
                        bootstrap_sha256,
                        fixtures_sha256,
                        player_count
                    )
                    VALUES (?, '2026-27', ?, ?, ?, 2);
                    """,
                    (
                        gameweek,
                        capture_times[gameweek],
                        cls._hash("b", gameweek),
                        cls._hash("f", gameweek),
                    ),
                )
                connection.execute(
                    """
                    INSERT INTO official_fpl_events (
                        capture_id,
                        event_id,
                        deadline_utc
                    )
                    VALUES (?, ?, ?);
                    """,
                    (gameweek, gameweek, deadlines[gameweek]),
                )
                connection.executemany(
                    """
                    INSERT INTO official_fpl_players (
                        capture_id,
                        player_id,
                        position,
                        total_points
                    )
                    VALUES (?, ?, ?, ?);
                    """,
                    [
                        (
                            gameweek,
                            1,
                            "goalkeeper",
                            cumulative_points[gameweek][1],
                        ),
                        (
                            gameweek,
                            2,
                            "forward",
                            cumulative_points[gameweek][2],
                        ),
                    ],
                )

            for outcome_id, (gameweek, available_at, points) in outcome_rows.items():
                connection.execute(
                    """
                    INSERT INTO official_fpl_outcome_captures (
                        outcome_capture_id,
                        season_code,
                        gameweek,
                        available_at_utc,
                        live_sha256,
                        player_count
                    )
                    VALUES (?, '2026-27', ?, ?, ?, 2);
                    """,
                    (
                        outcome_id,
                        gameweek,
                        available_at,
                        cls._hash("a", outcome_id),
                    ),
                )
                connection.executemany(
                    """
                    INSERT INTO official_fpl_player_outcomes (
                        outcome_capture_id,
                        player_id,
                        total_points
                    )
                    VALUES (?, ?, ?);
                    """,
                    [
                        (outcome_id, 1, points[1]),
                        (outcome_id, 2, points[2]),
                    ],
                )

    @staticmethod
    def _hash(prefix: str, value: int) -> str:
        return (prefix + str(value)).ljust(64, "0")


if __name__ == "__main__":
    unittest.main()
