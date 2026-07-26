from __future__ import annotations

import hashlib
import json
import sys
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.official_underlying_ablation import (  # noqa: E402
    CANDIDATE_FEATURES,
    MODEL_NAMES,
    UNDERLYING_FEATURES,
    UNDERLYING_RIDGE,
    _augment_samples,
    evaluate_official_underlying_ablation,
    main,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    CONTINUOUS_FEATURES,
    MODEL_NAME as RIDGE_MODEL_NAME,
    _build_table,
    _open_connection,
    _samples_for_table,
)
from tests.analytics import test_temporal_ridge as ridge_test_helpers  # noqa: E402


class OfficialUnderlyingAblationTests(unittest.TestCase):
    def test_identical_folds_compare_core_and_underlying_variants(self) -> None:
        with ridge_test_helpers.TemporalRidgeTests._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_official_underlying_ablation(
                database,
                season_code="2026-27",
            )
            repeated = evaluate_official_underlying_ablation(
                database,
                season_code="2026-27",
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertEqual(
            "official-underlying-feature-ablation-v1",
            report["evaluatorVersion"],
        )
        self.assertEqual(
            "exploratory-feature-ablation-not-promoted",
            report["researchStatus"],
        )
        self.assertFalse(report["isPromoted"])
        self.assertEqual(1, report["eligibleFoldCount"])
        self.assertEqual(64, len(report["dataIdentitySha256"]))
        self.assertEqual(64, len(report["runIdentitySha256"]))
        self.assertEqual(set(MODEL_NAMES), {
            model["name"] for model in report["models"]
        })
        self.assertEqual(
            list(UNDERLYING_FEATURES),
            report["configuration"]["underlyingCandidateFeatures"],
        )
        self.assertEqual(
            len(CONTINUOUS_FEATURES) + len(UNDERLYING_FEATURES),
            len(CANDIDATE_FEATURES),
        )

        fold = report["folds"][0]
        self.assertEqual(4, fold["gameweek"])
        self.assertEqual(3, fold["trainingGameweeks"])
        self.assertEqual(3, fold["trainingRows"])
        self.assertEqual(
            [11, 20, 30],
            [item["outcomeCaptureId"] for item in fold["training"]],
        )
        self.assertEqual(
            82,
            fold["diagnostics"][RIDGE_MODEL_NAME]["modelFeatureCount"],
        )
        self.assertEqual(
            82 + (2 * len(UNDERLYING_FEATURES)),
            fold["diagnostics"][UNDERLYING_RIDGE]["modelFeatureCount"],
        )
        for model in fold["models"]:
            self.assertEqual(2, model["metrics"]["count"])

    def test_candidate_features_preserve_values_and_missingness(self) -> None:
        with ridge_test_helpers.TemporalRidgeTests._database() as database:
            table = _build_table(database, "2026-27", 4)
            connection = _open_connection(database)
            try:
                samples = _samples_for_table(connection, table, 40)
            finally:
                connection.close()
            augmented = _augment_samples(samples, table)

        player = next(sample for sample in augmented if sample.player_id == 1)
        new_player = next(
            sample for sample in augmented if sample.player_id == 2
        )
        self.assertEqual(
            0.6,
            player.features["historyRolling3ExpectedGoalsMean"],
        )
        self.assertEqual(
            3.0,
            player.features["historyRolling3ExpectedGoalsSampleCount"],
        )
        self.assertEqual(
            0.65,
            player.features["historyEwmaExpectedGoalsMean"],
        )
        self.assertIsNone(
            new_player.features["historyRolling3ExpectedGoalsMean"]
        )
        self.assertEqual(
            0.0,
            new_player.features[
                "historyRolling3ExpectedGoalsSampleCount"
            ],
        )
        self.assertEqual(CANDIDATE_FEATURES, tuple(player.features))

    def test_cli_reports_insufficient_data_and_refuses_overwrite(self) -> None:
        with ridge_test_helpers.TemporalRidgeTests._database() as database:
            insufficient = evaluate_official_underlying_ablation(
                database,
                season_code="2026-27",
                minimum_training_gameweeks=4,
            )
            output = database.parent / "official-underlying.json"
            first_exit = main(
                [
                    "--database",
                    str(database),
                    "--season",
                    "2026-27",
                    "--output",
                    str(output),
                ]
            )
            written = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                second_exit = main(
                    [
                        "--database",
                        str(database),
                        "--season",
                        "2026-27",
                        "--output",
                        str(output),
                    ]
                )

        self.assertEqual("insufficient-data", insufficient["status"])
        self.assertEqual(
            "no-official-eligible-folds",
            insufficient["reason"],
        )
        self.assertEqual(0, first_exit)
        self.assertEqual("complete", written["status"])
        self.assertEqual(1, second_exit)
        self.assertEqual(
            "output.already-exists",
            json.loads(errors.getvalue())["errorCode"],
        )


if __name__ == "__main__":
    unittest.main()
