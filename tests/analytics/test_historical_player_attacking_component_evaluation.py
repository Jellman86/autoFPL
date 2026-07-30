from __future__ import annotations

import sys
import unittest
from pathlib import Path

from numpy.testing import assert_allclose

sys.path.insert(
    0,
    str(Path(__file__).resolve().parents[2] / "src" / "analytics"),
)

from autofpl_analytics.historical_player_attacking_component_evaluation import (  # noqa: E402,E501
    ASSIST_MODEL,
    DIRECT_MODEL,
    DIXON_COLES_ALLOCATOR_MODEL,
    GOAL_MODEL,
    LEAGUE_ALLOCATOR_MODEL,
    POSITION_ALLOCATOR_MODEL,
    AttackingObservation,
    _allocate_events,
    _normalise_weights,
    _predict_poisson,
    _screen,
)
from autofpl_analytics.multi_season_evaluation import FEATURES  # noqa: E402
from autofpl_analytics.temporal_ridge import Prediction, Sample  # noqa: E402


class HistoricalPlayerAttackingComponentTests(unittest.TestCase):
    def test_poisson_model_emits_positive_deterministic_intensities(self):
        training = [
            self._sample(
                player_id=index,
                actual=int(index % 5 == 0),
                feature=float(index),
            )
            for index in range(1, 61)
        ]
        target = [
            self._sample(101, 0, 3.0),
            self._sample(102, 0, 58.0),
        ]

        first, _ = _predict_poisson(training, target, GOAL_MODEL)
        second, _ = _predict_poisson(training, target, GOAL_MODEL)

        self.assertEqual(
            [row.predicted for row in first],
            [row.predicted for row in second],
        )
        self.assertTrue(all(row.predicted > 0 for row in first))

    def test_team_allocations_preserve_attributed_intensity(self):
        target = [
            self._sample(101, 0, 1.0, position="forward"),
            self._sample(102, 0, 1.0, position="midfielder"),
        ]
        observations = {
            101: self._observation(101, "forward"),
            102: self._observation(102, "midfielder"),
        }
        appearance = [
            self._prediction(101, "forward", 0.8),
            self._prediction(102, "midfielder", 0.6),
        ]
        conditional = {
            "goal": [
                self._prediction(101, "forward", 0.4, GOAL_MODEL),
                self._prediction(102, "midfielder", 0.1, GOAL_MODEL),
            ],
            "assist": [
                self._prediction(101, "forward", 0.2, ASSIST_MODEL),
                self._prediction(102, "midfielder", 0.3, ASSIST_MODEL),
            ],
        }
        predictions = _allocate_events(
            target,
            observations,
            appearance,
            conditional,
            {
                "league-home-away-independent-poisson": {
                    (9, "Team"): 1.2,
                },
                "time-decayed-dixon-coles": {
                    (9, "Team"): 1.5,
                },
            },
            {"goal": 0.9, "assist": 0.7},
            {
                "goal": {"forward": 0.2, "midfielder": 0.1},
                "assist": {"forward": 0.1, "midfielder": 0.2},
            },
        )

        for event, fraction in (("goal", 0.9), ("assist", 0.7)):
            rows = [
                row
                for row in predictions
                if row.model == DIXON_COLES_ALLOCATOR_MODEL
                and row.event == event
            ]
            assert_allclose(
                1.5 * fraction,
                sum(row.intensity for row in rows),
            )

    def test_weight_fallback_is_normalised(self):
        result = _normalise_weights(
            {1: 0.0, 2: 0.0},
            {1: 1.0, 2: 3.0},
        )

        self.assertEqual({1: 0.25, 2: 0.75}, result)

    def test_screen_keeps_materiality_gate_fixed(self):
        passing = _screen(
            self._model_summaries(1.0, 0.98),
            self._folds(1.0, 0.98),
        )
        failing = _screen(
            self._model_summaries(1.0, 0.995),
            self._folds(1.0, 0.995),
        )

        self.assertTrue(passing["passes"])
        self.assertFalse(failing["passes"])
        self.assertFalse(
            failing["gates"]["combinedNllImprovement"]
        )

    @staticmethod
    def _sample(
        player_id: int,
        actual: int,
        feature: float,
        *,
        position: str = "midfielder",
    ) -> Sample:
        return Sample(
            season_code="test",
            gameweek=1,
            player_id=player_id,
            position=position,
            features={
                **{name: None for name in FEATURES},
                "priorMinutesMean": feature,
            },
            actual=actual,
        )

    @staticmethod
    def _prediction(
        player_id: int,
        position: str,
        predicted: float,
        model: str = "appearance",
    ) -> Prediction:
        return Prediction(
            model=model,
            season_code="test",
            gameweek=1,
            player_id=player_id,
            position=position,
            predicted=predicted,
            actual=0,
        )

    @staticmethod
    def _observation(
        player_id: int,
        position: str,
    ) -> AttackingObservation:
        return AttackingObservation(
            player_code=player_id,
            position=position,
            team_name="Team",
            fixture_id=9,
            minutes=90,
            goals=0,
            assists=0,
        )

    @staticmethod
    def _summary(name: str, nll: float):
        metrics = {
            "poissonNegativeLogLikelihood": nll,
            "eventBrierScore": nll,
        }
        return {
            "name": name,
            "metrics": metrics,
            "slices": {
                "event": {
                    "goal": metrics,
                    "assist": metrics,
                },
                "position": {
                    "defender": metrics,
                    "forward": metrics,
                    "goalkeeper": metrics,
                    "midfielder": metrics,
                },
            },
        }

    @classmethod
    def _model_summaries(
        cls,
        incumbent: float,
        challenger: float,
    ):
        return [
            cls._summary(DIRECT_MODEL, incumbent),
            cls._summary(LEAGUE_ALLOCATOR_MODEL, challenger + 0.01),
            cls._summary(DIXON_COLES_ALLOCATOR_MODEL, challenger),
            cls._summary(POSITION_ALLOCATOR_MODEL, challenger + 0.02),
        ]

    @classmethod
    def _folds(cls, incumbent: float, challenger: float):
        return [
            {
                "seasonCode": "test",
                "gameweek": index,
                "models": cls._model_summaries(
                    incumbent,
                    challenger,
                ),
            }
            for index in range(1, 25)
        ]


if __name__ == "__main__":
    unittest.main()
