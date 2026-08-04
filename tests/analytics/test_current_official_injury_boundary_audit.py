from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import patch

import brotli

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_official_injury_boundary_audit import (
    ARTIFACT_VERSION,
    _build_from_documents,
    _load_source,
    _parse_source_payload,
    main,
)
from autofpl_analytics.temporal_ridge import TemporalRidgeError


class CurrentOfficialInjuryBoundaryAuditTests(unittest.TestCase):
    def test_resolved_and_unresolved_rows_are_audited_without_guessing(
        self,
    ) -> None:
        artifact = _build_from_documents(
            self._lineage(),
            self._snapshot(),
            self._source_rows(),
            self._claims(),
            self._official_players(),
            evidence_cutoff=self._cutoff(),
        )
        repeat = _build_from_documents(
            self._lineage(),
            self._snapshot(),
            self._source_rows(),
            self._claims(),
            self._official_players(),
            evidence_cutoff=self._cutoff(),
        )

        self.assertEqual(artifact, repeat)
        self.assertEqual(ARTIFACT_VERSION, artifact["artifactVersion"])
        self.assertFalse(artifact["influencesAdvice"])
        self.assertEqual(4, artifact["coverage"]["sourceRowCount"])
        self.assertEqual(3, artifact["coverage"]["resolvedRowCount"])
        self.assertEqual(1, artifact["coverage"]["unresolvedRowCount"])
        self.assertEqual(0.75, artifact["coverage"]["identityResolutionRate"])
        self.assertEqual(
            1,
            artifact["coverage"]["sourcePlaceholderInjuryRowCount"],
        )
        self.assertEqual(
            1,
            artifact["coverage"]["selectedListedPlayerCount"],
        )
        self.assertEqual(
            1,
            artifact["coverage"]["boundaryAlternativeListedPlayerCount"],
        )
        unresolved = artifact["unresolvedRows"][0]
        self.assertEqual("Former Player", unresolved["sourcePlayerName"])
        self.assertEqual("unresolved-in-scope-team", unresolved["scope"])
        self.assertIsNone(unresolved["resolvedPlayer"])
        self.assertIsNone(unresolved["claim"])
        self.assertEqual(
            {1, 2, 3, 16},
            {player["playerId"] for player in unresolved["scopeTeamPlayers"]},
        )
        selected = next(
            player for player in artifact["selectedPlayers"] if player["playerId"] == 1
        )
        self.assertEqual("listed-doubtful", selected["injuryListingStatus"])
        self.assertEqual(
            "selected-squad",
            selected["sourceRow"]["scope"],
        )
        not_listed = next(
            player for player in artifact["selectedPlayers"] if player["playerId"] == 2
        )
        self.assertEqual(
            "not-listed-in-source",
            not_listed["injuryListingStatus"],
        )

    def test_claim_must_bind_one_exact_raw_source_span(self) -> None:
        claims = self._claims()
        claims[0]["source_span"] = "Team 1 injury list: Unknown — Knee"

        with self.assertRaises(TemporalRidgeError) as captured:
            _build_from_documents(
                self._lineage(),
                self._snapshot(),
                self._source_rows(),
                claims,
                self._official_players(),
                evidence_cutoff=self._cutoff(),
            )

        self.assertEqual(
            "official-injury.claim-binding",
            captured.exception.code,
        )

    def test_source_payload_requires_complete_unique_safe_shape(self) -> None:
        payload = self._payload()
        rows = _parse_source_payload(
            json.dumps(payload, separators=(",", ":")).encode()
        )
        self.assertEqual(4, len(rows))

        payload["clubs"][1]["teamName"] = "Team 1"
        with self.assertRaises(TemporalRidgeError) as captured:
            _parse_source_payload(json.dumps(payload).encode())
        self.assertEqual(
            "official-injury.club-duplicate",
            captured.exception.code,
        )

    def test_source_loader_is_cutoff_bound_and_checks_integrity(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "audit.db"
            self._create_database(database)
            lineage = self._lineage()
            snapshot, rows, claims, players = _load_source(
                database,
                lineage,
                self._cutoff(),
            )
            self.assertEqual(9, snapshot["snapshot_id"])
            self.assertEqual(4, len(rows))
            self.assertEqual([], claims)
            self.assertEqual(16, len(players))

            with self.assertRaises(TemporalRidgeError) as waiting:
                _load_source(
                    database,
                    lineage,
                    datetime(2026, 8, 4, 9, tzinfo=timezone.utc),
                )
            self.assertEqual(
                "official-injury.source-not-found",
                waiting.exception.code,
            )

            connection = sqlite3.connect(database)
            connection.execute(
                "UPDATE research_source_snapshots SET content_bytes = content_bytes + 1"
            )
            connection.commit()
            connection.close()
            with self.assertRaises(TemporalRidgeError) as tampered:
                _load_source(
                    database,
                    lineage,
                    self._cutoff(),
                )
            self.assertEqual(
                "official-injury.source-integrity",
                tampered.exception.code,
            )

    @patch(
        "autofpl_analytics.current_official_injury_boundary_audit."
        "build_current_official_injury_boundary_audit"
    )
    def test_successful_cli_writes_output(
        self,
        build: unittest.mock.Mock,
    ) -> None:
        build.return_value = {"schemaVersion": "1.0", "status": "ok"}
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "result.json"
            code = main(
                [
                    "--database",
                    str(Path(temporary) / "source.db"),
                    "--evidence-cutoff-utc",
                    "2026-08-04T10:40:00Z",
                    "--output",
                    str(output),
                ]
            )
            self.assertEqual(0, code)
            self.assertEqual(
                build.return_value,
                json.loads(output.read_text()),
            )

    @staticmethod
    def _cutoff() -> datetime:
        return datetime(2026, 8, 4, 10, 40, tzinfo=timezone.utc)

    @staticmethod
    def _lineage() -> dict:
        players = [
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
            }
            for player_id in range(1, 16)
        ]
        return {
            "artifactVersion": "current-lineup-evidence-boundary-audit-v1.1",
            "dataIdentitySha256": "a" * 64,
            "runIdentitySha256": "b" * 64,
            "seasonCode": "2026-27",
            "openingGameweek": 1,
            "deadlineUtc": "2026-08-21T17:30:00Z",
            "officialCaptureId": 41,
            "players": players,
            "boundaryAlternatives": [
                {
                    "playerId": 16,
                    "webName": "Alternative",
                    "teamId": 1,
                    "teamName": "Team 1",
                    "position": "midfielder",
                    "boundaryForSelectedPlayerIds": [1],
                }
            ],
        }

    @staticmethod
    def _snapshot() -> dict:
        return {
            "snapshot_id": 9,
            "status": "shadow-only",
            "source_key": "premier-league-injuries",
            "source_class": "official-availability-aggregation",
            "canonical_url": (
                "https://www.premierleague.com/en/latest-player-injuries"
            ),
            "final_url": ("https://www.premierleague.com/en/latest-player-injuries"),
            "dependence_group": ("official-premier-league-and-club-reporting"),
            "transport_key": "playwright-mcp",
            "transport_version": "playwright-mcp/v0.0.78",
            "season_code": "2026-27",
            "gameweek": 1,
            "deadline_utc": "2026-08-21T17:30:00Z",
            "identity_capture_id": 41,
            "retrieved_at_utc": "2026-08-04T10:39:00Z",
            "available_at_utc": "2026-08-04T10:39:00Z",
            "source_revision": 35,
            "content_sha256": "c" * 64,
            "content_bytes": 100,
        }

    @staticmethod
    def _source_rows() -> list[dict]:
        values = [
            ("Team 1", "Player 1", "Knee"),
            ("Team 1", "Alternative", "Ankle"),
            ("Team 2", "Outside", "Back"),
            ("Team 1", "Former Player", "-"),
        ]
        return [
            {
                "clubIndex": index // 2,
                "rowIndex": index,
                "sourceTeamName": team,
                "sourcePlayerName": player,
                "injury": injury,
                "sourceSpan": f"{team} injury list: {player} — {injury}",
            }
            for index, (team, player, injury) in enumerate(values)
        ]

    @classmethod
    def _claims(cls) -> list[dict]:
        rows = cls._source_rows()
        return [
            cls._claim(1, 1, rows[0]["sourceSpan"]),
            cls._claim(2, 16, rows[1]["sourceSpan"]),
            cls._claim(3, 17, rows[2]["sourceSpan"]),
        ]

    @staticmethod
    def _claim(claim_id: int, player_id: int, source_span: str) -> dict:
        return {
            "claim_id": claim_id,
            "status": "quarantined",
            "source_key": "premier-league-injuries",
            "available_at_utc": "2026-08-04T10:39:00Z",
            "content_sha256": "c" * 64,
            "source_revision": 35,
            "season_code": "2026-27",
            "gameweek": 1,
            "deadline_utc": "2026-08-21T17:30:00Z",
            "identity_capture_id": 41,
            "player_id": player_id,
            "claim_type": "availability",
            "availability_status": "doubtful",
            "start_status": None,
            "forecast_probability": None,
            "directness": "reported",
            "source_span": source_span,
            "extraction_method": "deterministic",
            "extraction_version": "premier-league-injuries/v1",
            "extraction_confidence": "1",
            "duplicate_cluster_key": f"{player_id:064x}",
            "claim_content_sha256": f"{claim_id:064x}",
        }

    @staticmethod
    def _official_players() -> list[dict]:
        result = []
        for player_id in range(1, 18):
            team_id = 1 if player_id in {1, 2, 3, 16} else 2
            result.append(
                {
                    "player_id": player_id,
                    "code": 1000 + player_id,
                    "team_id": team_id,
                    "team_name": f"Team {team_id}",
                    "position": "midfielder",
                    "first_name": f"First {player_id}",
                    "second_name": f"Last {player_id}",
                    "web_name": (
                        "Alternative"
                        if player_id == 16
                        else "Outside"
                        if player_id == 17
                        else f"Player {player_id}"
                    ),
                    "status": "a",
                }
            )
        return result

    @classmethod
    def _payload(cls) -> dict:
        clubs = [{"teamName": f"Team {index}", "rows": []} for index in range(1, 21)]
        for source_row in cls._source_rows():
            team_id = int(source_row["sourceTeamName"].split()[-1])
            clubs[team_id - 1]["rows"].append(
                {
                    "playerName": source_row["sourcePlayerName"],
                    "injury": source_row["injury"],
                    "updateUrl": None,
                }
            )
        return {
            "schemaVersion": "premier-league-injury-dom/v1",
            "sourceUrl": ("https://www.premierleague.com/en/latest-player-injuries"),
            "pageTitle": "Latest player injuries",
            "renderedWidgetSha256": "d" * 64,
            "clubs": clubs,
        }

    @classmethod
    def _create_database(cls, path: Path) -> None:
        content = json.dumps(
            cls._payload(),
            separators=(",", ":"),
            sort_keys=True,
        ).encode()
        connection = sqlite3.connect(path)
        connection.executescript(
            """
            CREATE TABLE schema_migrations (version INTEGER);
            INSERT INTO schema_migrations VALUES (41);
            CREATE TABLE research_source_snapshots (
                snapshot_id INTEGER PRIMARY KEY,
                status TEXT,
                source_key TEXT,
                source_class TEXT,
                canonical_url TEXT,
                final_url TEXT,
                dependence_group TEXT,
                transport_key TEXT,
                transport_version TEXT,
                season_code TEXT,
                gameweek INTEGER,
                deadline_utc TEXT,
                identity_capture_id INTEGER,
                retrieved_at_utc TEXT,
                available_at_utc TEXT,
                source_revision INTEGER,
                content_sha256 TEXT,
                content_bytes INTEGER,
                content_brotli BLOB
            );
            CREATE TABLE evidence_claims (
                claim_id INTEGER,
                status TEXT,
                source_key TEXT,
                available_at_utc TEXT,
                content_sha256 TEXT,
                source_revision INTEGER,
                season_code TEXT,
                gameweek INTEGER,
                deadline_utc TEXT,
                identity_capture_id INTEGER,
                player_id INTEGER,
                claim_type TEXT,
                availability_status TEXT,
                start_status TEXT,
                forecast_probability TEXT,
                directness TEXT,
                source_span TEXT,
                extraction_method TEXT,
                extraction_version TEXT,
                extraction_confidence TEXT,
                duplicate_cluster_key TEXT,
                claim_content_sha256 TEXT
            );
            CREATE TABLE official_fpl_teams (
                capture_id INTEGER,
                team_id INTEGER,
                name TEXT
            );
            CREATE TABLE official_fpl_players (
                capture_id INTEGER,
                player_id INTEGER,
                code INTEGER,
                team_id INTEGER,
                position TEXT,
                first_name TEXT,
                second_name TEXT,
                web_name TEXT,
                status TEXT
            );
            """
        )
        snapshot = cls._snapshot()
        connection.execute(
            """
            INSERT INTO research_source_snapshots VALUES (
                ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
            )
            """,
            (
                snapshot["snapshot_id"],
                snapshot["status"],
                snapshot["source_key"],
                snapshot["source_class"],
                snapshot["canonical_url"],
                snapshot["final_url"],
                snapshot["dependence_group"],
                snapshot["transport_key"],
                snapshot["transport_version"],
                snapshot["season_code"],
                snapshot["gameweek"],
                snapshot["deadline_utc"],
                snapshot["identity_capture_id"],
                snapshot["retrieved_at_utc"],
                snapshot["available_at_utc"],
                snapshot["source_revision"],
                hashlib.sha256(content).hexdigest(),
                len(content),
                brotli.compress(content),
            ),
        )
        teams = sorted(
            {
                (41, player["team_id"], player["team_name"])
                for player in cls._official_players()
            }
        )
        connection.executemany(
            "INSERT INTO official_fpl_teams VALUES (?, ?, ?)",
            teams,
        )
        connection.executemany(
            "INSERT INTO official_fpl_players VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [
                (
                    41,
                    player["player_id"],
                    player["code"],
                    player["team_id"],
                    player["position"],
                    player["first_name"],
                    player["second_name"],
                    player["web_name"],
                    player["status"],
                )
                for player in cls._official_players()[:16]
            ],
        )
        connection.commit()
        connection.close()


if __name__ == "__main__":
    unittest.main()
