from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_opening_policy_data import (  # noqa: E402
    OpeningPlayer,
)
from autofpl_analytics.historical_promoted_appearance_evaluation import (  # noqa: E402,E501
    _match_target_player,
    _name_tokens,
    _parse_extractions,
    _screen,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)


class HistoricalPromotedAppearanceEvaluationTests(unittest.TestCase):
    def test_name_tokens_normalise_accents_and_football_letters(
        self,
    ) -> None:
        self.assertEqual(
            ("johann", "berg", "gudmundsson"),
            _name_tokens("Jóhann Berg Guðmundsson"),
        )
        self.assertEqual(
            ("maximilian", "wober"),
            _name_tokens("Maximilian Wöber"),
        )

    def test_bridge_accepts_unique_same_team_token_subset(self) -> None:
        player = self._player(101)
        target_index = {
            (
                "Ipswich",
                _name_tokens("Omari Giraud-Hutchinson"),
            ): [player],
            (
                "Other",
                _name_tokens("Omari Hutchinson"),
            ): [self._player(102)],
        }

        matched, method = _match_target_player(
            "Ipswich",
            "Omari Hutchinson",
            target_index,
        )

        self.assertEqual(player, matched)
        self.assertEqual("unique-token-subset-full-name", method)

    def test_bridge_rejects_one_token_and_ambiguous_proposals(self) -> None:
        first = self._player(101)
        second = self._player(102)
        target_index = {
            ("Burnley", _name_tokens("Lucas Pires Silva")): [first],
            ("Burnley", _name_tokens("Lucas João")): [second],
        }

        matched, method = _match_target_player(
            "Burnley",
            "Lucas",
            target_index,
        )

        self.assertIsNone(matched)
        self.assertEqual("unresolved", method)

    def test_screen_passes_material_stable_gain(self) -> None:
        predictions = self._predictions(
            incumbent=(0.70, 0.30),
            challenger=(0.90, 0.10),
            actual=(1, 0),
        )
        targets = [
            {"challengerWins": True},
            {"challengerWins": True},
            {"challengerWins": True},
        ]

        result = _screen(predictions, targets)

        self.assertTrue(result["passes"])
        self.assertTrue(all(result["gates"].values()))

    def test_screen_rejects_aggregate_gain_with_target_instability(
        self,
    ) -> None:
        predictions = self._predictions(
            incumbent=(0.70, 0.30),
            challenger=(0.90, 0.10),
            actual=(1, 0),
        )
        targets = [
            {"challengerWins": True},
            {"challengerWins": False},
            {"challengerWins": False},
        ]

        result = _screen(predictions, targets)

        self.assertFalse(result["passes"])
        self.assertFalse(result["gates"]["minimumTargetWins"])

    def test_extraction_arguments_require_unique_registered_pairs(
        self,
    ) -> None:
        with self.assertRaises(TemporalRidgeError):
            _parse_extractions(
                [
                    "2021-22=first.json",
                    "2021-22=second.json",
                ]
            )

    @staticmethod
    def _player(player_code: int) -> OpeningPlayer:
        return OpeningPlayer(
            season_element_id=player_code,
            player_code=player_code,
            web_name=f"Player {player_code}",
            position="midfielder",
            team_name="Ipswich",
            team_id=1,
            price_tenths=50,
            points=(0,) * 8,
            minutes=(0,) * 8,
            observed_gameweeks=tuple(range(1, 9)),
        )

    @staticmethod
    def _predictions(
        *,
        incumbent: tuple[float, float],
        challenger: tuple[float, float],
        actual: tuple[int, int],
    ) -> list[dict]:
        rows = []
        for season_index in range(3):
            for player_index in range(20):
                row_index = player_index % 2
                for gameweek in range(1, 9):
                    rows.append(
                        {
                            "targetSeasonCode": (
                                f"season-{season_index}"
                            ),
                            "playerCode": player_index,
                            "position": (
                                "defender"
                                if player_index % 2 == 0
                                else "midfielder"
                            ),
                            "gameweek": gameweek,
                            "actual": actual[row_index],
                            "incumbent": incumbent[row_index],
                            "challenger": challenger[row_index],
                        }
                    )
        return rows


if __name__ == "__main__":
    unittest.main()
