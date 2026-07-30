from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_scoring_component_reconstruction_audit import (  # noqa: E402,E501
    _component_points,
)


class HistoricalScoringComponentReconstructionAuditTests(
    unittest.TestCase
):
    def test_pre_2024_goalkeeper_scoring_reconstructs_all_components(
        self,
    ) -> None:
        row = self._row(
            minutes=90,
            goals_scored=1,
            assists=1,
            clean_sheets=1,
            goals_conceded=4,
            saves=7,
            penalties_saved=1,
            penalties_missed=1,
            yellow_cards=1,
            red_cards=1,
            own_goals=1,
            bonus=3,
        )

        points = _component_points("2023-24", "GKP", row)

        self.assertEqual(
            {
                "appearance": 2,
                "goals": 6,
                "assists": 3,
                "cleanSheets": 4,
                "goalsConceded": -2,
                "saves": 2,
                "penaltySaves": 5,
                "penaltyMisses": -2,
                "yellowCards": -1,
                "redCards": -3,
                "ownGoals": -2,
                "bonus": 3,
                "defensiveContributions": 0,
            },
            points,
        )
        self.assertEqual(15, sum(points.values()))

    def test_goalkeeper_goal_rule_changes_from_2024_25(self) -> None:
        row = self._row(minutes=1, goals_scored=1)

        before = _component_points("2023-24", "GKP", row)
        after = _component_points("2024-25", "GKP", row)

        self.assertEqual(6, before["goals"])
        self.assertEqual(10, after["goals"])
        self.assertEqual(1, after["appearance"])

    def test_2025_26_defensive_contribution_thresholds_are_capped(
        self,
    ) -> None:
        defender_below = self._row(defensive_contribution=9)
        defender_at = self._row(defensive_contribution=10)
        midfielder_below = self._row(defensive_contribution=11)
        midfielder_above = self._row(defensive_contribution=24)

        self.assertEqual(
            0,
            _component_points(
                "2025-26", "DEF", defender_below
            )["defensiveContributions"],
        )
        self.assertEqual(
            2,
            _component_points(
                "2025-26", "DEF", defender_at
            )["defensiveContributions"],
        )
        self.assertEqual(
            0,
            _component_points(
                "2025-26", "MID", midfielder_below
            )["defensiveContributions"],
        )
        self.assertEqual(
            2,
            _component_points(
                "2025-26", "MID", midfielder_above
            )["defensiveContributions"],
        )

    @staticmethod
    def _row(**overrides: int) -> dict[str, str]:
        values = {
            "minutes": 0,
            "goals_scored": 0,
            "assists": 0,
            "clean_sheets": 0,
            "goals_conceded": 0,
            "saves": 0,
            "penalties_saved": 0,
            "penalties_missed": 0,
            "yellow_cards": 0,
            "red_cards": 0,
            "own_goals": 0,
            "bonus": 0,
            "defensive_contribution": 0,
        }
        values.update(overrides)
        return {name: str(value) for name, value in values.items()}


if __name__ == "__main__":
    unittest.main()
