from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.historical_opening_policy_evaluation import (  # noqa: E402
    _score_actual,
    _select_policy,
    _select_roles,
)
from autofpl_analytics.historical_opening_policy_registration import (  # noqa: E402
    REFERENCE_POLICY_KEY,
)
from autofpl_analytics.temporal_ridge import _sha256  # noqa: E402


class HistoricalOpeningPolicyEvaluationTests(unittest.TestCase):
    def test_retained_evaluation_is_self_consistent_and_not_promoted(
        self,
    ) -> None:
        path = (
            Path(__file__).resolve().parents[2]
            / "docs"
            / "research"
            / "results"
            / "historical-opening-policy-evaluation-v1.json"
        )
        retained = json.loads(path.read_text(encoding="utf-8"))
        expected = retained.pop("runIdentitySha256")

        self.assertEqual(expected, _sha256(retained))
        self.assertTrue(retained["targetOutcomesOpened"])
        self.assertEqual(
            REFERENCE_POLICY_KEY,
            retained["decision"]["selectedPolicyKey"],
        )
        self.assertFalse(
            retained["prospectiveBoundary"]["isPromoted"]
        )
        self.assertFalse(
            retained["prospectiveBoundary"]["mayInfluenceAdvice"]
        )

    def test_post_horizon_roles_maximise_legal_preseason_mean(
        self,
    ) -> None:
        candidates = self._candidates()
        by_id = {
            int(player["playerId"]): player for player in candidates
        }
        means = {
            1: 1.0,
            2: 0.0,
            3: 10.0,
            4: 9.0,
            5: 8.0,
            6: 1.0,
            7: 0.0,
            8: 7.0,
            9: 6.0,
            10: 5.0,
            11: 4.0,
            12: 3.0,
            13: 10.0,
            14: 2.0,
            15: 1.0,
        }

        roles = _select_roles(
            tuple(range(1, 16)),
            by_id,
            means,
            gameweek=7,
        )

        self.assertEqual(7, roles["gameweek"])
        self.assertEqual(
            [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14],
            roles["startingPlayerIds"],
        )
        self.assertEqual(3, roles["captainPlayerId"])
        self.assertEqual(13, roles["viceCaptainPlayerId"])
        self.assertEqual(2, roles["replacementGoalkeeperPlayerId"])
        self.assertEqual(
            [6, 15, 7],
            roles["outfieldSubstitutePlayerIds"],
        )

    def test_actual_scorer_applies_substitutions_and_captain_fallback(
        self,
    ) -> None:
        candidates = self._candidates()
        roles = {
            "gameweek": 1,
            "startingPlayerIds": [1, 3, 4, 5, 6, 8, 9, 10, 11, 13, 14],
            "captainPlayerId": 8,
            "viceCaptainPlayerId": 13,
            "replacementGoalkeeperPlayerId": 2,
            "outfieldSubstitutePlayerIds": [7, 12, 15],
        }
        selection = {
            "playerIds": list(range(1, 16)),
            "gameweeks": [
                {**roles, "gameweek": gameweek}
                for gameweek in range(1, 9)
            ],
        }
        first_week_points = {
            player_id: 1 for player_id in range(1, 16)
        }
        first_week_points.update(
            {1: 0, 2: 3, 3: 0, 7: 2, 8: 0, 13: 5}
        )
        did_play = {
            player_id: True for player_id in range(1, 16)
        }
        did_play.update({1: False, 3: False, 8: False})
        outcomes = {
            player_id: SimpleNamespace(
                points=(first_week_points[player_id],) + (0,) * 7,
                minutes=((90 if did_play[player_id] else 0),) + (0,) * 7,
            )
            for player_id in range(1, 16)
        }

        score = _score_actual(selection, candidates, outcomes)

        self.assertEqual(23, score["totalPoints"])
        self.assertEqual(23, score["weekly"][0]["points"])
        self.assertEqual(5, score["weekly"][0]["captainBonusPoints"])
        self.assertEqual(
            3,
            score["weekly"][0]["activatedSubstituteCount"],
        )
        self.assertEqual(
            0,
            score["weekly"][0]["unreplacedStarterCount"],
        )

    def test_registered_stability_gates_select_consistent_leader(
        self,
    ) -> None:
        results = self._target_results([103, 104, 101])

        _, decision = _select_policy(results)

        self.assertEqual(
            "8-expected-points",
            decision["selectedPolicyKey"],
        )
        self.assertFalse(decision["referenceRetained"])
        self.assertTrue(all(decision["stabilityGates"].values()))

    def test_registered_stability_gates_retain_reference_on_regression(
        self,
    ) -> None:
        results = self._target_results([103, 106, 97])

        _, decision = _select_policy(results)

        self.assertEqual(
            "8-expected-points",
            decision["rankedLeaderPolicyKey"],
        )
        self.assertEqual(
            REFERENCE_POLICY_KEY,
            decision["selectedPolicyKey"],
        )
        self.assertTrue(decision["referenceRetained"])
        self.assertFalse(
            decision["stabilityGates"]["worstTargetRegression"]
        )

    @staticmethod
    def _candidates() -> list[dict[str, object]]:
        positions = (
            ["goalkeeper"] * 2
            + ["defender"] * 5
            + ["midfielder"] * 5
            + ["forward"] * 3
        )
        return [
            {
                "playerId": index,
                "position": position,
            }
            for index, position in enumerate(positions, start=1)
        ]

    @staticmethod
    def _target_results(
        leader_scores: list[int],
    ) -> list[dict[str, object]]:
        results = []
        for season_index, season in enumerate(
            ("2023-24", "2024-25", "2025-26")
        ):
            policies = []
            for horizon in (3, 6, 8):
                for optimizer_policy in (
                    "expected-points",
                    "downside-balanced",
                ):
                    key = f"{horizon}-{optimizer_policy}"
                    if key == REFERENCE_POLICY_KEY:
                        score = 100
                    elif key == "8-expected-points":
                        score = leader_scores[season_index]
                    else:
                        score = 90
                    policies.append(
                        {
                            "evaluationPolicyKey": key,
                            "horizonGameweeks": horizon,
                            "optimizerPolicyKey": optimizer_policy,
                            "realisedOutcome": {
                                "totalPoints": score,
                            },
                        }
                    )
            results.append(
                {
                    "targetSeasonCode": season,
                    "policies": policies,
                }
            )
        return results


if __name__ == "__main__":
    unittest.main()
