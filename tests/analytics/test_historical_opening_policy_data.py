from __future__ import annotations

import csv
import hashlib
import io
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import brotli

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.historical_opening_policy_data import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    EXPECTED_CAPTURE_IDENTITIES,
    REGISTERED_SEASONS,
    TARGET_SEASONS,
    build_historical_opening_policy_data,
)
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402


class HistoricalOpeningPolicyDataTests(unittest.TestCase):
    def test_expanding_folds_use_raw_price_and_are_read_only(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            expected = self._create_database(database)
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            with patch.dict(
                EXPECTED_CAPTURE_IDENTITIES,
                expected,
                clear=True,
            ):
                first = build_historical_opening_policy_data(database)
                second = build_historical_opening_policy_data(database)
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, first["artifactVersion"])
        self.assertEqual(
            list(TARGET_SEASONS),
            first["evaluationTargetSeasonCodes"],
        )
        self.assertEqual(3, first["evaluationTargetCount"])
        self.assertTrue(first["decisionConstraint"]["sameGameweekXpExcluded"])
        self.assertEqual(
            [
                [REGISTERED_SEASONS[0]],
                list(REGISTERED_SEASONS[:2]),
                list(REGISTERED_SEASONS[:3]),
            ],
            [
                target["trainingSeasonCodes"]
                for target in first["evaluationTargets"]
            ],
        )
        for target in first["evaluationTargets"]:
            self.assertEqual(15, target["openingPlayerCount"])
            self.assertEqual(5, target["teamCount"])
            self.assertEqual(
                {
                    "defender": 5,
                    "forward": 3,
                    "goalkeeper": 2,
                    "midfielder": 5,
                },
                target["positionCounts"],
            )
            self.assertEqual(40, target["openingPriceMinimumTenths"])
            self.assertEqual(54, target["openingPriceMaximumTenths"])
            self.assertEqual(
                {str(gameweek): 15 for gameweek in range(1, 9)},
                target["outcomeObservedPlayerCounts"],
            )
            self.assertEqual(64, len(target["openingCohortIdentitySha256"]))
            self.assertEqual(64, len(target["outcomeIdentitySha256"]))

    def test_raw_payload_hash_mismatch_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            expected = self._create_database(database)
            connection = sqlite3.connect(database)
            connection.execute(
                """
                UPDATE historical_fpl_season_captures
                SET gameweeks_csv_brotli = ?
                WHERE season_code = '2023-24';
                """,
                (brotli.compress(b"element,position,team,value,GW\n"),),
            )
            connection.commit()
            connection.close()
            with patch.dict(
                EXPECTED_CAPTURE_IDENTITIES,
                expected,
                clear=True,
            ):
                with self.assertRaises(TemporalRidgeError) as raised:
                    build_historical_opening_policy_data(database)

        self.assertEqual("data.raw-gameweeks-sha256", raised.exception.code)

    @staticmethod
    def _create_database(database: Path) -> dict[str, dict[str, object]]:
        connection = sqlite3.connect(database)
        connection.executescript(
            """
            CREATE TABLE schema_migrations (
                version INTEGER PRIMARY KEY
            );
            INSERT INTO schema_migrations VALUES (31);
            CREATE TABLE historical_fpl_season_captures (
                capture_id INTEGER PRIMARY KEY,
                season_code TEXT NOT NULL,
                source_revision TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                players_sha256 TEXT NOT NULL,
                gameweeks_sha256 TEXT NOT NULL,
                player_count INTEGER NOT NULL,
                player_gameweek_count INTEGER NOT NULL,
                stable_code_count INTEGER NOT NULL,
                gameweeks_csv_brotli BLOB NOT NULL
            );
            CREATE TABLE historical_fpl_players (
                capture_id INTEGER NOT NULL,
                season_element_id INTEGER NOT NULL,
                player_code INTEGER NOT NULL,
                web_name TEXT NOT NULL,
                PRIMARY KEY (capture_id, season_element_id)
            );
            CREATE TABLE historical_fpl_player_gameweeks (
                capture_id INTEGER NOT NULL,
                player_code INTEGER NOT NULL,
                gameweek INTEGER NOT NULL,
                fixture_id INTEGER NOT NULL,
                total_points INTEGER NOT NULL,
                minutes INTEGER NOT NULL
            );
            """
        )
        expected: dict[str, dict[str, object]] = {}
        for capture_id, season in enumerate(REGISTERED_SEASONS, start=1):
            payload = HistoricalOpeningPolicyDataTests._payload(capture_id)
            payload_hash = hashlib.sha256(payload).hexdigest()
            player_gameweek_count = 15 * 8
            revision = f"{capture_id:040d}"
            players_hash = f"{capture_id + 10:064x}"
            connection.execute(
                """
                INSERT INTO historical_fpl_season_captures VALUES (
                    ?, ?, ?, ?, ?, ?, 15, ?, 15, ?
                );
                """,
                (
                    capture_id,
                    season,
                    revision,
                    f"2026-07-{capture_id:02d}T00:00:00+00:00",
                    players_hash,
                    payload_hash,
                    player_gameweek_count,
                    brotli.compress(payload),
                ),
            )
            expected[season] = {
                "sourceRevision": revision,
                "playersSha256": players_hash,
                "gameweeksSha256": payload_hash,
                "playerCount": 15,
                "playerGameweekCount": player_gameweek_count,
            }
            for element_id in range(1, 16):
                player_code = capture_id * 1000 + element_id
                connection.execute(
                    """
                    INSERT INTO historical_fpl_players
                    VALUES (?, ?, ?, ?);
                    """,
                    (
                        capture_id,
                        element_id,
                        player_code,
                        f"Player {element_id}",
                    ),
                )
                for gameweek in range(1, 9):
                    connection.execute(
                        """
                        INSERT INTO historical_fpl_player_gameweeks
                        VALUES (?, ?, ?, ?, ?, ?);
                        """,
                        (
                            capture_id,
                            player_code,
                            gameweek,
                            gameweek * 100 + element_id,
                            (element_id + gameweek) % 10,
                            90 if element_id % 3 else 0,
                        ),
                    )
        connection.commit()
        connection.close()
        return expected

    @staticmethod
    def _payload(capture_id: int) -> bytes:
        output = io.StringIO(newline="")
        writer = csv.DictWriter(
            output,
            fieldnames=[
                "element",
                "position",
                "team",
                "value",
                "xP",
                "GW",
            ],
            lineterminator="\n",
        )
        writer.writeheader()
        positions = ["GKP"] * 2 + ["DEF"] * 5 + ["MID"] * 5 + ["FWD"] * 3
        for element_id, position in enumerate(positions, start=1):
            writer.writerow(
                {
                    "element": element_id,
                    "position": position,
                    "team": f"Club {(element_id - 1) % 5 + 1}",
                    "value": 39 + element_id,
                    "xP": 99 + capture_id,
                    "GW": 1,
                }
            )
        return output.getvalue().encode("utf-8")


if __name__ == "__main__":
    unittest.main()
