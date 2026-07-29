from __future__ import annotations

import copy
import sqlite3
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_joint_scenario_forecast import (  # noqa: E402
    APPEARANCE_VARIANT,
    ARTIFACT_TYPE,
    POINT_AVAILABILITY_FUSION,
    RAW_APPEARANCE_VARIANT,
    SCREEN_DATA_IDENTITY,
    SCREEN_RUN_IDENTITY,
    STATUS,
    _build_from_artifacts,
    _retained_screen,
)
from autofpl_analytics.historical_joint_scenario_evaluation import (  # noqa: E402
    EVALUATOR_VERSION,
    MODEL_NAME,
)
from autofpl_analytics.multi_season_player_forecast import (  # noqa: E402
    STATUS as POINT_STATUS,
)
from autofpl_analytics.preseason_participation_forecast import (  # noqa: E402
    STATUS as PARTICIPATION_STATUS,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics import (  # noqa: E402
    test_historical_preseason_evaluation as historical_helpers,
)


class CurrentJointScenarioForecastTests(unittest.TestCase):
    def test_current_generation_binds_the_retained_screen(self) -> None:
        screen = _retained_screen()

        self.assertEqual("complete", screen["status"])
        self.assertEqual(
            "passes-retrospective-screen",
            screen["retrospectiveScreen"]["status"],
        )
        self.assertEqual(
            SCREEN_DATA_IDENTITY,
            screen["dataIdentitySha256"],
        )
        self.assertEqual(
            SCREEN_RUN_IDENTITY,
            screen["runIdentitySha256"],
        )
        self.assertFalse(screen["isPromoted"])
        self.assertFalse(screen["mayInfluenceAdvice"])

    def test_current_shadow_is_deterministic_aligned_and_non_serving(
        self,
    ) -> None:
        with self._database() as database:
            point, participation, screen = self._artifacts(database)
            first = _build_from_artifacts(
                database,
                point,
                participation,
                screen,
            )
            second = _build_from_artifacts(
                database,
                point,
                participation,
                screen,
            )

        self.assertEqual(first, second)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual(STATUS, first["status"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["influencesAdvice"])
        self.assertEqual(12, first["scenarioCount"])
        self.assertEqual(2, first["playerCount"])
        self.assertEqual(
            POINT_AVAILABILITY_FUSION,
            first["pointAvailabilityFusion"],
        )
        self.assertEqual(12, len(first["pointRows"]))
        self.assertEqual(12, len(first["playedRows"]))
        self.assertTrue(first["scenarioContentSha256"])
        self.assertEqual(
            0,
            first["scenarioDiagnostics"][
                "nonPlayingNonZeroPointCount"
            ],
        )
        for point_row, played_row in zip(
            first["pointRows"],
            first["playedRows"],
        ):
            self.assertEqual(2, len(point_row))
            self.assertEqual(2, len(played_row))
            for points, played in zip(point_row, played_row):
                if not played:
                    self.assertEqual(0, points)

    def test_misaligned_current_players_fail_closed(self) -> None:
        with self._database() as database:
            point, participation, screen = self._artifacts(database)
            participation = copy.deepcopy(participation)
            participation["players"][0]["playerCode"] = 9999
            with self.assertRaises(TemporalRidgeError) as caught:
                _build_from_artifacts(
                    database,
                    point,
                    participation,
                    screen,
                )

        self.assertEqual(
            "scenario.current-player-identity",
            caught.exception.code,
        )

    def test_missing_candidate_screen_fails_closed(self) -> None:
        with self._database() as database:
            point, participation, screen = self._artifacts(database)
            screen = copy.deepcopy(screen)
            screen["distributionModels"] = []
            with self.assertRaises(TemporalRidgeError) as caught:
                _build_from_artifacts(
                    database,
                    point,
                    participation,
                    screen,
                )

        self.assertEqual(
            "scenario.retrospective-screen-model",
            caught.exception.code,
        )

    def _artifacts(self, database: Path):
        with sqlite3.connect(database) as connection:
            row = connection.execute(
                """
                SELECT capture_id, season_code, players_sha256,
                       gameweeks_sha256
                FROM historical_fpl_season_captures
                WHERE season_code = '2025-26';
                """
            ).fetchone()
        assert row is not None
        capture_id, source_season, players_sha, gameweeks_sha = row
        cutoff = "2026-07-20T12:00:00+00:00"
        common_players = [
            {
                "playerId": 1,
                "playerCode": 1001,
                "webName": "Alpha",
                "teamId": 1,
                "teamName": "One",
                "position": "midfielder",
            },
            {
                "playerId": 2,
                "playerCode": 1002,
                "webName": "Beta",
                "teamId": 2,
                "teamName": "Two",
                "position": "midfielder",
            },
        ]
        point_players = [
            {
                **player,
                "expectedPoints": 3.25 + index,
                "historicalIdentityStatus": (
                    "latest-historical-season-only"
                ),
            }
            for index, player in enumerate(common_players)
        ]
        participation_players = [
            {
                **player,
                "officialStatus": "a",
                "officialChanceOfPlayingNextRound": None,
                "priorSeasonIdentityStatus": "stable-code-match",
                "variants": {
                    RAW_APPEARANCE_VARIANT: {
                        "appearanceProbability": probability,
                    },
                    APPEARANCE_VARIANT: {
                        "appearanceProbability": probability,
                    }
                },
            }
            for player, probability in zip(
                common_players,
                (0.75, 0.5),
            )
        ]
        historical_capture = {
            "captureId": capture_id,
            "seasonCode": source_season,
            "playersSha256": players_sha,
            "gameweeksSha256": gameweeks_sha,
        }
        point = {
            "status": POINT_STATUS,
            "influencesAdvice": False,
            "seasonCode": "2026-27",
            "gameweek": 1,
            "officialCaptureId": 99,
            "deadlineUtc": "2026-08-15T10:00:00+00:00",
            "decisionCutoffUtc": cutoff,
            "modelKey": "test-point-model",
            "runIdentitySha256": "a" * 64,
            "training": {
                "historicalCaptures": [historical_capture],
            },
            "players": point_players,
        }
        participation = {
            "status": PARTICIPATION_STATUS,
            "influencesAdvice": False,
            "seasonCode": "2026-27",
            "gameweek": 1,
            "officialCaptureId": 99,
            "decisionCutoffUtc": cutoff.replace("+00:00", "Z"),
            "runIdentitySha256": "b" * 64,
            "training": {
                "seasonCode": source_season,
                "historicalCaptureId": capture_id,
                "playersSha256": players_sha,
                "gameweeksSha256": gameweeks_sha,
            },
            "players": participation_players,
        }
        screen = {
            "evaluatorVersion": EVALUATOR_VERSION,
            "status": "complete",
            "isPromoted": False,
            "mayInfluenceAdvice": False,
            "dataIdentitySha256": "c" * 64,
            "runIdentitySha256": "d" * 64,
            "distributionModels": [
                {
                    "name": MODEL_NAME,
                    "metrics": {"meanCrps": 0.7},
                }
            ],
            "retrospectiveScreen": {
                "status": "passes-retrospective-screen",
                "aggregateCrpsImprovementFraction": 0.1,
                "foldWins": 8,
                "foldCount": 8,
            },
        }
        return point, participation, screen

    def _database(self):
        helper = (
            historical_helpers.HistoricalPreseasonEvaluationTests()
        )
        return helper._database()


if __name__ == "__main__":
    unittest.main()
