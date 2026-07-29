from __future__ import annotations

import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.historical_transfer_aware_opening_evaluation import (  # noqa: E402
    _score_challenger_actual,
    _screen,
)


class HistoricalTransferAwareOpeningEvaluationTests(unittest.TestCase):
    def test_reused_holdout_screen_requires_stable_improvement(self) -> None:
        passing = self._targets([3, 4, 1])
        failing = self._targets([10, 8, -5])

        passed = _screen(passing)
        failed = _screen(failing)

        self.assertTrue(passed["passes"])
        self.assertEqual(
            "retain-for-2026-27-prospective-shadow",
            passed["decision"],
        )
        self.assertFalse(failed["passes"])
        self.assertEqual("do-not-retain", failed["decision"])
        self.assertFalse(failed["gates"]["worstTargetRegression"])

    def test_actual_scorer_uses_each_week_owned_squad(self) -> None:
        candidates = self._candidates()
        first_roles = {
            "gameweek": 1,
            "squadPlayerIds": list(range(1, 16)),
            "startingPlayerIds": [1, 3, 4, 5, 6, 8, 9, 10, 11, 13, 14],
            "captainPlayerId": 13,
            "viceCaptainPlayerId": 14,
            "replacementGoalkeeperPlayerId": 2,
            "outfieldSubstitutePlayerIds": [7, 12, 15],
        }
        later_roles = {
            "squadPlayerIds": list(range(1, 15)) + [16],
            "startingPlayerIds": [1, 3, 4, 5, 6, 8, 9, 10, 11, 13, 16],
            "captainPlayerId": 16,
            "viceCaptainPlayerId": 13,
            "replacementGoalkeeperPlayerId": 2,
            "outfieldSubstitutePlayerIds": [7, 12, 14],
        }
        plan = {
            "gameweeks": [
                first_roles,
                *[
                    {**later_roles, "gameweek": gameweek}
                    for gameweek in range(2, 9)
                ],
            ]
        }
        outcomes = {
            player_id: SimpleNamespace(
                points=tuple(
                    10 if player_id == 16 and gameweek >= 2 else 1
                    for gameweek in range(1, 9)
                ),
                minutes=(90,) * 8,
            )
            for player_id in range(1, 17)
        }

        score = _score_challenger_actual(
            plan,
            candidates,
            outcomes,
        )

        self.assertEqual(8, len(score["weekly"]))
        self.assertEqual(12, score["weekly"][0]["points"])
        self.assertEqual(30, score["weekly"][1]["points"])
        self.assertEqual(12 + 7 * 30, score["totalPoints"])

    @staticmethod
    def _targets(differences: list[int]) -> list[dict[str, int]]:
        return [
            {"realisedPointDifference": difference}
            for difference in differences
        ]

    @staticmethod
    def _candidates() -> list[dict[str, object]]:
        positions = (
            ["goalkeeper"] * 2
            + ["defender"] * 5
            + ["midfielder"] * 5
            + ["forward"] * 4
        )
        return [
            {
                "playerId": index,
                "position": position,
            }
            for index, position in enumerate(positions, start=1)
        ]


if __name__ == "__main__":
    unittest.main()
