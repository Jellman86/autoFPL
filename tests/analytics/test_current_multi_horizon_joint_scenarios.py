from __future__ import annotations

import copy
import hashlib
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_joint_scenario_forecast import (  # noqa: E402
    APPEARANCE_VARIANT,
    RAW_APPEARANCE_VARIANT,
    _build_from_artifacts as build_single,
    _retained_screen,
)
from autofpl_analytics.current_multi_horizon_joint_scenarios import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    PATH_ENGINE,
    STATUS,
    _build_from_artifacts,
)
from autofpl_analytics.current_multi_horizon_player_forecast import (  # noqa: E402
    ARTIFACT_VERSION as POINT_ARTIFACT_VERSION,
    STATUS as POINT_STATUS,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics import (  # noqa: E402
    test_current_joint_scenario_forecast as joint_helpers,
)


class CurrentMultiHorizonJointScenariosTests(unittest.TestCase):
    def test_shadow_is_deterministic_read_only_and_complete(self) -> None:
        helper = joint_helpers.CurrentJointScenarioForecastTests()
        with helper._database() as database:
            point, participation, screen = helper._artifacts(database)
            multi_point = self._multi_point(point)
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            first = _build_from_artifacts(
                database,
                multi_point,
                participation,
                screen,
            )
            second = _build_from_artifacts(
                database,
                multi_point,
                participation,
                screen,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, first["artifactVersion"])
        self.assertEqual(STATUS, first["status"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["influencesAdvice"])
        self.assertEqual(PATH_ENGINE, first["pathConstruction"]["engine"])
        self.assertEqual(list(range(1, 9)), first["targetGameweeks"])
        self.assertEqual([3, 6, 8], first["decisionHorizons"])
        self.assertEqual(2, first["playerCount"])
        self.assertEqual(12, first["scenarioCount"])
        self.assertEqual(8, len(first["weeks"]))
        for week in first["weeks"]:
            self.assertEqual(
                list(range(1, 13)),
                sorted(week["sourceGameweeksByPath"]),
            )
            self.assertEqual(first["scenarioCount"], len(week["pointRows"]))
            self.assertEqual(first["scenarioCount"], len(week["playedRows"]))
            for points, played in zip(
                week["pointRows"],
                week["playedRows"],
            ):
                self.assertEqual(first["playerCount"], len(points))
                self.assertEqual(first["playerCount"], len(played))
                for value, appeared in zip(points, played):
                    if not appeared:
                        self.assertEqual(0, value)

    def test_gw1_matches_single_shadow_then_future_reverts_to_raw(self) -> None:
        helper = joint_helpers.CurrentJointScenarioForecastTests()
        with helper._database() as database:
            single_point, participation, screen = helper._artifacts(database)
            multi_point = self._multi_point(single_point)
            multi = _build_from_artifacts(
                database,
                multi_point,
                participation,
                screen,
            )
            single = build_single(
                database,
                single_point,
                participation,
                screen,
            )

        self.assertEqual(single["pointRows"], multi["weeks"][0]["pointRows"])
        self.assertEqual(
            single["playedRows"],
            multi["weeks"][0]["playedRows"],
        )
        for player in multi["players"]:
            original = next(
                row
                for row in single["players"]
                if row["playerId"] == player["playerId"]
            )
            self.assertEqual(
                original["pointMean"],
                player["gameweeks"][0]["pointMean"],
            )
            self.assertEqual(
                APPEARANCE_VARIANT,
                player["gameweeks"][0]["appearanceVariant"],
            )
            self.assertEqual(
                RAW_APPEARANCE_VARIANT,
                player["gameweeks"][1]["appearanceVariant"],
            )
            self.assertEqual(
                player["gameweeks"][1]["pointMeanBeforeAvailability"],
                player["gameweeks"][1]["pointMean"],
            )

    def test_weekly_path_permutations_preserve_marginals(self) -> None:
        helper = joint_helpers.CurrentJointScenarioForecastTests()
        with helper._database() as database:
            point, participation, screen = helper._artifacts(database)
            artifact = _build_from_artifacts(
                database,
                self._multi_point(point),
                participation,
                screen,
            )

        gw1 = artifact["weeks"][0]
        self.assertEqual(
            list(range(1, 13)),
            gw1["sourceGameweeksByPath"],
        )
        for week in artifact["weeks"][1:]:
            self.assertEqual(
                list(range(1, 13)),
                sorted(week["sourceGameweeksByPath"]),
            )
            self.assertNotEqual(
                gw1["sourceGameweeksByPath"],
                week["sourceGameweeksByPath"],
            )

    def test_misaligned_or_incomplete_sources_fail_closed(self) -> None:
        helper = joint_helpers.CurrentJointScenarioForecastTests()
        with helper._database() as database:
            point, participation, screen = helper._artifacts(database)
            multi_point = self._multi_point(point)
            participation = copy.deepcopy(participation)
            participation["officialCaptureId"] = 100
            with self.assertRaises(TemporalRidgeError) as context:
                _build_from_artifacts(
                    database,
                    multi_point,
                    participation,
                    screen,
                )
        self.assertEqual(
            "multi-scenario.source-alignment",
            context.exception.code,
        )

        with helper._database() as database:
            point, participation, screen = helper._artifacts(database)
            multi_point = self._multi_point(point)
            multi_point["players"][0]["gameweeks"].pop()
            with self.assertRaises(TemporalRidgeError) as context:
                _build_from_artifacts(
                    database,
                    multi_point,
                    participation,
                    screen,
                )
        self.assertEqual(
            "multi-scenario.point-gameweek-coverage",
            context.exception.code,
        )

    @staticmethod
    def _multi_point(single: dict) -> dict:
        players = []
        for player in single["players"]:
            means = [
                {
                    "gameweek": gameweek,
                    "expectedPoints": round(
                        player["expectedPoints"]
                        + (0.0 if gameweek == 1 else gameweek / 10.0),
                        6,
                    ),
                }
                for gameweek in range(1, 9)
            ]
            players.append(
                {
                    **{
                        key: player[key]
                        for key in (
                            "playerId",
                            "playerCode",
                            "webName",
                            "teamId",
                            "teamName",
                            "position",
                            "historicalIdentityStatus",
                        )
                    },
                    "gameweeks": means,
                    "horizons": [
                        {
                            "gameweekCount": horizon,
                            "throughGameweek": horizon,
                            "expectedPoints": round(
                                sum(
                                    row["expectedPoints"]
                                    for row in means[:horizon]
                                ),
                                6,
                            ),
                        }
                        for horizon in (3, 6, 8)
                    ],
                }
            )
        return {
            "artifactVersion": POINT_ARTIFACT_VERSION,
            "status": POINT_STATUS,
            "influencesAdvice": False,
            "seasonCode": single["seasonCode"],
            "openingGameweek": 1,
            "targetGameweeks": list(range(1, 9)),
            "decisionHorizons": [3, 6, 8],
            "officialCaptureId": single["officialCaptureId"],
            "deadlineUtc": single["deadlineUtc"],
            "decisionCutoffUtc": single["decisionCutoffUtc"],
            "modelKey": single["modelKey"],
            "runIdentitySha256": single["runIdentitySha256"],
            "training": single["training"],
            "players": players,
        }


if __name__ == "__main__":
    unittest.main()
