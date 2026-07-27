from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import unittest
from contextlib import contextmanager, redirect_stderr
from io import StringIO
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.cross_season_ablation import (  # noqa: E402
    CANDIDATE_FEATURES,
    CROSS_FEATURES,
    CROSS_RIDGE,
    MODEL_NAMES,
    _augment_samples,
    evaluate_cross_season_ablation,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    CONTINUOUS_FEATURES,
    _build_table,
    _open_connection,
    _samples_for_table,
)
from tests.analytics import test_temporal_ridge as ridge_helpers  # noqa: E402


class CrossSeasonAblationTests(unittest.TestCase):
    def test_identical_folds_compare_cross_season_candidates(self) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_cross_season_ablation(
                database,
                season_code="2026-27",
            )
            repeated = evaluate_cross_season_ablation(
                database,
                season_code="2026-27",
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertEqual(
            "cross-season-feature-ablation-v1",
            report["evaluatorVersion"],
        )
        self.assertFalse(report["isPromoted"])
        self.assertEqual(1, report["eligibleFoldCount"])
        self.assertEqual([], report["excludedFolds"])
        self.assertEqual(
            set(MODEL_NAMES),
            {model["name"] for model in report["models"]},
        )
        self.assertEqual(
            list(CROSS_FEATURES),
            report["configuration"]["crossSeasonCandidateFeatures"],
        )
        self.assertEqual(
            len(CONTINUOUS_FEATURES) + len(CROSS_FEATURES),
            len(CANDIDATE_FEATURES),
        )
        fold = report["folds"][0]
        self.assertEqual(4, fold["gameweek"])
        self.assertEqual(3, fold["trainingGameweeks"])
        self.assertEqual(3, fold["trainingRows"])
        self.assertEqual(
            82 + (2 * len(CROSS_FEATURES)),
            fold["diagnostics"][CROSS_RIDGE]["modelFeatureCount"],
        )
        for model in fold["models"]:
            self.assertEqual(2, model["metrics"]["count"])

    def test_features_preserve_prior_values_and_missing_identity(self) -> None:
        with self._database() as database:
            table = _build_table(database, "2026-27", 4)
            connection = _open_connection(database)
            try:
                samples = _samples_for_table(connection, table, 40)
                augmented = _augment_samples(connection, samples, table)
            finally:
                connection.close()

        assert augmented is not None
        known = next(sample for sample in augmented if sample.player_id == 1)
        missing = next(sample for sample in augmented if sample.player_id == 2)
        self.assertEqual(1.0, known.features["priorHasIdentity"])
        self.assertEqual(
            45.0,
            known.features["priorSeasonMinutesMean"],
        )
        self.assertEqual(
            1.0,
            known.features["priorTrailingZeroMinuteGameweeks"],
        )
        self.assertEqual(0.0, missing.features["priorHasIdentity"])
        self.assertIsNone(missing.features["priorSeasonMinutesMean"])
        self.assertEqual(CANDIDATE_FEATURES, tuple(known.features))

    def test_source_incomplete_fold_is_excluded_and_cli_refuses_overwrite(
        self,
    ) -> None:
        with self._database(
            archive_available_at="2026-10-01T00:00:00+00:00"
        ) as database:
            report = evaluate_cross_season_ablation(
                database,
                season_code="2026-27",
            )
        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual(
            "no-cross-season-source-complete-folds",
            report["reason"],
        )
        self.assertEqual(1, len(report["excludedFolds"]))

        with self._database() as database:
            output = database.parent / "cross-season-ablation.json"
            self.assertEqual(
                0,
                main(
                    [
                        "--database",
                        str(database),
                        "--season",
                        "2026-27",
                        "--output",
                        str(output),
                    ]
                ),
            )
            written = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                second = main(
                    [
                        "--database",
                        str(database),
                        "--season",
                        "2026-27",
                        "--output",
                        str(output),
                    ]
                )
        self.assertEqual("complete", written["status"])
        self.assertEqual(1, second)
        error_lines = [
            line
            for line in errors.getvalue().splitlines()
            if line.strip()
        ]
        self.assertEqual(
            "output.already-exists",
            json.loads(error_lines[-1])["errorCode"],
        )

    @contextmanager
    def _database(
        self,
        archive_available_at: str = "2026-07-20T00:00:00+00:00",
    ):
        with ridge_helpers.TemporalRidgeTests._database() as path:
            self._add_archive(path, archive_available_at)
            yield path

    @staticmethod
    def _add_archive(path: Path, available_at: str) -> None:
        with sqlite3.connect(path) as connection:
            connection.executemany(
                "INSERT OR IGNORE INTO schema_migrations (version) VALUES (?);",
                [(version,) for version in range(10, 20)],
            )
            connection.executescript(
                """
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
                INSERT INTO historical_fpl_season_captures VALUES (
                    90, 'vaastav-fpl-historical/v1', '2025-26',
                    'f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a',
                    ?, ?, ?, 1, 3, 1
                );
                """,
                (available_at, "a" * 64, "b" * 64),
            )
            connection.execute(
                """
                INSERT INTO historical_fpl_players VALUES (
                    90, 101, 'midfielder', 1, 'a', NULL, ?, NULL
                );
                """,
                ("c" * 64,),
            )
            connection.executemany(
                """
                INSERT INTO historical_fpl_player_gameweeks VALUES (
                    90, 101, ?, ?, ?, 'Prior', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
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
                        "0.1",
                        "0.2",
                        "0.3",
                        "0.4",
                        defensive,
                        2,
                        1,
                    )
                    for gameweek, minutes, starts, points, defensive in (
                        (1, 90, 1, 3, 4),
                        (2, 45, 1, 0, 2),
                        (3, 0, 0, 6, 0),
                    )
                ],
            )


if __name__ == "__main__":
    unittest.main()
