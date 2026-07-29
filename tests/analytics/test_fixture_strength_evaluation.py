from __future__ import annotations

import sys
import unittest
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))
TEST_ROOT = Path(__file__).resolve().parent
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from autofpl_analytics.fixture_strength_evaluation import (  # noqa: E402
    ALL_FEATURES,
    FIXTURE_FEATURES,
    FIXTURE_MODEL,
    _build_fixture_feature_table,
    evaluate_fixture_strength,
)
from autofpl_analytics.historical_preseason_evaluation import (  # noqa: E402
    _load_capture,
)
from autofpl_analytics.multi_season_evaluation import (  # noqa: E402
    Origin,
    TREE_MODEL,
    _build_feature_table,
    _open_connection,
)
class FixtureStrengthEvaluationTests(unittest.TestCase):
    def test_identical_fold_ablation_is_deterministic_and_not_promoted(
        self,
    ) -> None:
        from test_multi_season_evaluation import MultiSeasonEvaluationTests

        helper = MultiSeasonEvaluationTests()
        with helper._database() as database:
            first = evaluate_fixture_strength(
                database,
                evaluation_start_gameweek=2,
                minimum_training_origins=3,
            )
            second = evaluate_fixture_strength(
                database,
                evaluation_start_gameweek=2,
                minimum_training_origins=3,
            )

        self.assertEqual(first, second)
        self.assertEqual("complete", first["status"])
        self.assertEqual(4, first["eligibleFoldCount"])
        self.assertFalse(first["isPromoted"])
        self.assertFalse(first["comparison"]["isPromoted"])
        self.assertEqual(
            {TREE_MODEL, FIXTURE_MODEL},
            {model["name"] for model in first["models"]},
        )
        self.assertEqual(
            list(FIXTURE_FEATURES),
            first["configuration"]["fixtureStrengthFeatures"],
        )

    def test_fixture_features_use_only_earlier_gameweeks(self) -> None:
        from test_multi_season_evaluation import MultiSeasonEvaluationTests

        helper = MultiSeasonEvaluationTests()
        with helper._database() as database:
            connection = _open_connection(database)
            try:
                captures = [
                    _load_capture(connection, season)
                    for season in ("2024-25", "2025-26")
                ]
                exact = [
                    capture for capture in captures if capture is not None
                ]
                plain = _build_feature_table(connection, exact)
                before = _build_fixture_feature_table(
                    connection, exact, plain
                )
            finally:
                connection.close()
            target_before = next(
                sample
                for sample in before[Origin(1, 4, "2025-26")]
                if sample.player_id == 1001
            )
            import sqlite3

            with sqlite3.connect(database) as writable:
                writable.execute(
                    """
                    UPDATE historical_fpl_player_gameweeks
                    SET total_points = 99
                    WHERE capture_id = 2
                      AND gameweek = 5;
                    """
                )
            connection = _open_connection(database)
            try:
                plain = _build_feature_table(connection, exact)
                after = _build_fixture_feature_table(
                    connection, exact, plain
                )
            finally:
                connection.close()
            target_after = next(
                sample
                for sample in after[Origin(1, 4, "2025-26")]
                if sample.player_id == 1001
            )

        self.assertEqual(target_before, target_after)
        self.assertEqual(tuple(ALL_FEATURES), tuple(target_before.features))


if __name__ == "__main__":
    unittest.main()
