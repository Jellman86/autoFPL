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

from autofpl_analytics.baseline import (
    EvaluationError,
    _empirical_crps,
    _empirical_distribution,
    _empirical_quantile,
    evaluate_database,
    main,
)


class BaselineEvaluationTests(unittest.TestCase):
    def test_empirical_distribution_math_matches_exact_reference(self) -> None:
        distribution = _empirical_distribution([90, 60, 0, 90])

        self.assertEqual(41.25, _empirical_crps(distribution, 120))
        self.assertEqual(45.0, _empirical_quantile(distribution, 0.25))
        self.assertEqual(90.0, _empirical_quantile(distribution, 0.9))

    def test_rolling_origins_exclude_corrections_unavailable_at_deadline(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            database = Path(temporary_directory) / "autofpl.db"
            self._create_database(database)
            self._seed_complete_history(database)

            report = evaluate_database(database, season_code="2026-27")

        self.assertEqual("complete", report["status"])
        self.assertEqual("1.3", report["schemaVersion"])
        self.assertEqual("baseline-evaluation-v4", report["evaluatorVersion"])
        self.assertEqual("exploratory-baseline-not-promoted", report["researchStatus"])
        self.assertEqual(5, report["configuration"]["calibrationBinCount"])
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
        probability_models = {
            model["name"]: model for model in report["probabilityModels"]
        }
        self.assertEqual(
            {
                "global-played60-rate",
                "position-played60-rate",
                "player-played60-rate",
                "official-start-rate",
            },
            set(probability_models),
        )
        player_rate = probability_models["player-played60-rate"]
        self.assertEqual(4, player_rate["metrics"]["count"])
        self.assertEqual(0.425347, player_rate["metrics"]["brierScore"])
        self.assertEqual(1.069167, player_rate["metrics"]["logLoss"])
        self.assertEqual(0.5, player_rate["metrics"]["observedRate"])
        self.assertEqual(
            [
                (0.2, 0.4, 1),
                (0.4, 0.6, 1),
                (0.6, 0.8, 2),
            ],
            [
                (
                    item["lowerBoundInclusive"],
                    item["upperBound"],
                    item["count"],
                )
                for item in player_rate["calibrationBins"]
            ],
        )
        self.assertEqual(
            0.645833,
            player_rate["metrics"]["expectedCalibrationError"],
        )
        self.assertEqual(
            2,
            player_rate["slices"]["position"]["forward"]["count"],
        )
        for model in probability_models.values():
            self.assertGreaterEqual(model["metrics"]["meanProbability"], 0.0)
            self.assertLessEqual(model["metrics"]["meanProbability"], 1.0)
        expected_minutes_models = {
            model["name"]: model
            for model in report["expectedMinutesModels"]
        }
        self.assertEqual(
            {
                "minutes-zero",
                "minutes-global-expanding-mean",
                "minutes-position-expanding-mean",
                "minutes-player-expanding-mean",
                "minutes-player-last",
                "minutes-official-running-mean",
            },
            set(expected_minutes_models),
        )
        player_minutes = expected_minutes_models[
            "minutes-player-expanding-mean"
        ]
        self.assertEqual(4, player_minutes["metrics"]["count"])
        self.assertEqual(75.0, player_minutes["metrics"]["mae"])
        self.assertEqual(75.746287, player_minutes["metrics"]["rmse"])
        self.assertEqual(7.5, player_minutes["metrics"]["meanError"])
        self.assertEqual(
            2,
            player_minutes["slices"]["position"]["goalkeeper"]["count"],
        )
        self.assertEqual(
            75.0,
            expected_minutes_models["minutes-zero"]["metrics"]["rmse"],
        )
        point_distribution_models = {
            model["name"]: model
            for model in report["pointDistributionModels"]
        }
        self.assertEqual(
            {
                "points-zero-degenerate",
                "points-global-empirical",
                "points-position-empirical",
                "points-player-empirical",
            },
            set(point_distribution_models),
        )
        player_points_distribution = point_distribution_models[
            "points-player-empirical"
        ]
        self.assertEqual(
            3.875,
            player_points_distribution["metrics"]["meanCrps"],
        )
        self.assertEqual(
            1.5,
            player_points_distribution["metrics"][
                "meanDistributionSampleCount"
            ],
        )
        self.assertEqual(
            5,
            len(
                player_points_distribution["metrics"][
                    "quantileCalibration"
                ]
            ),
        )
        self.assertEqual(
            3,
            len(
                player_points_distribution["metrics"][
                    "centralIntervals"
                ]
            ),
        )
        minutes_distribution_models = {
            model["name"]: model
            for model in report["minutesDistributionModels"]
        }
        self.assertEqual(
            {
                "minutes-zero-degenerate",
                "minutes-global-empirical",
                "minutes-position-empirical",
                "minutes-player-empirical",
            },
            set(minutes_distribution_models),
        )
        self.assertEqual(
            67.5,
            minutes_distribution_models["minutes-player-empirical"][
                "metrics"
            ]["meanCrps"],
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

    def test_additive_newer_database_schema_is_supported(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            database = Path(temporary_directory) / "autofpl.db"
            self._create_database(database)
            self._seed_complete_history(database)
            with sqlite3.connect(database) as connection:
                connection.executemany(
                    """
                    INSERT INTO schema_migrations
                        (version, name, applied_at_utc)
                    VALUES (?, ?, '2026-07-02T00:00:00.0000000Z');
                    """,
                    [(8, "additive-eight"), (9, "additive-nine")],
                )

            report = evaluate_database(database, season_code="2026-27")

        self.assertEqual("complete", report["status"])

    def test_expected_minutes_scores_double_gameweek_total_without_cap(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            database = Path(temporary_directory) / "autofpl.db"
            self._create_database(database)
            self._seed_complete_history(database)

            report = evaluate_database(database, season_code="2026-27")

        models = {
            model["name"]: model
            for model in report["expectedMinutesModels"]
        }
        self.assertEqual(4, models["minutes-zero"]["metrics"]["count"])
        self.assertEqual(75.0, models["minutes-zero"]["metrics"]["rmse"])
        distribution_models = {
            model["name"]: model
            for model in report["minutesDistributionModels"]
        }
        self.assertEqual(
            52.5,
            distribution_models["minutes-zero-degenerate"]["metrics"][
                "meanCrps"
            ],
        )

    def test_no_pairs_returns_machine_readable_insufficient_data(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            database = Path(temporary_directory) / "autofpl.db"
            self._create_database(database)

            report = evaluate_database(database)

        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual("no-complete-replay-outcome-pairs", report["reason"])
        self.assertEqual(0, report["eligibleFoldCount"])
        self.assertEqual([], report["models"])
        self.assertEqual([], report["probabilityModels"])
        self.assertEqual([], report["expectedMinutesModels"])
        self.assertEqual([], report["pointDistributionModels"])
        self.assertEqual([], report["minutesDistributionModels"])

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
                    (7, 'official-fpl-player-photo', '2026-07-01T00:00:00.0000000Z');

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
                    minutes INTEGER NOT NULL,
                    starts INTEGER NOT NULL,
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
                    minutes INTEGER NOT NULL,
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
        cumulative_starts = {
            1: {1: 0, 2: 0},
            2: {1: 1, 2: 1},
            3: {1: 1, 2: 2},
        }
        cumulative_minutes = {
            1: {1: 0, 2: 0},
            2: {1: 90, 2: 30},
            3: {1: 90, 2: 150},
        }
        outcome_rows = {
            1: (
                1,
                "2026-08-02T18:00:00.0000000Z",
                {1: 4, 2: 2},
                {1: 90, 2: 30},
            ),
            2: (
                1,
                "2026-08-10T18:00:00.0000000Z",
                {1: 10, 2: 2},
                {1: 90, 2: 60},
            ),
            3: (
                2,
                "2026-08-09T18:00:00.0000000Z",
                {1: 6, 2: 0},
                {1: 0, 2: 90},
            ),
            4: (
                3,
                "2026-08-16T18:00:00.0000000Z",
                {1: 2, 2: 8},
                {1: 120, 2: 0},
            ),
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
                        total_points,
                        minutes,
                        starts
                    )
                    VALUES (?, ?, ?, ?, ?, ?);
                    """,
                    [
                        (
                            gameweek,
                            1,
                            "goalkeeper",
                            cumulative_points[gameweek][1],
                            cumulative_minutes[gameweek][1],
                            cumulative_starts[gameweek][1],
                        ),
                        (
                            gameweek,
                            2,
                            "forward",
                            cumulative_points[gameweek][2],
                            cumulative_minutes[gameweek][2],
                            cumulative_starts[gameweek][2],
                        ),
                    ],
                )

            for outcome_id, (
                gameweek,
                available_at,
                points,
                minutes,
            ) in outcome_rows.items():
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
                        total_points,
                        minutes
                    )
                    VALUES (?, ?, ?, ?);
                    """,
                    [
                        (outcome_id, 1, points[1], minutes[1]),
                        (outcome_id, 2, points[2], minutes[2]),
                    ],
                )

    @staticmethod
    def _hash(prefix: str, value: int) -> str:
        return (prefix + str(value)).ljust(64, "0")


if __name__ == "__main__":
    unittest.main()
