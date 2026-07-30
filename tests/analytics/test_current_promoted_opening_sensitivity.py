from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_promoted_opening_sensitivity import (  # noqa: E402,E501
    _load_current_sources,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)


class CurrentPromotedOpeningSensitivityTests(unittest.TestCase):
    def test_reviewed_stable_code_can_join_a_later_current_roster(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "extraction.json"
            path.write_text(
                json.dumps(self._document()),
                encoding="utf-8",
            )

            players, identity = _load_current_sources(
                path,
                official_capture_id=19,
                current_player_codes={146426},
            )

        self.assertEqual({146426}, set(players))
        self.assertEqual(13, identity["sourceIdentityCaptureId"])
        self.assertEqual(19, identity["forecastOfficialCaptureId"])
        self.assertEqual(22, players[146426].appearances)

    def test_reviewed_code_absent_from_current_roster_fails_closed(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "extraction.json"
            path.write_text(
                json.dumps(self._document()),
                encoding="utf-8",
            )

            with self.assertRaises(TemporalRidgeError):
                _load_current_sources(
                    path,
                    official_capture_id=19,
                    current_player_codes={999999},
                )

    @staticmethod
    def _document() -> dict:
        return {
            "schemaVersion": "1.0",
            "sourceKey": (
                "fbref-championship-playing-time-2025-26"
            ),
            "competitionSeason": "2025-26",
            "snapshotId": 27,
            "contentSha256": (
                "0c80ce784b02cbcb21692859effc969109c6e54376e9f511ad3b6b49b32fa55b"
            ),
            "identityCaptureId": 13,
            "rowCount": 1,
            "reviewedIdentityCount": 1,
            "players": [
                {
                    "sourcePlayerId": "d6192210",
                    "playerName": "Semi Ajayi",
                    "teamName": "Hull City",
                    "appearances": 22,
                    "starts": 18,
                    "minutes": 1656,
                    "identityStatus": "reviewed-v1",
                    "officialPlayerCode": 146426,
                }
            ],
        }


if __name__ == "__main__":
    unittest.main()
