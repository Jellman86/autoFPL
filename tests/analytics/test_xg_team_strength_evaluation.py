from __future__ import annotations

import sqlite3
import sys
import unittest
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))
TEST_ROOT = Path(__file__).resolve().parent
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from autofpl_analytics.team_goal_strength_evaluation import (  # noqa: E402
    BASELINE_MODEL,
    CHALLENGER_MODEL,
)
from autofpl_analytics.xg_team_strength_evaluation import (  # noqa: E402
    XG_MODEL,
    evaluate_xg_team_strength,
)


class XgTeamStrengthEvaluationTests(unittest.TestCase):
    def test_xg_and_goal_models_share_identical_folds(self) -> None:
        from test_team_goal_strength_evaluation import (
            TeamGoalStrengthEvaluationTests,
        )

        helper = TeamGoalStrengthEvaluationTests()
        with helper._database() as database:
            first = evaluate_xg_team_strength(
                database,
                evaluation_start_gameweek=5,
                minimum_training_origins=3,
            )
            second = evaluate_xg_team_strength(
                database,
                evaluation_start_gameweek=5,
                minimum_training_origins=3,
            )

        self.assertEqual(first, second)
        self.assertEqual("complete", first["status"])
        self.assertFalse(first["isPromoted"])
        self.assertEqual(4, first["eligibleFoldCount"])
        self.assertEqual(
            {BASELINE_MODEL, CHALLENGER_MODEL, XG_MODEL},
            {model["name"] for model in first["models"]},
        )
        for fold in first["folds"]:
            self.assertEqual(
                "goals",
                fold["diagnostics"][CHALLENGER_MODEL]["rateTarget"],
            )
            self.assertEqual(
                "expected-goals",
                fold["diagnostics"][XG_MODEL]["rateTarget"],
            )

    def test_future_expected_goals_cannot_change_earlier_fold(self) -> None:
        from test_team_goal_strength_evaluation import (
            TeamGoalStrengthEvaluationTests,
        )

        helper = TeamGoalStrengthEvaluationTests()
        with helper._database() as database:
            before = evaluate_xg_team_strength(
                database,
                evaluation_start_gameweek=7,
                minimum_training_origins=3,
            )["folds"][0]
            with sqlite3.connect(database) as writable:
                writable.execute(
                    """
                    UPDATE historical_fpl_player_gameweeks
                    SET expected_goals = '9.9'
                    WHERE capture_id = 2
                      AND gameweek = 8;
                    """
                )
            after = evaluate_xg_team_strength(
                database,
                evaluation_start_gameweek=7,
                minimum_training_origins=3,
            )["folds"][0]

        self.assertEqual(before, after)


if __name__ == "__main__":
    unittest.main()
