from __future__ import annotations

import hashlib
import json
import math
import sys
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.historical_participation_coherence_evaluation import (  # noqa: E402
    _project_probabilities,
    evaluate_historical_participation_coherence,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics import (  # noqa: E402
    test_historical_preseason_evaluation as historical_helpers,
)


class HistoricalParticipationCoherenceEvaluationTests(unittest.TestCase):
    def test_projection_is_exact_and_leaves_coherent_values_unchanged(
        self,
    ) -> None:
        self.assertEqual(
            {
                "appearance": 0.8,
                "start": 0.6,
                "played-60": 0.4,
            },
            _project_probabilities(0.8, 0.6, 0.4),
        )
        projected_one = _project_probabilities(0.4, 0.8, 0.2)
        self.assertAlmostEqual(0.6, projected_one["appearance"])
        self.assertAlmostEqual(0.6, projected_one["start"])
        self.assertEqual(0.2, projected_one["played-60"])

        projected_both = _project_probabilities(0.4, 0.8, 0.7)
        for value in projected_both.values():
            self.assertAlmostEqual(1.9 / 3.0, value)

        projected_pool = _project_probabilities(0.5, 0.8, 0.55)
        self.assertEqual(0.65, projected_pool["appearance"])
        self.assertEqual(0.65, projected_pool["start"])
        self.assertEqual(0.55, projected_pool["played-60"])

    def test_projection_rejects_non_probabilities(self) -> None:
        for invalid in (-0.01, 1.01, math.nan, math.inf, -math.inf):
            with self.subTest(invalid=invalid):
                with self.assertRaises(TemporalRidgeError):
                    _project_probabilities(invalid, 0.5, 0.5)

    def test_evaluation_is_deterministic_temporal_and_read_only(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_historical_participation_coherence(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            repeated = evaluate_historical_participation_coherence(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertEqual(
            "secondary-holdout-diagnostic-not-a-new-promotion-test",
            report["researchStatus"],
        )
        self.assertFalse(report["isPromoted"])
        self.assertFalse(report["canReplaceCurrentRawProbabilities"])
        self.assertFalse(report["productImportReady"])
        holdout = report["lockedHoldout"]
        self.assertEqual(4, holdout["foldCount"])
        self.assertEqual(
            [9, 10, 11, 12],
            [fold["gameweek"] for fold in holdout["folds"]],
        )
        self.assertEqual(0, holdout["projectedViolationCount"])
        self.assertEqual(
            {"appearance", "start", "played-60"},
            {target["target"] for target in report["targets"]},
        )
        for fold in holdout["folds"]:
            self.assertTrue(
                all(
                    gameweek < fold["gameweek"]
                    for gameweek in fold["trainingGameweeks"]
                )
            )
            self.assertEqual(0, fold["projectedViolationCount"])
        for target in report["targets"]:
            self.assertFalse(target["recommendation"]["isPromoted"])
            self.assertGreaterEqual(target["raw"]["metrics"]["brierScore"], 0)
            self.assertGreaterEqual(
                target["projected"]["metrics"]["brierScore"],
                0,
            )

    def test_missing_archive_and_cli_overwrite_fail_closed(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database(include_capture=False) as database:
            report = evaluate_historical_participation_coherence(database)
        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual(
            "historical-season-archive-not-found",
            report["reason"],
        )

        with helper._database() as database:
            output = database.parent / "coherence.json"
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
