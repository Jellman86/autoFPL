from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.current_appearance_hurdle_player_forecast import (  # noqa: E402
    ARTIFACT_TYPE,
    ARTIFACT_VERSION,
    HISTORICAL_EVALUATION_RUN_IDENTITY,
    STATUS,
    build_current_appearance_hurdle_player_forecast,
    main,
)
from autofpl_analytics.current_multi_horizon_player_forecast import (  # noqa: E402
    DECISION_HORIZONS,
    TARGET_GAMEWEEKS,
)
from tests.analytics import (  # noqa: E402
    test_current_multi_horizon_player_forecast as helpers,
)


class CurrentAppearanceHurdlePlayerForecastTests(unittest.TestCase):
    def test_current_shadow_is_deterministic_complete_and_read_only(
        self,
    ) -> None:
        helper = helpers.CurrentMultiHorizonPlayerForecastTests()
        with helper._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            first = build_current_appearance_hurdle_player_forecast(
                database
            )
            second = build_current_appearance_hurdle_player_forecast(
                database
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
        self.assertEqual(ARTIFACT_VERSION, first["artifactVersion"])
        self.assertEqual(STATUS, first["status"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["influencesAdvice"])
        self.assertEqual(list(TARGET_GAMEWEEKS), first["targetGameweeks"])
        self.assertEqual(
            list(DECISION_HORIZONS),
            first["decisionHorizons"],
        )
        self.assertEqual(6, first["playerCount"])
        self.assertEqual(
            HISTORICAL_EVALUATION_RUN_IDENTITY,
            first["training"]["historicalEvaluation"][
                "runIdentitySha256"
            ],
        )
        for player in first["players"]:
            self.assertEqual(8, len(player["gameweeks"]))
            for row in player["gameweeks"]:
                self.assertGreaterEqual(
                    row["appearanceProbability"],
                    0.0,
                )
                self.assertLessEqual(
                    row["appearanceProbability"],
                    1.0,
                )
                self.assertAlmostEqual(
                    row["expectedPoints"],
                    round(
                        row["appearanceProbability"]
                        * row["conditionalExpectedPoints"],
                        6,
                    ),
                    places=5,
                )
            for horizon in player["horizons"]:
                self.assertEqual(
                    horizon["expectedPoints"],
                    round(
                        sum(
                            row["expectedPoints"]
                            for row in player["gameweeks"]
                            if row["gameweek"]
                            <= horizon["throughGameweek"]
                        ),
                        6,
                    ),
                )

    def test_future_fixture_revision_preserves_earlier_hurdle_means(
        self,
    ) -> None:
        helper = helpers.CurrentMultiHorizonPlayerForecastTests()
        with helper._database() as database:
            before = build_current_appearance_hurdle_player_forecast(
                database
            )
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
            after = build_current_appearance_hurdle_player_forecast(
                database
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
            before["fixtureSchedule"][3],
            after["fixtureSchedule"][3],
        )

    def test_cli_refuses_existing_output(self) -> None:
        helper = helpers.CurrentMultiHorizonPlayerForecastTests()
        with helper._database() as database:
            output = database.parent / "hurdle-current.json"
            arguments = [
                "--database",
                str(database),
                "--output",
                str(output),
            ]
            self.assertEqual(0, main(arguments))
            written = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                repeated = main(arguments)

        self.assertEqual(STATUS, written["status"])
        self.assertEqual(1, repeated)
        self.assertIn("output.already-exists", errors.getvalue())


if __name__ == "__main__":
    unittest.main()
