from __future__ import annotations

import hashlib
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_official_published_opening_squad import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    STATUS,
    _build_from_scenario,
)
from autofpl_analytics.current_selected_opening_squad import (  # noqa: E402
    _build_from_scenario as build_incumbent,
)
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402
from tests.analytics import (  # noqa: E402
    test_current_multi_horizon_initial_squad as multi_squad_fixture,
)


class CurrentOfficialPublishedOpeningSquadTests(unittest.TestCase):
    def test_full_coverage_baseline_is_deterministic_legal_and_read_only(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            scenario = multi_squad_fixture.CurrentMultiHorizonInitialSquadTests._database_and_scenario(  # noqa: E501
                database
            )
            self._add_published_projections(database, scenario)
            incumbent = build_incumbent(database, scenario)
            before = hashlib.sha256(database.read_bytes()).hexdigest()

            first = _build_from_scenario(database, scenario, incumbent)
            second = _build_from_scenario(database, scenario, incumbent)
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, first["artifactVersion"])
        self.assertEqual(STATUS, first["status"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["influencesAdvice"])
        self.assertEqual(22, first["source"]["publishedPlayerCount"])
        self.assertEqual(22, first["source"]["candidateCoverageCount"])
        self.assertEqual(1.0, first["source"]["candidateCoverageFraction"])
        self.assertEqual(0.0, first["challenger"]["solver"]["mipGap"])
        self.assertEqual(
            first["challenger"]["solver"]["objectiveValue"],
            first["surrogateComparison"]["challengerObjectiveValue"],
        )
        self.assertGreaterEqual(
            first["surrogateComparison"]["challengerDelta"],
            0,
        )
        self.assertEqual(
            8,
            len(first["challenger"]["exactScoreOnRetainedScenarios"]["weekly"]),
        )
        selection = first["challenger"]["selection"]
        self.assertEqual(15, len(selection["playerIds"]))
        self.assertEqual(8, len(selection["gameweeks"]))
        incumbent_ids = set(first["incumbent"]["selection"]["playerIds"])
        challenger_ids = set(selection["playerIds"])
        self.assertEqual(
            incumbent_ids - challenger_ids,
            {
                player["playerId"]
                for player in first["selectionChange"]["removedPlayers"]
            },
        )
        self.assertEqual(
            challenger_ids - incumbent_ids,
            {player["playerId"] for player in first["selectionChange"]["addedPlayers"]},
        )
        for player in (
            first["selectionChange"]["removedPlayers"]
            + first["selectionChange"]["addedPlayers"]
        ):
            self.assertIn("officialPublishedExpectedPoints", player)
            self.assertIn("publishedProjectionDelta", player)

    def test_missing_projection_and_misaligned_cutoff_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            scenario = multi_squad_fixture.CurrentMultiHorizonInitialSquadTests._database_and_scenario(  # noqa: E501
                database
            )
            self._add_published_projections(database, scenario)
            incumbent = build_incumbent(database, scenario)

            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE official_fpl_players
                    SET expected_points_next = NULL
                    WHERE capture_id = 18 AND player_id = 1;
                    """
                )
            with self.assertRaises(TemporalRidgeError) as missing:
                _build_from_scenario(database, scenario, incumbent)
            self.assertEqual(
                "official-published.coverage",
                missing.exception.code,
            )

            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE official_fpl_players
                    SET expected_points_next = '4.0'
                    WHERE capture_id = 18 AND player_id = 1;
                    """
                )
                connection.execute(
                    """
                    UPDATE official_fpl_captures
                    SET available_at_utc = '2026-07-29T17:00:00Z'
                    WHERE capture_id = 18;
                    """
                )
            with self.assertRaises(TemporalRidgeError) as cutoff:
                _build_from_scenario(database, scenario, incumbent)
            self.assertEqual("official-published.cutoff", cutoff.exception.code)

    @staticmethod
    def _add_published_projections(
        database: Path,
        scenario: dict,
    ) -> None:
        with sqlite3.connect(database) as connection:
            connection.execute(
                """
                ALTER TABLE official_fpl_players
                ADD COLUMN expected_points_next TEXT;
                """
            )
            connection.execute(
                """
                UPDATE official_fpl_players
                SET expected_points_next = printf(
                    '%.1f',
                    CASE
                        WHEN player_id IN (1, 5, 12, 19) THEN 8.0
                        ELSE 1.0 + (player_id % 5) * 0.4
                    END
                );
                """
            )
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
                CREATE TABLE official_fpl_events (
                    capture_id INTEGER NOT NULL,
                    event_id INTEGER NOT NULL,
                    deadline_utc TEXT NOT NULL
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
                (
                    scenario["decisionCutoffUtc"],
                    "c" * 64,
                    "d" * 64,
                ),
            )
            connection.execute(
                """
                INSERT INTO official_fpl_events
                    (capture_id, event_id, deadline_utc)
                VALUES (18, 1, ?);
                """,
                (scenario["deadlineUtc"],),
            )


if __name__ == "__main__":
    unittest.main()
