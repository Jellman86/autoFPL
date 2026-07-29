from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.historical_joint_scenario_evaluation import (  # noqa: E402
    APPEARANCE_MODEL,
    MODEL_NAME,
    PLAYER_EMPIRICAL_MODEL,
    POSITION_EMPIRICAL_MODEL,
    QUANTISED_APPEARANCE_MODEL,
    TREE_DEGENERATE_MODEL,
    evaluate_historical_joint_scenarios,
    generate_joint_fold,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    Prediction,
    Sample,
    TemporalRidgeError,
)
from tests.analytics import (  # noqa: E402
    test_historical_preseason_evaluation as historical_helpers,
)


class HistoricalJointScenarioEvaluationTests(unittest.TestCase):
    def test_retrospective_folds_are_deterministic_and_read_only(self) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            first = evaluate_historical_joint_scenarios(
                database,
                minimum_training_gameweeks=3,
                evaluation_start_gameweek=10,
            )
            second = evaluate_historical_joint_scenarios(
                database,
                minimum_training_gameweeks=3,
                evaluation_start_gameweek=10,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual("complete", first["status"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["mayInfluenceAdvice"])
        self.assertEqual(3, first["foldCount"])
        self.assertEqual(
            {
                MODEL_NAME,
                TREE_DEGENERATE_MODEL,
                PLAYER_EMPIRICAL_MODEL,
                POSITION_EMPIRICAL_MODEL,
            },
            {
                model["name"]
                for model in first["distributionModels"]
            },
        )
        self.assertEqual(
            {
                QUANTISED_APPEARANCE_MODEL,
                APPEARANCE_MODEL,
                "player-appearance-empirical",
            },
            {model["name"] for model in first["appearanceModels"]},
        )
        self.assertEqual(
            [10, 11, 12],
            [fold["gameweek"] for fold in first["folds"]],
        )
        for fold in first["folds"]:
            self.assertEqual(
                len(fold["trainingGameweeks"]),
                fold["scenarioCount"],
            )
            self.assertEqual(
                0,
                fold["diagnostics"]["nonPlayingNonZeroPointCount"],
            )

    def test_future_outcome_does_not_change_an_earlier_fold(self) -> None:
        with self._database() as database:
            before = evaluate_historical_joint_scenarios(
                database,
                minimum_training_gameweeks=3,
                evaluation_start_gameweek=10,
            )["folds"][0]
            with sqlite3.connect(database) as writable:
                writable.execute(
                    """
                    UPDATE historical_fpl_player_gameweeks
                    SET total_points = 99,
                        minutes = 180,
                        starts = 2
                    WHERE gameweek = 12 AND player_code = 1001;
                    """
                )
            after = evaluate_historical_joint_scenarios(
                database,
                minimum_training_gameweeks=3,
                evaluation_start_gameweek=10,
            )["folds"][0]

        self.assertEqual(before, after)

    def test_joint_fold_keeps_rows_aligned_and_zeroes_nonplayers(self) -> None:
        point_rows = {
            1: [
                self._sample(1, 1, "midfielder", 5),
                self._sample(1, 2, "midfielder", 2),
            ],
            2: [
                self._sample(2, 1, "midfielder", 0),
                self._sample(2, 2, "midfielder", 8),
            ],
        }
        appearance_rows = {
            1: [
                self._sample(1, 1, "midfielder", 1),
                self._sample(1, 2, "midfielder", 1),
            ],
            2: [
                self._sample(2, 1, "midfielder", 0),
                self._sample(2, 2, "midfielder", 1),
            ],
        }
        target = [
            self._sample(3, 1, "midfielder", 0),
            self._sample(3, 3, "midfielder", 0),
        ]
        means = [
            self._prediction(3, 1, "midfielder", 3.0),
            self._prediction(3, 3, "midfielder", 4.0),
        ]
        appearances = [
            self._prediction(3, 1, "midfielder", 0.5),
            self._prediction(3, 3, "midfielder", 1.0),
        ]

        fold = generate_joint_fold(
            point_rows,
            appearance_rows,
            target,
            means,
            appearances,
        )

        self.assertEqual((1, 3), fold.player_ids)
        self.assertEqual((1, 2), fold.source_gameweeks)
        self.assertEqual((2, 2), fold.points.shape)
        self.assertEqual((2, 2), fold.played.shape)
        self.assertEqual(2, fold.self_donor_assignments)
        self.assertEqual(2, fold.fallback_donor_assignments)
        self.assertEqual(1, int(np.sum(fold.played[:, 0])))
        self.assertEqual(2, int(np.sum(fold.played[:, 1])))
        self.assertFalse(np.any((fold.points != 0) & ~fold.played))

    def test_misaligned_point_and_appearance_rows_fail_closed(self) -> None:
        points = {1: [self._sample(1, 1, "forward", 2)]}
        appearance = {1: [self._sample(1, 2, "forward", 1)]}

        with self.assertRaises(TemporalRidgeError) as caught:
            generate_joint_fold(
                points,
                appearance,
                [self._sample(2, 1, "forward", 0)],
                [self._prediction(2, 1, "forward", 2.0)],
                [self._prediction(2, 1, "forward", 0.8)],
            )

        self.assertEqual(
            "scenario.player-alignment",
            caught.exception.code,
        )

    def test_missing_archive_and_cli_overwrite_fail_closed(self) -> None:
        with self._database(include_capture=False) as database:
            missing = evaluate_historical_joint_scenarios(
                database,
                minimum_training_gameweeks=3,
                evaluation_start_gameweek=10,
            )
        self.assertEqual("insufficient-data", missing["status"])
        self.assertEqual(
            "historical-season-archive-not-found",
            missing["reason"],
        )

        with self._database() as database:
            output = database.parent / "joint-scenario.json"
            arguments = [
                "--database",
                str(database),
                "--minimum-training-gameweeks",
                "3",
                "--evaluation-start-gameweek",
                "10",
                "--output",
                str(output),
            ]
            self.assertEqual(0, main(arguments))
            written = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                repeated = main(arguments)

        self.assertEqual("complete", written["status"])
        self.assertEqual(1, repeated)
        self.assertIn("output.already-exists", errors.getvalue())

    @staticmethod
    def _sample(
        gameweek: int,
        player_id: int,
        position: str,
        actual: int,
    ) -> Sample:
        return Sample(
            season_code="2025-26",
            gameweek=gameweek,
            player_id=player_id,
            position=position,
            features={},
            actual=actual,
        )

    @staticmethod
    def _prediction(
        gameweek: int,
        player_id: int,
        position: str,
        value: float,
    ) -> Prediction:
        return Prediction(
            model="fixture",
            season_code="2025-26",
            gameweek=gameweek,
            player_id=player_id,
            position=position,
            predicted=value,
            actual=0,
        )

    def _database(self, include_capture: bool = True):
        helper = (
            historical_helpers.HistoricalPreseasonEvaluationTests()
        )
        return helper._database(include_capture=include_capture)


if __name__ == "__main__":
    unittest.main()
