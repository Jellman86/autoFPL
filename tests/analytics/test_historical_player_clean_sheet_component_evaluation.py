from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_player_clean_sheet_component_evaluation import (  # noqa: E402,E501
    ComponentObservation,
    _combine_component,
    _distribution_gates,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    Prediction,
    Sample,
    TemporalRidgeError,
)


class HistoricalPlayerCleanSheetComponentEvaluationTests(
    unittest.TestCase
):
    def test_component_mean_uses_coherent_minutes_and_position_points(
        self,
    ) -> None:
        sample = self._sample(player_id=7, position="defender", actual=6)
        observations = {
            7: ComponentObservation(
                player_code=7,
                position="defender",
                team_name="Example",
                fixture_id=42,
                minutes=90,
                total_points=6,
                clean_sheet_points=4,
            )
        }

        points, clean_sheets = _combine_component(
            [sample],
            observations,
            [self._prediction(sample, "appearance", 0.8, 1)],
            [self._prediction(sample, "played-60", 0.9, 1)],
            [self._prediction(sample, "residual", 2.0, 2)],
            {(42, "Example"): 0.25},
            "component",
            "clean-sheet",
        )

        self.assertAlmostEqual(2.4, points[0].predicted)
        self.assertEqual(6, points[0].actual)
        self.assertAlmostEqual(0.2, clean_sheets[0].predicted)
        self.assertEqual(1, clean_sheets[0].actual)

        misaligned = self._sample(
            player_id=8,
            position="defender",
            actual=6,
        )
        with self.assertRaises(TemporalRidgeError) as caught:
            _combine_component(
                [sample],
                observations,
                [self._prediction(misaligned, "appearance", 0.8, 1)],
                [self._prediction(sample, "played-60", 0.9, 1)],
                [self._prediction(sample, "residual", 2.0, 2)],
                {(42, "Example"): 0.25},
                "component",
                "clean-sheet",
            )
        self.assertEqual(
            "clean-sheet-component.prediction-alignment",
            caught.exception.code,
        )

    def test_distribution_gate_requires_proper_score_and_fold_gain(
        self,
    ) -> None:
        league = {
            "brierScore": 0.10,
            "logLoss": 0.30,
            "calibrationError10": 0.02,
        }
        challenger = {
            "brierScore": 0.09,
            "logLoss": 0.28,
            "calibrationError10": 0.01,
        }

        passed = _distribution_gates(league, challenger, 13, 24)
        failed = _distribution_gates(league, challenger, 12, 24)

        self.assertTrue(all(passed.values()))
        self.assertFalse(failed["strictMajorityFoldWins"])

    @staticmethod
    def _sample(
        *,
        player_id: int,
        position: str,
        actual: int,
    ) -> Sample:
        return Sample(
            season_code="2025-26",
            gameweek=31,
            player_id=player_id,
            position=position,
            features={},
            actual=actual,
        )

    @staticmethod
    def _prediction(
        sample: Sample,
        model: str,
        predicted: float,
        actual: int,
    ) -> Prediction:
        return Prediction(
            model=model,
            season_code=sample.season_code,
            gameweek=sample.gameweek,
            player_id=sample.player_id,
            position=sample.position,
            predicted=predicted,
            actual=actual,
        )


if __name__ == "__main__":
    unittest.main()
