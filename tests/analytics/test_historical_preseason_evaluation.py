from __future__ import annotations

import hashlib
import json
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

from autofpl_analytics.historical_preseason_evaluation import (  # noqa: E402
    BASELINE_MODELS,
    CANDIDATE_MODELS,
    FEATURES,
    _build_samples,
    _load_capture,
    evaluate_historical_preseason,
    main,
)
from autofpl_analytics.temporal_ridge import _open_connection  # noqa: E402


class HistoricalPreseasonEvaluationTests(unittest.TestCase):
    def test_locked_holdout_evaluates_only_development_selected_models(
        self,
    ) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_historical_preseason(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=10,
            )
            repeated = evaluate_historical_preseason(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=10,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertEqual(
            "historical-preseason-evaluation-v1",
            report["evaluatorVersion"],
        )
        self.assertFalse(report["isPromoted"])
        self.assertEqual(6, report["development"]["foldCount"])
        self.assertEqual(3, report["lockedHoldout"]["foldCount"])
        self.assertEqual(
            set((*CANDIDATE_MODELS, *BASELINE_MODELS)),
            {
                model["name"]
                for model in report["development"]["models"]
            },
        )
        recommendation = report["preseasonBridgeRecommendation"]
        selected = recommendation["selectedCandidate"]
        comparator = recommendation["selectedBaseline"]
        self.assertIn(selected, CANDIDATE_MODELS)
        self.assertIn(comparator, BASELINE_MODELS)
        self.assertEqual(
            {selected, *BASELINE_MODELS},
            {
                model["name"]
                for model in report["lockedHoldout"]["models"]
            },
        )
        self.assertEqual(
            list(FEATURES),
            report["configuration"]["features"],
        )
        self.assertEqual(
            list(range(10, 13)),
            [
                fold["gameweek"]
                for fold in report["lockedHoldout"]["folds"]
            ],
        )

    def test_future_outcome_does_not_change_earlier_feature_sample(self) -> None:
        with self._database() as database:
            connection = _open_connection(database)
            try:
                capture = _load_capture(connection, "2025-26")
                assert capture is not None
                before = _build_samples(connection, capture)[11]
            finally:
                connection.close()
            with sqlite3.connect(database) as writable:
                writable.execute(
                    """
                    UPDATE historical_fpl_player_gameweeks
                    SET total_points = 99,
                        minutes = 180,
                        expected_goals = '9.9'
                    WHERE gameweek = 12 AND player_code = 1001;
                    """
                )
            connection = _open_connection(database)
            try:
                capture = _load_capture(connection, "2025-26")
                assert capture is not None
                after = _build_samples(connection, capture)[11]
            finally:
                connection.close()

        self.assertEqual(before, after)

    def test_missing_archive_and_cli_overwrite_are_fail_closed(self) -> None:
        with self._database(include_capture=False) as database:
            report = evaluate_historical_preseason(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=10,
            )
        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual(
            "historical-season-archive-not-found",
            report["reason"],
        )
        self.assertFalse(report["lockedHoldout"]["opened"])

        with self._database() as database:
            output = database.parent / "preseason.json"
            arguments = [
                "--database",
                str(database),
                "--minimum-training-gameweeks",
                "3",
                "--holdout-start-gameweek",
                "10",
                "--output",
                str(output),
            ]
            self.assertEqual(0, main(arguments))
            written = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                repeated = main(arguments)
        self.assertEqual("complete", written["status"])
        self.assertEqual(1, repeated)
        self.assertIn("output.already-exists", errors.getvalue())

    @contextmanager
    def _database(self, include_capture: bool = True):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "autofpl.db"
            with sqlite3.connect(path) as connection:
                connection.executescript(
                    """
                    CREATE TABLE schema_migrations (
                        version INTEGER PRIMARY KEY
                    );
                    INSERT INTO schema_migrations VALUES (19);

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
                    CREATE TABLE historical_fpl_players (
                        capture_id INTEGER NOT NULL,
                        player_code INTEGER NOT NULL,
                        position TEXT NOT NULL
                    );
                    CREATE TABLE historical_fpl_player_gameweeks (
                        capture_id INTEGER NOT NULL,
                        player_code INTEGER NOT NULL,
                        gameweek INTEGER NOT NULL,
                        fixture_id INTEGER NOT NULL,
                        kickoff_utc TEXT NOT NULL,
                        was_home INTEGER NOT NULL,
                        minutes INTEGER NOT NULL,
                        starts INTEGER NOT NULL,
                        total_points INTEGER NOT NULL,
                        expected_goals TEXT NOT NULL,
                        expected_assists TEXT NOT NULL,
                        expected_goal_involvements TEXT NOT NULL,
                        expected_goals_conceded TEXT NOT NULL,
                        defensive_contribution INTEGER NOT NULL
                    );
                    """
                )
                if include_capture:
                    player_count = 12
                    gameweek_count = 12
                    connection.execute(
                        """
                        INSERT INTO historical_fpl_season_captures VALUES (
                            1, '2025-26', 'fixture-revision',
                            '2026-07-01T00:00:00+00:00',
                            ?, ?, ?, ?, ?
                        );
                        """,
                        (
                            "a" * 64,
                            "b" * 64,
                            player_count,
                            player_count * gameweek_count,
                            player_count,
                        ),
                    )
                    positions = (
                        "goalkeeper",
                        "defender",
                        "midfielder",
                        "forward",
                    )
                    connection.executemany(
                        "INSERT INTO historical_fpl_players VALUES (1, ?, ?);",
                        [
                            (1000 + index, positions[index % len(positions)])
                            for index in range(1, player_count + 1)
                        ],
                    )
                    rows = []
                    for gameweek in range(1, gameweek_count + 1):
                        for index in range(1, player_count + 1):
                            active = index <= 8 and (
                                gameweek + index
                            ) % 5 != 0
                            minutes = 90 if active else 0
                            starts = int(active)
                            form = (index % 4) + (gameweek % 3)
                            points = form if active else 0
                            rows.append(
                                (
                                    1,
                                    1000 + index,
                                    gameweek,
                                    gameweek * 100 + index,
                                    (
                                        f"2026-01-{gameweek:02d}"
                                        "T15:00:00+00:00"
                                    ),
                                    (gameweek + index) % 2,
                                    minutes,
                                    starts,
                                    points,
                                    str(0.1 * form if active else 0.0),
                                    str(0.05 * form if active else 0.0),
                                    str(0.15 * form if active else 0.0),
                                    str(0.4 if active else 0.0),
                                    form if active else 0,
                                )
                            )
                    connection.executemany(
                        """
                        INSERT INTO historical_fpl_player_gameweeks VALUES (
                            ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
                        );
                        """,
                        rows,
                    )
            yield path


if __name__ == "__main__":
    unittest.main()
