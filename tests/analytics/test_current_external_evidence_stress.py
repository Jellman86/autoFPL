from __future__ import annotations

import hashlib
import sqlite3
import sys
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_appearance_hurdle_player_forecast import (  # noqa: E402
    HISTORICAL_EVALUATION_DATA_IDENTITY,
    HISTORICAL_EVALUATION_RUN_IDENTITY,
)
from autofpl_analytics.current_external_evidence_stress import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    STATUS,
    STRESS_METHOD,
    _build_from_scenario_and_claims,
)
from autofpl_analytics.current_multi_horizon_initial_squad import (  # noqa: E402
    HORIZONS,
    _build_candidates,
    _optimise_horizon,
    _week_matrices,
)
from autofpl_analytics.current_multi_horizon_joint_scenarios import (  # noqa: E402
    ARTIFACT_VERSION as SCENARIO_ARTIFACT_VERSION,
    STATUS as SCENARIO_STATUS,
)


class CurrentExternalEvidenceStressTests(unittest.TestCase):
    def test_external_claims_create_deterministic_non_serving_stresses(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            scenario = self._database_and_scenario(database)
            candidates = _build_candidates(database, scenario)
            incumbent, _ = _optimise_horizon(
                candidates,
                _week_matrices(scenario),
                6,
                0.0,
            )
            incumbent_id = int(incumbent["playerIds"][0])
            nonselected_id = next(
                int(player["playerId"])
                for player in candidates
                if int(player["playerId"])
                not in set(incumbent["playerIds"])
            )
            claims = [
                self._claim(
                    1,
                    "ffscout-predicted-lineups",
                    incumbent_id,
                ),
                self._claim(
                    2,
                    "premier-league-injuries",
                    incumbent_id,
                    claim_type="availability",
                ),
                self._claim(
                    3,
                    "straightred-lineup-consensus",
                    nonselected_id,
                    probability=0.25,
                ),
            ]
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            first = _build_from_scenario_and_claims(
                database,
                scenario,
                claims,
                evidence_cutoff=datetime(
                    2026,
                    8,
                    21,
                    12,
                    tzinfo=timezone.utc,
                ),
            )
            second = _build_from_scenario_and_claims(
                database,
                scenario,
                claims,
                evidence_cutoff=datetime(
                    2026,
                    8,
                    21,
                    12,
                    tzinfo=timezone.utc,
                ),
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, first["artifactVersion"])
        self.assertEqual(STATUS, first["status"])
        self.assertEqual(STRESS_METHOD, first["stressMethod"]["method"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["influencesAdvice"])
        self.assertEqual(3, first["coverage"]["adverseClaimCount"])
        self.assertEqual(
            1,
            first["coverage"]["selectedAdversePlayerCount"],
        )
        self.assertEqual(64, len(first["dataIdentitySha256"]))
        self.assertEqual(64, len(first["runIdentitySha256"]))

        selected = next(
            row
            for row in first["stressScenarios"]
            if row["scenarioKey"]
            == f"selected-player-{incumbent_id}"
        )
        self.assertEqual(
            [
                "ffscout-predicted-lineups",
                "premier-league-injuries",
            ],
            selected["sourceKeys"],
        )
        self.assertEqual(1, selected["affectedPlayerCount"])
        self.assertEqual(15, len(selected["selectionPlayerIds"]))
        self.assertGreaterEqual(
            len(
                selected[
                    "unappliedAdverseEvidenceForAddedPlayers"
                ]
            ),
            0,
        )
        self.assertIn(
            selected["stressDecision"],
            {
                "consider-alternative-if-source-trusted",
                "retain-incumbent",
            },
        )

        independent = next(
            row
            for row in first["stressScenarios"]
            if row["scenarioKey"] == "all-independent-sources"
        )
        self.assertNotIn(
            "straightred-lineup-consensus",
            independent["sourceKeys"],
        )
        dependent = next(
            row
            for row in first["stressScenarios"]
            if row["scenarioKey"]
            == "source-wide-straightred-lineup-consensus"
        )
        self.assertEqual(
            [nonselected_id],
            dependent["affectedPlayerIds"],
        )

    @staticmethod
    def _claim(
        claim_id: int,
        source_key: str,
        player_id: int,
        *,
        claim_type: str = "start",
        probability: float | None = None,
    ) -> dict:
        return {
            "claimId": claim_id,
            "sourceKey": source_key,
            "claimType": claim_type,
            "availableAtUtc": "2026-08-21T12:00:00Z",
            "startStatus": (
                "does-not-start" if claim_type == "start" else None
            ),
            "availabilityStatus": (
                "doubtful" if claim_type == "availability" else None
            ),
            "forecastProbability": probability,
            "claimContentSha256": f"{claim_id:064x}",
            "duplicateClusterKey": f"{player_id:064x}",
            "playerId": player_id,
            "webName": f"Player {player_id}",
            "teamId": (player_id - 1) % 10 + 1,
            "teamName": f"Team {(player_id - 1) % 10 + 1}",
            "position": "unknown",
        }

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
                points = [
                    1 + (index * 3 + gameweek + path * 2) % 9
                    for index in range(len(players))
                ]
                played = [
                    not (
                        path == 0 and (index + gameweek) % 11 == 0
                    )
                    for index in range(len(players))
                ]
                for index, did_play in enumerate(played):
                    if not did_play:
                        points[index] = 0
                point_rows.append(points)
                played_rows.append(played)
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
            "variant": {
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
            },
        }


if __name__ == "__main__":
    unittest.main()
