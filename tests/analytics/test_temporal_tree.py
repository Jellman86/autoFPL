from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.temporal_ridge import (  # noqa: E402
    CONTINUOUS_FEATURES,
    Sample,
)
from autofpl_analytics.temporal_tree import (  # noqa: E402
    MODEL_NAME,
    _predict_tree,
    evaluate_temporal_tree,
)
from tests.analytics import test_temporal_ridge as ridge_test_helpers  # noqa: E402


class TemporalTreeTests(unittest.TestCase):
    def test_tree_uses_the_identical_expanding_origin_fold(self) -> None:
        with ridge_test_helpers.TemporalRidgeTests._database() as database:
            first = evaluate_temporal_tree(
                database,
                season_code="2026-27",
            )
            second = evaluate_temporal_tree(
                database,
                season_code="2026-27",
            )

        self.assertEqual(first, second)
        self.assertEqual("complete", first["status"])
        self.assertEqual("temporal-tabular-v1", first["evaluatorVersion"])
        self.assertEqual(1, first["eligibleFoldCount"])
        self.assertEqual(
            {
                "temporal-ridge",
                "hist-gradient-boosting",
                "zero-points",
                "position-expanding-mean",
                "player-last-points",
                "official-running-mean",
            },
            {model["name"] for model in first["models"]},
        )
        fold = first["folds"][0]
        self.assertEqual(3, fold["trainingGameweeks"])
        self.assertEqual(3, fold["treeDiagnostics"]["trainingRows"])
        self.assertEqual(43, fold["treeDiagnostics"]["candidateFeatureCount"])
        self.assertGreater(
            fold["treeDiagnostics"]["modelFeatureCount"],
            0,
        )
        self.assertEqual(
            "1.9.0",
            fold["treeDiagnostics"]["libraryVersion"],
        )
        self.assertEqual(
            3,
            fold["treeDiagnostics"]["missingTrainingValues"][
                "chanceNextRound"
            ],
        )

    def test_fixed_tree_learns_a_nonlinear_threshold(self) -> None:
        training = [
            self._sample(index, float(index), 0 if index < 40 else 10)
            for index in range(80)
        ]
        target = [
            self._sample(100, 10.0, 0),
            self._sample(101, 70.0, 10),
        ]

        predictions, diagnostics = _predict_tree(training, target)

        self.assertEqual(100, diagnostics["completedIterations"])
        self.assertEqual(MODEL_NAME, predictions[0].model)
        self.assertLess(predictions[0].predicted, 2.0)
        self.assertGreater(predictions[1].predicted, 8.0)

    def test_insufficient_data_preserves_honest_state(self) -> None:
        with ridge_test_helpers.TemporalRidgeTests._database() as database:
            report = evaluate_temporal_tree(
                database,
                season_code="2026-27",
                minimum_training_gameweeks=4,
            )

        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual([], report["models"])
        self.assertEqual("temporal-tabular-v1", report["evaluatorVersion"])

    @staticmethod
    def _sample(
        player_id: int,
        price: float,
        actual: int,
    ) -> Sample:
        features = {
            name: 0.0
            for name in CONTINUOUS_FEATURES
        }
        features["priceTenths"] = price
        return Sample(
            season_code="2026-27",
            gameweek=4,
            player_id=player_id,
            position="midfielder",
            features=features,
            actual=actual,
        )


if __name__ == "__main__":
    unittest.main()
