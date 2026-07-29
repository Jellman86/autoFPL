from __future__ import annotations

import hashlib
import sqlite3
import sys
import tempfile
import unittest
from contextlib import contextmanager
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.team_goal_strength_evaluation import (  # noqa: E402
    BASELINE_MODEL,
    CHALLENGER_MODEL,
    MAXIMUM_GOALS,
    Rates,
    _score_matrix,
    _tau,
    evaluate_team_goal_strength,
)


class TeamGoalStrengthEvaluationTests(unittest.TestCase):
    def test_expanding_score_evaluation_is_deterministic_and_read_only(
        self,
    ) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_team_goal_strength(
                database,
                evaluation_start_gameweek=5,
                minimum_training_origins=3,
            )
            repeated = evaluate_team_goal_strength(
                database,
                evaluation_start_gameweek=5,
                minimum_training_origins=3,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertFalse(report["isPromoted"])
        self.assertEqual(4, report["eligibleFoldCount"])
        self.assertEqual(
            {BASELINE_MODEL, CHALLENGER_MODEL},
            {model["name"] for model in report["models"]},
        )
        self.assertEqual(
            [5, 6, 7, 8],
            [fold["gameweek"] for fold in report["folds"]],
        )
        self.assertTrue(
            all(
                fold["challengerDiagnostics"]["iterations"] > 1
                for fold in report["folds"]
            )
        )
        indexed = {model["name"]: model for model in report["models"]}
        self.assertNotEqual(
            indexed[BASELINE_MODEL]["metrics"],
            indexed[CHALLENGER_MODEL]["metrics"],
        )
        for model in report["models"]:
            for value in model["metrics"].values():
                self.assertGreaterEqual(value, 0)

    def test_later_result_cannot_change_earlier_fold(self) -> None:
        with self._database() as database:
            before = evaluate_team_goal_strength(
                database,
                evaluation_start_gameweek=7,
                minimum_training_origins=3,
            )["folds"][0]
            with sqlite3.connect(database) as writable:
                writable.execute(
                    """
                    UPDATE historical_fpl_player_gameweeks
                    SET goals_scored = 8
                    WHERE capture_id = 2
                      AND gameweek = 8;
                    """
                )
            after = evaluate_team_goal_strength(
                database,
                evaluation_start_gameweek=7,
                minimum_training_origins=3,
            )["folds"][0]

        self.assertEqual(before, after)

    def test_dixon_coles_matrix_is_normalised_and_low_score_adjusted(
        self,
    ) -> None:
        rates = Rates(1.5, 1.1, -0.08)
        matrix = _score_matrix(rates)

        self.assertEqual(
            (MAXIMUM_GOALS + 1, MAXIMUM_GOALS + 1), matrix.shape
        )
        self.assertAlmostEqual(1.0, float(matrix.sum()), places=12)
        self.assertGreater(_tau(0, 0, 1.5, 1.1, -0.08), 1.0)
        self.assertEqual(1.0, _tau(2, 2, 1.5, 1.1, -0.08))

    @contextmanager
    def _database(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "autofpl.db"
            with sqlite3.connect(path) as connection:
                connection.executescript(
                    """
                    CREATE TABLE schema_migrations (
                        version INTEGER PRIMARY KEY
                    );
                    INSERT INTO schema_migrations VALUES (24);
                    CREATE TABLE historical_fpl_season_captures (
                        capture_id INTEGER PRIMARY KEY,
                        season_code TEXT NOT NULL,
                        source_revision TEXT NOT NULL,
                        available_at_utc TEXT NOT NULL,
                        players_sha256 TEXT NOT NULL,
                        gameweeks_sha256 TEXT NOT NULL,
                        player_count INTEGER NOT NULL,
                        player_gameweek_count INTEGER NOT NULL,
                        stable_code_count INTEGER NOT NULL
                    );
                    CREATE TABLE historical_fpl_player_gameweeks (
                        capture_id INTEGER NOT NULL,
                        player_code INTEGER NOT NULL,
                        gameweek INTEGER NOT NULL,
                        fixture_id INTEGER NOT NULL,
                        kickoff_utc TEXT NOT NULL,
                        team_name TEXT NOT NULL,
                        was_home INTEGER NOT NULL,
                        goals_scored INTEGER NOT NULL
                    );
                    """
                )
                self._insert_season(connection, 1, "2024-25", 2024)
                self._insert_season(connection, 2, "2025-26", 2025)
            yield path

    @staticmethod
    def _insert_season(
        connection: sqlite3.Connection,
        capture_id: int,
        season_code: str,
        year: int,
    ) -> None:
        teams = ("Alpha", "Bravo", "Charlie", "Delta")
        fixtures = (
            (0, 1),
            (2, 3),
        )
        rows = []
        fixture_id = capture_id * 1000
        for gameweek in range(1, 9):
            rotated = tuple(
                (teams[(home + gameweek - 1) % 4],
                 teams[(away + gameweek - 1) % 4])
                for home, away in fixtures
            )
            for home, away in rotated:
                fixture_id += 1
                home_goals = 3 if home == "Alpha" else 2 if home == "Bravo" else 1
                away_goals = 2 if away == "Alpha" else 1 if away == "Bravo" else 0
                kickoff = (
                    f"{year}-08-{gameweek:02d}T15:00:00+00:00"
                )
                rows.extend(
                    (
                        (
                            capture_id,
                            teams.index(home) + 1,
                            gameweek,
                            fixture_id,
                            kickoff,
                            home,
                            1,
                            home_goals,
                        ),
                        (
                            capture_id,
                            teams.index(away) + 1,
                            gameweek,
                            fixture_id,
                            kickoff,
                            away,
                            0,
                            away_goals,
                        ),
                    )
                )
        connection.execute(
            """
            INSERT INTO historical_fpl_season_captures VALUES (
                ?, ?, ?, ?, ?, ?, ?, ?, ?
            );
            """,
            (
                capture_id,
                season_code,
                f"revision-{capture_id}",
                f"{year + 1}-07-01T00:00:00+00:00",
                str(capture_id) * 64,
                str(capture_id + 2) * 64,
                len(teams),
                len(rows),
                len(teams),
            ),
        )
        connection.executemany(
            """
            INSERT INTO historical_fpl_player_gameweeks VALUES (
                ?, ?, ?, ?, ?, ?, ?, ?
            );
            """,
            rows,
        )


if __name__ == "__main__":
    unittest.main()
