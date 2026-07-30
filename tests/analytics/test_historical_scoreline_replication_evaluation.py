from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_scoreline_replication_evaluation import (  # noqa: E402,E501
    TARGET_SEASONS,
    _aggregate,
)
from autofpl_analytics.team_goal_strength_evaluation import (  # noqa: E402
    BASELINE_MODEL,
    CHALLENGER_MODEL,
)


class HistoricalScorelineReplicationEvaluationTests(unittest.TestCase):
    def test_fixed_screen_retains_stable_match_and_clean_sheet_gain(
        self,
    ) -> None:
        reports = [
            self._report(season, clean_sheet_challenger=0.17)
            for season in TARGET_SEASONS
        ]

        result = _aggregate(reports)

        self.assertTrue(result["fixedScreenPassed"])
        self.assertEqual(24, result["foldCount"])
        self.assertEqual(24, result["jointNllFoldWins"])
        self.assertEqual(24, result["cleanSheetBrierFoldWins"])
        self.assertGreater(
            result["cleanSheetBrierImprovementFraction"],
            0.01,
        )

    def test_fixed_screen_rejects_one_target_clean_sheet_regression(
        self,
    ) -> None:
        reports = [
            self._report(
                season,
                clean_sheet_challenger=(
                    0.21 if index == 1 else 0.15
                ),
            )
            for index, season in enumerate(TARGET_SEASONS)
        ]

        result = _aggregate(reports)

        self.assertFalse(result["targetCleanSheetNonRegression"])
        self.assertFalse(result["fixedScreenPassed"])

    @staticmethod
    def _report(
        season: str,
        *,
        clean_sheet_challenger: float,
    ) -> dict:
        baseline = {
            "matchCount": 10,
            "jointNegativeLogLikelihood": 3.0,
            "outcomeNegativeLogLikelihood": 1.1,
            "outcomeBrier": 0.65,
            "goalRmse": 1.2,
            "cleanSheetBrier": 0.2,
        }
        challenger = {
            "matchCount": 10,
            "jointNegativeLogLikelihood": 2.8,
            "outcomeNegativeLogLikelihood": 1.0,
            "outcomeBrier": 0.6,
            "goalRmse": 1.1,
            "cleanSheetBrier": clean_sheet_challenger,
        }
        folds = []
        for gameweek in range(31, 39):
            folds.append(
                {
                    "gameweek": gameweek,
                    "models": [
                        {"name": BASELINE_MODEL, "metrics": baseline},
                        {
                            "name": CHALLENGER_MODEL,
                            "metrics": challenger,
                        },
                    ],
                }
            )
        return {
            "configuration": {"evaluationSeasonCode": season},
            "captures": [],
            "folds": folds,
            "models": [
                {"name": BASELINE_MODEL, "metrics": baseline},
                {"name": CHALLENGER_MODEL, "metrics": challenger},
            ],
            "runIdentitySha256": season,
        }


if __name__ == "__main__":
    unittest.main()
