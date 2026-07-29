from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_training_window_evaluation import (  # noqa: E402
    CHALLENGER_SEASONS,
    INCUMBENT_SEASONS,
    _compare_reports,
)
from autofpl_analytics.multi_season_evaluation import TREE_MODEL  # noqa: E402
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402


class HistoricalTrainingWindowEvaluationTests(unittest.TestCase):
    def test_fixed_gate_retains_only_stable_material_improvement(
        self,
    ) -> None:
        incumbent = self._report(
            INCUMBENT_SEASONS,
            mae=1.0,
            rmse=2.0,
            fold_mae=1.0,
            position_mae=1.0,
            run_identity="a" * 64,
        )
        challenger = self._report(
            CHALLENGER_SEASONS,
            mae=0.98,
            rmse=1.95,
            fold_mae=0.98,
            position_mae=0.99,
            run_identity="b" * 64,
        )

        result = _compare_reports(incumbent, challenger)

        self.assertEqual(
            "retain-four-season-prospective-shadow",
            result["decision"],
        )
        self.assertTrue(all(result["gates"].values()))
        self.assertEqual(8, result["comparison"]["foldWins"])
        self.assertEqual(0.02, result["comparison"]["maeImprovementFraction"])
        self.assertFalse(result["isPromoted"])
        self.assertFalse(result["influencesAdvice"])

        weak = self._report(
            CHALLENGER_SEASONS,
            mae=0.995,
            rmse=1.99,
            fold_mae=0.995,
            position_mae=0.99,
            run_identity="c" * 64,
        )
        rejected = _compare_reports(incumbent, weak)

        self.assertEqual(
            "do-not-retain-four-season-window",
            rejected["decision"],
        )
        self.assertFalse(
            rejected["gates"]["aggregateMaeImprovement"]
        )

    def test_target_archive_mismatch_fails_closed(self) -> None:
        incumbent = self._report(
            INCUMBENT_SEASONS,
            mae=1.0,
            rmse=2.0,
            fold_mae=1.0,
            position_mae=1.0,
            run_identity="a" * 64,
        )
        challenger = self._report(
            CHALLENGER_SEASONS,
            mae=0.98,
            rmse=1.95,
            fold_mae=0.98,
            position_mae=0.99,
            run_identity="b" * 64,
        )
        challenger["captures"][-1]["gameweeksSha256"] = "d" * 64

        with self.assertRaises(TemporalRidgeError) as caught:
            _compare_reports(incumbent, challenger)

        self.assertEqual(
            "training-window.target-archive",
            caught.exception.code,
        )

    @staticmethod
    def _report(
        seasons: tuple[str, ...],
        *,
        mae: float,
        rmse: float,
        fold_mae: float,
        position_mae: float,
        run_identity: str,
    ) -> dict:
        capture = {
            "captureId": 4,
            "seasonCode": "2025-26",
            "sourceRevision": "e" * 40,
            "availableAtUtc": "2026-07-26T18:17:21+00:00",
            "playersSha256": "f" * 64,
            "gameweeksSha256": "0" * 64,
            "playerCount": 690,
            "playerGameweekCount": 29_747,
            "stableCodeCount": 690,
        }

        def model(model_mae: float, model_rmse: float) -> dict:
            return {
                "name": TREE_MODEL,
                "metrics": {
                    "count": 690,
                    "mae": model_mae,
                    "rmse": model_rmse,
                    "meanError": 0.0,
                },
                "slices": {
                    "position": {
                        position: {
                            "count": 100,
                            "mae": position_mae,
                            "rmse": model_rmse,
                            "meanError": 0.0,
                        }
                        for position in (
                            "goalkeeper",
                            "defender",
                            "midfielder",
                            "forward",
                        )
                    }
                },
            }

        folds = [
            {
                "seasonCode": "2025-26",
                "gameweek": gameweek,
                "trainingRows": (
                    50_000 if len(seasons) == 4 else 25_000
                ),
                "targetPlayerCount": 690,
                "models": [model(fold_mae, rmse)],
            }
            for gameweek in range(31, 39)
        ]
        return {
            "status": "complete",
            "configuration": {
                "seasonCodes": list(seasons),
                "evaluationSeasonCode": "2025-26",
                "evaluationStartGameweek": 31,
            },
            "captures": [
                {
                    **capture,
                    "seasonCode": season,
                    "captureId": (
                        4 if season == "2025-26" else index + 1
                    ),
                }
                for index, season in enumerate(seasons)
            ],
            "folds": folds,
            "models": [model(mae, rmse)],
            "runIdentitySha256": run_identity,
        }


if __name__ == "__main__":
    unittest.main()
