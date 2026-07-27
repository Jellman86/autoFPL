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

from autofpl_analytics.preseason_participation_forecast import (  # noqa: E402
    EVALUATION_DATA_IDENTITY,
    EVALUATION_RUN_IDENTITY,
    MINUTES_BASELINE,
    build_preseason_participation_forecast,
    main,
)
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402
from tests.analytics import test_preseason_player_forecast as point_helpers  # noqa: E402


class PreseasonParticipationForecastTests(unittest.TestCase):
    def test_supported_models_emit_current_probabilities_read_only(self) -> None:
        helper = point_helpers.PreseasonPlayerForecastTests()
        with helper._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            artifact = build_preseason_participation_forecast(database)
            repeated = build_preseason_participation_forecast(database)
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(artifact, repeated)
        self.assertEqual(before, after)
        self.assertEqual(
            "historical-preseason-participation-player-forecast",
            artifact["artifactType"],
        )
        self.assertFalse(artifact["isPromoted"])
        self.assertFalse(artifact["influencesAdvice"])
        self.assertEqual(5, artifact["playerCount"])
        self.assertEqual(6, artifact["officialPlayerCount"])
        self.assertEqual(1, artifact["ineligiblePlayerCount"])
        self.assertEqual(
            artifact["playerCount"],
            artifact["probabilityCoherentPlayerCount"]
            + artifact["probabilityIncoherentPlayerCount"],
        )
        self.assertEqual(
            EVALUATION_DATA_IDENTITY,
            artifact["training"]["minutesBaseline"][
                "evaluationDataIdentitySha256"
            ],
        )
        self.assertEqual(
            EVALUATION_RUN_IDENTITY,
            artifact["training"]["minutesBaseline"][
                "evaluationRunIdentitySha256"
            ],
        )
        self.assertEqual(
            {"appearance", "start", "played-60"},
            set(artifact["training"]["models"]),
        )
        for model in artifact["training"]["models"].values():
            self.assertEqual(
                EVALUATION_RUN_IDENTITY,
                model["evaluationRunIdentitySha256"],
            )
            self.assertEqual(
                artifact["training"]["trainingRowCount"],
                model["diagnostics"]["trainingRows"],
            )
        for player in artifact["players"]:
            for key in (
                "appearanceProbability",
                "startProbability",
                "played60Probability",
            ):
                self.assertGreaterEqual(player[key], 0.0)
                self.assertLessEqual(player[key], 1.0)
            self.assertGreaterEqual(player["expectedMinutes"], 0.0)
            self.assertEqual(
                MINUTES_BASELINE,
                player["expectedMinutesModelKey"],
            )

        injured = self._player(artifact, 3)
        self.assertEqual("i", injured["officialStatus"])
        self.assertEqual(
            "authoritative-current-official-not-modelled",
            injured["availabilityStatus"],
        )
        new_player = self._player(artifact, 5)
        self.assertEqual(
            "position-mean-missing-prior-identity",
            new_player["expectedMinutesIdentity"],
        )

    def test_fixed_target_and_cli_overwrite_fail_closed(self) -> None:
        helper = point_helpers.PreseasonPlayerForecastTests()
        with helper._database() as database:
            with self.assertRaises(TemporalRidgeError) as context:
                build_preseason_participation_forecast(
                    database,
                    season_code="2026-27",
                    gameweek=2,
                )
            self.assertEqual("configuration.target", context.exception.code)

            output = database.parent / "participation.json"
            arguments = ["--database", str(database), "--output", str(output)]
            self.assertEqual(0, main(arguments))
            written = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                repeated = main(arguments)
        self.assertEqual(
            "provisional-preseason-participation-challenger",
            written["status"],
        )
        self.assertEqual(1, repeated)
        self.assertIn("output.already-exists", errors.getvalue())

    def test_archive_must_match_retained_participation_result(self) -> None:
        helper = point_helpers.PreseasonPlayerForecastTests()
        with helper._database() as database:
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    UPDATE historical_fpl_season_captures
                    SET players_sha256 = ?;
                    """,
                    ("f" * 64,),
                )
            with self.assertRaises(TemporalRidgeError) as context:
                build_preseason_participation_forecast(database)
        self.assertEqual("data.archive-not-evaluated", context.exception.code)

    @staticmethod
    def _player(artifact: dict, player_id: int) -> dict:
        return next(
            player
            for player in artifact["players"]
            if player["playerId"] == player_id
        )


if __name__ == "__main__":
    unittest.main()
