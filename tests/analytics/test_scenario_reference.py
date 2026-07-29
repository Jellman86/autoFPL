from __future__ import annotations

import sys
import unittest
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.scenario_reference import (  # noqa: E402
    ENGINE_VERSION,
    RANDOM_STREAM,
    ScenarioScoreBatch,
    ScenarioValidationError,
    SelectionDefinition,
    compare_paired_scores,
    sample_joint_scenarios,
    score_selection_scenarios,
    summarise_scores,
)


class ScenarioReferenceTests(unittest.TestCase):
    def test_exact_all_played_score_includes_captain_bonus(self) -> None:
        selection = self._selection()
        points = np.arange(1, 16, dtype=np.int64)[None, :]
        played = np.ones_like(points, dtype=np.bool_)

        result = score_selection_scenarios(selection, points, played)

        np.testing.assert_array_equal([92], result.total_points)
        np.testing.assert_array_equal([8], result.captain_bonus_points)
        np.testing.assert_array_equal(
            [0],
            result.activated_substitute_count,
        )
        np.testing.assert_array_equal(
            [0],
            result.unreplaced_starter_count,
        )

    def test_goalkeeper_and_outfield_auto_subs_match_domain_order(self) -> None:
        selection = self._selection()
        points = np.arange(1, 16, dtype=np.int64)[None, :]
        played = np.ones_like(points, dtype=np.bool_)
        points[0, 0] = 0
        played[0, 0] = False
        points[0, 2] = 0
        played[0, 2] = False

        result = score_selection_scenarios(selection, points, played)

        # GK 2 replaces GK 1. First outfield bench player 12 can replace
        # defender 3 while retaining a legal 3-5-2.
        np.testing.assert_array_equal([102], result.total_points)
        np.testing.assert_array_equal(
            [2],
            result.activated_substitute_count,
        )
        np.testing.assert_array_equal(
            [0],
            result.unreplaced_starter_count,
        )

    def test_auto_sub_skips_an_illegal_formation_then_uses_next_bench(self) -> None:
        selection = self._selection(
            starting_player_ids=(1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14),
            outfield_substitute_player_ids=(15, 6, 7),
        )
        points = np.ones((1, 15), dtype=np.int64)
        played = np.ones_like(points, dtype=np.bool_)
        points[0, 2] = 0
        played[0, 2] = False

        result = score_selection_scenarios(selection, points, played)

        # Forward 15 cannot replace the missing third defender. Defender 6
        # then becomes the one legal substitute.
        np.testing.assert_array_equal([12], result.total_points)
        np.testing.assert_array_equal(
            [1],
            result.activated_substitute_count,
        )

    def test_captaincy_passes_to_vice_and_unreplaced_starter_is_visible(
        self,
    ) -> None:
        selection = self._selection()
        points = np.arange(1, 16, dtype=np.int64)[None, :]
        played = np.ones_like(points, dtype=np.bool_)
        points[0, 7] = 0
        played[0, 7] = False
        for player_id in (12, 7, 15):
            points[0, player_id - 1] = 0
            played[0, player_id - 1] = False

        result = score_selection_scenarios(selection, points, played)

        np.testing.assert_array_equal([85], result.total_points)
        np.testing.assert_array_equal([9], result.captain_bonus_points)
        np.testing.assert_array_equal(
            [1],
            result.unreplaced_starter_count,
        )

    def test_joint_sampling_is_seeded_and_never_builds_hybrid_rows(self) -> None:
        first_points = np.arange(1, 16, dtype=np.int64)
        second_points = np.arange(15, 0, -1, dtype=np.int64)
        support = np.stack((first_points, second_points))
        played = np.ones_like(support, dtype=np.bool_)

        first = sample_joint_scenarios(support, played, 128, seed=8675309)
        second = sample_joint_scenarios(support, played, 128, seed=8675309)

        self.assertEqual("numpy-pcg64-v1", RANDOM_STREAM)
        np.testing.assert_array_equal(
            first.source_indices,
            second.source_indices,
        )
        np.testing.assert_array_equal(first.points, second.points)
        self.assertEqual(
            {tuple(first_points), tuple(second_points)},
            {tuple(row) for row in first.points},
        )

    def test_summary_and_paired_comparison_use_identical_scenarios(self) -> None:
        reference = self._scores([40, 50, 60, 70])
        candidate = self._scores([42, 49, 65, 70])

        summary = summarise_scores(candidate)
        comparison = compare_paired_scores(reference, candidate)

        self.assertEqual(ENGINE_VERSION, summary["engineVersion"])
        self.assertEqual(56.5, summary["meanPoints"])
        self.assertEqual(1.5, comparison["meanPointsDelta"])
        self.assertEqual(0.5, comparison["probabilityCandidateWins"])
        self.assertEqual(0.25, comparison["probabilityTie"])
        self.assertEqual(0.25, comparison["probabilityCandidateLoses"])

    def test_nonplaying_points_fail_closed(self) -> None:
        selection = self._selection()
        points = np.ones((1, 15), dtype=np.int64)
        played = np.ones_like(points, dtype=np.bool_)
        played[0, 0] = False

        with self.assertRaises(ScenarioValidationError) as caught:
            score_selection_scenarios(selection, points, played)

        self.assertEqual(
            "scenario.nonplayer-points.nonzero",
            caught.exception.code,
        )

    def test_points_outside_domain_integer_range_fail_closed(self) -> None:
        selection = self._selection()
        points = np.ones((1, 15), dtype=np.int64)
        played = np.ones_like(points, dtype=np.bool_)
        points[0, 0] = np.iinfo(np.int32).max + 1

        with self.assertRaises(ScenarioValidationError) as caught:
            score_selection_scenarios(selection, points, played)

        self.assertEqual(
            "scenario.points.out-of-range",
            caught.exception.code,
        )

    def test_invalid_starting_formation_fails_closed(self) -> None:
        with self.assertRaises(ScenarioValidationError) as caught:
            self._selection(
                starting_player_ids=(
                    1,
                    3,
                    4,
                    8,
                    9,
                    10,
                    11,
                    12,
                    13,
                    14,
                    15,
                ),
                outfield_substitute_player_ids=(5, 6, 7),
            )

        self.assertEqual(
            "selection.starting.formation",
            caught.exception.code,
        )

    @staticmethod
    def _selection(
        starting_player_ids: tuple[int, ...] = (
            1,
            3,
            4,
            5,
            6,
            8,
            9,
            10,
            11,
            13,
            14,
        ),
        outfield_substitute_player_ids: tuple[int, ...] = (12, 7, 15),
    ) -> SelectionDefinition:
        return SelectionDefinition.create(
            player_ids=tuple(range(1, 16)),
            positions=(
                "goalkeeper",
                "goalkeeper",
                "defender",
                "defender",
                "defender",
                "defender",
                "defender",
                "midfielder",
                "midfielder",
                "midfielder",
                "midfielder",
                "midfielder",
                "forward",
                "forward",
                "forward",
            ),
            starting_player_ids=starting_player_ids,
            replacement_goalkeeper_player_id=2,
            outfield_substitute_player_ids=outfield_substitute_player_ids,
            captain_player_id=8,
            vice_captain_player_id=9,
        )

    @staticmethod
    def _scores(values: list[int]) -> ScenarioScoreBatch:
        array = np.asarray(values, dtype=np.int64)
        zeros = np.zeros_like(array)
        return ScenarioScoreBatch(array, zeros, zeros, zeros)


if __name__ == "__main__":
    unittest.main()
