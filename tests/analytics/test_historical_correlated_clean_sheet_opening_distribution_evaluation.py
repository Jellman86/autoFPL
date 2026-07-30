from __future__ import annotations

import sys
import json
import tempfile
import unittest
from pathlib import Path

import numpy as np

sys.path.insert(
    0,
    str(Path(__file__).resolve().parents[2] / "src" / "analytics"),
)

from autofpl_analytics.historical_correlated_clean_sheet_opening_distribution_evaluation import (  # noqa: E402,E501
    CHALLENGER_APPEARANCE,
    CHALLENGER_MODEL,
    INCUMBENT_APPEARANCE,
    INCUMBENT_MODEL,
    SCORELINE_REPLICATION_DATA_IDENTITY,
    SCORELINE_REPLICATION_RUN_IDENTITY,
    _correlated_clean_sheet_joint,
    _load_scoreline_identity,
    _reconcile_column,
    _scoreline_clean_sheet_rows,
    _screen,
)
from autofpl_analytics.temporal_ridge import Prediction, Sample  # noqa: E402
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402


class HistoricalCorrelatedCleanSheetOpeningDistributionTests(
    unittest.TestCase
):
    def test_fixture_scoreline_draws_preserve_joint_clean_sheets(self):
        rows, diagnostics = _scoreline_clean_sheet_rows(
            [
                {
                    "fixtureId": 17,
                    "homeTeam": "Home",
                    "awayTeam": "Away",
                    "homeRate": 1.0,
                    "awayRate": 1.0,
                    "rho": 0.0,
                    "scoreMatrix": np.full((2, 2), 0.25),
                    "homeCleanSheetProbability": 0.5,
                    "awayCleanSheetProbability": 0.5,
                }
            ],
            4,
            1,
        )

        home = rows[(17, "Home")]
        away = rows[(17, "Away")]
        self.assertEqual(2, int(home.sum()))
        self.assertEqual(2, int(away.sum()))
        self.assertEqual(1, int((home & away).sum()))
        self.assertEqual(0.25, diagnostics[0]["zeroZeroScenarioFrequency"])

    def test_joint_replaces_donor_clean_sheets_and_preserves_mean(self):
        point_donors = {
            gameweek: [
                self._sample(gameweek, points)
            ]
            for gameweek, points in enumerate((2, 6, 2, 6), start=1)
        }
        residual_donors = {
            gameweek: [
                self._sample(gameweek, 2)
            ]
            for gameweek in point_donors
        }
        appearance_donors = {
            gameweek: [
                self._sample(gameweek, 1)
            ]
            for gameweek in point_donors
        }
        target = [self._sample(5, 0)]
        mean = [self._prediction(5, 4.0)]
        appearance = [self._prediction(5, 1.0)]

        result, diagnostics = _correlated_clean_sheet_joint(
            point_donors,
            residual_donors,
            appearance_donors,
            target,
            mean,
            appearance,
            {(101, 99): 1.0},
            [
                {
                    "fixtureId": 99,
                    "homeTeam": "Team",
                    "awayTeam": "Opponent",
                    "homeRate": 1.0,
                    "awayRate": 1.0,
                    "rho": 0.0,
                    "scoreMatrix": np.full((2, 2), 0.25),
                    "homeCleanSheetProbability": 0.5,
                    "awayCleanSheetProbability": 0.5,
                }
            ],
            {101: "Team"},
        )

        self.assertEqual(4.0, float(np.mean(result.points[:, 0])))
        self.assertEqual({2, 6}, set(result.points[:, 0].tolist()))
        self.assertEqual(
            0.0,
            diagnostics[
                "maximumAbsoluteMeanDeltaAfterReconciliation"
            ],
        )

    def test_reconciliation_respects_nonplaying_rows(self):
        points = np.asarray([0, 2, 3], dtype=np.int64)
        played = np.asarray([False, True, True])

        _reconcile_column(points, played, 3, 1, 101)

        self.assertEqual(0, int(points[0]))
        self.assertEqual(8, int(points.sum()))

    def test_screen_requires_material_crps_gain(self):
        appearances = [
            self._appearance_summary(INCUMBENT_APPEARANCE),
            self._appearance_summary(CHALLENGER_APPEARANCE),
        ]
        passing = _screen(
            [
                self._distribution_summary(INCUMBENT_MODEL, 1.0),
                self._distribution_summary(CHALLENGER_MODEL, 0.98),
            ],
            appearances,
            self._targets(1.0, 0.98),
        )
        failing = _screen(
            [
                self._distribution_summary(INCUMBENT_MODEL, 1.0),
                self._distribution_summary(CHALLENGER_MODEL, 1.001),
            ],
            appearances,
            self._targets(1.0, 1.001),
        )

        self.assertTrue(passing["passes"])
        self.assertFalse(failing["passes"])
        self.assertFalse(
            failing["gates"]["aggregateCrpsImprovement"]
        )

    def test_scoreline_source_identity_is_required(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "scoreline.json"
            document = {
                "dataIdentitySha256": (
                    SCORELINE_REPLICATION_DATA_IDENTITY
                ),
                "runIdentitySha256": (
                    SCORELINE_REPLICATION_RUN_IDENTITY
                ),
                "decision": (
                    "retain-scoreline-model-for-player-component-ablation"
                ),
            }
            path.write_text(json.dumps(document), encoding="utf-8")

            self.assertEqual(
                SCORELINE_REPLICATION_DATA_IDENTITY,
                _load_scoreline_identity(path)[
                    "dataIdentitySha256"
                ],
            )
            document["decision"] = "rejected"
            path.write_text(json.dumps(document), encoding="utf-8")
            with self.assertRaisesRegex(
                TemporalRidgeError,
                "scoreline replication identity",
            ):
                _load_scoreline_identity(path)

    @staticmethod
    def _sample(gameweek: int, actual: int) -> Sample:
        return Sample(
            season_code="test",
            gameweek=gameweek,
            player_id=101,
            position="defender",
            features={},
            actual=actual,
        )

    @staticmethod
    def _prediction(gameweek: int, predicted: float) -> Prediction:
        return Prediction(
            model="test",
            season_code="test",
            gameweek=gameweek,
            player_id=101,
            position="defender",
            predicted=predicted,
            actual=0,
        )

    @staticmethod
    def _distribution_summary(name: str, crps: float):
        return {
            "name": name,
            "metrics": {"meanCrps": crps},
            "slices": {
                "position": {
                    "defender": {"meanCrps": crps},
                }
            },
        }

    @staticmethod
    def _appearance_summary(name: str):
        return {
            "name": name,
            "metrics": {
                "brierScore": 0.2,
                "logLoss": 0.5,
            },
        }

    @classmethod
    def _targets(cls, incumbent: float, challenger: float):
        return [
            {
                "targetSeasonCode": season,
                "distributionModels": [
                    cls._distribution_summary(
                        INCUMBENT_MODEL,
                        incumbent,
                    ),
                    cls._distribution_summary(
                        CHALLENGER_MODEL,
                        challenger,
                    ),
                ],
                "meanPreservation": {
                    "maximumAbsolutePointMeanDelta": 0.0,
                },
            }
            for season in ("a", "b", "c")
        ]


if __name__ == "__main__":
    unittest.main()
