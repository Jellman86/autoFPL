from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import unittest
from contextlib import contextmanager, redirect_stderr
from io import StringIO
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.current_multi_horizon_player_forecast import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    DECISION_HORIZONS,
    STATUS,
    TARGET_GAMEWEEKS,
    build_current_multi_horizon_player_forecast,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics import test_multi_season_player_forecast as helpers  # noqa: E402


class CurrentMultiHorizonPlayerForecastTests(unittest.TestCase):
    def test_fixed_forecast_is_deterministic_read_only_and_complete(self) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            artifact = build_current_multi_horizon_player_forecast(database)
            repeated = build_current_multi_horizon_player_forecast(database)
            original_gw1 = (
                helpers.build_multi_season_player_forecast(database)
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(artifact, repeated)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, artifact["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, artifact["artifactVersion"])
        self.assertEqual(STATUS, artifact["status"])
        self.assertFalse(artifact["isPromoted"])
        self.assertFalse(artifact["influencesAdvice"])
        self.assertEqual(list(TARGET_GAMEWEEKS), artifact["targetGameweeks"])
        self.assertEqual(
            list(DECISION_HORIZONS),
            artifact["decisionHorizons"],
        )
        self.assertEqual(8, len(artifact["fixtureSchedule"]))
        self.assertTrue(
            all(row["fixtureCount"] == 1 for row in artifact["fixtureSchedule"])
        )
        self.assertTrue(
            all(row["teamCount"] == 2 for row in artifact["fixtureSchedule"])
        )
        self.assertEqual(6, artifact["playerCount"])
        self.assertEqual(7, artifact["officialPlayerCount"])
        self.assertEqual(1, artifact["ineligiblePlayerCount"])
        original_by_id = {
            player["playerId"]: player["expectedPoints"]
            for player in original_gw1["players"]
        }
        for player in artifact["players"]:
            self.assertEqual(
                original_by_id[player["playerId"]],
                player["gameweeks"][0]["expectedPoints"],
            )
            self.assertEqual(
                list(TARGET_GAMEWEEKS),
                [row["gameweek"] for row in player["gameweeks"]],
            )
            self.assertEqual(
                list(DECISION_HORIZONS),
                [row["gameweekCount"] for row in player["horizons"]],
            )
            for horizon in player["horizons"]:
                expected = round(
                    sum(
                        row["expectedPoints"]
                        for row in player["gameweeks"]
                        if row["gameweek"]
                        <= horizon["throughGameweek"]
                    ),
                    6,
                )
                self.assertEqual(expected, horizon["expectedPoints"])

    def test_future_fixture_revision_does_not_change_earlier_forecasts(self) -> None:
        with self._database() as database:
            before = build_current_multi_horizon_player_forecast(database)
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE official_fpl_fixtures
                    SET kickoff_utc = '2026-09-13T20:00:00+00:00',
                        home_team_id = 2,
                        away_team_id = 1
                    WHERE capture_id = 50 AND event_id = 4;
                    """
                )
            after = build_current_multi_horizon_player_forecast(database)

        self.assertEqual(
            before["fixtureSchedule"][:3],
            after["fixtureSchedule"][:3],
        )
        self.assertNotEqual(
            before["fixtureSchedule"][3],
            after["fixtureSchedule"][3],
        )
        for before_player, after_player in zip(
            before["players"],
            after["players"],
        ):
            self.assertEqual(
                before_player["gameweeks"][:3],
                after_player["gameweeks"][:3],
            )
        self.assertNotEqual(
            before["dataIdentitySha256"],
            after["dataIdentitySha256"],
        )

    def test_fixture_coverage_and_kickoffs_fail_closed(self) -> None:
        with self._database() as database:
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    DELETE FROM official_fpl_fixtures
                    WHERE capture_id = 50 AND event_id = 6;
                    """
                )
            with self.assertRaises(TemporalRidgeError) as context:
                build_current_multi_horizon_player_forecast(database)
        self.assertEqual("data.target-fixtures-not-found", context.exception.code)

        with self._database() as database:
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE official_fpl_fixtures
                    SET kickoff_utc = NULL
                    WHERE capture_id = 50 AND event_id = 8;
                    """
                )
            with self.assertRaises(TemporalRidgeError) as context:
                build_current_multi_horizon_player_forecast(database)
        self.assertEqual("data.target-kickoff-not-found", context.exception.code)

    def test_fixed_target_and_cli_overwrite_fail_closed(self) -> None:
        with self._database() as database:
            with self.assertRaises(TemporalRidgeError) as context:
                build_current_multi_horizon_player_forecast(
                    database,
                    season_code="2025-26",
                )
            self.assertEqual("configuration.target", context.exception.code)

            output = database.parent / "multi-horizon-shadow.json"
            arguments = ["--database", str(database), "--output", str(output)]
            self.assertEqual(0, main(arguments))
            written = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                repeated = main(arguments)
        self.assertEqual(STATUS, written["status"])
        self.assertEqual(1, repeated)
        self.assertIn("output.already-exists", errors.getvalue())

    @contextmanager
    def _database(self):
        helper = helpers.MultiSeasonPlayerForecastTests()
        with helper._database() as path:
            with sqlite3.connect(path) as connection:
                connection.executemany(
                    """
                    INSERT INTO official_fpl_fixtures VALUES (
                        50, ?, ?, ?, 1, 2
                    );
                    """,
                    [
                        (
                            900 + gameweek,
                            gameweek,
                            f"2026-0{8 + (gameweek - 1) // 4}-"
                            f"{20 + gameweek:02d}T19:00:00+00:00",
                        )
                        for gameweek in range(2, 9)
                    ],
                )
            yield path


if __name__ == "__main__":
    unittest.main()
