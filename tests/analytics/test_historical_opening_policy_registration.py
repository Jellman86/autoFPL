from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.historical_opening_policy_registration import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    REFERENCE_POLICY_KEY,
    STATUS,
    build_historical_opening_policy_registration,
)
from autofpl_analytics.historical_opening_scenario_reconstruction import (  # noqa: E402
    ARTIFACT_VERSION as SCENARIO_ARTIFACT_VERSION,
    STATUS as SCENARIO_STATUS,
    _appearance_target_sample,
)
from autofpl_analytics.temporal_ridge import _sha256  # noqa: E402


class HistoricalOpeningPolicyRegistrationTests(unittest.TestCase):
    def test_retained_registration_hash_is_self_consistent(self) -> None:
        path = (
            Path(__file__).resolve().parents[2]
            / "docs"
            / "research"
            / "results"
            / "historical-opening-policy-registration-v1.json"
        )
        retained = json.loads(path.read_text(encoding="utf-8"))
        expected = retained.pop("runIdentitySha256")

        self.assertEqual(expected, _sha256(retained))
        self.assertFalse(retained["targetOutcomesOpened"])

    def test_registration_is_deterministic_and_opens_no_outcomes(self) -> None:
        scenario = self._scenario()
        with patch(
            "autofpl_analytics."
            "historical_opening_policy_registration."
            "build_historical_opening_scenario_reconstruction",
            return_value=scenario,
        ):
            first = build_historical_opening_policy_registration(
                Path("unused.db")
            )
            second = build_historical_opening_policy_registration(
                Path("unused.db")
            )

        self.assertEqual(first, second)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, first["artifactVersion"])
        self.assertEqual(STATUS, first["status"])
        self.assertFalse(first["targetOutcomesOpened"])
        self.assertEqual(6, len(first["registeredPolicies"]))
        self.assertEqual(
            [1, 2, 3, 4, 5, 6, 7, 8],
            first["commonOutcomeScoring"]["gameweeks"],
        )
        self.assertEqual(
            REFERENCE_POLICY_KEY,
            first["selectionRule"]["referencePolicyKey"],
        )
        self.assertFalse(
            first["prospectiveBoundary"]["mayInfluenceAdvice"]
        )
        self.assertEqual(64, len(first["dataIdentitySha256"]))
        self.assertEqual(64, len(first["runIdentitySha256"]))

    def test_appearance_target_uses_raw_prior_history_and_zero_fixture(
        self,
    ) -> None:
        sample = _appearance_target_sample(
            "2025-26",
            SimpleNamespace(
                player_code=42,
                position="midfielder",
                team_name="Club",
            ),
            {},
            {
                1: {
                    "teams": {},
                    "fixtureIds": {1},
                    "allKickoffs": ["2025-08-15T19:00:00Z"],
                }
            },
        )

        self.assertEqual(42, sample.player_id)
        self.assertEqual(0, sample.actual)
        self.assertEqual(0.0, sample.features["targetFixtureCount"])
        self.assertEqual(0.0, sample.features["targetHomeFixtureRate"])
        self.assertIsNone(sample.features["priorAppearanceRate"])

    @staticmethod
    def _scenario() -> dict[str, object]:
        targets = []
        for index, season in enumerate(
            ("2023-24", "2024-25", "2025-26"),
            start=1,
        ):
            targets.append(
                {
                    "targetSeasonCode": season,
                    "targetCaptureId": index,
                    "latestPriorSeasonCode": f"prior-{index}",
                    "playerCount": 600 + index,
                    "scenarioCount": 38,
                    "forecastIdentitySha256": f"{index:064x}",
                    "scenarioContentSha256": f"{index + 10:064x}",
                }
            )
        return {
            "artifactVersion": SCENARIO_ARTIFACT_VERSION,
            "status": SCENARIO_STATUS,
            "dataIdentitySha256": "a" * 64,
            "runIdentitySha256": "b" * 64,
            "temporalDesign": {
                "targetPerformanceFieldsRead": False,
            },
            "targets": targets,
        }


if __name__ == "__main__":
    unittest.main()
