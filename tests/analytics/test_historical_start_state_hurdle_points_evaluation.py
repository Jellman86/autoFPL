from __future__ import annotations

import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_start_state_hurdle_points_evaluation import (  # noqa: E402,E501
    COHERENT_START_MODEL,
    START_STATE_MODEL,
    _combine_start_state_hurdle,
    _coherent_start_predictions,
    _evaluate,
)
from autofpl_analytics.multi_season_evaluation import (  # noqa: E402
    FEATURES,
    Origin,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    Prediction,
    Sample,
    TemporalRidgeError,
)


class HistoricalStartStateHurdlePointsEvaluationTests(
    unittest.TestCase
):
    def test_mixture_and_coherence_are_exact(self) -> None:
        target = [self._sample("2025-26", 31, 7, "forward", 4)]
        appearance = [
            self._prediction("appearance", 7, 0.7, 1)
        ]
        raw_start = [self._prediction("start", 7, 0.8, 1)]
        coherent, diagnostics = _coherent_start_predictions(
            appearance,
            raw_start,
        )

        combined = _combine_start_state_hurdle(
            target,
            appearance,
            coherent,
            [self._prediction("starter", 7, 6.0, 4)],
            [self._prediction("substitute", 7, 2.0, 4)],
        )

        self.assertEqual(COHERENT_START_MODEL, coherent[0].model)
        self.assertEqual(0.7, coherent[0].predicted)
        self.assertEqual(1, diagnostics["adjustmentCount"])
        self.assertAlmostEqual(4.2, combined[0].predicted)
        self.assertEqual(START_STATE_MODEL, combined[0].model)

        misaligned = [self._prediction("substitute", 8, 2.0, 4)]
        with self.assertRaises(TemporalRidgeError) as caught:
            _combine_start_state_hurdle(
                target,
                appearance,
                coherent,
                [self._prediction("starter", 7, 6.0, 4)],
                misaligned,
            )
        self.assertEqual(
            "start-state.prediction-alignment",
            caught.exception.code,
        )

    def test_synthetic_expanding_origin_is_deterministic(self) -> None:
        samples, observations = self._synthetic_origins()

        first = _evaluate(samples, observations, [])
        second = _evaluate(samples, observations, [])

        self.assertEqual(first, second)
        self.assertEqual("complete", first["status"])
        self.assertEqual(2, first["comparison"]["foldCount"])
        self.assertEqual(
            16,
            first["appearanceComponent"]["metrics"]["count"],
        )
        self.assertEqual(
            16,
            first["startComponent"]["rawMetrics"]["count"],
        )
        self.assertIn(
            first["decision"],
            {
                (
                    "retain-start-state-hurdle-for-distribution-"
                    "policy-screen"
                ),
                "do-not-retain-start-state-hurdle",
            },
        )
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["influencesAdvice"])

    @classmethod
    def _synthetic_origins(cls) -> tuple[dict, dict]:
        samples = {}
        observations = {}
        origins = [
            *[
                Origin(0, gameweek, "2024-25")
                for gameweek in range(1, 11)
            ],
            *[
                Origin(1, gameweek, "2025-26")
                for gameweek in range(1, 33)
            ],
        ]
        positions = ["defender"] * 4 + ["forward"] * 4
        for origin in origins:
            origin_samples = []
            origin_observations = {}
            for player_id, position in enumerate(positions, start=1):
                appeared = (origin.gameweek + player_id) % 5 != 0
                started = (
                    appeared
                    and (origin.gameweek + player_id) % 3 != 0
                )
                points = (
                    2 + (origin.gameweek + player_id) % 8
                    if started
                    else 1 + (origin.gameweek + player_id) % 3
                    if appeared
                    else 0
                )
                sample = cls._sample(
                    origin.season_code,
                    origin.gameweek,
                    player_id,
                    position,
                    points,
                )
                origin_samples.append(sample)
                origin_observations[player_id] = SimpleNamespace(
                    player_code=player_id,
                    position=position,
                    total_points=points,
                    minutes=90 if started else 20 if appeared else 0,
                    starts=int(started),
                )
            samples[origin] = origin_samples
            observations[origin] = origin_observations
        return samples, observations

    @staticmethod
    def _sample(
        season: str,
        gameweek: int,
        player_id: int,
        position: str,
        actual: int,
    ) -> Sample:
        return Sample(
            season_code=season,
            gameweek=gameweek,
            player_id=player_id,
            position=position,
            features={
                name: float(
                    (index + gameweek + player_id * 3) % 17
                )
                for index, name in enumerate(FEATURES)
            },
            actual=actual,
        )

    @staticmethod
    def _prediction(
        model: str,
        player_id: int,
        predicted: float,
        actual: int,
    ) -> Prediction:
        return Prediction(
            model=model,
            season_code="2025-26",
            gameweek=31,
            player_id=player_id,
            position="forward",
            predicted=predicted,
            actual=actual,
        )


if __name__ == "__main__":
    unittest.main()
