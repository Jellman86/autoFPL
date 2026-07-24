from __future__ import annotations

import unittest

from tools.governance.check_pr_title import is_valid_pr_title


class PullRequestTitlePolicyTests(unittest.TestCase):
    def test_accepts_conventional_titles(self) -> None:
        accepted = (
            "feat: add squad projection endpoint",
            "fix(optimizer): reject infeasible transfer plan",
            "docs!: revise public data boundary",
            "chore(release): v0.1.0",
        )
        for title in accepted:
            with self.subTest(title=title):
                self.assertTrue(is_valid_pr_title(title))

    def test_rejects_non_conventional_or_unbounded_titles(self) -> None:
        rejected = (
            "Add squad projection endpoint",
            "feat add squad projection endpoint",
            "feature: add squad projection endpoint",
            "feat: Add squad projection endpoint",
            "feat: x",
            "feat: " + "x" * 95,
        )
        for title in rejected:
            with self.subTest(title=title):
                self.assertFalse(is_valid_pr_title(title))


if __name__ == "__main__":
    unittest.main()
