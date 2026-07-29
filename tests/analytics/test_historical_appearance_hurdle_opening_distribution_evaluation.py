from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_appearance_hurdle_opening_distribution_evaluation import (  # noqa: E402
    CHALLENGER_APPEARANCE,
    CHALLENGER_MODEL,
    INCUMBENT_APPEARANCE,
    INCUMBENT_MODEL,
    _screen,
)


class HistoricalAppearanceHurdleOpeningDistributionEvaluationTests(
    unittest.TestCase
):
    def test_fixed_screen_retains_proper_score_and_calibration_gain(
        self,
    ) -> None:
        result = _screen(
            self._distribution_models(0.70, 0.67),
            self._appearance_models(
                incumbent_brier=0.10,
                challenger_brier=0.09,
                incumbent_log_loss=0.32,
                challenger_log_loss=0.30,
            ),
            self._targets(
                incumbent=(0.70, 0.72, 0.68),
                challenger=(0.66, 0.71, 0.65),
            ),
        )

        self.assertTrue(result["passes"])
        self.assertEqual(3, result["targetSeasonWins"])
        self.assertGreaterEqual(
            result["aggregateCrpsImprovementFraction"],
            0.01,
        )
        self.assertTrue(all(result["gates"].values()))

    def test_fixed_screen_rejects_appearance_regression(
        self,
    ) -> None:
        result = _screen(
            self._distribution_models(0.70, 0.67),
            self._appearance_models(
                incumbent_brier=0.10,
                challenger_brier=0.11,
                incumbent_log_loss=0.32,
                challenger_log_loss=0.34,
            ),
            self._targets(
                incumbent=(0.70, 0.72, 0.68),
                challenger=(0.66, 0.71, 0.65),
            ),
        )

        self.assertFalse(result["passes"])
        self.assertFalse(
            result["gates"]["appearanceBrierNonRegression"]
        )
        self.assertFalse(
            result["gates"]["appearanceLogLossNonRegression"]
        )

    def test_fixed_screen_rejects_position_regression(self) -> None:
        models = self._distribution_models(0.70, 0.67)
        models[1]["slices"]["position"]["forward"]["meanCrps"] = 0.80
        result = _screen(
            models,
            self._appearance_models(
                incumbent_brier=0.10,
                challenger_brier=0.09,
                incumbent_log_loss=0.32,
                challenger_log_loss=0.30,
            ),
            self._targets(
                incumbent=(0.70, 0.72, 0.68),
                challenger=(0.66, 0.71, 0.65),
            ),
        )

        self.assertFalse(result["passes"])
        self.assertFalse(result["gates"]["positionCrpsStability"])

    @staticmethod
    def _distribution_models(
        incumbent_crps: float,
        challenger_crps: float,
    ) -> list[dict]:
        return [
            {
                "name": INCUMBENT_MODEL,
                "metrics": {"meanCrps": incumbent_crps},
                "slices": {
                    "position": {
                        "defender": {"meanCrps": 0.65},
                        "forward": {"meanCrps": 0.72},
                    }
                },
            },
            {
                "name": CHALLENGER_MODEL,
                "metrics": {"meanCrps": challenger_crps},
                "slices": {
                    "position": {
                        "defender": {"meanCrps": 0.63},
                        "forward": {"meanCrps": 0.70},
                    }
                },
            },
        ]

    @staticmethod
    def _appearance_models(
        *,
        incumbent_brier: float,
        challenger_brier: float,
        incumbent_log_loss: float,
        challenger_log_loss: float,
    ) -> list[dict]:
        return [
            {
                "name": INCUMBENT_APPEARANCE,
                "metrics": {
                    "brierScore": incumbent_brier,
                    "logLoss": incumbent_log_loss,
                },
            },
            {
                "name": CHALLENGER_APPEARANCE,
                "metrics": {
                    "brierScore": challenger_brier,
                    "logLoss": challenger_log_loss,
                },
            },
        ]

    @staticmethod
    def _targets(
        *,
        incumbent: tuple[float, ...],
        challenger: tuple[float, ...],
    ) -> list[dict]:
        return [
            {
                "targetSeasonCode": f"season-{index}",
                "distributionModels": [
                    {
                        "name": INCUMBENT_MODEL,
                        "metrics": {"meanCrps": incumbent_score},
                    },
                    {
                        "name": CHALLENGER_MODEL,
                        "metrics": {"meanCrps": challenger_score},
                    },
                ],
            }
            for index, (incumbent_score, challenger_score) in enumerate(
                zip(incumbent, challenger, strict=True),
                start=1,
            )
        ]


if __name__ == "__main__":
    unittest.main()
