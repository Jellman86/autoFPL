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

from autofpl_analytics.historical_conditional_participation_evaluation import (  # noqa: E402
    _factorized_probability,
    evaluate_historical_conditional_participation,
    main,
)
from autofpl_analytics.historical_participation_coherence_evaluation import (  # noqa: E402
    evaluate_historical_participation_coherence,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
    _sha256,
)
from tests.analytics import (  # noqa: E402
    test_historical_preseason_evaluation as historical_helpers,
)


class HistoricalConditionalParticipationEvaluationTests(unittest.TestCase):
    def test_factorization_is_exact_and_fail_closed(self) -> None:
        self.assertEqual(0.0, _factorized_probability(0.0, 1.0))
        self.assertEqual(0.24, _factorized_probability(0.3, 0.8))
        self.assertEqual(1.0, _factorized_probability(1.0, 1.0))
        for appearance, conditional in (
            (-0.1, 0.5),
            (1.1, 0.5),
            (0.5, float("nan")),
            (float("inf"), 0.5),
        ):
            with self.subTest(
                appearance=appearance,
                conditional=conditional,
            ):
                with self.assertRaises(TemporalRidgeError):
                    _factorized_probability(appearance, conditional)

    def test_evaluation_preserves_appearance_and_is_read_only(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            raw_path = database.parent / "raw.json"
            raw = evaluate_historical_participation_coherence(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            raw_path.write_text(
                json.dumps(raw, sort_keys=True),
                encoding="utf-8",
            )
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            report = evaluate_historical_conditional_participation(
                database,
                raw_path,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            repeated = evaluate_historical_conditional_participation(
                database,
                raw_path,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(report, repeated)
        self.assertEqual(before, after)
        self.assertEqual("complete", report["status"])
        self.assertEqual(
            "exploratory-reused-holdout-candidate-screen-not-promotion",
            report["researchStatus"],
        )
        self.assertFalse(report["isPromoted"])
        self.assertFalse(report["canReplaceCurrentRawProbabilities"])
        self.assertFalse(report["productImportReady"])
        self.assertEqual(4, report["holdout"]["foldCount"])
        self.assertEqual(0, report["holdout"]["coherenceViolationCount"])
        self.assertTrue(report["holdout"]["rawAppearanceReproduced"])
        self.assertEqual(
            [9, 10, 11, 12],
            [fold["gameweek"] for fold in report["holdout"]["folds"]],
        )
        appearance = next(
            target
            for target in report["targets"]
            if target["target"] == "appearance"
        )
        self.assertEqual(
            appearance["raw"]["metrics"],
            appearance["factorized"]["metrics"],
        )
        for fold in report["holdout"]["folds"]:
            fold_appearance = next(
                target
                for target in fold["targets"]
                if target["target"] == "appearance"
            )
            self.assertEqual(
                fold_appearance["raw"]["metrics"],
                fold_appearance["factorized"]["metrics"],
            )
            self.assertEqual(
                fold_appearance["raw"]["slices"],
                fold_appearance["factorized"]["slices"],
            )
            for child in ("start", "played-60"):
                diagnostics = fold["diagnostics"]["conditional"][child]
                self.assertGreater(diagnostics["trainingRows"], 0)
                self.assertLess(
                    diagnostics["trainingRows"],
                    fold["diagnostics"]["appearance"]["trainingRows"],
                )

    def test_reproduced_appearance_mismatch_fails_closed(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            raw = evaluate_historical_participation_coherence(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            appearance = next(
                target
                for target in raw["lockedHoldout"]["folds"][0]["targets"]
                if target["target"] == "appearance"
            )
            appearance["raw"]["metrics"]["brierScore"] = 0
            raw["runIdentitySha256"] = _sha256(
                {
                    key: value
                    for key, value in raw.items()
                    if key != "runIdentitySha256"
                }
            )
            raw_path = database.parent / "mismatched-raw.json"
            raw_path.write_text(
                json.dumps(raw, sort_keys=True),
                encoding="utf-8",
            )
            with self.assertRaises(TemporalRidgeError) as error:
                evaluate_historical_conditional_participation(
                    database,
                    raw_path,
                    minimum_training_gameweeks=3,
                    holdout_start_gameweek=9,
                )
        self.assertEqual(
            "evaluation.raw-appearance-reproduction-mismatch",
            error.exception.code,
        )

    def test_raw_integrity_and_cli_overwrite_fail_closed(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            raw = evaluate_historical_participation_coherence(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            tampered = json.loads(json.dumps(raw))
            tampered["targets"][0]["raw"]["metrics"]["brierScore"] = 0
            tampered_path = database.parent / "tampered-raw.json"
            tampered_path.write_text(
                json.dumps(tampered, sort_keys=True),
                encoding="utf-8",
            )
            with self.assertRaises(TemporalRidgeError) as tampered_error:
                evaluate_historical_conditional_participation(
                    database,
                    tampered_path,
                    minimum_training_gameweeks=3,
                    holdout_start_gameweek=9,
                )
            self.assertEqual(
                "evaluation.raw-report-integrity-mismatch",
                tampered_error.exception.code,
            )

            raw_path = database.parent / "raw.json"
            raw_path.write_text(
                json.dumps(raw, sort_keys=True),
                encoding="utf-8",
            )
            output = database.parent / "conditional.json"
            arguments = [
                "--database",
                str(database),
                "--raw-report",
                str(raw_path),
                "--minimum-training-gameweeks",
                "3",
                "--holdout-start-gameweek",
                "9",
                "--output",
                str(output),
            ]
            self.assertEqual(0, main(arguments))
            errors = StringIO()
            with redirect_stderr(errors):
                repeated = main(arguments)
        self.assertEqual(1, repeated)
        self.assertIn("output.already-exists", errors.getvalue())


if __name__ == "__main__":
    unittest.main()
