from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_official_creative_opening_evaluation import (  # noqa: E402,E501
    CHALLENGER_APPEARANCE,
    CHALLENGER_DISTRIBUTION,
    INCUMBENT_APPEARANCE,
    INCUMBENT_DISTRIBUTION,
    _distribution_screen,
)
from autofpl_analytics.multi_season_evaluation import FEATURES  # noqa: E402
from autofpl_analytics.official_creative_features import (  # noqa: E402
    FEATURES_WITH_OFFICIAL_CREATIVE,
    OfficialCreativeObservation,
    add_official_creative_features,
)
from autofpl_analytics.temporal_ridge import Sample  # noqa: E402


class HistoricalOfficialCreativeOpeningEvaluationTests(
    unittest.TestCase
):
    def test_enrichment_uses_only_supplied_prior_history(self) -> None:
        prior = [
            self._observation(1, bps=10.0, influence=20.0),
            self._observation(2, bps=20.0, influence=40.0),
            self._observation(3, bps=30.0, influence=60.0),
        ]
        future = self._observation(4, bps=1000.0, influence=1000.0)

        before = add_official_creative_features(
            self._sample(),
            prior,
        )
        unchanged = add_official_creative_features(
            self._sample(),
            prior,
        )
        after = add_official_creative_features(
            self._sample(),
            [*prior, future],
        )

        self.assertEqual(before.features, unchanged.features)
        self.assertEqual(20.0, before.features["priorBpsMean"])
        self.assertEqual(40.0, before.features["rolling3InfluenceMean"])
        self.assertNotEqual(
            before.features["priorBpsMean"],
            after.features["priorBpsMean"],
        )
        self.assertEqual(
            FEATURES_WITH_OFFICIAL_CREATIVE,
            tuple(before.features),
        )

    def test_joint_screen_retains_material_stable_gain(self) -> None:
        result = _distribution_screen(
            self._distribution_models(0.90, 0.88),
            self._appearance_models(0.20, 0.19, 0.60, 0.58),
            self._targets(
                incumbent=(0.90, 0.92, 0.88),
                challenger=(0.88, 0.91, 0.86),
            ),
        )

        self.assertTrue(result["passes"])
        self.assertTrue(all(result["gates"].values()))
        self.assertEqual(3, result["targetSeasonWins"])

    def test_joint_screen_rejects_better_crps_with_worse_appearance(
        self,
    ) -> None:
        result = _distribution_screen(
            self._distribution_models(0.90, 0.88),
            self._appearance_models(0.20, 0.21, 0.60, 0.62),
            self._targets(
                incumbent=(0.90, 0.92, 0.88),
                challenger=(0.88, 0.91, 0.86),
            ),
        )

        self.assertFalse(result["passes"])
        self.assertFalse(
            result["gates"]["appearanceBrierNonRegression"]
        )
        self.assertFalse(
            result["gates"]["appearanceLogLossNonRegression"]
        )

    @staticmethod
    def _sample() -> Sample:
        return Sample(
            season_code="2025-26",
            gameweek=1,
            player_id=1,
            position="midfielder",
            features={feature: 0.0 for feature in FEATURES},
            actual=0,
        )

    @staticmethod
    def _observation(
        gameweek: int,
        *,
        bps: float,
        influence: float,
    ) -> OfficialCreativeObservation:
        return OfficialCreativeObservation(
            season_code="2024-25",
            season_index=0,
            gameweek=gameweek,
            player_code=1,
            bps=bps,
            influence=influence,
            creativity=influence / 2.0,
            threat=influence / 3.0,
        )

    @staticmethod
    def _distribution_models(
        incumbent_crps: float,
        challenger_crps: float,
    ) -> list[dict]:
        return [
            {
                "name": INCUMBENT_DISTRIBUTION,
                "metrics": {"meanCrps": incumbent_crps},
                "slices": {
                    "position": {
                        "defender": {"meanCrps": 0.90},
                        "forward": {"meanCrps": 1.00},
                    }
                },
            },
            {
                "name": CHALLENGER_DISTRIBUTION,
                "metrics": {"meanCrps": challenger_crps},
                "slices": {
                    "position": {
                        "defender": {"meanCrps": 0.88},
                        "forward": {"meanCrps": 0.99},
                    }
                },
            },
        ]

    @staticmethod
    def _appearance_models(
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
                        "name": INCUMBENT_DISTRIBUTION,
                        "metrics": {"meanCrps": incumbent_score},
                    },
                    {
                        "name": CHALLENGER_DISTRIBUTION,
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
