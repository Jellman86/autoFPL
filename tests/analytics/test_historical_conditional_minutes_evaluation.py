from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.historical_conditional_minutes_evaluation import (  # noqa: E402
    BASELINE_MODEL,
    CANDIDATE_MODEL,
    UNCONDITIONAL_MODEL,
    _expected_minutes,
    evaluate_historical_conditional_minutes,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics import (  # noqa: E402
    test_historical_preseason_evaluation as historical_helpers,
)


class HistoricalConditionalMinutesEvaluationTests(unittest.TestCase):
    def test_expected_minutes_factorization_is_bounded_and_fail_closed(
        self,
    ) -> None:
        self.assertEqual(0.0, _expected_minutes(0.0, 70.0, 1.0))
        self.assertEqual(42.0, _expected_minutes(0.6, 70.0, 1.0))
        self.assertEqual(90.0, _expected_minutes(1.0, 120.0, 1.0))
        self.assertEqual(120.0, _expected_minutes(1.0, 120.0, 2.0))
        for appearance, conditional, fixtures in (
            (-0.1, 60.0, 1.0),
            (1.1, 60.0, 1.0),
            (0.5, float("nan"), 1.0),
            (0.5, 60.0, 0.0),
            (0.5, 60.0, 1.5),
        ):
            with self.subTest(
                appearance=appearance,
                conditional=conditional,
                fixtures=fixtures,
            ):
                with self.assertRaises(TemporalRidgeError):
                    _expected_minutes(appearance, conditional, fixtures)

    def test_evaluation_is_deterministic_temporal_and_read_only(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            self._introduce_non_appearances(database)
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_historical_conditional_minutes(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            repeated = evaluate_historical_conditional_minutes(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertFalse(report["isPromoted"])
        self.assertFalse(report["productImportReady"])
        self.assertEqual(4, report["eligibleFoldCount"])
        self.assertEqual(
            [9, 10, 11, 12],
            [fold["gameweek"] for fold in report["folds"]],
        )
        self.assertEqual(
            {CANDIDATE_MODEL, BASELINE_MODEL, UNCONDITIONAL_MODEL},
            {model["name"] for model in report["models"]},
        )
        for fold in report["folds"]:
            self.assertLess(
                fold["conditionalTrainingRowCount"],
                fold["trainingRowCount"],
            )
            self.assertTrue(
                all(
                    gameweek < fold["gameweek"]
                    for gameweek in fold["trainingGameweeks"]
                )
            )

    def test_future_minutes_cannot_change_an_earlier_fold(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            self._introduce_non_appearances(database)
            before = evaluate_historical_conditional_minutes(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            connection = sqlite3.connect(database)
            try:
                connection.execute(
                    """
                    UPDATE historical_fpl_player_gameweeks
                    SET minutes = minutes + 1
                    WHERE gameweek = 12;
                    """
                )
                connection.commit()
            finally:
                connection.close()
            after = evaluate_historical_conditional_minutes(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )

        self.assertEqual(before["folds"][0], after["folds"][0])

    def test_missing_archive_and_cli_overwrite_fail_closed(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database(include_capture=False) as database:
            report = evaluate_historical_conditional_minutes(database)
        self.assertEqual("insufficient-data", report["status"])

        with helper._database() as database:
            self._introduce_non_appearances(database)
            output = database.parent / "conditional-minutes.json"
            arguments = [
                "--database",
                str(database),
                "--minimum-training-gameweeks",
                "3",
                "--holdout-start-gameweek",
                "9",
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
    def _introduce_non_appearances(database: Path) -> None:
        connection = sqlite3.connect(database)
        try:
            connection.execute(
                """
                UPDATE historical_fpl_player_gameweeks
                SET minutes = 0, starts = 0
                WHERE gameweek % 4 = 0
                  AND player_code % 3 = 0;
                """
            )
            connection.commit()
        finally:
            connection.close()


if __name__ == "__main__":
    unittest.main()
