from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_appearance_hurdle_opening_evaluation import (  # noqa: E402
    _screen,
)


class HistoricalAppearanceHurdleOpeningEvaluationTests(
    unittest.TestCase
):
    def test_fixed_screen_retains_a_stable_material_challenger(
        self,
    ) -> None:
        result = _screen(
            self._comparisons(
                incumbent=(300, 310, 320),
                challenger=(303, 314, 321),
            )
        )

        self.assertTrue(result["passes"])
        self.assertEqual(3, result["targetWins"])
        self.assertEqual(0, result["targetLosses"])
        self.assertEqual(2.666667, result["meanDifferencePoints"])
        self.assertTrue(all(result["gates"].values()))

    def test_fixed_screen_rejects_material_worst_target_regression(
        self,
    ) -> None:
        result = _screen(
            self._comparisons(
                incumbent=(300, 310, 320),
                challenger=(310, 312, 317),
            )
        )

        self.assertFalse(result["passes"])
        self.assertGreaterEqual(result["meanDifferencePoints"], 2.0)
        self.assertGreaterEqual(result["targetWins"], 2)
        self.assertFalse(
            result["gates"]["worstTargetRegression"]
        )

    def test_fixed_screen_rejects_weak_average_gain(self) -> None:
        result = _screen(
            self._comparisons(
                incumbent=(300, 310, 320),
                challenger=(301, 312, 320),
            )
        )

        self.assertFalse(result["passes"])
        self.assertFalse(result["gates"]["meanImprovement"])
        self.assertTrue(result["gates"]["targetWins"])

    @staticmethod
    def _comparisons(
        incumbent: tuple[int, ...],
        challenger: tuple[int, ...],
    ) -> list[dict[str, object]]:
        return [
            {
                "targetSeasonCode": f"season-{index}",
                "comparison": {
                    "incumbentTotalPoints": incumbent_score,
                    "challengerTotalPoints": challenger_score,
                    "differencePoints": (
                        challenger_score - incumbent_score
                    ),
                },
            }
            for index, (incumbent_score, challenger_score) in enumerate(
                zip(incumbent, challenger, strict=True),
                start=1,
            )
        ]


if __name__ == "__main__":
    unittest.main()
