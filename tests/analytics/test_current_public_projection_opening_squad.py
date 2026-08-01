from __future__ import annotations

import sys
import unittest
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_public_projection_opening_squad import (  # noqa: E402
    _match_projections,
    _parse_source_document,
    _projection_overlay_matrices,
)
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402


class CurrentPublicProjectionOpeningSquadTests(unittest.TestCase):
    def test_source_is_cutoff_bound_and_matches_only_exact_identity(self) -> None:
        scenario = {
            "openingGameweek": 1,
            "deadlineUtc": "2026-08-21T17:30:00+00:00",
        }
        snapshot = {"availableAtUtc": "2026-08-01T17:00:00+00:00"}
        rows = [
            {
                "name": f"Player {index}",
                "team": f"T{index:02d}",
                "position": ("GK", "DEF", "MID", "FWD")[index % 4],
                "price": 40 + index,
                "prPoints": 2.0 + index / 10,
            }
            for index in range(20)
        ]
        document = {
            "gameweek": 1,
            "deadlineIso": "2026-08-21T17:30:00.000Z",
            "generatedAt": "2026-08-01T16:00:00.000Z",
            "source": "https://fpl.solioanalytics.com/api/data/latest",
            "topProjected": rows,
        }

        projections = _parse_source_document(
            document,
            scenario,
            snapshot,
        )
        official = {
            index + 1: {
                "webName": f"Player {index}",
                "teamShortName": f"T{index:02d}",
                "position": ("goalkeeper", "defender", "midfielder", "forward")[
                    index % 4
                ],
                "priceTenths": 40 + index,
            }
            for index in range(20)
        }
        matches, unmatched = _match_projections(projections, official)

        self.assertEqual(20, len(matches))
        self.assertEqual([], unmatched)
        self.assertEqual(2.0, matches[1]["projectedPoints"])

        document["generatedAt"] = "2026-08-22T16:00:00.000Z"
        with self.assertRaises(TemporalRidgeError):
            _parse_source_document(document, scenario, snapshot)

    def test_overlay_changes_only_matched_gameweek_one_mean(self) -> None:
        candidates = [
            {"playerId": 10},
            {"playerId": 20},
        ]
        matrices = [
            (
                np.asarray([[1, 2], [3, 4]], dtype=np.int64),
                np.asarray([[True, True], [True, True]]),
            ),
            (
                np.asarray([[5, 6], [7, 8]], dtype=np.int64),
                np.asarray([[True, True], [True, True]]),
            ),
            (
                np.asarray([[1, 1], [1, 1]], dtype=np.int64),
                np.asarray([[True, True], [True, True]]),
            ),
            (
                np.asarray([[2, 2], [2, 2]], dtype=np.int64),
                np.asarray([[True, True], [True, True]]),
            ),
            (
                np.asarray([[3, 3], [3, 3]], dtype=np.int64),
                np.asarray([[True, True], [True, True]]),
            ),
            (
                np.asarray([[4, 4], [4, 4]], dtype=np.int64),
                np.asarray([[True, True], [True, True]]),
            ),
        ]
        adjusted = _projection_overlay_matrices(
            matrices,
            candidates,
            {20: {"projectedPoints": 9.25}},
        )

        self.assertEqual([1.0, 3.0], adjusted[0][0][:, 0].tolist())
        self.assertEqual([9.25, 9.25], adjusted[0][0][:, 1].tolist())
        self.assertEqual([[5.0, 6.0], [7.0, 8.0]], adjusted[1][0].tolist())
        self.assertEqual([[1, 2], [3, 4]], matrices[0][0].tolist())


if __name__ == "__main__":
    unittest.main()
