from __future__ import annotations

import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_appearance_hurdle_points_evaluation import (  # noqa: E402
    HURDLE_MODEL,
    _combine_hurdle,
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


class HistoricalAppearanceHurdlePointsEvaluationTests(
    unittest.TestCase
):
    def test_factorization_is_exact_and_identity_checked(self) -> None:
        target = [self._sample("2025-26", 31, 7, "forward", 4)]
        appearance = [
            Prediction(
                "appearance",
                "2025-26",
                31,
                7,
                "forward",
                0.75,
                1,
            )
        ]
        conditional = [
            Prediction(
                "conditional",
                "2025-26",
                31,
                7,
                "forward",
                6.0,
                4,
            )
        ]

        combined = _combine_hurdle(
            target,
            appearance,
            conditional,
        )

        self.assertEqual(HURDLE_MODEL, combined[0].model)
        self.assertEqual(4.5, combined[0].predicted)
        self.assertEqual(4, combined[0].actual)

        misaligned = [
            Prediction(
                "conditional",
                "2025-26",
                31,
                8,
                "forward",
                6.0,
                4,
            )
        ]
        with self.assertRaises(TemporalRidgeError) as caught:
            _combine_hurdle(target, appearance, misaligned)
        self.assertEqual(
            "hurdle-points.prediction-alignment",
            caught.exception.code,
        )

    def test_synthetic_expanding_origin_is_deterministic_and_complete(
        self,
    ) -> None:
        samples, observations = self._synthetic_origins()

        first = _evaluate(samples, observations, [])
        second = _evaluate(samples, observations, [])

        self.assertEqual(first, second)
        self.assertEqual("complete", first["status"])
        self.assertEqual(2, first["comparison"]["foldCount"])
        self.assertEqual(
            2,
            len(first["comparison"]["positionSlices"]),
        )
        self.assertEqual(
            16,
            first["appearanceComponent"]["metrics"]["count"],
        )
        self.assertIn(
            first["decision"],
            {
                "retain-appearance-hurdle-prospective-shadow",
                "do-not-retain-appearance-hurdle",
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
            for index, position in enumerate(positions, start=1):
                appeared = (origin.gameweek + index) % 4 != 0
                points = (
                    1 + (origin.gameweek + index * 2) % 8
                    if appeared
                    else 0
                )
                sample = cls._sample(
                    origin.season_code,
                    origin.gameweek,
                    index,
                    position,
                    points,
                )
                origin_samples.append(sample)
                origin_observations[index] = SimpleNamespace(
                    player_code=index,
                    position=position,
                    total_points=points,
                    minutes=90 if appeared else 0,
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
        features = {
            name: float(
                (
                    feature_index
                    + gameweek
                    + player_id * 3
                )
                % 17
            )
            for feature_index, name in enumerate(FEATURES)
        }
        return Sample(
            season_code=season,
            gameweek=gameweek,
            player_id=player_id,
            position=position,
            features=features,
            actual=actual,
        )


if __name__ == "__main__":
    unittest.main()
