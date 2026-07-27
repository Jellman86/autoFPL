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

from autofpl_analytics.historical_joint_participation_evaluation import (  # noqa: E402
    _marginals,
    _state,
    _state_marginals,
    evaluate_historical_joint_participation,
    main,
)
from autofpl_analytics.historical_participation_coherence_evaluation import (  # noqa: E402
    evaluate_historical_participation_coherence,
)
from autofpl_analytics.temporal_ridge import (  # noqa: E402
    TemporalRidgeError,
)
from tests.analytics import (  # noqa: E402
    test_historical_preseason_evaluation as historical_helpers,
)


class HistoricalJointParticipationEvaluationTests(unittest.TestCase):
    def test_five_states_and_marginals_are_exact(self) -> None:
        combinations = (
            ((0, 0, 0), 0),
            ((1, 0, 0), 1),
            ((1, 0, 1), 2),
            ((1, 1, 0), 3),
            ((1, 1, 1), 4),
        )
        for values, expected in combinations:
            with self.subTest(values=values):
                state = _state(*values)
                self.assertEqual(expected, state)
                self.assertEqual(
                    {
                        "appearance": values[0],
                        "start": values[1],
                        "played-60": values[2],
                    },
                    _state_marginals(state),
                )
        marginals = _marginals((0.2, 0.3, 0.2, 0.1, 0.2))
        self.assertAlmostEqual(0.8, marginals["appearance"])
        self.assertAlmostEqual(0.3, marginals["start"])
        self.assertAlmostEqual(0.4, marginals["played-60"])

    def test_invalid_states_and_probabilities_fail_closed(self) -> None:
        for values in ((0, 1, 0), (0, 0, 1), (2, 0, 0)):
            with self.subTest(values=values):
                with self.assertRaises(TemporalRidgeError):
                    _state(*values)
        for probabilities in (
            (0.5, 0.5),
            (0.2, 0.2, 0.2, 0.2, 0.3),
            (-0.1, 0.2, 0.2, 0.3, 0.4),
            (float("nan"), 0.25, 0.25, 0.25, 0.25),
        ):
            with self.subTest(probabilities=probabilities):
                with self.assertRaises(TemporalRidgeError):
                    _marginals(probabilities)

    def test_evaluation_reuses_raw_identity_and_is_read_only(self) -> None:
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
            report = evaluate_historical_joint_participation(
                database,
                raw_path,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            repeated = evaluate_historical_joint_participation(
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
        self.assertEqual(
            [9, 10, 11, 12],
            [fold["gameweek"] for fold in report["holdout"]["folds"]],
        )
        self.assertEqual(12 * 4, report["jointStateMetrics"]["count"])
        self.assertEqual(
            {"appearance", "start", "played-60"},
            {target["target"] for target in report["targets"]},
        )
        for target in report["targets"]:
            self.assertFalse(target["recommendation"]["isPromoted"])

    def test_raw_identity_mismatch_and_cli_overwrite_fail_closed(self) -> None:
        helper = historical_helpers.HistoricalPreseasonEvaluationTests()
        with helper._database() as database:
            raw = evaluate_historical_participation_coherence(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            invalid_path = database.parent / "invalid-raw.json"
            raw["provenance"]["sourceRevision"] = "wrong"
            invalid_path.write_text(
                json.dumps(raw, sort_keys=True),
                encoding="utf-8",
            )
            with self.assertRaises(TemporalRidgeError):
                evaluate_historical_joint_participation(
                    database,
                    invalid_path,
                    minimum_training_gameweeks=3,
                    holdout_start_gameweek=9,
                )

            valid_raw = evaluate_historical_participation_coherence(
                database,
                minimum_training_gameweeks=3,
                holdout_start_gameweek=9,
            )
            tampered = json.loads(json.dumps(valid_raw))
            tampered["targets"][0]["raw"]["metrics"]["brierScore"] = 0
            tampered_path = database.parent / "tampered-raw.json"
            tampered_path.write_text(
                json.dumps(tampered, sort_keys=True),
                encoding="utf-8",
            )
            with self.assertRaises(TemporalRidgeError) as tampered_error:
                evaluate_historical_joint_participation(
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
                json.dumps(valid_raw, sort_keys=True),
                encoding="utf-8",
            )
            output = database.parent / "joint.json"
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
