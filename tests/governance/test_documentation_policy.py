from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.governance.check_documentation import documentation_violations


class DocumentationPolicyTests(unittest.TestCase):
    def test_accepts_existing_local_fragment_external_and_parenthesized_links(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "docs").mkdir()
            (root / "README.md").write_text(
                "# Status\n\n"
                "[Docs](docs/index.md#documentation) "
                "[Section](#status) "
                "[Guide](docs/guide(v1).md) "
                "[Web](https://example.com)\n",
                encoding="utf-8",
            )
            (root / "docs/index.md").write_text(
                "# Documentation\n\n# Repeated\n\n# Repeated\n",
                encoding="utf-8",
            )
            (root / "docs/guide(v1).md").write_text("# Guide\n", encoding="utf-8")

            self.assertEqual([], documentation_violations(root))

    def test_accepts_reference_style_links_and_duplicate_heading_fragments(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "docs").mkdir()
            (root / "README.md").write_text(
                "[Second repeated heading][repeated]\n\n"
                "[repeated]: docs/index.md#repeated-1\n",
                encoding="utf-8",
            )
            (root / "docs/index.md").write_text(
                "# Repeated\n\n# Repeated\n",
                encoding="utf-8",
            )

            self.assertEqual([], documentation_violations(root))

    def test_reports_missing_reference_style_target(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "README.md").write_text(
                "[Missing guide][guide]\n\n[guide]: docs/missing.md\n",
                encoding="utf-8",
            )

            violations = documentation_violations(root)

        self.assertEqual(["README.md -> docs/missing.md (missing)"], violations)

    def test_reports_missing_local_targets(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "README.md").write_text(
                "[Missing](docs/missing.md)\n",
                encoding="utf-8",
            )

            violations = documentation_violations(root)

        self.assertEqual(["README.md -> docs/missing.md (missing)"], violations)

    def test_reports_missing_same_file_and_cross_file_fragments(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "docs").mkdir()
            (root / "README.md").write_text(
                "# Present\n\n"
                "[Same](#missing) [Other](docs/index.md#absent)\n",
                encoding="utf-8",
            )
            (root / "docs/index.md").write_text("# Existing\n", encoding="utf-8")

            violations = documentation_violations(root)

        self.assertEqual(
            [
                "README.md -> #missing (missing fragment)",
                "README.md -> docs/index.md#absent (missing fragment)",
            ],
            violations,
        )

    def test_reports_links_that_escape_the_repository(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "README.md").write_text("[Outside](../private.md)\n", encoding="utf-8")

            violations = documentation_violations(root)

        self.assertEqual(["README.md -> ../private.md (outside repository)"], violations)


if __name__ == "__main__":
    unittest.main()
