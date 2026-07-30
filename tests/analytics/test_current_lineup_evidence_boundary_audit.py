from __future__ import annotations

import sys
import unittest
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_lineup_evidence_boundary_audit import (  # noqa: E402,E501
    ARTIFACT_VERSION,
    _build_from_documents,
)


class CurrentLineupEvidenceBoundaryAuditTests(unittest.TestCase):
    def test_latest_revision_is_used_without_counting_consensus_twice(
        self,
    ) -> None:
        incumbent = self._incumbent()
        claims = [
            self._claim(
                1,
                1,
                "ffscout-predicted-lineups",
                "starts",
                "2026-07-30T00:00:00Z",
            ),
            self._claim(
                2,
                1,
                "ffscout-predicted-lineups",
                "does-not-start",
                "2026-07-30T01:00:00Z",
            ),
            self._claim(
                3,
                1,
                "straightred-lineup-consensus",
                "starts",
                "2026-07-30T01:00:00Z",
                probability=0.75,
            ),
            *[
                self._claim(
                    player_id + 3,
                    player_id,
                    "ffscout-predicted-lineups",
                    "starts",
                    "2026-07-30T01:00:00Z",
                )
                for player_id in range(2, 16)
            ],
        ]

        artifact = _build_from_documents(
            incumbent,
            claims,
            evidence_cutoff=datetime(
                2026,
                7,
                30,
                1,
                15,
                tzinfo=timezone.utc,
            ),
        )

        self.assertEqual(ARTIFACT_VERSION, artifact["artifactVersion"])
        self.assertFalse(artifact["influencesAdvice"])
        self.assertEqual(15, artifact["coverage"]["selectedPlayerCount"])
        self.assertEqual(
            1,
            artifact["coverage"]["categoricalConflictCount"],
        )
        self.assertEqual(
            [1],
            [
                row["playerId"]
                for row in artifact["selectionRiskPlayers"]
            ],
        )
        player = next(
            row for row in artifact["players"] if row["playerId"] == 1
        )
        self.assertEqual("conflicting", player["categoricalStartSignal"])
        self.assertEqual(2, len(player["latestClaims"]))
        self.assertNotIn(
            1,
            [row["claimId"] for row in player["latestClaims"]],
        )
        consensus = next(
            row
            for row in player["latestClaims"]
            if row["sourceKey"] == "straightred-lineup-consensus"
        )
        self.assertTrue(consensus["isDependentConsensus"])
        self.assertIsNone(player["modelStartProbability"])

    def test_predicted_xi_omission_is_risk_not_forecast_mutation(
        self,
    ) -> None:
        artifact = _build_from_documents(
            self._incumbent(),
            [
                self._claim(
                    player_id,
                    player_id,
                    "ffscout-predicted-lineups",
                    (
                        "does-not-start"
                        if player_id in (6, 11)
                        else "starts"
                    ),
                    "2026-07-30T01:00:00Z",
                )
                for player_id in range(1, 16)
            ],
            evidence_cutoff=datetime(
                2026,
                7,
                30,
                1,
                15,
                tzinfo=timezone.utc,
            ),
        )

        self.assertEqual(
            13,
            artifact["coverage"]["predictedStarterCount"],
        )
        self.assertEqual(
            2,
            artifact["coverage"]["predictedNonStarterCount"],
        )
        self.assertEqual(
            {6, 11},
            {
                row["playerId"]
                for row in artifact["selectionRiskPlayers"]
            },
        )
        self.assertEqual(
            (
                "retain-v2-without-uncalibrated-lineup-mutation-and-"
                "prioritise-start-substitute-zero-mixture"
            ),
            artifact["decision"],
        )

    @staticmethod
    def _incumbent() -> dict:
        return {
            "artifactVersion": "current-selected-opening-squad-shadow-v2",
            "runIdentitySha256": "a" * 64,
            "seasonCode": "2026-27",
            "openingGameweek": 1,
            "deadlineUtc": "2026-08-21T17:30:00Z",
            "decisionCutoffUtc": "2026-07-29T22:38:42Z",
            "officialCaptureId": 19,
            "selection": {
                "players": [
                    {
                        "playerId": player_id,
                        "webName": f"Player {player_id}",
                        "teamId": (player_id - 1) // 3 + 1,
                        "teamName": f"Team {(player_id - 1) // 3 + 1}",
                        "position": (
                            "goalkeeper"
                            if player_id <= 2
                            else "defender"
                            if player_id <= 7
                            else "midfielder"
                            if player_id <= 12
                            else "forward"
                        ),
                        "modelAppearanceProbability": 0.9,
                    }
                    for player_id in range(1, 16)
                ]
            },
        }

    @staticmethod
    def _claim(
        claim_id: int,
        player_id: int,
        source_key: str,
        start_status: str,
        available_at_utc: str,
        *,
        probability: float | None = None,
    ) -> dict:
        return {
            "claimId": claim_id,
            "status": "quarantined",
            "sourceKey": source_key,
            "availableAtUtc": available_at_utc,
            "contentSha256": f"{claim_id:064x}",
            "sourceRevision": claim_id,
            "playerId": player_id,
            "claimType": "start",
            "availabilityStatus": None,
            "startStatus": start_status,
            "forecastProbability": probability,
            "directness": "model-forecast",
            "sourceSpan": f"Player {player_id}: {start_status}",
            "extractionVersion": "test/v1",
            "duplicateClusterKey": f"{player_id:064x}",
            "claimContentSha256": f"{claim_id + 1000:064x}",
        }


if __name__ == "__main__":
    unittest.main()
