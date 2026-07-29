from __future__ import annotations

import hashlib
import json
import sys
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.current_minutes_distribution_forecast import (  # noqa: E402
    SCREEN_RUN_IDENTITY,
    build_current_minutes_distribution_forecast,
    main,
)
from tests.analytics import test_preseason_player_forecast as point_helpers  # noqa: E402


class CurrentMinutesDistributionForecastTests(unittest.TestCase):
    def test_frozen_current_shadow_is_deterministic_and_read_only(self) -> None:
        helper = point_helpers.PreseasonPlayerForecastTests()
        with helper._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            artifact = build_current_minutes_distribution_forecast(database)
            repeated = build_current_minutes_distribution_forecast(database)
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(artifact, repeated)
        self.assertEqual(before, after)
        self.assertEqual(
            "current-minutes-distribution-shadow",
            artifact["artifactType"],
        )
        self.assertEqual("prospective-shadow-unscored", artifact["status"])
        self.assertFalse(artifact["isPromoted"])
        self.assertFalse(artifact["influencesAdvice"])
        self.assertFalse(artifact["productImportReady"])
        self.assertEqual(SCREEN_RUN_IDENTITY, artifact["model"][
            "historicalScreenRunIdentitySha256"
        ])
        self.assertEqual(5, artifact["playerCount"])
        self.assertEqual(
            artifact["playerCount"],
            artifact["playerSupportCount"]
            + artifact["positionFallbackCount"],
        )
        for player in artifact["players"]:
            self.assertEqual(
                {"rawSupported", "officialCeilingProspective"},
                set(player["variants"]),
            )
            for variant in player["variants"].values():
                self.assertAlmostEqual(
                    1.0,
                    sum(
                        item["probability"]
                        for item in variant["support"]
                    ),
                    places=5,
                )
                self.assertFalse(variant["influencesAdvice"])

        injured = self._player(artifact, 3)
        self.assertEqual(
            1.0,
            injured["variants"]["officialCeilingProspective"][
                "zeroMinutesProbability"
            ],
        )
        self.assertEqual(
            0.0,
            injured["variants"]["officialCeilingProspective"][
                "expectedMinutes"
            ],
        )
        new_player = self._player(artifact, 5)
        self.assertEqual(
            "position-positive-minutes-fallback",
            new_player["conditionalSupportIdentity"],
        )

    def test_cli_refuses_to_overwrite(self) -> None:
        helper = point_helpers.PreseasonPlayerForecastTests()
        with helper._database() as database:
            output = database.parent / "minutes-distribution.json"
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
        self.assertEqual("prospective-shadow-unscored", written["status"])
        self.assertEqual(1, repeated)
        self.assertIn("output.already-exists", errors.getvalue())

    @staticmethod
    def _player(artifact: dict, player_id: int) -> dict:
        return next(
            player
            for player in artifact["players"]
            if player["playerId"] == player_id
        )


if __name__ == "__main__":
    unittest.main()
