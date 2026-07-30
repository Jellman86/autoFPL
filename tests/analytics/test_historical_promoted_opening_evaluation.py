from __future__ import annotations

import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.baseline import (  # noqa: E402
    DistributionPrediction,
    ProbabilityPrediction,
    _empirical_distribution,
)
from autofpl_analytics.historical_promoted_appearance_evaluation import (  # noqa: E402,E501
    PriorCompetitionPlayer,
)
from autofpl_analytics.historical_promoted_opening_evaluation import (  # noqa: E402,E501
    CHALLENGER_APPEARANCE,
    CHALLENGER_DISTRIBUTION,
    INCUMBENT_APPEARANCE,
    INCUMBENT_DISTRIBUTION,
    _all_player_non_regression,
    _pool_appearance_predictions,
)
from autofpl_analytics.temporal_ridge import Prediction  # noqa: E402


class HistoricalPromotedOpeningEvaluationTests(unittest.TestCase):
    def test_pool_changes_only_bridged_player_probability(self) -> None:
        players = [
            SimpleNamespace(player_code=1, position="goalkeeper"),
            SimpleNamespace(player_code=2, position="midfielder"),
        ]
        incumbent = [
            self._prediction(1, "goalkeeper", 0.8),
            self._prediction(2, "midfielder", 0.6),
        ]
        source = PriorCompetitionPlayer(
            source_player_id="source-1",
            player_name="Player One",
            appearances=46,
            starts=46,
            minutes=4140,
        )

        result = _pool_appearance_predictions(
            self._FixedModel(0.4),
            {1: source},
            1,
            players,
            incumbent,
        )

        self.assertEqual(CHALLENGER_APPEARANCE, result[0].model)
        self.assertAlmostEqual(0.6, result[0].predicted)
        self.assertAlmostEqual(0.6, result[1].predicted)
        self.assertEqual(incumbent[1].player_id, result[1].player_id)

    def test_all_player_gate_accepts_joint_non_regression(self) -> None:
        distributions = [
            self._distribution(INCUMBENT_DISTRIBUTION, (0, 4), 1),
            self._distribution(CHALLENGER_DISTRIBUTION, (1, 1), 1),
        ]
        appearances = [
            self._appearance(INCUMBENT_APPEARANCE, 0.6, 1),
            self._appearance(CHALLENGER_APPEARANCE, 0.8, 1),
        ]

        result = _all_player_non_regression(
            distributions,
            appearances,
        )

        self.assertTrue(result["passes"])
        self.assertTrue(all(result["gates"].values()))

    def test_all_player_gate_rejects_appearance_regression(self) -> None:
        distributions = [
            self._distribution(INCUMBENT_DISTRIBUTION, (0, 4), 1),
            self._distribution(CHALLENGER_DISTRIBUTION, (1, 1), 1),
        ]
        appearances = [
            self._appearance(INCUMBENT_APPEARANCE, 0.8, 1),
            self._appearance(CHALLENGER_APPEARANCE, 0.6, 1),
        ]

        result = _all_player_non_regression(
            distributions,
            appearances,
        )

        self.assertFalse(result["passes"])
        self.assertFalse(
            result["gates"]["appearanceBrierNonRegression"]
        )

    @staticmethod
    def _prediction(
        player_id: int,
        position: str,
        probability: float,
    ) -> Prediction:
        return Prediction(
            model="incumbent",
            season_code="2025-26",
            gameweek=1,
            player_id=player_id,
            position=position,
            predicted=probability,
            actual=0,
        )

    @staticmethod
    def _distribution(
        model: str,
        samples: tuple[int, ...],
        actual: int,
    ) -> DistributionPrediction:
        return DistributionPrediction(
            model=model,
            season_code="2025-26",
            gameweek=1,
            player_id=1,
            position="midfielder",
            distribution=_empirical_distribution(samples),
            actual=actual,
        )

    @staticmethod
    def _appearance(
        model: str,
        probability: float,
        actual: int,
    ) -> ProbabilityPrediction:
        return ProbabilityPrediction(
            model=model,
            season_code="2025-26",
            gameweek=1,
            player_id=1,
            position="midfielder",
            probability=probability,
            actual=actual,
        )

    class _FixedModel:
        def __init__(self, probability: float) -> None:
            self._probability = probability

        def predict_proba(self, values: object) -> np.ndarray:
            del values
            return np.asarray(
                [[1.0 - self._probability, self._probability]]
            )


if __name__ == "__main__":
    unittest.main()
