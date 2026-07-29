from __future__ import annotations

import sys
import unittest
from pathlib import Path
from unittest.mock import patch

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.historical_opening_forecast_reconstruction import (  # noqa: E402
    TARGET_GAMEWEEKS,
    _reconstruct_target,
    _target_sample,
)
from autofpl_analytics.historical_opening_policy_data import (  # noqa: E402
    OpeningFold,
    OpeningPlayer,
)
from autofpl_analytics.historical_preseason_evaluation import (  # noqa: E402
    HistoricalCapture,
)
from autofpl_analytics.multi_season_evaluation import Origin  # noqa: E402
from autofpl_analytics.temporal_ridge import Prediction, Sample  # noqa: E402


class HistoricalOpeningForecastReconstructionTests(unittest.TestCase):
    def test_zero_fixture_proxy_is_explicit_and_does_not_divide_by_zero(
        self,
    ) -> None:
        player = self._player()
        sample = _target_sample(
            "2023-24",
            1,
            2,
            player,
            (),
            {
                2: {
                    "teams": {},
                    "fixtureIds": {1},
                    "allKickoffs": ["2023-08-19T12:30:00Z"],
                }
            },
        )

        self.assertEqual(0.0, sample.features["targetFixtureCount"])
        self.assertEqual(0.0, sample.features["targetHomeFixtureRate"])
        self.assertEqual(0, sample.actual)

    def test_reconstruction_uses_only_constraint_cohort_and_prior_training(
        self,
    ) -> None:
        training_capture = self._capture(1, "2022-23")
        target_capture = self._capture(2, "2023-24")
        fold = OpeningFold(
            target_capture=target_capture,
            training_captures=(training_capture,),
            players=(self._player(),),
            raw_gameweeks_sha256=target_capture.gameweeks_sha256,
        )
        training = Sample(
            season_code="2022-23",
            gameweek=1,
            player_id=101,
            position="goalkeeper",
            features={},
            actual=4,
        )
        fixture_proxy = {
            gameweek: {
                "teams": {
                    "Club 1": [
                        {
                            "fixtureId": gameweek,
                            "kickoffUtc": (
                                f"2023-09-{gameweek:02d}T12:00:00Z"
                            ),
                            "wasHome": gameweek % 2 == 0,
                        }
                    ]
                },
                "fixtureIds": {gameweek},
                "allKickoffs": [
                    f"2023-09-{gameweek:02d}T12:00:00Z"
                ],
            }
            for gameweek in TARGET_GAMEWEEKS
        }

        def predict(
            _training: list[Sample],
            target: list[Sample],
            **_: object,
        ) -> tuple[list[Prediction], dict[str, int]]:
            self.assertEqual([training], _training)
            return (
                [
                    Prediction(
                        model="tree",
                        season_code=sample.season_code,
                        gameweek=sample.gameweek,
                        player_id=sample.player_id,
                        position=sample.position,
                        predicted=float(sample.gameweek),
                        actual=sample.actual,
                    )
                    for sample in target
                ],
                {"trainingRows": len(_training)},
            )

        with (
            patch(
                "autofpl_analytics."
                "historical_opening_forecast_reconstruction."
                "_build_feature_table",
                return_value={
                    Origin(0, 1, "2022-23"): [training],
                },
            ),
            patch(
                "autofpl_analytics."
                "historical_opening_forecast_reconstruction."
                "_load_observations",
                return_value=[],
            ),
            patch(
                "autofpl_analytics."
                "historical_opening_forecast_reconstruction."
                "_load_fixture_proxy",
                return_value=fixture_proxy,
            ),
            patch(
                "autofpl_analytics."
                "historical_opening_forecast_reconstruction."
                "_predict_tree",
                side_effect=predict,
            ),
        ):
            result = _reconstruct_target(object(), fold)

        self.assertEqual("2023-24", result["targetSeasonCode"])
        self.assertEqual(1, result["trainingRowCount"])
        self.assertEqual(1, result["playerCount"])
        player = result["players"][0]
        self.assertEqual([], list(fold.players[0].points))
        self.assertEqual(6.0, player["horizonExpectedPoints"]["3"])
        self.assertEqual(21.0, player["horizonExpectedPoints"]["6"])
        self.assertEqual(36.0, player["horizonExpectedPoints"]["8"])

    @staticmethod
    def _capture(capture_id: int, season: str) -> HistoricalCapture:
        return HistoricalCapture(
            capture_id=capture_id,
            season_code=season,
            source_revision="a" * 40,
            available_at_utc="2026-07-29T00:00:00+00:00",
            players_sha256="b" * 64,
            gameweeks_sha256="c" * 64,
            player_count=1,
            player_gameweek_count=1,
            stable_code_count=1,
        )

    @staticmethod
    def _player() -> OpeningPlayer:
        return OpeningPlayer(
            season_element_id=1,
            player_code=101,
            web_name="Keeper",
            position="goalkeeper",
            team_name="Club 1",
            team_id=1,
            price_tenths=45,
            points=(),
            minutes=(),
            observed_gameweeks=(),
        )


if __name__ == "__main__":
    unittest.main()
