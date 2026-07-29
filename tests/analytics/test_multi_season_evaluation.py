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
    _load_capture,
)
from autofpl_analytics.multi_season_evaluation import (  # noqa: E402
    FEATURES,
    MODEL_NAMES,
    Origin,
    _build_feature_table,
    _open_connection,
    evaluate_multi_season,
    main,
)


class MultiSeasonEvaluationTests(unittest.TestCase):
    def test_identical_folds_compare_fixed_models_read_only(self) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_multi_season(
                database,
                evaluation_start_gameweek=2,
                minimum_training_origins=3,
            )
            repeated = evaluate_multi_season(
                database,
                evaluation_start_gameweek=2,
                minimum_training_origins=3,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertEqual(
            "multi-season-expanding-origin-v1",
            report["evaluatorVersion"],
        )
        self.assertFalse(report["isPromoted"])
        self.assertEqual(4, report["eligibleFoldCount"])
        self.assertEqual(
            [2, 3, 4, 5],
            [fold["gameweek"] for fold in report["folds"]],
        )
        self.assertEqual(
            set(MODEL_NAMES),
            {model["name"] for model in report["models"]},
        )
        self.assertEqual(
            list(FEATURES),
            report["configuration"]["features"],
        )
        first = report["folds"][0]
        self.assertEqual(
            {"2024-25": 30, "2025-26": 6},
            first["trainingRowsBySeason"],
        )
        self.assertEqual(4, first["targetPriorSeasonIdentityCount"])
        self.assertEqual(6, first["targetPlayerCount"])
        self.assertEqual(
            "retrospective-screen-only",
            report["comparison"]["decision"],
        )

    def test_exact_identity_and_legacy_missingness_are_explicit(self) -> None:
        with self._database() as database:
            connection = _open_connection(database)
            try:
                captures = [
                    _load_capture(connection, season)
                    for season in ("2024-25", "2025-26")
                ]
                table = _build_feature_table(
                    connection,
                    [capture for capture in captures if capture is not None],
                )
            finally:
                connection.close()

        first_current = table[Origin(1, 1, "2025-26")]
        known = next(sample for sample in first_current if sample.player_id == 1001)
        new = next(sample for sample in first_current if sample.player_id == 2001)
        self.assertEqual(1.0, known.features["priorSeasonIdentity"])
        self.assertEqual(1.0, known.features["priorPositionChanged"])
        self.assertEqual(5.0, known.features["priorSeasonGameweekCount"])
        self.assertIsNone(
            known.features["priorDefensiveContributionMean"]
        )
        self.assertEqual(
            0.0,
            known.features["priorDefensiveContributionObservedCount"],
        )
        self.assertEqual(0.0, new.features["priorSeasonIdentity"])
        self.assertIsNone(new.features["priorPositionChanged"])
        self.assertEqual(0.0, new.features["priorSeasonGameweekCount"])
        self.assertEqual(tuple(FEATURES), tuple(known.features))

    def test_future_outcome_does_not_change_earlier_feature_row(self) -> None:
        with self._database() as database:
            before = self._sample(database, 4, 1001)
            with sqlite3.connect(database) as writable:
                writable.execute(
                    """
                    UPDATE historical_fpl_player_gameweeks
                    SET total_points = 99,
                        minutes = 180,
                        defensive_contribution = 99
                    WHERE capture_id = 2
                      AND gameweek = 5
                      AND player_code = 1001;
                    """
                )
            after = self._sample(database, 4, 1001)

        self.assertEqual(before, after)

    def test_missing_archive_and_cli_overwrite_fail_closed(self) -> None:
        with self._database(include_first=False) as database:
            report = evaluate_multi_season(
                database,
                evaluation_start_gameweek=2,
                minimum_training_origins=3,
            )
        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual(
            "historical-season-archive-not-found",
            report["reason"],
        )
        self.assertEqual(["2024-25"], report["missingSeasonCodes"])

        with self._database() as database:
            output = database.parent / "multi-season.json"
            arguments = [
                "--database",
                str(database),
                "--evaluation-start-gameweek",
                "2",
                "--minimum-training-origins",
                "3",
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

    @staticmethod
    def _sample(database: Path, gameweek: int, player_code: int):
        connection = _open_connection(database)
        try:
            captures = [
                _load_capture(connection, season)
                for season in ("2024-25", "2025-26")
            ]
            table = _build_feature_table(
                connection,
                [capture for capture in captures if capture is not None],
            )
            return next(
                sample
                for sample in table[Origin(1, gameweek, "2025-26")]
                if sample.player_id == player_code
            )
        finally:
            connection.close()

    @contextmanager
    def _database(self, include_first: bool = True):
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
                        team_name TEXT NOT NULL,
                        opponent_team_id INTEGER NOT NULL,
                        was_home INTEGER NOT NULL,
                        minutes INTEGER NOT NULL,
                        starts INTEGER NOT NULL,
                        total_points INTEGER NOT NULL,
                        expected_goals TEXT NOT NULL,
                        expected_assists TEXT NOT NULL,
                        expected_goal_involvements TEXT NOT NULL,
                        expected_goals_conceded TEXT NOT NULL,
                        defensive_contribution INTEGER
                    );
                    """
                )
                if include_first:
                    self._insert_season(
                        connection,
                        capture_id=1,
                        season_code="2024-25",
                        player_codes=range(1001, 1007),
                        kickoff_year=2024,
                        legacy_defensive=True,
                    )
                self._insert_season(
                    connection,
                    capture_id=2,
                    season_code="2025-26",
                    player_codes=(1001, 1002, 1003, 1004, 2001, 2002),
                    kickoff_year=2025,
                    legacy_defensive=False,
                )
            yield path

    @staticmethod
    def _insert_season(
        connection: sqlite3.Connection,
        capture_id: int,
        season_code: str,
        player_codes,
        kickoff_year: int,
        legacy_defensive: bool,
    ) -> None:
        codes = tuple(player_codes)
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
                f"{kickoff_year + 1}-07-01T00:00:00+00:00",
                f"{capture_id}" * 64,
                f"{capture_id + 2}" * 64,
                len(codes),
                len(codes) * 5,
                len(codes),
            ),
        )
        positions = (
            "goalkeeper",
            "defender",
            "midfielder",
            "forward",
        )
        player_rows = []
        for index, player_code in enumerate(codes):
            position = positions[index % len(positions)]
            if capture_id == 2 and player_code == 1001:
                position = "midfielder"
            player_rows.append((capture_id, player_code, position))
        connection.executemany(
            "INSERT INTO historical_fpl_players VALUES (?, ?, ?);",
            player_rows,
        )
        rows = []
        for gameweek in range(1, 6):
            for index, player_code in enumerate(codes):
                active = (gameweek + index) % 4 != 0
                minutes = 90 if active else 0
                points = (index % 4) + gameweek if active else 0
                rows.append(
                    (
                        capture_id,
                        player_code,
                        gameweek,
                        capture_id * 1000 + gameweek * 10 + index,
                        (
                        f"{kickoff_year}-08-{gameweek:02d}"
                            "T15:00:00+00:00"
                        ),
                        f"Team {index % 3}",
                        ((index + gameweek) % 3) + 1,
                        (gameweek + index) % 2,
                        minutes,
                        int(active),
                        points,
                        str(points / 20),
                        str(points / 30),
                        str(points / 12),
                        str(points / 8),
                        None if legacy_defensive else points,
                    )
                )
        connection.executemany(
            """
            INSERT INTO historical_fpl_player_gameweeks VALUES (
                ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
            );
            """,
            rows,
        )


if __name__ == "__main__":
    unittest.main()
