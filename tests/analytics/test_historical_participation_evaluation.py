from __future__ import annotations

import hashlib
import json
import sys
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.historical_participation_evaluation import (  # noqa: E402
    BINARY_TARGETS,
    MINUTES_TARGET,
    evaluate_historical_participation,
    main,
)
from autofpl_analytics.historical_preseason_evaluation import (  # noqa: E402
    _build_samples,
    _load_capture,
)
from autofpl_analytics.temporal_ridge import _open_connection  # noqa: E402
from tests.analytics import (  # noqa: E402
    test_historical_preseason_evaluation as historical_helpers,
)


class HistoricalParticipationEvaluationTests(unittest.TestCase):
    def test_fixed_targets_are_deterministic_temporal_and_read_only(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_historical_participation(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            repeated = evaluate_historical_participation(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertFalse(report["isPromoted"])
        self.assertEqual(
            {*BINARY_TARGETS, MINUTES_TARGET},
            {task["target"] for task in report["tasks"]},
        )
        for task in report["tasks"]:
            self.assertEqual("complete", task["status"])
            self.assertGreater(task["development"]["foldCount"], 0)
            self.assertEqual(4, task["lockedHoldout"]["foldCount"])
            self.assertEqual(
                [9, 10, 11, 12],
                [
                    fold["gameweek"]
                    for fold in task["lockedHoldout"]["folds"]
                ],
            )
            self.assertNotIn(
                task["candidate"],
                {
                    model["name"]
                    for model in task["development"]["models"]
                },
            )
            self.assertTrue(
                all(
                    not fold["diagnostics"]["candidateFit"]
                    for fold in task["development"]["folds"]
                )
            )
            for fold in task["lockedHoldout"]["folds"]:
                self.assertTrue(
                    all(
                        gameweek < fold["gameweek"]
                        for gameweek in fold["trainingGameweeks"]
                    )
                )
            self.assertFalse(task["recommendation"]["isPromoted"])
            metric = (
                "brierScore"
                if task["target"] in BINARY_TARGETS
                else "mae"
            )
            self.assertTrue(
                all(
                    model["metrics"][metric] >= 0
                    for model in task["lockedHoldout"]["models"]
                )
            )

    def test_historical_targets_match_appearance_start_and_minutes_rules(
        self,
    ) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            connection = _open_connection(database)
            try:
                capture = _load_capture(connection, "2025-26")
                self.assertIsNotNone(capture)
                appearance = _build_samples(
                    connection,
                    capture,
                    target_name="appearance",
                )
                starts = _build_samples(
                    connection,
                    capture,
                    target_name="start",
                )
                played_60 = _build_samples(
                    connection,
                    capture,
                    target_name="played-60",
                )
                minutes = _build_samples(
                    connection,
                    capture,
                    target_name="minutes",
                )
            finally:
                connection.close()

        for gameweek in appearance:
            for index, sample in enumerate(appearance[gameweek]):
                self.assertEqual(
                    sample.actual,
                    starts[gameweek][index].actual,
                )
                self.assertEqual(
                    sample.actual,
                    played_60[gameweek][index].actual,
                )
                self.assertEqual(
                    90 * sample.actual,
                    minutes[gameweek][index].actual,
                )

    def test_missing_archive_and_cli_overwrite_fail_closed(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database(include_capture=False) as database:
            report = evaluate_historical_participation(database)
        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual(
            "historical-season-archive-not-found",
            report["reason"],
        )

        with helper._database() as database:
            output = database.parent / "participation.json"
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


if __name__ == "__main__":
    unittest.main()
