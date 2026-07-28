from __future__ import annotations

import copy
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.prior_competition_coverage import (  # noqa: E402
    ARTIFACT_VERSION,
    build_prior_competition_coverage,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
    _sha256,
)


class PriorCompetitionCoverageTests(unittest.TestCase):
    def test_audit_separates_prior_competition_and_other_history_gaps(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            forecast_path = Path(directory) / "forecast.json"
            self._write_forecast(forecast_path)
            first = build_prior_competition_coverage(
                forecast_path,
                ["Promoted FC"],
            )
            second = build_prior_competition_coverage(
                forecast_path,
                ["Promoted FC"],
            )

        self.assertEqual(first, second)
        self.assertEqual(ARTIFACT_VERSION, first["artifactVersion"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["influencesForecast"])
        self.assertEqual(4, first["summary"]["currentPlayerCount"])
        self.assertEqual(1, first["summary"]["archiveIdentityMatchCount"])
        self.assertEqual(3, first["summary"]["historyGapCount"])
        self.assertEqual(
            2,
            first["summary"]["priorCompetitionHistoryRequiredCount"],
        )
        self.assertEqual(
            1,
            first["summary"][
                "externalOrNewPlayerHistoryRequiredCount"
            ],
        )
        self.assertEqual(
            0.25,
            first["summary"]["archiveIdentityCoverageFraction"],
        )
        gaps = {
            player["playerCode"]: player
            for player in first["playersRequiringHistory"]
        }
        self.assertEqual(
            "prior-competition-match-history",
            gaps[102]["coverageRequirement"],
        )
        self.assertEqual(
            "external-or-new-player-match-history",
            gaps[104]["coverageRequirement"],
        )
        self.assertEqual(
            "explicit-current-official-code-to-source-player-id",
            gaps[102]["identityResolution"],
        )
        self.assertNotIn(101, gaps)
        promoted = next(
            team
            for team in first["teams"]
            if team["teamName"] == "Promoted FC"
        )
        self.assertTrue(promoted["priorCompetitionClub"])
        self.assertEqual(2, promoted["historyGapCount"])
        self.assertEqual(
            "blocked",
            first["promotionBoundary"]["status"],
        )
        self.assertEqual(64, len(first["dataIdentitySha256"]))
        self.assertEqual(64, len(first["runIdentitySha256"]))

    def test_source_forecast_identity_and_counts_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            forecast_path = root / "forecast.json"
            document = self._write_forecast(forecast_path)

            tampered = copy.deepcopy(document)
            tampered["players"][0]["webName"] = "Tampered"
            forecast_path.write_text(
                json.dumps(tampered),
                encoding="utf-8",
            )
            with self.assertRaises(TemporalRidgeError) as context:
                build_prior_competition_coverage(
                    forecast_path,
                    ["Promoted FC"],
                )
            self.assertEqual(
                "coverage.forecast-identity-mismatch",
                context.exception.code,
            )

            inconsistent = copy.deepcopy(document)
            inconsistent["priorSeasonIdentityMissingCount"] = 2
            inconsistent = self._sign(inconsistent)
            forecast_path.write_text(
                json.dumps(inconsistent),
                encoding="utf-8",
            )
            with self.assertRaises(TemporalRidgeError) as context:
                build_prior_competition_coverage(
                    forecast_path,
                    ["Promoted FC"],
                )
            self.assertEqual(
                "coverage.forecast-invalid",
                context.exception.code,
            )

    def test_club_configuration_and_cli_overwrite_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            forecast_path = root / "forecast.json"
            output_path = root / "coverage.json"
            self._write_forecast(forecast_path)
            with self.assertRaises(TemporalRidgeError) as context:
                build_prior_competition_coverage(
                    forecast_path,
                    ["Unknown FC"],
                )
            self.assertEqual(
                "coverage.prior-competition-club-unknown",
                context.exception.code,
            )

            arguments = [
                "--forecast",
                str(forecast_path),
                "--prior-competition-club",
                "Promoted FC",
                "--output",
                str(output_path),
            ]
            self.assertEqual(0, main(arguments))
            stderr = StringIO()
            with redirect_stderr(stderr):
                result = main(arguments)
            self.assertEqual(1, result)
            self.assertIn("output.already-exists", stderr.getvalue())

    @classmethod
    def _write_forecast(cls, path: Path) -> dict:
        players = [
            cls._player(
                1,
                101,
                "Keeper",
                "goalkeeper",
                1,
                "Established FC",
                "stable-code-match",
            ),
            cls._player(
                2,
                102,
                "Defender",
                "defender",
                2,
                "Promoted FC",
                "no-prior-season-match",
            ),
            cls._player(
                3,
                103,
                "Forward",
                "forward",
                2,
                "Promoted FC",
                "no-prior-season-match",
            ),
            cls._player(
                4,
                104,
                "New Signing",
                "midfielder",
                1,
                "Established FC",
                "no-prior-season-match",
            ),
        ]
        document = {
            "schemaVersion": "1.0",
            "artifactType": (
                "historical-preseason-participation-player-forecast"
            ),
            "artifactVersion": (
                "preseason-participation-player-forecast-v1.2"
            ),
            "seasonCode": "2026-27",
            "gameweek": 1,
            "deadlineUtc": "2026-08-01T12:00:00+00:00",
            "decisionCutoffUtc": "2026-07-26T10:00:00+00:00",
            "officialCaptureId": 10,
            "playerCount": 4,
            "priorSeasonIdentityMatchCount": 1,
            "priorSeasonIdentityMissingCount": 3,
            "players": players,
            "dataIdentitySha256": "a" * 64,
        }
        signed = cls._sign(document)
        path.write_text(
            json.dumps(signed, sort_keys=True),
            encoding="utf-8",
        )
        return signed

    @staticmethod
    def _sign(document: dict) -> dict:
        signed = copy.deepcopy(document)
        signed.pop("runIdentitySha256", None)
        signed["runIdentitySha256"] = _sha256(signed)
        return signed

    @staticmethod
    def _player(
        player_id: int,
        player_code: int,
        web_name: str,
        position: str,
        team_id: int,
        team_name: str,
        identity_status: str,
    ) -> dict:
        return {
            "playerId": player_id,
            "playerCode": player_code,
            "webName": web_name,
            "position": position,
            "teamId": team_id,
            "teamName": team_name,
            "priorSeasonIdentityStatus": identity_status,
        }


if __name__ == "__main__":
    unittest.main()
