from __future__ import annotations

import sys
import unittest
from pathlib import Path
from unittest.mock import patch

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.current_appearance_hurdle_opening_squad import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    STATUS,
    build_current_appearance_hurdle_opening_squad,
)
from autofpl_analytics.current_multi_horizon_initial_squad import (  # noqa: E402
    SELECTED_POLICY_KEY,
)

MODULE = (
    "autofpl_analytics.current_appearance_hurdle_opening_squad"
)


class CurrentAppearanceHurdleOpeningSquadTests(unittest.TestCase):
    def test_artifact_compares_exact_paired_paths_and_selection_change(
        self,
    ) -> None:
        incumbent_selection = {
            "playerIds": list(range(1, 16)),
            "budgetTenths": 980,
        }
        challenger_selection = {
            "playerIds": list(range(2, 17)),
            "budgetTenths": 985,
        }
        scenario = {
            "seasonCode": "2026-27",
            "openingGameweek": 1,
            "decisionCutoffUtc": "2026-07-29T00:00:00+00:00",
            "officialCaptureId": 99,
            "scenarioCount": 3,
            "artifactVersion": "scenario-v1",
            "runIdentitySha256": "scenario-run",
            "variant": {
                "variantKey": "appearance-hurdle-points",
                "historicalEvaluation": {"decision": "retain"},
            },
        }
        challenger_score = {
            "pathTotalPoints": [310, 320, 330],
            "meanPoints": 320.0,
        }
        incumbent_score = {
            "pathTotalPoints": [309, 321, 326],
            "meanPoints": 318.666667,
        }
        multi = {
            "runIdentitySha256": "multi-run",
            "policies": [
                {
                    "isSelectedForProspectiveScoring": True,
                    "evaluationPolicyKey": SELECTED_POLICY_KEY,
                    "horizonGameweeks": 6,
                    "policyKey": "expected-points",
                    "objective": 317.5,
                    "selection": challenger_selection,
                    "solver": {"status": "optimal", "relativeGap": 0.0},
                    "exactScenarioScore": challenger_score,
                }
            ],
        }
        incumbent = {
            "officialCaptureId": 99,
            "runIdentitySha256": "incumbent-run",
            "selectedPolicy": {
                "evaluationPolicyKey": SELECTED_POLICY_KEY
            },
            "selection": incumbent_selection,
        }
        candidates = [
            {
                "playerId": player_id,
                "webName": f"Player {player_id}",
                "teamId": player_id % 4 + 1,
                "teamName": f"Team {player_id % 4 + 1}",
                "position": "MID",
                "priceTenths": 50,
            }
            for player_id in range(1, 17)
        ]

        with (
            patch(
                f"{MODULE}.build_current_appearance_hurdle_joint_scenarios",
                return_value=scenario,
            ),
            patch(
                f"{MODULE}.build_multi_squad_from_scenario",
                return_value=multi,
            ),
            patch(
                f"{MODULE}.build_current_selected_opening_squad",
                return_value=incumbent,
            ),
            patch(f"{MODULE}._build_candidates", return_value=candidates),
            patch(f"{MODULE}._week_matrices", return_value={}),
            patch(
                f"{MODULE}._score_horizon",
                return_value=incumbent_score,
            ),
        ):
            artifact = build_current_appearance_hurdle_opening_squad(
                Path("unused.db")
            )

        self.assertEqual(ARTIFACT_TYPE, artifact["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, artifact["artifactVersion"])
        self.assertEqual(STATUS, artifact["status"])
        self.assertFalse(artifact["isPromoted"])
        self.assertFalse(artifact["influencesAdvice"])
        self.assertEqual(
            [1],
            [
                player["playerId"]
                for player in artifact["selectionChange"][
                    "removedPlayers"
                ]
            ],
        )
        self.assertEqual(
            [16],
            [
                player["playerId"]
                for player in artifact["selectionChange"]["addedPlayers"]
            ],
        )
        self.assertEqual(
            1.333333,
            artifact["pairedScenarioComparison"]["meanPointDifference"],
        )
        self.assertEqual(
            0.666667,
            artifact["pairedScenarioComparison"][
                "challengerWinProbability"
            ],
        )
        self.assertEqual(
            "retain-current-hurdle-opening-squad-for-prospective-comparison",
            artifact["decision"],
        )
        self.assertEqual(64, len(artifact["runIdentitySha256"]))


if __name__ == "__main__":
    unittest.main()
