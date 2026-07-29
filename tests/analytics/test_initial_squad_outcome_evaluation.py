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
    build_current_initial_squad_candidate,
)
from autofpl_analytics.initial_squad_outcome_evaluation import (  # noqa: E402
    ARTIFACT_TYPE,
    build_initial_squad_outcome_evaluation,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics.test_current_initial_squad_candidate import (  # noqa: E402
    CurrentInitialSquadCandidateTests,
)

create_candidate_database = CurrentInitialSquadCandidateTests._create_database
del CurrentInitialSquadCandidateTests


class InitialSquadOutcomeEvaluationTests(unittest.TestCase):
    def test_frozen_candidate_and_components_are_scored_read_only(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / "autofpl.db"
            self._create_database(database, include_outcome=True)
            before = database.read_bytes()

            first = build_initial_squad_outcome_evaluation(database)
            second = build_initial_squad_outcome_evaluation(database)

            self.assertEqual(first, second)
            self.assertEqual(before, database.read_bytes())
            self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
            self.assertEqual(
                "prospective-outcome-evaluated",
                first["status"],
            )
            self.assertFalse(first["isPromotionDecision"])
            self.assertEqual(
                (
                    first["selectionOutcome"]["candidate"]["totalPoints"]
                    - first["selectionOutcome"]["model"]["totalPoints"]
                ),
                first["selectionOutcome"]["candidatePointsDelta"],
            )
            components = first["componentEvaluation"]
            self.assertEqual(
                19,
                components["baselinePointMean"]["cohortCount"],
            )
            self.assertEqual(
                19,
                components["jointScenarioPointDistribution"][
                    "cohortCount"
                ],
            )
            self.assertGreaterEqual(
                components["jointScenarioPointDistribution"]["meanCrps"],
                0,
            )
            self.assertGreaterEqual(
                components["jointScenarioAppearanceProbability"][
                    "brierScore"
                ],
                0,
            )
            self.assertEqual(
                1,
                first["outcomeSource"]["outcomeCaptureId"],
            )

    def test_missing_outcome_waits_and_tampering_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            database = root / "autofpl.db"
            output = root / "evaluation.json"
            self._create_database(database, include_outcome=False)

            with self.assertRaises(TemporalRidgeError) as waiting:
                build_initial_squad_outcome_evaluation(database)
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

            self._insert_outcome(database)
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE initial_squad_quality_shadow_artifacts
                    SET content_sha256 = ?;
                    """,
                    ("f" * 64,),
                )
            with self.assertRaises(TemporalRidgeError) as tampered:
                build_initial_squad_outcome_evaluation(database)
            self.assertEqual("initial-squad.content", tampered.exception.code)

    @staticmethod
    def _create_database(
        database: Path,
        include_outcome: bool,
    ) -> None:
        create_candidate_database(database)
        candidate = build_current_initial_squad_candidate(database)
        candidate_json = json.dumps(candidate, separators=(",", ":"))
        candidate_hash = hashlib.sha256(
            candidate_json.encode("utf-8")
        ).hexdigest()
        baseline = {
            "schemaVersion": "1.0",
            "status": "provisional-unvalidated",
            "modelKey": "official-market-baseline-v0-player-table",
            "seasonCode": "2026-27",
            "gameweek": 1,
            "deadlineUtc": candidate["deadlineUtc"],
            "decisionCutoffUtc": candidate["decisionCutoffUtc"],
            "officialCaptureId": 16,
            "distributionStatus": "uncalibrated-wide-interval",
            "players": [
                {
                    "playerId": player_id,
                    "expectedPoints": float((player_id % 4) + 1),
                }
                for player_id in range(1, 20)
            ],
            "limitations": ["Unvalidated baseline."],
        }
        baseline_json = json.dumps(baseline, separators=(",", ":"))
        baseline_hash = hashlib.sha256(
            baseline_json.encode("utf-8")
        ).hexdigest()
        with sqlite3.connect(database) as connection:
            connection.executescript(
                """
                CREATE TABLE initial_squad_quality_shadow_artifacts (
                    initial_squad_artifact_id INTEGER PRIMARY KEY,
                    official_capture_id INTEGER NOT NULL,
                    season_code TEXT NOT NULL,
                    gameweek INTEGER NOT NULL,
                    decision_cutoff_utc TEXT NOT NULL,
                    document_json TEXT NOT NULL,
                    content_sha256 TEXT NOT NULL
                );
                CREATE TABLE player_gameweek_forecast_artifacts (
                    forecast_artifact_id INTEGER PRIMARY KEY,
                    official_capture_id INTEGER NOT NULL,
                    season_code TEXT NOT NULL,
                    gameweek INTEGER NOT NULL,
                    document_json TEXT NOT NULL,
                    content_sha256 TEXT NOT NULL
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
                INSERT INTO initial_squad_quality_shadow_artifacts
                    (initial_squad_artifact_id, official_capture_id,
                     season_code, gameweek, decision_cutoff_utc,
                     document_json, content_sha256)
                VALUES (1, 16, '2026-27', 1, ?, ?, ?);
                """,
                (
                    candidate["decisionCutoffUtc"],
                    candidate_json,
                    candidate_hash,
                ),
            )
            connection.execute(
                """
                INSERT INTO player_gameweek_forecast_artifacts
                    (forecast_artifact_id, official_capture_id,
                     season_code, gameweek, document_json, content_sha256)
                VALUES (1, 16, '2026-27', 1, ?, ?);
                """,
                (baseline_json, baseline_hash),
            )
        if include_outcome:
            InitialSquadOutcomeEvaluationTests._insert_outcome(database)

    @staticmethod
    def _insert_outcome(database: Path) -> None:
        live = b'{"elements":[]}'
        live_hash = hashlib.sha256(live).hexdigest()
        with sqlite3.connect(database) as connection:
            connection.execute(
                """
                INSERT INTO official_fpl_outcome_captures
                    (outcome_capture_id, season_code, gameweek,
                     reference_capture_id, available_at_utc, live_sha256,
                     live_json, player_count)
                VALUES (
                    1, '2026-27', 1, 16, '2026-08-22T12:00:00Z',
                    ?, ?, 19
                );
                """,
                (live_hash, live),
            )
            for player_id in range(1, 20):
                minutes = 0 if player_id in {3, 8} else 90
                connection.execute(
                    """
                    INSERT INTO official_fpl_player_outcomes
                        (outcome_capture_id, player_id, minutes,
                         total_points)
                    VALUES (1, ?, ?, ?);
                    """,
                    (
                        player_id,
                        minutes,
                        0 if minutes == 0 else (player_id % 6) - 1,
                    ),
                )


if __name__ == "__main__":
    unittest.main()
