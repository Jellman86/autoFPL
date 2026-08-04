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

from autofpl_analytics.official_published_opening_squad_outcome_evaluation import (  # noqa: E402
    ARTIFACT_TYPE,
    build_official_published_opening_squad_outcome_evaluation,
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


class OfficialPublishedOpeningSquadOutcomeEvaluationTests(unittest.TestCase):
    def test_partial_outcomes_are_deterministic_and_read_only(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            selected = _create_selected_database(database)
            source = self._insert_official_published(database, selected, True)
            _insert_selected_outcomes(database, selected, range(1, 3))
            self._favour_challenger(database, source, range(1, 3))
            before = hashlib.sha256(database.read_bytes()).hexdigest()

            first = build_official_published_opening_squad_outcome_evaluation(
                database
            )
            second = build_official_published_opening_squad_outcome_evaluation(
                database
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual("prospective-outcome-partial", first["status"])
        self.assertFalse(first["isPromotionDecision"])
        self.assertEqual(1, first["officialPublishedSource"]["artifactId"])
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

    def test_complete_outcomes_support_review_but_never_auto_promote(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            selected = _create_selected_database(database)
            source = self._insert_official_published(database, selected, True)
            _insert_selected_outcomes(database, selected, range(1, 9))
            self._favour_challenger(database, source, range(1, 9))

            result = build_official_published_opening_squad_outcome_evaluation(
                database
            )
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
            self._insert_official_published(database, selected, True)

            with self.assertRaises(TemporalRidgeError) as waiting:
                build_official_published_opening_squad_outcome_evaluation(database)
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
                    UPDATE official_published_opening_squad_artifacts
                    SET content_sha256 = ?;
                    """,
                    ("f" * 64,),
                )
            with self.assertRaises(TemporalRidgeError) as tampered:
                build_official_published_opening_squad_outcome_evaluation(database)
            self.assertEqual("official-published.content", tampered.exception.code)

    def test_lineage_mismatch_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            selected = _create_selected_database(database)
            self._insert_official_published(database, selected, False)
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE official_published_opening_squad_artifacts
                    SET source_bootstrap_sha256 = ?;
                    """,
                    ("a" * 64,),
                )

            with self.assertRaises(TemporalRidgeError) as mismatch:
                build_official_published_opening_squad_outcome_evaluation(database)

        self.assertEqual("official-published.lineage", mismatch.exception.code)

    @staticmethod
    def _insert_official_published(
        database: Path,
        selected: dict,
        swap_captaincy: bool,
    ) -> dict:
        incumbent = copy.deepcopy(selected["selection"])
        challenger = copy.deepcopy(selected["selection"])
        if swap_captaincy:
            for roles in challenger["gameweeks"]:
                captain = roles["captainPlayerId"]
                roles["captainPlayerId"] = roles["viceCaptainPlayerId"]
                roles["viceCaptainPlayerId"] = captain
        available = selected["decisionCutoffUtc"]
        deadline = selected["deadlineUtc"]
        bootstrap_hash = "c" * 64
        fixtures_hash = "d" * 64
        incumbent_run_hash = "e" * 64
        data_hash = hashlib.sha256(b"official-data").hexdigest()
        run_hash = hashlib.sha256(b"official-run").hexdigest()
        document = {
            "schemaVersion": "1.0",
            "artifactType": "current-official-published-opening-squad-shadow",
            "artifactVersion": "current-official-published-opening-squad-shadow-v1",
            "status": "prospective-official-baseline-unscored",
            "isPromoted": False,
            "influencesAdvice": False,
            "seasonCode": "2026-27",
            "openingGameweek": 1,
            "deadlineUtc": deadline,
            "decisionCutoffUtc": available,
            "officialCaptureId": 18,
            "source": {
                "sourceKey": "official-fpl-ep-next",
                "availableAtUtc": available,
                "deadlineUtc": deadline,
                "bootstrapSha256": bootstrap_hash,
                "fixturesSha256": fixtures_hash,
            },
            "prospectiveScoreRegistration": {
                "outcomeGameweeks": list(range(1, 9)),
                "squadMembership": (
                    "fixed-opening-squad-no-transfers-for-all-eight-gameweeks"
                ),
                "roles": "all-eight-weeks-frozen-from-preseason-scenario-means",
                "realisedScorer": (
                    "exact-fpl-captain-fallback-and-ordered-auto-substitution"
                ),
                "outcomeStatus": "waiting-for-official-2026-27-outcomes",
            },
            "incumbent": {
                "selection": incumbent,
                "sourceRunIdentitySha256": incumbent_run_hash,
            },
            "challenger": {"selection": challenger},
            "selectionChange": {"overlapPlayerCount": 15},
            "decision": "retain-as-prospective-official-baseline-only",
            "dataIdentitySha256": data_hash,
            "runIdentitySha256": run_hash,
        }
        document_json = json.dumps(document, separators=(",", ":"))
        document_hash = hashlib.sha256(document_json.encode("utf-8")).hexdigest()
        with sqlite3.connect(database) as connection:
            connection.executescript(
                """
                CREATE TABLE official_fpl_captures (
                    capture_id INTEGER PRIMARY KEY,
                    season_code TEXT NOT NULL,
                    next_gameweek_number INTEGER NOT NULL,
                    available_at_utc TEXT NOT NULL,
                    bootstrap_sha256 TEXT NOT NULL,
                    fixtures_sha256 TEXT NOT NULL
                );
                CREATE TABLE official_published_opening_squad_artifacts (
                    artifact_id INTEGER PRIMARY KEY,
                    official_capture_id INTEGER NOT NULL,
                    source_bootstrap_sha256 TEXT NOT NULL,
                    source_fixtures_sha256 TEXT NOT NULL,
                    incumbent_run_identity_sha256 TEXT NOT NULL,
                    producer_data_identity_sha256 TEXT NOT NULL,
                    producer_run_identity_sha256 TEXT NOT NULL,
                    document_json TEXT NOT NULL,
                    content_sha256 TEXT NOT NULL
                );
                """
            )
            connection.execute(
                """
                INSERT INTO official_fpl_captures
                    (capture_id, season_code, next_gameweek_number,
                     available_at_utc, bootstrap_sha256, fixtures_sha256)
                VALUES (18, '2026-27', 1, ?, ?, ?);
                """,
                (available, bootstrap_hash, fixtures_hash),
            )
            connection.execute(
                """
                INSERT INTO official_fpl_events
                    (capture_id, event_id, deadline_utc)
                VALUES (18, 1, ?);
                """,
                (deadline,),
            )
            connection.execute(
                """
                INSERT INTO official_published_opening_squad_artifacts
                    (artifact_id, official_capture_id,
                     source_bootstrap_sha256, source_fixtures_sha256,
                     incumbent_run_identity_sha256,
                     producer_data_identity_sha256,
                     producer_run_identity_sha256,
                     document_json, content_sha256)
                VALUES (1, 18, ?, ?, ?, ?, ?, ?, ?);
                """,
                (
                    bootstrap_hash,
                    fixtures_hash,
                    incumbent_run_hash,
                    data_hash,
                    run_hash,
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
