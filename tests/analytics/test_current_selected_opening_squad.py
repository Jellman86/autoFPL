from __future__ import annotations

import hashlib
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_selected_opening_squad import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    STATUS,
    _build_from_scenario,
)
from autofpl_analytics.current_multi_horizon_initial_squad import (  # noqa: E402
    _build_from_scenario as build_multi_squad_from_scenario,
)
from tests.analytics import (  # noqa: E402
    test_current_multi_horizon_initial_squad as multi_squad_fixture,
)


class CurrentSelectedOpeningSquadTests(unittest.TestCase):
    def test_selected_squad_freezes_all_eight_roles_read_only(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            scenario = (
                multi_squad_fixture.CurrentMultiHorizonInitialSquadTests
                ._database_and_scenario(database)
            )
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            first = _build_from_scenario(database, scenario)
            second = _build_from_scenario(database, scenario)
            multi = build_multi_squad_from_scenario(database, scenario)
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, first["artifactVersion"])
        self.assertEqual(STATUS, first["status"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["influencesAdvice"])
        self.assertEqual(
            "6-expected-points",
            first["selectedPolicy"]["evaluationPolicyKey"],
        )
        self.assertEqual(15, len(first["selection"]["players"]))
        self.assertEqual(8, len(first["selection"]["gameweeks"]))
        self.assertEqual(
            list(range(1, 9)),
            [
                row["gameweek"]
                for row in first["selection"]["gameweeks"]
            ],
        )
        self.assertEqual(
            8,
            len(first["preseasonScenarioScore"]["weekly"]),
        )
        selected = next(
            policy
            for policy in multi["policies"]
            if policy["isSelectedForProspectiveScoring"]
        )
        self.assertTrue(
            any(value > 15 for value in first["selection"]["playerIds"])
        )
        self.assertEqual(
            selected["exactScenarioScore"]["weekly"],
            first["preseasonScenarioScore"]["weekly"][:6],
        )
        self.assertEqual(
            "waiting-for-official-2026-27-outcomes",
            first["prospectiveScoreRegistration"]["outcomeStatus"],
        )
        self.assertEqual(64, len(first["dataIdentitySha256"]))
        self.assertEqual(64, len(first["runIdentitySha256"]))


if __name__ == "__main__":
    unittest.main()
