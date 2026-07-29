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

from autofpl_analytics.current_scenario_selection_score import (  # noqa: E402
    ARTIFACT_TYPE,
    ENGINE_VERSION,
    STATUS,
    build_current_scenario_selection_score,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)


class CurrentScenarioSelectionScoreTests(unittest.TestCase):
    def test_exact_model_and_user_are_scored_on_paired_rows(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / "autofpl.db"
            self._create_database(database)
            before = database.read_bytes()

            first = build_current_scenario_selection_score(database)
            second = build_current_scenario_selection_score(database)

            self.assertEqual(first, second)
            self.assertEqual(before, database.read_bytes())
            self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
            self.assertEqual(STATUS, first["status"])
            self.assertEqual(ENGINE_VERSION, first["engineVersion"])
            self.assertFalse(first["isPromoted"])
            self.assertFalse(first["influencesAdvice"])
            self.assertEqual(2, first["scenarioCount"])
            self.assertEqual([97, 0], first["model"]["totalPointRows"])
            self.assertEqual([100, 0], first["user"]["totalPointRows"])
            self.assertEqual(
                1.5,
                first["userVsModel"]["meanPointsDelta"],
            )
            self.assertEqual(
                0.5,
                first["userVsModel"]["probabilityCandidateWins"],
            )
            self.assertEqual(
                "available",
                first["userSource"]["status"],
            )
            self.assertTrue(first["dataIdentitySha256"])
            self.assertTrue(first["runIdentitySha256"])

    def test_missing_user_selection_keeps_model_score_available(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / "autofpl.db"
            self._create_database(database, include_user=False)

            artifact = build_current_scenario_selection_score(database)

            self.assertEqual("missing", artifact["userSource"]["status"])
            self.assertIsNone(artifact["user"])
            self.assertIsNone(artifact["userVsModel"])
            self.assertEqual([97, 0], artifact["model"]["totalPointRows"])

    def test_corrupt_source_hash_and_existing_output_fail_closed(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            database = root / "autofpl.db"
            self._create_database(database)
            output = root / "score.json"
            output.write_text("preserve", encoding="utf-8")
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
            self.assertEqual("preserve", output.read_text(encoding="utf-8"))

            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE joint_scenario_shadow_artifacts
                    SET content_sha256 = ?;
                    """,
                    ("f" * 64,),
                )
            with self.assertRaises(TemporalRidgeError) as caught:
                build_current_scenario_selection_score(database)
            self.assertEqual(
                "scenario-score.scenario-content",
                caught.exception.code,
            )

    @staticmethod
    def _create_database(
        database: Path,
        include_user: bool = True,
    ) -> None:
        positions = (
            ["goalkeeper"] * 2
            + ["defender"] * 5
            + ["midfielder"] * 5
            + ["forward"] * 3
        )
        players = [
            {
                "columnIndex": index,
                "playerId": index + 1,
                "position": position,
            }
            for index, position in enumerate(positions)
        ]
        scenario = {
            "schemaVersion": "1.0",
            "artifactType": (
                "current-joint-player-gameweek-scenario-shadow"
            ),
            "artifactVersion": "current-joint-scenario-shadow-v1",
            "status": "prospective-shadow-unscored",
            "seasonCode": "2026-27",
            "gameweek": 1,
            "deadlineUtc": "2026-08-21T17:30:00+00:00",
            "decisionCutoffUtc": "2026-07-29T04:38:41+00:00",
            "officialCaptureId": 16,
            "scenarioCount": 2,
            "playerCount": 15,
            "players": players,
            "pointRows": [
                list(range(1, 16)),
                [0] * 15,
            ],
            "playedRows": [
                [True] * 15,
                [True] * 15,
            ],
            "scenarioContentSha256": "a" * 64,
            "runIdentitySha256": "b" * 64,
            "scenarioArtifactId": None,
            "scenarioArtifactContentSha256": None,
        }
        model_selection = {
            "startingPlayerIds": [
                1,
                3,
                4,
                5,
                6,
                8,
                9,
                10,
                11,
                13,
                14,
            ],
            "captainPlayerId": 13,
            "viceCaptainPlayerId": 8,
            "replacementGoalkeeperPlayerId": 2,
            "outfieldSubstitutePlayerIds": [7, 12, 15],
        }
        model_players = []
        for player_id, position in zip(range(1, 16), positions):
            if player_id in model_selection["startingPlayerIds"]:
                lineup_place = "starting"
                bench_order = None
            else:
                lineup_place = "bench"
                if position == "goalkeeper":
                    bench_order = 1
                else:
                    bench_order = (
                        model_selection[
                            "outfieldSubstitutePlayerIds"
                        ].index(player_id)
                        + 1
                    )
            captaincy = None
            if player_id == model_selection["captainPlayerId"]:
                captaincy = "captain"
            elif player_id == model_selection["viceCaptainPlayerId"]:
                captaincy = "vice-captain"
            model_players.append(
                {
                    "playerId": player_id,
                    "position": position,
                    "lineupPlace": lineup_place,
                    "benchOrder": bench_order,
                    "captaincy": captaincy,
                }
            )
        forecast = {
            "schemaVersion": "1.0",
            "isSynthetic": False,
            "snapshotId": 16,
            "gameweek": 1,
            "decisionCutoffUtc": "2026-07-29T04:38:41Z",
            "modelLabel": "Official market baseline v0",
            "selection": {"players": model_players},
            "forecastArtifactId": None,
            "forecastArtifactContentHash": None,
        }
        user_selection = {
            "startingPlayerIds": [
                1,
                3,
                4,
                5,
                6,
                8,
                9,
                10,
                11,
                13,
                15,
            ],
            "captainPlayerId": 15,
            "viceCaptainPlayerId": 8,
            "replacementGoalkeeperPlayerId": 2,
            "outfieldSubstitutePlayerIds": [7, 12, 14],
        }
        scenario_json = json.dumps(
            scenario,
            separators=(",", ":"),
        )
        forecast_json = json.dumps(
            forecast,
            separators=(",", ":"),
        )
        selection_json = json.dumps(
            user_selection,
            separators=(",", ":"),
        )
        with sqlite3.connect(database) as connection:
            connection.executescript(
                """
                CREATE TABLE joint_scenario_shadow_artifacts (
                    scenario_artifact_id INTEGER PRIMARY KEY,
                    official_capture_id INTEGER NOT NULL,
                    decision_cutoff_utc TEXT NOT NULL,
                    document_json TEXT NOT NULL,
                    content_sha256 TEXT NOT NULL
                );
                CREATE TABLE baseline_forecast_artifacts (
                    artifact_id INTEGER PRIMARY KEY,
                    capture_id INTEGER NOT NULL,
                    document_json TEXT NOT NULL,
                    content_sha256 TEXT NOT NULL
                );
                CREATE TABLE selection_revisions (
                    selection_revision_id INTEGER PRIMARY KEY,
                    revision INTEGER NOT NULL,
                    forecast_artifact_id INTEGER NOT NULL,
                    selection_json TEXT NOT NULL,
                    selection_content_sha256 TEXT NOT NULL,
                    locked_at_utc TEXT
                );
                """
            )
            connection.execute(
                """
                INSERT INTO joint_scenario_shadow_artifacts
                    (scenario_artifact_id, official_capture_id,
                     decision_cutoff_utc, document_json, content_sha256)
                VALUES (1, 16, '2026-07-29T04:38:41Z', ?, ?);
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
                INSERT INTO baseline_forecast_artifacts
                    (artifact_id, capture_id, document_json, content_sha256)
                VALUES (13, 16, ?, ?);
                """,
                (
                    forecast_json,
                    hashlib.sha256(
                        forecast_json.encode("utf-8")
                    ).hexdigest(),
                ),
            )
            if include_user:
                connection.execute(
                    """
                    INSERT INTO selection_revisions
                        (selection_revision_id, revision,
                         forecast_artifact_id, selection_json,
                         selection_content_sha256, locked_at_utc)
                    VALUES (1, 1, 13, ?, ?, NULL);
                    """,
                    (
                        selection_json,
                        hashlib.sha256(
                            selection_json.encode("utf-8")
                        ).hexdigest(),
                    ),
                )


if __name__ == "__main__":
    unittest.main()
