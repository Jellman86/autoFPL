from __future__ import annotations

import sys
import unittest
from pathlib import Path
from unittest.mock import patch

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.current_best_supported_opening_squad import (  # noqa: E402
    build_current_best_supported_opening_squad,
)
from autofpl_analytics.current_selected_opening_squad import (  # noqa: E402
    BEST_SUPPORTED_ARTIFACT_VERSION,
    BEST_SUPPORTED_STATUS,
)

MODULE = "autofpl_analytics.current_best_supported_opening_squad"


class CurrentBestSupportedOpeningSquadTests(unittest.TestCase):
    def test_wrapper_binds_hurdle_evaluation_and_v2_identity(
        self,
    ) -> None:
        historical = {
            "evaluatorVersion": "historical-appearance-hurdle-v1",
            "dataIdentitySha256": "a" * 64,
            "runIdentitySha256": "b" * 64,
        }
        scenario = {
            "variant": {
                "historicalEvaluation": historical,
            }
        }
        built = {"artifactVersion": BEST_SUPPORTED_ARTIFACT_VERSION}

        with (
            patch(
                f"{MODULE}.build_current_appearance_hurdle_joint_scenarios",
                return_value=scenario,
            ),
            patch(
                f"{MODULE}._build_from_scenario",
                return_value=built,
            ) as build,
        ):
            artifact = build_current_best_supported_opening_squad(
                Path("unused.db")
            )

        self.assertIs(built, artifact)
        build.assert_called_once()
        keyword = build.call_args.kwargs
        self.assertEqual(
            BEST_SUPPORTED_ARTIFACT_VERSION,
            keyword["artifact_version"],
        )
        self.assertEqual(BEST_SUPPORTED_STATUS, keyword["status"])
        self.assertTrue(keyword["influences_advice"])
        self.assertEqual(
            {
                "artifactVersion": historical["evaluatorVersion"],
                "dataIdentitySha256": historical[
                    "dataIdentitySha256"
                ],
                "runIdentitySha256": historical[
                    "runIdentitySha256"
                ],
            },
            keyword["model_evaluation_source"],
        )


if __name__ == "__main__":
    unittest.main()
