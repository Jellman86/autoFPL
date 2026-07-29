from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_selected_opening_squad import (  # noqa: E402
    _build_from_scenario,
)
from autofpl_analytics.selected_opening_squad_outcome_evaluation import (  # noqa: E402
    ARTIFACT_TYPE,
    DIAGNOSTIC_BENCHMARK_KEY,
    PRIMARY_BENCHMARK_KEY,
    REGISTRATION_DATA_IDENTITY,
    build_selected_opening_squad_outcome_evaluation,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics import (  # noqa: E402
    test_current_multi_horizon_initial_squad as multi_squad_fixture,
)


class SelectedOpeningSquadOutcomeEvaluationTests(unittest.TestCase):
    def test_registration_identity_is_canonical_and_bound(self) -> None:
        path = (
            ROOT
            / "docs"
            / "research"
            / "results"
            / "selected-opening-squad-prospective-registration-v1.json"
        )
        registration = json.loads(path.read_text(encoding="utf-8"))
        identity = registration.pop("dataIdentitySha256")
        encoded = json.dumps(
            registration,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
        self.assertEqual(
            REGISTRATION_DATA_IDENTITY,
            hashlib.sha256(encoded).hexdigest(),
        )
        self.assertEqual(REGISTRATION_DATA_IDENTITY, identity)

    def test_partial_outcomes_are_scored_without_a_promotion_decision(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            selected = self._create_database(database)
            self._insert_outcomes(database, selected, range(1, 3))
            before = hashlib.sha256(database.read_bytes()).hexdigest()

            first = build_selected_opening_squad_outcome_evaluation(
                database
            )
            second = build_selected_opening_squad_outcome_evaluation(
                database
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual("prospective-outcome-partial", first["status"])
        self.assertFalse(first["isPromotionDecision"])
        self.assertEqual(
            [1, 2],
            first["promotionEvidenceGate"]["observedOutcomeGameweeks"],
        )
        self.assertEqual(
            "waiting-for-eight-outcomes",
            first["promotionEvidenceGate"]["status"],
        )
        self.assertIsNone(
            first["promotionEvidenceGate"]["checks"][
                "primaryBenchmarkDeltaAtLeastTwoPoints"
            ]
        )
        self.assertFalse(first["promotionEvidenceGate"]["passes"])
        self.assertEqual(
            2,
            first["realisedScore"]["cumulative"][
                "observedGameweekCount"
            ],
        )

    def test_complete_outcomes_apply_the_frozen_gate(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            selected = self._create_database(database)
            self._insert_outcomes(database, selected, range(1, 9))

            result = build_selected_opening_squad_outcome_evaluation(
                database
            )

        self.assertEqual("prospective-outcome-complete", result["status"])
        self.assertEqual(
            list(range(1, 9)),
            result["promotionEvidenceGate"][
                "observedOutcomeGameweeks"
            ],
        )
        self.assertTrue(
            result["promotionEvidenceGate"]["checks"][
                "allEightOutcomesAvailable"
            ]
        )
        self.assertTrue(
            result["promotionEvidenceGate"]["checks"][
                "primaryBenchmarkDeltaAtLeastTwoPoints"
            ]
        )
        self.assertTrue(
            result["promotionEvidenceGate"]["checks"][
                "selectedScoreAtOrAbovePreseasonP10"
            ]
        )
        self.assertTrue(result["promotionEvidenceGate"]["passes"])
        cumulative = result["realisedScore"]["cumulative"]
        self.assertGreater(cumulative["primaryBenchmarkDelta"], 2)
        self.assertIn("diagnosticBenchmarkDelta", cumulative)
        self.assertEqual(
            PRIMARY_BENCHMARK_KEY,
            result["promotionEvidenceGate"]["primaryBenchmarkKey"],
        )
        self.assertEqual(
            DIAGNOSTIC_BENCHMARK_KEY,
            result["benchmarkSource"]["diagnosticBenchmarkKey"],
        )

    def test_missing_outcomes_wait_and_tampering_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            database = root / "autofpl.db"
            output = root / "evaluation.json"
            self._create_database(database)

            with self.assertRaises(TemporalRidgeError) as waiting:
                build_selected_opening_squad_outcome_evaluation(database)
            self.assertEqual("outcome.not-found", waiting.exception.code)
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
            self.assertFalse(output.exists())

            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE selected_opening_squad_shadow_artifacts
                    SET content_sha256 = ?;
                    """,
                    ("f" * 64,),
                )
            with self.assertRaises(TemporalRidgeError) as tampered:
                build_selected_opening_squad_outcome_evaluation(database)
            self.assertEqual(
                "selected-opening.content",
                tampered.exception.code,
            )

    @staticmethod
    def _create_database(database: Path) -> dict:
        scenario = (
            multi_squad_fixture.CurrentMultiHorizonInitialSquadTests
            ._database_and_scenario(database)
        )
        selected = _build_from_scenario(database, scenario)
        selected_json = json.dumps(
            selected,
            separators=(",", ":"),
        )
        selected_hash = hashlib.sha256(
            selected_json.encode("utf-8")
        ).hexdigest()
        roles = selected["selection"]["gameweeks"][0]
        player_ids = list(selected["selection"]["playerIds"])
        position_by_id = {
            int(player["playerId"]): str(player["position"])
            for player in selected["selection"]["players"]
        }
        selection = {
            "playerIds": player_ids,
            "positions": [
                position_by_id[player_id] for player_id in player_ids
            ],
            "startingPlayerIds": list(roles["startingPlayerIds"]),
            "replacementGoalkeeperPlayerId": roles[
                "replacementGoalkeeperPlayerId"
            ],
            "outfieldSubstitutePlayerIds": list(
                roles["outfieldSubstitutePlayerIds"]
            ),
            "captainPlayerId": roles["captainPlayerId"],
            "viceCaptainPlayerId": roles["viceCaptainPlayerId"],
        }
        diagnostic = dict(selection)
        diagnostic["captainPlayerId"] = selection["viceCaptainPlayerId"]
        diagnostic["viceCaptainPlayerId"] = selection["captainPlayerId"]
        benchmark = {
            "schemaVersion": "1.0",
            "artifactType": "current-initial-squad-quality-shadow",
            "artifactVersion": "current-initial-squad-quality-shadow-v1",
            "status": "prospective-shadow-unscored",
            "isPromoted": False,
            "influencesAdvice": False,
            "seasonCode": "2026-27",
            "gameweek": 1,
            "officialCaptureId": 18,
            "model": {"selection": selection},
            "candidate": {"selection": diagnostic},
        }
        benchmark_json = json.dumps(
            benchmark,
            separators=(",", ":"),
        )
        benchmark_hash = hashlib.sha256(
            benchmark_json.encode("utf-8")
        ).hexdigest()
        with sqlite3.connect(database) as connection:
            connection.executescript(
                """
                CREATE TABLE selected_opening_squad_shadow_artifacts (
                    selected_opening_squad_artifact_id INTEGER PRIMARY KEY,
                    official_capture_id INTEGER NOT NULL,
                    season_code TEXT NOT NULL,
                    opening_gameweek INTEGER NOT NULL,
                    decision_cutoff_utc TEXT NOT NULL,
                    document_json TEXT NOT NULL,
                    content_sha256 TEXT NOT NULL
                );
                CREATE TABLE initial_squad_quality_shadow_artifacts (
                    initial_squad_artifact_id INTEGER PRIMARY KEY,
                    official_capture_id INTEGER NOT NULL,
                    season_code TEXT NOT NULL,
                    gameweek INTEGER NOT NULL,
                    document_json TEXT NOT NULL,
                    content_sha256 TEXT NOT NULL
                );
                CREATE TABLE official_fpl_events (
                    capture_id INTEGER NOT NULL,
                    event_id INTEGER NOT NULL,
                    deadline_utc TEXT NOT NULL
                );
                CREATE TABLE official_fpl_outcome_captures (
                    outcome_capture_id INTEGER PRIMARY KEY,
                    season_code TEXT NOT NULL,
                    gameweek INTEGER NOT NULL,
                    reference_capture_id INTEGER NOT NULL,
                    available_at_utc TEXT NOT NULL,
                    live_sha256 TEXT NOT NULL,
                    live_json BLOB NOT NULL,
                    player_count INTEGER NOT NULL
                );
                CREATE TABLE official_fpl_player_outcomes (
                    outcome_capture_id INTEGER NOT NULL,
                    player_id INTEGER NOT NULL,
                    minutes INTEGER NOT NULL,
                    total_points INTEGER NOT NULL
                );
                """
            )
            connection.execute(
                """
                INSERT INTO selected_opening_squad_shadow_artifacts
                    (selected_opening_squad_artifact_id,
                     official_capture_id, season_code, opening_gameweek,
                     decision_cutoff_utc, document_json, content_sha256)
                VALUES (1, 18, '2026-27', 1, ?, ?, ?);
                """,
                (
                    selected["decisionCutoffUtc"],
                    selected_json,
                    selected_hash,
                ),
            )
            connection.execute(
                """
                INSERT INTO initial_squad_quality_shadow_artifacts
                    (initial_squad_artifact_id, official_capture_id,
                     season_code, gameweek, document_json, content_sha256)
                VALUES (1, 18, '2026-27', 1, ?, ?);
                """,
                (benchmark_json, benchmark_hash),
            )
        return selected

    @staticmethod
    def _insert_outcomes(
        database: Path,
        selected: dict,
        gameweeks: range,
    ) -> None:
        player_ids = {
            int(player["playerId"])
            for player in selected["selection"]["players"]
        }
        all_player_ids = set(range(1, 23))
        all_player_ids.update(player_ids)
        with sqlite3.connect(database) as connection:
            for gameweek in gameweeks:
                reference_capture = 100 + gameweek
                deadline = (
                    f"2026-{8 + (gameweek - 1) // 4:02d}-"
                    f"{21 + (gameweek - 1) % 4:02d}T17:30:00Z"
                )
                available = (
                    f"2026-{8 + (gameweek - 1) // 4:02d}-"
                    f"{22 + (gameweek - 1) % 4:02d}T12:00:00Z"
                )
                live = json.dumps(
                    {"event": gameweek},
                    separators=(",", ":"),
                ).encode("utf-8")
                live_hash = hashlib.sha256(live).hexdigest()
                connection.execute(
                    """
                    INSERT INTO official_fpl_events
                        (capture_id, event_id, deadline_utc)
                    VALUES (?, ?, ?);
                    """,
                    (reference_capture, gameweek, deadline),
                )
                connection.execute(
                    """
                    INSERT INTO official_fpl_outcome_captures
                        (outcome_capture_id, season_code, gameweek,
                         reference_capture_id, available_at_utc,
                         live_sha256, live_json, player_count)
                    VALUES (?, '2026-27', ?, ?, ?, ?, ?, ?);
                    """,
                    (
                        gameweek,
                        gameweek,
                        reference_capture,
                        available,
                        live_hash,
                        live,
                        len(all_player_ids),
                    ),
                )
                selected_captain = int(
                    selected["selection"]["gameweeks"][gameweek - 1][
                        "captainPlayerId"
                    ]
                )
                for player_id in sorted(all_player_ids):
                    points = (
                        20
                        if player_id == selected_captain
                        else (10 if player_id in player_ids else 1)
                    )
                    connection.execute(
                        """
                        INSERT INTO official_fpl_player_outcomes
                            (outcome_capture_id, player_id, minutes,
                             total_points)
                        VALUES (?, ?, 90, ?);
                        """,
                        (gameweek, player_id, points),
                    )


if __name__ == "__main__":
    unittest.main()
