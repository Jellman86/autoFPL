from __future__ import annotations

import hashlib
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_multi_horizon_initial_squad import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    HORIZONS,
    POLICIES,
    STATUS,
    _build_from_scenario,
    _lower_tail_cvar,
)
from autofpl_analytics.current_multi_horizon_joint_scenarios import (  # noqa: E402
    ARTIFACT_VERSION as SCENARIO_ARTIFACT_VERSION,
    STATUS as SCENARIO_STATUS,
)


class CurrentMultiHorizonInitialSquadTests(unittest.TestCase):
    def test_global_policies_are_deterministic_legal_and_read_only(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            scenario = self._database_and_scenario(database)
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            first = _build_from_scenario(database, scenario)
            second = _build_from_scenario(database, scenario)
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, first["artifactVersion"])
        self.assertEqual(STATUS, first["status"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["influencesAdvice"])
        self.assertIsNone(first["recommendedPolicyKey"])
        self.assertEqual(
            len(HORIZONS) * len(POLICIES),
            len(first["policies"]),
        )
        for policy in first["policies"]:
            selection = policy["selection"]
            players = selection["players"]
            self.assertEqual(15, len(selection["playerIds"]))
            self.assertLessEqual(selection["budgetTenths"], 1000)
            self.assertEqual(
                {
                    "goalkeeper": 2,
                    "defender": 5,
                    "midfielder": 5,
                    "forward": 3,
                },
                {
                    position: sum(
                        player["position"] == position
                        for player in players
                    )
                    for position in (
                        "goalkeeper",
                        "defender",
                        "midfielder",
                        "forward",
                    )
                },
            )
            self.assertEqual(
                policy["horizonGameweeks"],
                len(selection["gameweeks"]),
            )
            self.assertEqual(
                policy["horizonGameweeks"],
                len(policy["exactScenarioScore"]["weekly"]),
            )
            self.assertEqual(0.0, policy["solver"]["mipGap"])
            clubs = {
                team_id: sum(
                    player["teamId"] == team_id for player in players
                )
                for team_id in {player["teamId"] for player in players}
            }
            self.assertLessEqual(max(clubs.values()), 3)
            for roles in selection["gameweeks"]:
                self.assertEqual(11, len(roles["startingPlayerIds"]))
                self.assertIn(
                    roles["captainPlayerId"],
                    roles["startingPlayerIds"],
                )
                self.assertIn(
                    roles["viceCaptainPlayerId"],
                    roles["startingPlayerIds"],
                )
                self.assertNotEqual(
                    roles["captainPlayerId"],
                    roles["viceCaptainPlayerId"],
                )
                self.assertEqual(3, len(roles["outfieldSubstitutePlayerIds"]))

    def test_fractional_lower_tail_cvar_is_exact(self) -> None:
        self.assertEqual(
            7.0 / 6.0,
            _lower_tail_cvar(
                np.asarray([1, 2, 10, 20, 30, 40], dtype=float)
            ),
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
