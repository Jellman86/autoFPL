from __future__ import annotations

import hashlib
import sqlite3
import sys
import unittest
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.historical_minutes_distribution_evaluation import (  # noqa: E402
    CANDIDATE_MODEL,
    REFERENCE_MODELS,
    _crps,
    _hurdle_distribution,
    _weighted_distribution,
    evaluate_historical_minutes_distributions,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics import (  # noqa: E402
    test_historical_preseason_evaluation as historical_helpers,
)


class HistoricalMinutesDistributionEvaluationTests(unittest.TestCase):
    def test_weighted_crps_matches_exact_small_references(self) -> None:
        point_mass = _weighted_distribution((0.0,), (1.0,))
        self.assertEqual(10.0, _crps(point_mass, 10.0))
        symmetric = _weighted_distribution((0.0, 10.0), (0.5, 0.5))
        self.assertEqual(2.5, _crps(symmetric, 5.0))
        hurdle = _hurdle_distribution(0.5, (60, 90))
        self.assertAlmostEqual(1.0, sum(hurdle.weights))
        self.assertEqual((0.0, 60.0, 90.0), hurdle.values)
        with self.assertRaises(TemporalRidgeError):
            _weighted_distribution((0.0, 1.0), (0.2, 0.2))
        with self.assertRaises(TemporalRidgeError):
            _hurdle_distribution(1.1, (60,))

    def test_evaluation_is_deterministic_temporal_and_read_only(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            self._introduce_non_appearances(database)
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_historical_minutes_distributions(
                database,
                minimum_training_gameweeks=3,
                evaluation_start_gameweek=9,
            )
            repeated = evaluate_historical_minutes_distributions(
                database,
                minimum_training_gameweeks=3,
                evaluation_start_gameweek=9,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertFalse(report["isPromoted"])
        self.assertEqual(4, report["eligibleFoldCount"])
        self.assertEqual(
            [9, 10, 11, 12],
            [fold["gameweek"] for fold in report["folds"]],
        )
        self.assertEqual(
            {CANDIDATE_MODEL, *REFERENCE_MODELS},
            {model["name"] for model in report["models"]},
        )
        for model in report["models"]:
            self.assertGreaterEqual(model["metrics"]["meanCrps"], 0)
            self.assertGreaterEqual(
                model["metrics"]["central80Coverage"], 0
            )
            self.assertLessEqual(
                model["metrics"]["central80Coverage"], 1
            )

    def test_future_minutes_cannot_change_an_earlier_fold(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            self._introduce_non_appearances(database)
            before = evaluate_historical_minutes_distributions(
                database,
                minimum_training_gameweeks=3,
                evaluation_start_gameweek=9,
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
            after = evaluate_historical_minutes_distributions(
                database,
                minimum_training_gameweeks=3,
                evaluation_start_gameweek=9,
            )

        self.assertEqual(before["folds"][0], after["folds"][0])

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
