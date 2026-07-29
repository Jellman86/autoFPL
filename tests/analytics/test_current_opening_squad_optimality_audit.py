from __future__ import annotations

import hashlib
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_multi_horizon_initial_squad import (  # noqa: E402
    HORIZONS,
    _build_candidates,
    _optimise_horizon,
    _week_matrices,
)
from autofpl_analytics.current_appearance_hurdle_opening_optimality_audit import (  # noqa: E402
    ARTIFACT_VERSION as HURDLE_AUDIT_ARTIFACT_VERSION,
    _build_from_scenario as _build_hurdle_audit,
)
from autofpl_analytics.current_appearance_hurdle_player_forecast import (  # noqa: E402
    HISTORICAL_EVALUATION_DATA_IDENTITY,
    HISTORICAL_EVALUATION_RUN_IDENTITY,
)
from autofpl_analytics.current_multi_horizon_joint_scenarios import (  # noqa: E402
    ARTIFACT_VERSION as SCENARIO_ARTIFACT_VERSION,
    STATUS as SCENARIO_STATUS,
)
from autofpl_analytics.current_opening_squad_optimality_audit import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    BOOTSTRAP_METHOD,
    STATUS,
    _build_from_scenario,
)
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402


class CurrentOpeningSquadOptimalityAuditTests(unittest.TestCase):
    def test_audit_is_deterministic_read_only_and_conditionally_exact(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            scenario = self._database_and_scenario(database)
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            first = _build_from_scenario(
                database,
                scenario,
                bootstrap_replicates=4,
            )
            second = _build_from_scenario(
                database,
                scenario,
                bootstrap_replicates=4,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, first["artifactVersion"])
        self.assertEqual(STATUS, first["status"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["influencesAdvice"])
        self.assertEqual(
            15,
            len(first["selectedPlayerExclusionAudits"]),
        )
        incumbent_ids = set(
            first["incumbent"]["selection"]["playerIds"]
        )
        self.assertEqual(15, len(incumbent_ids))
        self.assertLessEqual(
            first["bestDistinctSquad"][
                "incumbentPlayerOverlapCount"
            ],
            14,
        )
        self.assertGreaterEqual(
            first["bestDistinctSquad"][
                "surrogateObjectiveRegretPoints"
            ],
            0.0,
        )
        for row in first["selectedPlayerExclusionAudits"]:
            excluded_id = row["excludedPlayer"]["playerId"]
            self.assertNotIn(
                excluded_id,
                row["conditionalSelectionPlayerIds"],
            )
            self.assertGreaterEqual(
                row["surrogateObjectiveRegretPoints"],
                0.0,
            )
            self.assertTrue(row["addedPlayers"])
        bootstrap = first["scenarioPathBootstrap"]
        self.assertEqual(BOOTSTRAP_METHOD, bootstrap["method"])
        self.assertEqual(4, bootstrap["replicateCount"])
        self.assertGreaterEqual(bootstrap["uniqueSquadCount"], 1)
        self.assertEqual(
            60,
            sum(
                row["selectionCount"]
                for row in bootstrap["playerSelectionFrequencies"]
            ),
        )

    def test_conditional_optimizer_rejects_conflicts(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            scenario = self._database_and_scenario(database)
            candidates = _build_candidates(database, scenario)
            matrices = _week_matrices(scenario)

            with self.assertRaises(TemporalRidgeError) as caught:
                _optimise_horizon(
                    candidates,
                    matrices,
                    6,
                    0.0,
                    required_player_ids=(1,),
                    excluded_player_ids=(1,),
                )

        self.assertEqual(
            "multi-squad.conditional-conflict",
            caught.exception.code,
        )

    def test_hurdle_v2_audit_requires_exact_model_lineage(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            scenario = self._database_and_scenario(database)
            scenario["variant"] = {
                "variantKey": "appearance-hurdle-points",
                "pointModelKey": (
                    "multi-season-appearance-hurdle-points"
                ),
                "historicalEvaluation": {
                    "evaluatorVersion": (
                        "historical-appearance-hurdle-points-"
                        "evaluation-v1"
                    ),
                    "dataIdentitySha256": (
                        HISTORICAL_EVALUATION_DATA_IDENTITY
                    ),
                    "runIdentitySha256": (
                        HISTORICAL_EVALUATION_RUN_IDENTITY
                    ),
                    "decision": (
                        "retain-appearance-hurdle-prospective-shadow"
                    ),
                },
            }

            first = _build_hurdle_audit(
                database,
                scenario,
                bootstrap_replicates=2,
            )
            second = _build_hurdle_audit(
                database,
                scenario,
                bootstrap_replicates=2,
            )
            scenario["variant"]["historicalEvaluation"][
                "dataIdentitySha256"
            ] = "0" * 64
            with self.assertRaises(TemporalRidgeError) as caught:
                _build_hurdle_audit(
                    database,
                    scenario,
                    bootstrap_replicates=2,
                )

        self.assertEqual(first, second)
        self.assertEqual(
            HURDLE_AUDIT_ARTIFACT_VERSION,
            first["artifactVersion"],
        )
        self.assertEqual(
            "appearance-hurdle-points",
            first["modelVariant"]["variantKey"],
        )
        self.assertEqual(
            "historical-appearance-hurdle-opening-policy-evaluation-v1",
            first["modelVariant"]["openingPolicyEvaluationSource"][
                "artifactVersion"
            ],
        )
        self.assertEqual(
            "hurdle-opening-audit.scenario",
            caught.exception.code,
        )

    @staticmethod
    def _database_and_scenario(database: Path) -> dict:
        positions = (
            ["goalkeeper"] * 3
            + ["defender"] * 7
            + ["midfielder"] * 7
            + ["forward"] * 5
        )
        players = []
        with sqlite3.connect(database) as connection:
            connection.execute(
                """
                CREATE TABLE official_fpl_players (
                    capture_id INTEGER NOT NULL,
                    player_id INTEGER NOT NULL,
                    team_id INTEGER NOT NULL,
                    position TEXT NOT NULL,
                    price_tenths INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    chance_next_round INTEGER
                );
                """
            )
            for index, position in enumerate(positions):
                player_id = index + 1
                team_id = index % 10 + 1
                players.append(
                    {
                        "columnIndex": index,
                        "playerId": player_id,
                        "playerCode": 10_000 + player_id,
                        "webName": f"Player {player_id}",
                        "teamId": team_id,
                        "teamName": f"Team {team_id}",
                        "position": position,
                    }
                )
                connection.execute(
                    """
                    INSERT INTO official_fpl_players
                    VALUES (18, ?, ?, ?, ?, 'a', NULL);
                    """,
                    (
                        player_id,
                        team_id,
                        position,
                        40 + index % 6,
                    ),
                )
        scenario_count = 5
        weeks = []
        for gameweek in range(1, 9):
            point_rows = []
            played_rows = []
            for path in range(scenario_count):
                point_rows.append(
                    [
                        1 + (index * 3 + gameweek + path * 2) % 9
                        for index in range(len(players))
                    ]
                )
                played_rows.append(
                    [
                        not (
                            path == 0
                            and (index + gameweek) % 11 == 0
                        )
                        for index in range(len(players))
                    ]
                )
                for index, played in enumerate(played_rows[-1]):
                    if not played:
                        point_rows[-1][index] = 0
            weeks.append(
                {
                    "gameweek": gameweek,
                    "pointRows": point_rows,
                    "playedRows": played_rows,
                }
            )
        return {
            "artifactVersion": SCENARIO_ARTIFACT_VERSION,
            "status": SCENARIO_STATUS,
            "influencesAdvice": False,
            "seasonCode": "2026-27",
            "openingGameweek": 1,
            "targetGameweeks": list(range(1, 9)),
            "decisionHorizons": list(HORIZONS),
            "deadlineUtc": "2026-08-21T17:30:00+00:00",
            "decisionCutoffUtc": "2026-07-29T16:38:42+00:00",
            "officialCaptureId": 18,
            "scenarioCount": scenario_count,
            "playerCount": len(players),
            "players": players,
            "weeks": weeks,
            "scenarioContentSha256": "a" * 64,
            "runIdentitySha256": "b" * 64,
        }


if __name__ == "__main__":
    unittest.main()
