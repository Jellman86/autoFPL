from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.governance.check_research_review import validate_research_gate


class ResearchReviewPolicyTests(unittest.TestCase):
    def test_requires_record_for_executable_analytics_change(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            violations = validate_research_gate(
                Path(temporary_directory),
                ["src/analytics/forecast.py"],
                "",
            )

        self.assertTrue(any("Research record" in item for item in violations), violations)

    def test_accepts_independently_accepted_review_with_experiment(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_experiment(root)
            self._write_review(root, status="accepted")

            violations = validate_research_gate(
                root,
                ["src/analytics/forecast.py"],
                "- **Research record:** `docs/research/reviews/minutes.md`",
            )

        self.assertEqual([], violations)

    def test_rejects_self_accepted_review(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_experiment(root)
            self._write_review(root, status="accepted", accepted_by="Alice")

            violations = validate_research_gate(
                root,
                ["src/analytics/forecast.py"],
                "- **Research record:** docs/research/reviews/minutes.md",
            )

        self.assertTrue(any("distinct from the owner" in item for item in violations), violations)

    def test_rejects_accepted_review_without_immutable_source(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_experiment(root)
            self._write_review(root, status="accepted", immutable_source="none recorded")

            violations = validate_research_gate(
                root,
                ["src/analytics/forecast.py"],
                "- **Research record:** docs/research/reviews/minutes.md",
            )

        self.assertTrue(any("immutable DOI or versioned arXiv" in item for item in violations), violations)

    def test_rejects_accepted_review_without_registered_experiment(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_review(root, status="accepted")

            violations = validate_research_gate(
                root,
                ["src/analytics/forecast.py"],
                "- **Research record:** docs/research/reviews/minutes.md",
            )

        self.assertTrue(any("registered experiment" in item for item in violations), violations)

    def test_accepts_independently_accepted_deterministic_exemption(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_review(
                root,
                status="exempt",
                classification="deterministic-non-inferential",
                experiment="not required",
                immutable_source="none required",
                exemption_rationale="Implements an exact rules constraint without changing forecasts, uncertain inputs or empirical claims.",
            )

            violations = validate_research_gate(
                root,
                ["src/analytics/optimisation/rules.py"],
                "- **Research record:** docs/research/reviews/minutes.md",
            )

        self.assertEqual([], violations)

    def test_does_not_trigger_for_non_analytics_change(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            violations = validate_research_gate(
                Path(temporary_directory),
                ["src/backend/AutoFpl.Api/Program.cs", "docs/index.md"],
                "",
            )

        self.assertEqual([], violations)

    def test_rejects_record_outside_review_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "docs").mkdir()
            (root / "docs" / "bypass.md").write_text("bypass\n", encoding="utf-8")

            violations = validate_research_gate(
                root,
                ["src/analytics/forecast.py"],
                "- **Research record:** docs/bypass.md",
            )

        self.assertTrue(any("docs/research/reviews" in item for item in violations), violations)

    @staticmethod
    def _write_experiment(root: Path) -> None:
        path = root / "docs" / "research" / "experiments" / "minutes.yaml"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("id: minutes-v1\nstatus: registered\n", encoding="utf-8")

    @staticmethod
    def _write_review(
        root: Path,
        *,
        status: str,
        accepted_by: str = "Bob",
        classification: str = "research-required",
        experiment: str = "docs/research/experiments/minutes.yaml",
        immutable_source: str = "https://doi.org/10.1198/016214506000001437",
        exemption_rationale: str = "not applicable",
    ) -> None:
        path = root / "docs" / "research" / "reviews" / "minutes.md"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            f"""# Minutes forecast evidence review

- **Review ID:** minutes-v1
- **Feature/decision:** Player minutes forecast
- **Issue:** https://github.com/Jellman86/autoFPL/issues/99
- **Experiment:** {experiment}
- **Owner:** Alice
- **Status:** {status}
- **Classification:** {classification}
- **Accepted by:** {accepted_by}
- **Accepted at (UTC):** 2026-07-24T19:00:00Z
- **Acceptance record:** https://github.com/Jellman86/autoFPL/pull/99#issuecomment-1
- **Exemption rationale:** {exemption_rationale}

## Evidence matrix

| Immutable citation | Method |
|---|---|
| {immutable_source} | Proper scoring |
""",
            encoding="utf-8",
        )


if __name__ == "__main__":
    unittest.main()
