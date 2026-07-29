from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
ANALYTICS_ROOT = REPOSITORY_ROOT / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.current_initial_squad_candidate import (  # noqa: E402
    ARTIFACT_TYPE,
    OPTIMIZER_VERSION,
    build_current_initial_squad_candidate,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics.test_current_scenario_selection_score import (  # noqa: E402
    CurrentScenarioSelectionScoreTests,
)

create_score_database = CurrentScenarioSelectionScoreTests._create_database
del CurrentScenarioSelectionScoreTests


class CurrentInitialSquadCandidateTests(unittest.TestCase):
    def test_global_surrogate_is_deterministic_legal_and_non_serving(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / "autofpl.db"
            self._create_database(database)
            before = database.read_bytes()

            first = build_current_initial_squad_candidate(database)
            second = build_current_initial_squad_candidate(database)

            self.assertEqual(first, second)
            self.assertEqual(before, database.read_bytes())
            self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
            self.assertFalse(first["isPromoted"])
            self.assertFalse(first["influencesAdvice"])
            self.assertEqual(
                OPTIMIZER_VERSION,
                first["optimizer"]["optimizerVersion"],
            )
            self.assertEqual(
                "global-linear-surrogate-optimum",
                first["optimizer"]["status"],
            )
            self.assertEqual(19, first["candidatePoolCount"])
            self.assertLessEqual(first["budgetTenths"], 1000)
            selection = first["candidate"]["selection"]
            self.assertEqual(15, len(selection["playerIds"]))
            self.assertEqual(15, len(set(selection["playerIds"])))
            self.assertEqual(11, len(selection["startingPlayerIds"]))
            self.assertTrue(
                {16, 17, 18, 19}.issubset(selection["playerIds"])
            )
            self.assertGreater(
                first["candidateVsModel"]["meanPointsDelta"],
                0,
            )

            selected = {
                player["playerId"]: player
                for player in first["players"]
            }
            positions = [
                selected[player_id]["position"]
                for player_id in selection["playerIds"]
            ]
            self.assertEqual(2, positions.count("goalkeeper"))
            self.assertEqual(5, positions.count("defender"))
            self.assertEqual(5, positions.count("midfielder"))
            self.assertEqual(3, positions.count("forward"))
            team_counts = {}
            for player in selected.values():
                team_id = player["teamId"]
                team_counts[team_id] = (
                    team_counts.get(team_id, 0) + 1
                )
            self.assertLessEqual(max(team_counts.values()), 3)

    def test_missing_official_identity_and_existing_output_fail_closed(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            database = root / "autofpl.db"
            output = root / "candidate.json"
            self._create_database(database)

            self.assertEqual(
                0,
                main(
                    [
                        "--database",
                        str(database),
                        "--output",
                        str(output),
                    ]
                ),
            )
            self.assertEqual(
                1,
                main(
                    [
                        "--database",
                        str(database),
                        "--output",
                        str(output),
                    ]
                ),
            )

            with sqlite3.connect(database) as connection:
                connection.execute(
                    "DELETE FROM official_fpl_players WHERE player_id = 19;"
                )
            with self.assertRaises(TemporalRidgeError) as caught:
                build_current_initial_squad_candidate(database)
            self.assertEqual(
                "initial-squad.official-identity",
                caught.exception.code,
            )

    @staticmethod
    def _create_database(database: Path) -> None:
        create_score_database(database)
        positions = (
            ["goalkeeper"] * 2
            + ["defender"] * 5
            + ["midfielder"] * 5
            + ["forward"] * 3
        )
        alternatives = [
            (16, "goalkeeper"),
            (17, "defender"),
            (18, "midfielder"),
            (19, "forward"),
        ]
        with sqlite3.connect(database) as connection:
            row = connection.execute(
                """
                SELECT document_json
                FROM joint_scenario_shadow_artifacts
                WHERE scenario_artifact_id = 1;
                """
            ).fetchone()
            scenario = json.loads(row[0])
            for player in scenario["players"]:
                player_id = int(player["playerId"])
                player.update(
                    {
                        "webName": f"Player {player_id}",
                        "teamId": ((player_id - 1) % 10) + 1,
                        "teamName": (
                            f"Team {((player_id - 1) % 10) + 1}"
                        ),
                        "pointMean": player_id / 2,
                        "appearanceProbability": 0.9,
                        "pointHistoryIdentityStatus": (
                            "both-historical-seasons"
                        ),
                        "participationHistoryIdentityStatus": (
                            "stable-code-match"
                        ),
                    }
                )
            for player_id, position in alternatives:
                scenario["players"].append(
                    {
                        "columnIndex": len(scenario["players"]),
                        "playerId": player_id,
                        "webName": f"Player {player_id}",
                        "teamId": player_id,
                        "teamName": f"Team {player_id}",
                        "position": position,
                        "pointMean": 40.0,
                        "appearanceProbability": 0.95,
                        "pointHistoryIdentityStatus": (
                            "both-historical-seasons"
                        ),
                        "participationHistoryIdentityStatus": (
                            "stable-code-match"
                        ),
                    }
                )
            scenario["playerCount"] = len(scenario["players"])
            for points in scenario["pointRows"]:
                points.extend([40, 40, 40, 40])
            for played in scenario["playedRows"]:
                played.extend([True, True, True, True])
            scenario_json = json.dumps(
                scenario,
                separators=(",", ":"),
            )
            connection.execute(
                """
                UPDATE joint_scenario_shadow_artifacts
                SET document_json = ?, content_sha256 = ?;
                """,
                (
                    scenario_json,
                    hashlib.sha256(
                        scenario_json.encode("utf-8")
                    ).hexdigest(),
                ),
            )
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
            for player_id, position in enumerate(
                positions,
                start=1,
            ):
                connection.execute(
                    """
                    INSERT INTO official_fpl_players
                        (capture_id, player_id, team_id, position,
                         price_tenths, status, chance_next_round)
                    VALUES (16, ?, ?, ?, 50, 'a', NULL);
                    """,
                    (
                        player_id,
                        ((player_id - 1) % 10) + 1,
                        position,
                    ),
                )
            for player_id, position in alternatives:
                connection.execute(
                    """
                    INSERT INTO official_fpl_players
                        (capture_id, player_id, team_id, position,
                         price_tenths, status, chance_next_round)
                    VALUES (16, ?, ?, ?, 45, 'a', NULL);
                    """,
                    (player_id, player_id, position),
                )


if __name__ == "__main__":
    unittest.main()
