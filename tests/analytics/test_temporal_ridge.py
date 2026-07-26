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

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
    _fit_ridge,
    evaluate_temporal_ridge,
    main,
)
from tests.analytics import test_feature_table as feature_test_helpers  # noqa: E402


class TemporalRidgeTests(unittest.TestCase):
    def test_ridge_solver_matches_small_exact_reference(self) -> None:
        intercept, coefficients = _fit_ridge(
            [[-1.0], [1.0]],
            [1.0, 3.0],
            penalty=2.0,
        )

        self.assertEqual(2.0, intercept)
        self.assertEqual([0.5], coefficients)

    def test_expanding_origin_uses_cutoff_safe_features_and_labels(self) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_temporal_ridge(
                database,
                season_code="2026-27",
            )
            repeated = evaluate_temporal_ridge(
                database,
                season_code="2026-27",
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertEqual("temporal-ridge-v1", report["evaluatorVersion"])
        self.assertEqual(
            "exploratory-challenger-not-promoted",
            report["researchStatus"],
        )
        self.assertFalse(report["isPromoted"])
        self.assertEqual(1, report["eligibleFoldCount"])
        self.assertEqual(64, len(report["dataIdentitySha256"]))
        self.assertEqual(64, len(report["runIdentitySha256"]))

        fold = report["folds"][0]
        self.assertEqual(4, fold["gameweek"])
        self.assertEqual(3, fold["trainingGameweeks"])
        self.assertEqual(3, fold["trainingRows"])
        self.assertEqual(
            [11, 20, 30],
            [item["outcomeCaptureId"] for item in fold["training"]],
        )
        self.assertEqual(
            "2026-09-11T12:00:00+00:00",
            fold["decisionCutoffUtc"],
        )
        self.assertEqual(
            3,
            fold["diagnostics"]["imputation"]["chanceNextRound"],
        )
        self.assertEqual(82, fold["diagnostics"]["modelFeatureCount"])
        self.assertEqual(
            {
                "temporal-ridge",
                "zero-points",
                "position-expanding-mean",
                "player-last-points",
                "official-running-mean",
            },
            {model["name"] for model in report["models"]},
        )
        for model in report["models"]:
            self.assertEqual(2, model["metrics"]["count"])

    def test_insufficient_history_is_machine_readable(self) -> None:
        with self._database() as database:
            report = evaluate_temporal_ridge(
                database,
                season_code="2026-27",
                minimum_training_gameweeks=4,
            )

        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual(
            "no-eligible-expanding-origin-folds",
            report["reason"],
        )
        self.assertEqual([], report["models"])

    def test_cli_refuses_overwrite(self) -> None:
        with self._database() as database:
            output = database.parent / "ridge.json"
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
            written = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                second_exit = main(
                    [
                        "--database",
                        str(database),
                        "--season",
                        "2026-27",
                        "--output",
                        str(output),
                    ]
                )

        self.assertEqual(0, first_exit)
        self.assertEqual("complete", written["status"])
        self.assertEqual(1, second_exit)
        self.assertEqual(
            "output.already-exists",
            json.loads(errors.getvalue())["errorCode"],
        )

    def test_rejects_non_positive_regularisation(self) -> None:
        with self._database() as database:
            with self.assertRaises(TemporalRidgeError) as raised:
                evaluate_temporal_ridge(database, ridge_penalty=0)

        self.assertEqual("configuration.ridge-penalty", raised.exception.code)

    class _database:
        def __init__(self) -> None:
            self._temporary_directory: tempfile.TemporaryDirectory[str] | None = (
                None
            )

        def __enter__(self) -> Path:
            self._temporary_directory = tempfile.TemporaryDirectory(
                prefix="autofpl-ridge-tests-"
            )
            path = Path(self._temporary_directory.name) / "autofpl.db"
            feature_test_helpers.FeatureTableTests._create_database(path)
            TemporalRidgeTests._extend_database(path)
            return path

        def __exit__(self, *_: object) -> None:
            assert self._temporary_directory is not None
            self._temporary_directory.cleanup()

    @staticmethod
    def _extend_database(path: Path) -> None:
        with sqlite3.connect(path) as connection:
            connection.executemany(
                "INSERT INTO schema_migrations (version) VALUES (?);",
                [(8,), (9,)],
            )
            connection.execute(
                """
                DELETE FROM official_fpl_players
                WHERE capture_id = 300 AND player_id = 2;
                """
            )
            connection.execute(
                """
                UPDATE official_fpl_captures
                SET player_count = 1
                WHERE capture_id = 300;
                """
            )
            for capture_id, gameweek, available_at, deadline in (
                (
                    100,
                    1,
                    "2026-08-21T12:00:00+00:00",
                    "2026-08-22T12:00:00+00:00",
                ),
                (
                    200,
                    2,
                    "2026-08-28T12:00:00+00:00",
                    "2026-08-29T12:00:00+00:00",
                ),
            ):
                connection.execute(
                    """
                    INSERT INTO official_fpl_captures (
                        capture_id,
                        season_code,
                        available_at_utc,
                        bootstrap_sha256,
                        fixtures_sha256,
                        player_count
                    ) VALUES (?, '2026-27', ?, ?, ?, 1);
                    """,
                    (
                        capture_id,
                        available_at,
                        TemporalRidgeTests._digest("bootstrap", capture_id),
                        TemporalRidgeTests._digest("fixtures", capture_id),
                    ),
                )
                connection.execute(
                    """
                    INSERT INTO official_fpl_events (
                        capture_id,
                        event_id,
                        deadline_utc
                    ) VALUES (?, ?, ?);
                    """,
                    (capture_id, gameweek, deadline),
                )
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
                connection.execute(
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
                    ) VALUES (?, 1, 101, 'Ada', 'Example', 2, 'midfielder',
                              75, 'a', NULL, '10.5', ?, ?, ?);
                    """,
                    (
                        capture_id,
                        0 if gameweek == 1 else 2,
                        0 if gameweek == 1 else 90,
                        0 if gameweek == 1 else 1,
                    ),
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
                ) VALUES (?, ?, ?, 1, 2, ?, ?, ?, ?);
                """,
                [
                    (
                        100,
                        101,
                        1,
                        "2026-08-22T15:00:00+00:00",
                        0,
                        None,
                        None,
                    ),
                    (
                        200,
                        201,
                        1,
                        "2026-08-22T15:00:00+00:00",
                        1,
                        1,
                        2,
                    ),
                    (
                        200,
                        202,
                        2,
                        "2026-08-29T15:00:00+00:00",
                        0,
                        None,
                        None,
                    ),
                ],
            )
            connection.execute(
                """
                INSERT INTO official_fpl_outcome_captures (
                    outcome_capture_id,
                    season_code,
                    gameweek,
                    available_at_utc,
                    live_sha256,
                    player_count
                ) VALUES (
                    40,
                    '2026-27',
                    4,
                    '2026-09-18T12:00:00+00:00',
                    ?,
                    2
                );
                """,
                (TemporalRidgeTests._digest("live", 40),),
            )
            connection.executemany(
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
                    red_cards
                ) VALUES (40, ?, ?, ?, ?, 0, 0, 0, 0, 0, 0, 0, 0);
                """,
                [(1, 90, 1, 5), (2, 0, 0, 0)],
            )

    @staticmethod
    def _digest(prefix: str, value: int) -> str:
        return hashlib.sha256(f"{prefix}-{value}".encode()).hexdigest()


if __name__ == "__main__":
    unittest.main()
