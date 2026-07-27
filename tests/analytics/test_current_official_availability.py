from __future__ import annotations

import sys
import unittest
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.current_official_availability import (  # noqa: E402
    RULE_VERSION,
    constrain_factorized_participation,
    official_appearance_ceiling,
)
from autofpl_analytics.temporal_ridge import TemporalRidgeError  # noqa: E402


class CurrentOfficialAvailabilityTests(unittest.TestCase):
    def test_fixed_status_mapping_is_explicit(self) -> None:
        for chance in (None, 100):
            with self.subTest(status="a", chance=chance):
                document = official_appearance_ceiling("a", chance)
                self.assertEqual(RULE_VERSION, document["ruleVersion"])
                self.assertEqual(
                    1.0,
                    document["appearanceProbabilityCeiling"],
                )
        doubtful = official_appearance_ceiling("d", 75)
        self.assertEqual(0.75, doubtful["appearanceProbabilityCeiling"])
        for status in ("i", "n", "s", "u"):
            with self.subTest(status=status):
                zero = official_appearance_ceiling(status, 0)
                self.assertEqual(
                    0.0,
                    zero["appearanceProbabilityCeiling"],
                )

    def test_unknown_or_inconsistent_status_fails_closed(self) -> None:
        for status, chance in (
            ("a", 75),
            ("d", None),
            ("d", 0),
            ("d", 100),
            ("i", None),
            ("s", 50),
            ("x", 0),
            ("a", -1),
            ("a", 101),
            ("a", 50.0),
            ("a", True),
        ):
            with self.subTest(status=status, chance=chance):
                with self.assertRaises(TemporalRidgeError):
                    official_appearance_ceiling(status, chance)

    def test_ceiling_preserves_conditionals_and_coherence(self) -> None:
        constrained = constrain_factorized_participation(
            appearance_probability=0.9,
            start_given_appearance=0.8,
            played_60_given_appearance=0.7,
            ceiling=0.75,
        )
        self.assertAlmostEqual(
            0.75,
            constrained["appearanceProbability"],
        )
        self.assertAlmostEqual(0.6, constrained["startProbability"])
        self.assertAlmostEqual(
            0.525,
            constrained["played60Probability"],
        )
        unchanged = constrain_factorized_participation(
            0.4,
            0.8,
            0.7,
            0.75,
        )
        self.assertAlmostEqual(
            0.4,
            unchanged["appearanceProbability"],
        )
        self.assertAlmostEqual(0.32, unchanged["startProbability"])
        zero = constrain_factorized_participation(0.9, 0.8, 0.7, 0.0)
        self.assertEqual(
            {
                "appearanceProbability": 0.0,
                "startProbability": 0.0,
                "played60Probability": 0.0,
            },
            zero,
        )
        for values in (
            (-0.1, 0.5, 0.5, 1.0),
            (0.5, 1.1, 0.5, 1.0),
            (0.5, 0.5, float("nan"), 1.0),
            (0.5, 0.5, 0.5, 1.1),
        ):
            with self.subTest(values=values):
                with self.assertRaises(TemporalRidgeError):
                    constrain_factorized_participation(*values)


if __name__ == "__main__":
    unittest.main()
