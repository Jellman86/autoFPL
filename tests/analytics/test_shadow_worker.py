from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.shadow_worker import (  # noqa: E402
    _load_persisted_point_forecast,
    generate_once,
    inspect_target,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)


class ShadowWorkerTests(unittest.TestCase):
    def test_persisted_point_input_requires_its_content_hash(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / "autofpl.db"
            self._create_database(database)
            document = json.dumps(
                {"officialCaptureId": 16},
                separators=(",", ":"),
            )
            content_hash = hashlib.sha256(
                document.encode("utf-8")
            ).hexdigest()
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    INSERT INTO multi_season_player_forecast_artifacts (
                        official_capture_id, decision_cutoff_utc,
                        document_json, content_sha256
                    )
                    VALUES (
                        16, '2026-07-29T04:38:41Z', ?, ?
                    );
                    """,
                    (document, content_hash),
                )

            loaded = _load_persisted_point_forecast(database)
            self.assertEqual(16, loaded["officialCaptureId"])

            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE multi_season_player_forecast_artifacts
                    SET content_sha256 = ?;
                    """,
                    ("f" * 64,),
                )
            with self.assertRaises(TemporalRidgeError) as caught:
                _load_persisted_point_forecast(database)
            self.assertEqual(
                "worker.point-forecast-invalid",
                caught.exception.code,
            )

    def test_missing_current_and_generated_states_are_explicit(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            database = root / "autofpl.db"
            inbox = root / "inbox"
            self._create_database(database)

            waiting = generate_once(database, inbox)
            self.assertEqual("waiting", waiting.status)
            self.assertEqual("no-official-capture", waiting.errorCode)

            self._insert_capture(database, 16)
            artifact = {
                "officialCaptureId": 16,
                "players": [],
            }
            with patch(
                "autofpl_analytics.shadow_worker."
                "build_multi_season_player_forecast",
                return_value=artifact,
            ):
                generated = generate_once(database, inbox)

            self.assertEqual("generated", generated.status)
            self.assertEqual(16, generated.officialCaptureId)
            output = inbox / str(generated.outputFile)
            self.assertEqual(artifact, json.loads(output.read_text()))
            self.assertEqual(0o600, output.stat().st_mode & 0o777)

            pending = generate_once(database, inbox)
            self.assertEqual("handoff-pending", pending.status)

    def test_point_shadow_advances_to_joint_scenario_handoff(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            database = root / "autofpl.db"
            self._create_database(database)
            self._insert_capture(database, 16)
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    INSERT INTO multi_season_player_forecast_artifacts
                        (official_capture_id)
                    VALUES (16);
                    """
                )

            target = inspect_target(database)
            self.assertTrue(target["hasExactPointShadow"])
            self.assertFalse(target["hasExactScenarioShadow"])
            artifact = {
                "officialCaptureId": 16,
                "pointRows": [[1]],
            }
            with patch(
                "autofpl_analytics.shadow_worker."
                "_build_joint_scenario_for_worker",
                return_value=artifact,
            ):
                generated = generate_once(database, root / "inbox")
            self.assertEqual("generated", generated.status)
            self.assertTrue(
                str(generated.outputFile).startswith(
                    "joint-scenario-shadow-capture-16-v2"
                )
            )
            self.assertEqual(
                artifact,
                json.loads(
                    (root / "inbox" / str(generated.outputFile))
                    .read_text()
                ),
            )

    def test_exact_shadows_and_unsupported_target_do_not_generate(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            database = root / "autofpl.db"
            self._create_database(database)
            self._insert_capture(database, 16)
            with sqlite3.connect(database) as connection:
                connection.executescript(
                    """
                    INSERT INTO multi_season_player_forecast_artifacts
                        (official_capture_id)
                    VALUES (16);
                    INSERT INTO joint_scenario_shadow_artifacts
                        (official_capture_id)
                    VALUES (16);
                    INSERT INTO baseline_forecast_artifacts
                        (capture_id)
                    VALUES (16);
                    """
                )

            target = inspect_target(database)
            self.assertTrue(target["hasExactPointShadow"])
            self.assertTrue(target["hasExactScenarioShadow"])
            self.assertFalse(target["hasExactInitialSquadQuality"])
            self.assertFalse(target["hasExactSelectedOpeningSquad"])
            self.assertFalse(
                target["hasExactBestSupportedOpeningSquad"]
            )
            self.assertFalse(target["hasExactSelectionScore"])
            candidate = {
                "officialCaptureId": 16,
                "candidate": {"totalPointRows": [10]},
            }
            with patch(
                "autofpl_analytics.shadow_worker."
                "build_current_initial_squad_candidate",
                return_value=candidate,
            ):
                generated = generate_once(database, root / "inbox")
            self.assertEqual("generated", generated.status)
            self.assertTrue(
                str(generated.outputFile).startswith(
                    "initial-squad-quality-capture-16"
                )
            )

            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    INSERT INTO initial_squad_quality_shadow_artifacts
                        (scenario_artifact_id, forecast_artifact_id,
                         optimizer_version)
                    VALUES (
                        1, 1, 'scipy-highs-linear-squad-surrogate-v1'
                    );
                    """
                )
            selected = {
                "officialCaptureId": 16,
                "selection": {"playerIds": list(range(1, 16))},
            }
            with patch(
                "autofpl_analytics.shadow_worker."
                "build_current_selected_opening_squad",
                return_value=selected,
            ):
                generated = generate_once(
                    database,
                    root / "selected-inbox",
                )
            self.assertEqual("generated", generated.status)
            self.assertTrue(
                str(generated.outputFile).startswith(
                    "selected-opening-squad-capture-16"
                )
            )
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    INSERT INTO selected_opening_squad_shadow_artifacts
                        (official_capture_id, evaluation_policy_key,
                         evaluation_data_identity_sha256)
                    VALUES (
                        16, '6-expected-points',
                        'e99bb4fce3615c91ecaf642037c6cebb3d36efffc2df54857ffbc4c27714e048'
                    );
                    """
                )
            best_supported = {
                "officialCaptureId": 16,
                "artifactVersion": (
                    "current-selected-opening-squad-shadow-v2"
                ),
            }
            with patch(
                "autofpl_analytics.shadow_worker."
                "build_current_best_supported_opening_squad",
                return_value=best_supported,
            ):
                generated = generate_once(
                    database,
                    root / "best-supported-inbox",
                )
            self.assertEqual("generated", generated.status)
            self.assertTrue(
                str(generated.outputFile).startswith(
                    "selected-opening-squad-capture-16-hurdle-v2"
                )
            )
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    INSERT INTO selected_opening_squad_shadow_artifacts
                        (official_capture_id, evaluation_policy_key,
                         evaluation_data_identity_sha256)
                    VALUES (
                        16, '6-expected-points',
                        '1ef395d722844b1833fb059d606606e0bf1f1d42732377474d671f1a3f17b16e'
                    );
                    """
                )
            score = {
                "officialCaptureId": 16,
                "model": {"totalPointRows": [10]},
            }
            with patch(
                "autofpl_analytics.shadow_worker."
                "build_current_scenario_selection_score",
                return_value=score,
            ):
                generated = generate_once(database, root / "score-inbox")
            self.assertEqual("generated", generated.status)
            self.assertTrue(
                str(generated.outputFile).startswith(
                    "selection-scenario-score-capture-16-selection-none"
                )
            )

            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    INSERT INTO selection_scenario_score_shadow_artifacts
                        (scenario_artifact_id, forecast_artifact_id,
                         selection_revision_id)
                    VALUES (1, 1, NULL);
                    """
                )
            strategies = {
                "officialCaptureId": 16,
                "strategies": {"balanced": {}},
            }
            with patch(
                "autofpl_analytics.shadow_worker."
                "build_current_selection_strategies",
                return_value=strategies,
            ):
                generated = generate_once(
                    database,
                    root / "strategy-inbox",
                )
            self.assertEqual("generated", generated.status)
            self.assertTrue(
                str(generated.outputFile).startswith(
                    "selection-role-strategies-capture-16-selection-none"
                )
            )

            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    INSERT INTO selection_role_strategy_shadow_artifacts
                        (score_artifact_id, search_version)
                    VALUES (1, 'deterministic-role-beam-v1');
                    """
                )
            self.assertEqual(
                "current",
                generate_once(database, root / "empty-inbox").status,
            )

            with sqlite3.connect(database) as connection:
                connection.executescript(
                    """
                    UPDATE official_fpl_captures
                    SET next_gameweek_number = 2
                    WHERE capture_id = 16;
                    DELETE FROM multi_season_player_forecast_artifacts;
                    DELETE FROM joint_scenario_shadow_artifacts;
                    """
                )
            unsupported = generate_once(database, root / "inbox")
            self.assertEqual("waiting", unsupported.status)
            self.assertEqual("unsupported-target", unsupported.errorCode)

    @staticmethod
    def _create_database(path: Path) -> None:
        with sqlite3.connect(path) as connection:
            connection.executescript(
                """
                CREATE TABLE official_fpl_captures (
                    capture_id INTEGER PRIMARY KEY,
                    season_code TEXT NOT NULL,
                    next_gameweek_number INTEGER,
                    available_at_utc TEXT NOT NULL
                );
                CREATE TABLE multi_season_player_forecast_artifacts (
                    forecast_artifact_id INTEGER PRIMARY KEY,
                    official_capture_id INTEGER NOT NULL,
                    decision_cutoff_utc TEXT,
                    document_json TEXT,
                    content_sha256 TEXT
                );
                CREATE TABLE joint_scenario_shadow_artifacts (
                    scenario_artifact_id INTEGER PRIMARY KEY,
                    official_capture_id INTEGER NOT NULL
                );
                CREATE TABLE baseline_forecast_artifacts (
                    artifact_id INTEGER PRIMARY KEY,
                    capture_id INTEGER NOT NULL
                );
                CREATE TABLE initial_squad_quality_shadow_artifacts (
                    initial_squad_artifact_id INTEGER PRIMARY KEY,
                    scenario_artifact_id INTEGER NOT NULL,
                    forecast_artifact_id INTEGER NOT NULL,
                    optimizer_version TEXT NOT NULL
                );
                CREATE TABLE selection_revisions (
                    selection_revision_id INTEGER PRIMARY KEY,
                    revision INTEGER NOT NULL,
                    forecast_artifact_id INTEGER NOT NULL
                );
                CREATE TABLE selected_opening_squad_shadow_artifacts (
                    selected_opening_squad_artifact_id INTEGER PRIMARY KEY,
                    official_capture_id INTEGER NOT NULL,
                    evaluation_policy_key TEXT NOT NULL,
                    evaluation_data_identity_sha256 TEXT NOT NULL
                );
                CREATE TABLE selection_scenario_score_shadow_artifacts (
                    score_artifact_id INTEGER PRIMARY KEY,
                    scenario_artifact_id INTEGER NOT NULL,
                    forecast_artifact_id INTEGER NOT NULL,
                    selection_revision_id INTEGER
                );
                CREATE TABLE selection_role_strategy_shadow_artifacts (
                    strategy_artifact_id INTEGER PRIMARY KEY,
                    score_artifact_id INTEGER NOT NULL,
                    search_version TEXT NOT NULL
                );
                """
            )

    @staticmethod
    def _insert_capture(path: Path, capture_id: int) -> None:
        with sqlite3.connect(path) as connection:
            connection.execute(
                """
                INSERT INTO official_fpl_captures
                    (capture_id, season_code, next_gameweek_number,
                     available_at_utc)
                VALUES (?, '2026-27', 1, '2026-07-29T04:38:41Z');
                """,
                (capture_id,),
            )
