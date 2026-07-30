from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(
    0,
    str(Path(__file__).resolve().parents[2] / "src" / "analytics"),
)

from autofpl_analytics.historical_appearance_hurdle_multi_horizon_policy_evaluation import (  # noqa: E402,E501
    HURDLE_SCENARIO_DATA_IDENTITY,
    HURDLE_SCENARIO_RUN_IDENTITY,
    REGISTRATION_DATA_IDENTITY,
    REGISTRATION_RUN_IDENTITY,
    _load_registration,
    _require_hurdle_scenario,
)
from autofpl_analytics.historical_appearance_hurdle_opening_evaluation import (  # noqa: E402,E501
    SCENARIO_ARTIFACT_VERSION,
)
from autofpl_analytics.historical_opening_policy_registration import (  # noqa: E402,E501
    ARTIFACT_VERSION as REGISTRATION_ARTIFACT_VERSION,
)
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402


class HistoricalAppearanceHurdleMultiHorizonPolicyTests(
    unittest.TestCase
):
    def test_original_registration_identity_and_gates_are_required(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "registration.json"
            document = self._registration()
            path.write_text(json.dumps(document), encoding="utf-8")

            loaded = _load_registration(path)

            self.assertEqual(
                REGISTRATION_DATA_IDENTITY,
                loaded["dataIdentitySha256"],
            )
            document["selectionRule"][
                "minimumTargetWinsOverReference"
            ] = 1
            path.write_text(json.dumps(document), encoding="utf-8")
            with self.assertRaisesRegex(
                TemporalRidgeError,
                "stability gates differ",
            ):
                _load_registration(path)

    def test_retained_hurdle_scenario_identity_is_required(self):
        scenario = {
            "artifactVersion": SCENARIO_ARTIFACT_VERSION,
            "dataIdentitySha256": HURDLE_SCENARIO_DATA_IDENTITY,
            "runIdentitySha256": HURDLE_SCENARIO_RUN_IDENTITY,
            "temporalDesign": {
                "targetPerformanceFieldsRead": False,
            },
        }

        _require_hurdle_scenario(scenario)

        scenario["runIdentitySha256"] = "0" * 64
        with self.assertRaisesRegex(
            TemporalRidgeError,
            "scenario identity differs",
        ):
            _require_hurdle_scenario(scenario)

    @staticmethod
    def _registration():
        return {
            "artifactVersion": REGISTRATION_ARTIFACT_VERSION,
            "dataIdentitySha256": REGISTRATION_DATA_IDENTITY,
            "runIdentitySha256": REGISTRATION_RUN_IDENTITY,
            "targetOutcomesOpened": False,
            "registeredPolicies": [],
            "commonOutcomeScoring": {},
            "selectionRule": {
                "referencePolicyKey": "6-expected-points",
                "minimumMeanImprovementOverReferencePoints": 2.0,
                "minimumTargetWinsOverReference": 2,
                "maximumWorstTargetRegressionPoints": 2.0,
            },
        }


if __name__ == "__main__":
    unittest.main()
