from __future__ import annotations

import copy
import hashlib
import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.public_projection_opening_squad_outcome_evaluation import (  # noqa: E402
    ARTIFACT_TYPE,
    build_public_projection_opening_squad_outcome_evaluation,
    main,
)
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402
from tests.analytics import (  # noqa: E402
    test_selected_opening_squad_outcome_evaluation as selected_test,
)


def _create_selected_database(database: Path) -> dict:
    return selected_test.SelectedOpeningSquadOutcomeEvaluationTests._create_database(
        database
    )


def _insert_selected_outcomes(
    database: Path,
    selected: dict,
    gameweeks: range,
) -> None:
    selected_test.SelectedOpeningSquadOutcomeEvaluationTests._insert_outcomes(
        database,
        selected,
        gameweeks,
    )


class PublicProjectionOpeningSquadOutcomeEvaluationTests(unittest.TestCase):
    def test_partial_outcomes_use_latest_frozen_source_and_are_read_only(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            selected = _create_selected_database(database)
            self._insert_public_projection(database, selected, 1, False)
            latest = self._insert_public_projection(
                database,
                selected,
                2,
                True,
            )
            _insert_selected_outcomes(database, selected, range(1, 3))
            self._favour_challenger(database, latest, range(1, 3))
            before = hashlib.sha256(database.read_bytes()).hexdigest()

            first = build_public_projection_opening_squad_outcome_evaluation(database)
            second = build_public_projection_opening_squad_outcome_evaluation(database)
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual("prospective-outcome-partial", first["status"])
        self.assertFalse(first["isPromotionDecision"])
        self.assertEqual(2, first["publicProjectionSource"]["artifactId"])
        self.assertEqual(
            [1, 2],
            first["evidenceStatus"]["observedOutcomeGameweeks"],
        )
        self.assertIsNone(
            first["evidenceStatus"]["checks"]["challengerOutscoredIncumbent"]
        )
        self.assertFalse(first["evidenceStatus"]["supportsPromotionReview"])
        self.assertGreater(
            first["realisedScore"]["cumulative"]["challengerDelta"],
            0,
        )

    def test_complete_outcomes_support_review_but_not_automatic_promotion(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            selected = _create_selected_database(database)
            source = self._insert_public_projection(
                database,
                selected,
                1,
                True,
            )
            _insert_selected_outcomes(database, selected, range(1, 9))
            self._favour_challenger(database, source, range(1, 9))

            result = build_public_projection_opening_squad_outcome_evaluation(database)
            output = Path(temporary) / "evaluation.json"
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
            written = json.loads(output.read_text(encoding="utf-8"))

        self.assertEqual("prospective-outcome-complete", result["status"])
        self.assertEqual(result, written)
        self.assertEqual(
            list(range(1, 9)),
            result["evidenceStatus"]["observedOutcomeGameweeks"],
        )
        self.assertTrue(result["evidenceStatus"]["checks"]["allEightOutcomesAvailable"])
        self.assertTrue(
            result["evidenceStatus"]["checks"]["challengerOutscoredIncumbent"]
        )
        self.assertTrue(result["evidenceStatus"]["supportsPromotionReview"])
        self.assertFalse(result["evidenceStatus"]["isSufficientForAutomaticPromotion"])
        cumulative = result["realisedScore"]["cumulative"]
        self.assertEqual(8, cumulative["challengerWeeklyWins"])
        self.assertGreater(cumulative["challengerDelta"], 0)

    def test_missing_outcomes_wait_and_tampering_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            database = root / "autofpl.db"
            output = root / "evaluation.json"
            selected = _create_selected_database(database)
            self._insert_public_projection(database, selected, 1, True)

            with self.assertRaises(TemporalRidgeError) as waiting:
                build_public_projection_opening_squad_outcome_evaluation(database)
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
                    UPDATE public_projection_opening_squad_artifacts
                    SET content_sha256 = ?;
                    """,
                    ("f" * 64,),
                )
            with self.assertRaises(TemporalRidgeError) as tampered:
                build_public_projection_opening_squad_outcome_evaluation(database)
            self.assertEqual(
                "public-projection.content",
                tampered.exception.code,
            )

    @staticmethod
    def _insert_public_projection(
        database: Path,
        selected: dict,
        artifact_id: int,
        swap_captaincy: bool,
    ) -> dict:
        incumbent = copy.deepcopy(selected["selection"])
        challenger = copy.deepcopy(selected["selection"])
        if swap_captaincy:
            for roles in challenger["gameweeks"]:
                captain = roles["captainPlayerId"]
                roles["captainPlayerId"] = roles["viceCaptainPlayerId"]
                roles["viceCaptainPlayerId"] = captain
        snapshot_id = artifact_id
        available = f"2026-08-0{artifact_id}T12:00:00Z"
        source_content_hash = hashlib.sha256(
            f"source-{artifact_id}".encode("utf-8")
        ).hexdigest()
        document = {
            "schemaVersion": "1.0",
            "artifactType": "current-public-projection-opening-squad-shadow",
            "artifactVersion": ("current-public-projection-opening-squad-shadow-v1"),
            "status": "prospective-external-challenger-unscored",
            "isPromoted": False,
            "influencesAdvice": False,
            "seasonCode": "2026-27",
            "openingGameweek": 1,
            "deadlineUtc": selected["deadlineUtc"],
            "evidenceDecisionCutoffUtc": available,
            "officialCaptureId": 18,
            "source": {
                "sourceKey": "solio-public-projections",
                "snapshotId": snapshot_id,
                "contentSha256": source_content_hash,
            },
            "prospectiveScoreRegistration": {
                "outcomeGameweeks": list(range(1, 9)),
                "realisedScorer": (
                    "exact-fpl-captain-fallback-and-ordered-auto-substitution"
                ),
            },
            "incumbent": {"selection": incumbent},
            "challenger": {"selection": challenger},
            "selectionChange": {"overlapPlayerCount": 15},
            "decision": "retain-as-prospective-external-challenger-only",
        }
        document_json = json.dumps(document, separators=(",", ":"))
        document_hash = hashlib.sha256(document_json.encode("utf-8")).hexdigest()
        with sqlite3.connect(database) as connection:
            connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS research_source_snapshots (
                    snapshot_id INTEGER PRIMARY KEY,
                    source_key TEXT NOT NULL,
                    identity_capture_id INTEGER NOT NULL,
                    available_at_utc TEXT NOT NULL,
                    content_sha256 TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS
                    public_projection_opening_squad_artifacts (
                        artifact_id INTEGER PRIMARY KEY,
                        official_capture_id INTEGER NOT NULL,
                        source_snapshot_id INTEGER NOT NULL,
                        source_content_sha256 TEXT NOT NULL,
                        document_json TEXT NOT NULL,
                        content_sha256 TEXT NOT NULL
                    );
                """
            )
            connection.execute(
                """
                INSERT INTO research_source_snapshots
                    (snapshot_id, source_key, identity_capture_id,
                     available_at_utc, content_sha256)
                VALUES (?, 'solio-public-projections', 18, ?, ?);
                """,
                (snapshot_id, available, source_content_hash),
            )
            connection.execute(
                """
                INSERT INTO public_projection_opening_squad_artifacts
                    (artifact_id, official_capture_id, source_snapshot_id,
                     source_content_sha256, document_json, content_sha256)
                VALUES (?, 18, ?, ?, ?, ?);
                """,
                (
                    artifact_id,
                    snapshot_id,
                    source_content_hash,
                    document_json,
                    document_hash,
                ),
            )
        return document

    @staticmethod
    def _favour_challenger(
        database: Path,
        source: dict,
        gameweeks: range,
    ) -> None:
        roles_by_gameweek = {
            int(row["gameweek"]): row
            for row in source["challenger"]["selection"]["gameweeks"]
        }
        with sqlite3.connect(database) as connection:
            for gameweek in gameweeks:
                connection.execute(
                    """
                    UPDATE official_fpl_player_outcomes
                    SET total_points = 30
                    WHERE outcome_capture_id = ? AND player_id = ?;
                    """,
                    (
                        gameweek,
                        int(roles_by_gameweek[gameweek]["captainPlayerId"]),
                    ),
                )


if __name__ == "__main__":
    unittest.main()
