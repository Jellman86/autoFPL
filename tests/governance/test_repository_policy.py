from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path

from tools.governance.check_repository import check_repository


class RepositoryPolicyTests(unittest.TestCase):
    def test_chatgpt_mcp_architecture_boundary_is_mandatory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            decision = root / "docs/adr/0005-chatgpt-mcp-interface.md"
            decision.parent.mkdir(parents=True, exist_ok=True)
            decision.write_text("# Chat interface\n", encoding="utf-8")

            violations = check_repository(root)

        self.assertTrue(
            any(
                "0005-chatgpt-mcp-interface.md" in violation
                and "missing mandatory policy marker" in violation
                for violation in violations
            ),
            violations,
        )

    def test_model_provider_adapter_boundary_is_mandatory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            decision = root / "docs/adr/0006-optional-model-provider-adapters.md"
            decision.parent.mkdir(parents=True, exist_ok=True)
            decision.write_text("# Model provider\n", encoding="utf-8")

            violations = check_repository(root)

        self.assertTrue(
            any(
                "0006-optional-model-provider-adapters.md" in violation
                and "missing mandatory policy marker" in violation
                for violation in violations
            ),
            violations,
        )

    def test_research_and_security_records_are_mandatory(self) -> None:
        from tools.governance.check_repository import REQUIRED_PATHS

        expected = {
            ".github/workflows/ci.yml",
            ".github/workflows/security.yml",
            "docs/research/evidence-base.md",
            "docs/research/experiment-template.yaml",
            "docs/research/model-card-template.md",
            "docs/research/dataset-card-template.md",
            "docs/security/threat-model.md",
        }

        self.assertTrue(expected.issubset(REQUIRED_PATHS), expected - set(REQUIRED_PATHS))

    def test_reports_missing_required_governance_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            violations = check_repository(Path(temporary_directory))

        self.assertTrue(
            any("AGENTS.md" in violation for violation in violations),
            violations,
        )

    def test_rejects_mutable_third_party_action_references(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            self._write_workflow(
                root,
                """name: CI
permissions:
  contents: read
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
""",
            )

            violations = check_repository(root)

        self.assertTrue(
            any("full commit SHA" in violation for violation in violations),
            violations,
        )

    def test_rejects_write_all_permissions(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            self._write_workflow(
                root,
                """name: CI
permissions: write-all
jobs:
  test:
    runs-on: ubuntu-latest
    steps: []
""",
            )

            violations = check_repository(root)

        self.assertTrue(
            any("write-all" in violation for violation in violations),
            violations,
        )

    def test_requires_explicit_workflow_permissions(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            self._write_workflow(
                root,
                """name: CI
jobs:
  test:
    runs-on: ubuntu-latest
    steps: []
""",
            )

            violations = check_repository(root)

        self.assertTrue(
            any("explicit permissions" in violation for violation in violations),
            violations,
        )

    def test_rejects_granular_write_permissions(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            self._write_workflow(
                root,
                """name: CI
permissions:
  contents: write
jobs:
  test:
    runs-on: ubuntu-latest
    steps: []
""",
            )

            violations = check_repository(root)

        self.assertTrue(
            any("write permission" in violation for violation in violations),
            violations,
        )

    def test_rejects_job_level_write_all_permissions(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            self._write_workflow(
                root,
                """name: CI
permissions:
  contents: read
jobs:
  test:
    permissions: write-all
    runs-on: ubuntu-latest
    steps: []
""",
            )

            violations = check_repository(root)

        self.assertTrue(
            any("write-all" in violation for violation in violations),
            violations,
        )

    def test_rejects_inline_job_write_permissions(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            self._write_workflow(
                root,
                """name: CI
permissions: {contents: read}
jobs:
  test:
    permissions: {contents: write}
    runs-on: ubuntu-latest
    steps: []
""",
            )

            violations = check_repository(root)

        self.assertTrue(
            any("write permission" in violation for violation in violations),
            violations,
        )

    def test_rejects_weakened_mandatory_policy_content(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            policy = root / "docs/compliance/fpl-terms-boundary.md"
            policy.write_text("# Boundary\nHuman approval is useful.\n", encoding="utf-8")

            violations = check_repository(root)

        self.assertTrue(
            any("mandatory policy marker" in violation for violation in violations),
            violations,
        )

    def test_ignores_untracked_gitignored_credentials(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            (root / ".gitignore").write_text(".env\n", encoding="utf-8")
            (root / ".env").write_text("LOCAL_ONLY=true\n", encoding="utf-8")
            subprocess.run(
                ["git", "init", "-q", "-b", "main"],
                cwd=root,
                check=True,
            )
            subprocess.run(["git", "add", "."], cwd=root, check=True)

            violations = check_repository(root)

        self.assertFalse(
            any("credential-shaped" in violation for violation in violations),
            violations,
        )

    def test_rejects_tracked_credentials(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            (root / ".env").write_text("NOT_A_REAL_SECRET=true\n", encoding="utf-8")
            subprocess.run(
                ["git", "init", "-q", "-b", "main"],
                cwd=root,
                check=True,
            )
            subprocess.run(["git", "add", "-f", ".env"], cwd=root, check=True)

            violations = check_repository(root)

        self.assertTrue(
            any("credential-shaped" in violation for violation in violations),
            violations,
        )

    def test_accepts_sha_pinned_read_only_workflow(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            self._write_workflow(
                root,
                """name: CI
permissions:
  contents: read
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
""",
            )

            violations = check_repository(root)

        self.assertEqual([], violations)

    @staticmethod
    def _write_required_files(root: Path) -> None:
        from tools.governance.check_repository import (
            REQUIRED_CONTENT_MARKERS,
            REQUIRED_PATHS,
        )

        for relative_path in REQUIRED_PATHS:
            path = root / relative_path
            path.parent.mkdir(parents=True, exist_ok=True)
            content = "placeholder\n"
            if relative_path == ".github/workflows/security.yml":
                content = """name: Secret scan
permissions:
  contents: read
  pull-requests: read
jobs:
  gitleaks:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          persist-credentials: false
"""
            elif path.parent.name == "workflows":
                content = "name: Required\npermissions: {}\njobs: {}\n"
            elif relative_path in REQUIRED_CONTENT_MARKERS:
                content = "\n".join(REQUIRED_CONTENT_MARKERS[relative_path]) + "\n"
            path.write_text(content, encoding="utf-8")

    @staticmethod
    def _write_workflow(root: Path, content: str) -> None:
        path = root / ".github" / "workflows" / "ci.yml"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
