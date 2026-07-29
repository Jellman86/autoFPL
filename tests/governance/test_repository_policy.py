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

    def test_ai_decision_orchestrator_boundary_is_mandatory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            decision = root / "docs/adr/0007-evidence-grounded-ai-decision-orchestrator.md"
            decision.parent.mkdir(parents=True, exist_ok=True)
            decision.write_text("# AI mind\n", encoding="utf-8")

            violations = check_repository(root)

        self.assertTrue(
            any(
                "0007-evidence-grounded-ai-decision-orchestrator.md" in violation
                and "missing mandatory policy marker" in violation
                for violation in violations
            ),
            violations,
        )

    def test_agpl_license_is_mandatory(self) -> None:
        from tools.governance.check_repository import REQUIRED_PATHS

        self.assertIn("LICENSE", REQUIRED_PATHS)

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            (root / "LICENSE").unlink()

            violations = check_repository(root)

        self.assertTrue(
            any(
                "LICENSE" in violation
                and "missing required governance file" in violation
                for violation in violations
            ),
            violations,
        )

    def test_repository_rejects_request_body_logging(self) -> None:
        unsafe_sources = {
            "http logging body field": (
                "builder.Services.AddHttpLogging(options => "
                "options.LoggingFields = HttpLoggingFields.RequestBody);"
            ),
            "request body in logger scope": (
                "logger.BeginScope(new { Body = context.Request.Body });"
            ),
            "request dto in structured log": (
                'logger.LogInformation("request={Request}", request);'
            ),
            "aliased dto in structured log": (
                'logger.LogInformation("payload={Payload}", payload);'
            ),
            "aliased dto in logger scope": (
                "logger.BeginScope(new { Payload = payload });"
            ),
            "aliased dto in generic log call": (
                "logger.Log(LogLevel.Information, new EventId(), payload, null, "
                "static (state, _) => state.ToString());"
            ),
            "aliased dto in console output": "Console.WriteLine(payload);",
            "application logger dependency": "ILogger<Program> logger = app.Logger;",
        }

        for name, source in unsafe_sources.items():
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self._write_required_files(root)
                program = root / "src/backend/AutoFpl.Api/Program.cs"
                program.parent.mkdir(parents=True, exist_ok=True)
                program.write_text(source, encoding="utf-8")

                violations = check_repository(root)

                self.assertTrue(
                    any(
                        "request-body logging" in violation
                        for violation in violations
                    ),
                    violations,
                )

    def test_repository_rejects_backend_logging_helpers(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            helper = root / "src/backend/AutoFpl.Domain/EvidenceLogger.cs"
            helper.parent.mkdir(parents=True, exist_ok=True)
            helper.write_text(
                'logger.LogInformation("payload={Payload}", payload);',
                encoding="utf-8",
            )

            violations = check_repository(root)

        self.assertTrue(
            any("request-body logging" in violation for violation in violations),
            violations,
        )

    def test_repository_rejects_manual_route_catalog_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            program = root / "src/backend/AutoFpl.Api/Program.cs"
            program.parent.mkdir(parents=True, exist_ok=True)
            program.write_text(
                'app.MapPost("/api/v1/current", () => Results.Ok());',
                encoding="utf-8",
            )
            catalog = root / "contracts/manual-evidence/v1/current-post-routes.json"
            catalog.write_text(
                '{"routes":[{"method":"POST","path":"/api/v1/stale"}]}',
                encoding="utf-8",
            )

            violations = check_repository(root)

        self.assertTrue(
            any("manual evidence route catalog" in violation for violation in violations),
            violations,
        )

    def test_public_security_workflows_are_mandatory(self) -> None:
        from tools.governance.check_repository import REQUIRED_PATHS

        expected = {
            ".github/workflows/codeql.yml",
            ".github/workflows/dependency-review.yml",
        }
        self.assertTrue(expected.issubset(REQUIRED_PATHS), expected - set(REQUIRED_PATHS))

    def test_dotnet_ci_and_dependency_controls_are_mandatory(self) -> None:
        from tools.governance.check_repository import (
            REQUIRED_CONTENT_MARKERS,
            REQUIRED_PATHS,
        )

        self.assertIn(".github/dependabot.yml", REQUIRED_PATHS)

        expected_markers = {
            ".github/workflows/ci.yml": {
                "actions/setup-dotnet@",
                "dotnet restore src/backend/AutoFpl.slnx --locked-mode",
                "dotnet test src/backend/AutoFpl.slnx --no-restore",
            },
            ".github/workflows/codeql.yml": {
                "actions/setup-dotnet@",
                "language: [python, csharp]",
                "github/codeql-action/autobuild@",
            },
            ".github/dependabot.yml": {
                "package-ecosystem: nuget",
                "package-ecosystem: docker",
                '"/src/backend"',
                '"/tests/backend"',
            },
        }

        for relative_path, markers in expected_markers.items():
            configured = set(REQUIRED_CONTENT_MARKERS.get(relative_path, ()))
            self.assertTrue(markers.issubset(configured), markers - configured)

    def test_prediction_quality_private_research_boundary_is_mandatory(self) -> None:
        from tools.governance.check_repository import (
            REQUIRED_CONTENT_MARKERS,
            REQUIRED_PATHS,
        )

        decision = "docs/adr/0009-prediction-quality-first-private-research.md"
        self.assertIn(decision, REQUIRED_PATHS)
        self.assertIn("prediction quality", REQUIRED_CONTENT_MARKERS[decision])
        self.assertIn("without direct written permission", REQUIRED_CONTENT_MARKERS[decision])
        self.assertIn("Byparr", REQUIRED_CONTENT_MARKERS[decision])
        self.assertNotIn(
            "written Premier League permission",
            REQUIRED_CONTENT_MARKERS["AGENTS.md"],
        )
        self.assertNotIn(
            "explicit authorisation",
            REQUIRED_CONTENT_MARKERS["docs/standards/data-governance.md"],
        )

    def test_manual_evidence_catalog_is_mandatory(self) -> None:
        from tools.governance.check_repository import (
            REQUIRED_CONTENT_MARKERS,
            REQUIRED_PATHS,
        )

        schema = "contracts/manual-evidence/v1/manual-evidence-catalog.schema.json"
        catalog = "contracts/manual-evidence/v1/current-post-routes.json"
        self.assertIn(schema, REQUIRED_PATHS)
        self.assertIn(catalog, REQUIRED_PATHS)
        self.assertIn(
            '"replayAvailabilityPolicy": "not-before-receipt"',
            REQUIRED_CONTENT_MARKERS[catalog],
        )
        self.assertIn(
            '"requestBodyLogging": "disabled"',
            REQUIRED_CONTENT_MARKERS[catalog],
        )
        self.assertIn(
            '"derivedRequestValuesAccepted": false',
            REQUIRED_CONTENT_MARKERS[catalog],
        )

    def test_data_source_contract_and_policy_gate_are_mandatory(self) -> None:
        from tools.governance.check_repository import (
            REQUIRED_CONTENT_MARKERS,
            REQUIRED_PATHS,
        )

        expected_paths = {
            "contracts/data-source/v1/source-record.schema.json",
            "contracts/data-source/v1/examples/manual.json",
            "contracts/data-source/v1/examples/synthetic.json",
            "tools/governance/check_data_source_policy.py",
        }
        self.assertTrue(expected_paths.issubset(REQUIRED_PATHS), expected_paths - set(REQUIRED_PATHS))

        command = "python3 tools/governance/check_data_source_policy.py"
        for relative_path in ("Makefile", ".github/workflows/ci.yml"):
            configured = set(REQUIRED_CONTENT_MARKERS.get(relative_path, ()))
            self.assertIn(command, configured)


    def test_container_pipeline_and_scoped_package_write_are_mandatory(self) -> None:
        from tools.governance.check_repository import (
            REQUIRED_CONTENT_MARKERS,
            REQUIRED_PATHS,
            _workflow_permission_violations,
        )

        workflow_path = ".github/workflows/container.yml"
        self.assertIn(workflow_path, REQUIRED_PATHS)

        expected_markers = {
            "persist-credentials: false",
            "scripts/ci_container_smoke.sh",
            "--input /scan/autofpl.tar",
            "IMAGE_NAME: ghcr.io/jellman86/autofpl",
            "docker tag \"${LOCAL_IMAGE}\" \"${IMAGE_NAME}:sha-${GITHUB_SHA}\"",
            "docker push \"${IMAGE_NAME}:sha-${GITHUB_SHA}\"",
            "docker push \"${IMAGE_NAME}:dev\"",
            "packages: write",
            "refs/heads/dev",
            "sha-${GITHUB_SHA}",
        }
        configured = set(REQUIRED_CONTENT_MARKERS.get(workflow_path, ()))
        self.assertTrue(expected_markers.issubset(configured), expected_markers - configured)

        container_workflow = """name: Container
permissions:
  contents: read
jobs:
  container-publish:
    permissions:
      contents: read
      packages: write
    runs-on: ubuntu-latest
    steps: []
"""
        self.assertEqual(
            [],
            _workflow_permission_violations(
                container_workflow,
                Path(workflow_path),
            ),
        )

        wrong_workflow = _workflow_permission_violations(
            container_workflow,
            Path(".github/workflows/ci.yml"),
        )
        self.assertTrue(
            any("packages write permission" in violation for violation in wrong_workflow),
            wrong_workflow,
        )

        wrong_job = _workflow_permission_violations(
            container_workflow.replace("container-publish:", "container-build:"),
            Path(workflow_path),
        )
        self.assertTrue(
            any("packages write permission" in violation for violation in wrong_job),
            wrong_job,
        )

        analytics_workflow = container_workflow.replace(
            "container-publish:",
            "analytics-container-publish:",
        )
        self.assertEqual(
            [],
            _workflow_permission_violations(
                analytics_workflow,
                Path(".github/workflows/analytics-container.yml"),
            ),
        )

    def test_codeql_security_events_write_is_the_only_scoped_exception(self) -> None:
        from tools.governance.check_repository import _workflow_permission_violations

        codeql = """name: CodeQL
permissions:
  actions: read
  contents: read
  security-events: write
jobs: {}
"""
        self.assertEqual(
            [],
            _workflow_permission_violations(
                codeql,
                Path(".github/workflows/codeql.yml"),
            ),
        )

        wrong_workflow = _workflow_permission_violations(
            codeql,
            Path(".github/workflows/ci.yml"),
        )
        self.assertTrue(
            any("security-events write permission" in violation for violation in wrong_workflow),
            wrong_workflow,
        )

        excessive_codeql = codeql.replace("contents: read", "contents: write")
        violations = _workflow_permission_violations(
            excessive_codeql,
            Path(".github/workflows/codeql.yml"),
        )
        self.assertTrue(
            any("contents write permission" in violation for violation in violations),
            violations,
        )

        job_scoped = """name: CodeQL
permissions:
  contents: read
jobs:
  analyze:
    permissions:
      security-events: write
    runs-on: ubuntu-latest
    steps: []
"""
        violations = _workflow_permission_violations(
            job_scoped,
            Path(".github/workflows/codeql.yml"),
        )
        self.assertTrue(
            any("security-events write permission" in violation for violation in violations),
            violations,
        )

    def test_dependency_review_forbids_unbounded_manual_dispatch(self) -> None:
        from tools.governance.check_repository import (
            _dependency_review_trigger_violations,
        )

        relative = Path(".github/workflows/dependency-review.yml")
        violations = _dependency_review_trigger_violations(
            "on:\n  pull_request:\n  workflow_dispatch:\n",
            relative,
        )
        self.assertTrue(
            any("workflow_dispatch is forbidden" in violation for violation in violations),
            violations,
        )
        self.assertEqual(
            [],
            _dependency_review_trigger_violations(
                "on:\n  pull_request:\n",
                relative,
            ),
        )

    def test_ci_comment_cannot_satisfy_documentation_validation_step(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self._write_required_files(root)
            self._write_workflow(
                root,
                """name: CI
permissions:
  contents: read
# python3 tools/governance/check_documentation.py
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
      - uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
      - run: dotnet restore src/backend/AutoFpl.slnx --locked-mode
      - run: dotnet test src/backend/AutoFpl.slnx --no-restore
""",
            )

            violations = check_repository(root)

        self.assertTrue(
            any("documentation validation run step" in violation for violation in violations),
            violations,
        )

    def test_documentation_validation_step_must_be_failure_enforcing(self) -> None:
        from tools.governance.check_repository import _required_ci_step_violations

        relative = Path(".github/workflows/ci.yml")
        workflows = {
            "conditional step": """jobs:
  test:
    steps:
      - if: ${{ false }}
        run: python3 tools/governance/check_documentation.py
""",
            "continue-on-error step": """jobs:
  test:
    steps:
      - continue-on-error: true
        run: python3 tools/governance/check_documentation.py
""",
            "conditional job": """jobs:
  test:
    if: ${{ false }}
    steps:
      - run: python3 tools/governance/check_documentation.py
""",
            "continue-on-error job": """jobs:
  test:
    continue-on-error: true
    steps:
      - run: python3 tools/governance/check_documentation.py
""",
        }

        for name, workflow in workflows.items():
            with self.subTest(name=name):
                violations = _required_ci_step_violations(workflow, relative)
                self.assertTrue(
                    any("unconditional and failure-enforcing" in item for item in violations),
                    violations,
                )

        self.assertEqual(
            [],
            _required_ci_step_violations(
                """jobs:
  test:
    steps:
      - continue-on-error: false
        run: python3 tools/governance/check_documentation.py
""",
                relative,
            ),
        )

    def test_research_and_security_standards_are_available(self) -> None:
        from tools.governance.check_repository import (
            REQUIRED_CONTENT_MARKERS,
            REQUIRED_PATHS,
        )

        expected = {
            ".github/workflows/ci.yml",
            ".github/workflows/security.yml",
            "docs/research/evidence-base.md",
            "docs/research/literature-review-template.md",
            "docs/research/experiment-template.yaml",
            "docs/research/model-card-template.md",
            "docs/research/dataset-card-template.md",
            "docs/security/threat-model.md",
        }

        self.assertTrue(expected.issubset(REQUIRED_PATHS), expected - set(REQUIRED_PATHS))

        required_research_markers = {
            "Evidence-led implementation",
            "final holdout",
            "point-in-time",
            "rolling-origin evaluation",
        }
        configured = set(
            REQUIRED_CONTENT_MARKERS.get("docs/standards/research.md", ())
        )
        self.assertTrue(
            required_research_markers.issubset(configured),
            required_research_markers - configured,
        )

    def test_documentation_controls_are_mandatory(self) -> None:
        from tools.governance.check_repository import (
            REQUIRED_CONTENT_MARKERS,
            REQUIRED_PATHS,
        )

        expected_paths = {
            "CHANGELOG.md",
            "docs/index.md",
            "docs/roadmap.md",
            "docs/standards/documentation.md",
            "tools/governance/check_documentation.py",
        }
        self.assertTrue(
            expected_paths.issubset(REQUIRED_PATHS),
            expected_paths - set(REQUIRED_PATHS),
        )

        expected_markers = {
            "CHANGELOG.md": {"## Unreleased", "Keep a Changelog"},
            "docs/standards/documentation.md": {
                "## Source of truth",
                "## Information architecture",
                "## Safety and evidence requirements",
                "## Validation checklist",
            },
        }
        for relative_path, markers in expected_markers.items():
            configured = set(REQUIRED_CONTENT_MARKERS.get(relative_path, ()))
            self.assertTrue(markers.issubset(configured), markers - configured)

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
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
      - run: dotnet restore src/backend/AutoFpl.slnx --locked-mode
      - run: dotnet test src/backend/AutoFpl.slnx --no-restore
      - run: python3 tools/governance/check_data_source_policy.py
      - run: python3 tools/governance/check_documentation.py
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
            elif relative_path == ".github/workflows/codeql.yml":
                content = """name: CodeQL
permissions:
  actions: read
  contents: read
  security-events: write
jobs:
  analyze:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        language: [python, csharp]
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          persist-credentials: false
      - uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
      - uses: github/codeql-action/init@e4fba868fa4b1b91e1fdab776edc8cfbe6e9fb81 # v4.37.3
        with:
          queries: security-extended
      - uses: github/codeql-action/autobuild@e4fba868fa4b1b91e1fdab776edc8cfbe6e9fb81 # v4.37.3
"""
            elif relative_path == ".github/workflows/dependency-review.yml":
                content = """name: Dependency review
permissions:
  contents: read
jobs:
  dependency-review:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/dependency-review-action@a1d282b36b6f3519aa1f3fc636f609c47dddb294 # v5.0.0
        with:
          vulnerability-check: true
          license-check: true
          fail-on-severity: moderate
          allow-licenses: AGPL-3.0-only
"""
            elif relative_path == ".github/workflows/container.yml":
                content = """name: Container
permissions:
  contents: read
env:
  IMAGE_NAME: ghcr.io/jellman86/autofpl
jobs:
  container-publish:
    permissions:
      contents: read
      packages: write
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          persist-credentials: false
      - run: |
          scripts/ci_container_smoke.sh
          --input /scan/autofpl.tar
          docker tag "${LOCAL_IMAGE}" "${IMAGE_NAME}:sha-${GITHUB_SHA}"
          docker push "${IMAGE_NAME}:sha-${GITHUB_SHA}"
          docker push "${IMAGE_NAME}:dev"
          refs/heads/dev
          sha-${GITHUB_SHA}
"""
            elif relative_path == ".github/workflows/analytics-container.yml":
                content = """name: Analytics container
permissions:
  contents: read
env:
  IMAGE_NAME: ghcr.io/jellman86/autofpl-analytics
jobs:
  analytics-container-publish:
    permissions:
      contents: read
      packages: write
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          persist-credentials: false
      - run: |
          Dockerfile.analytics
          scripts/ci_analytics_container_smoke.sh
          --input /scan/autofpl-analytics.tar
          --ignore-unfixed
          docker tag "${LOCAL_IMAGE}" "${IMAGE_NAME}:sha-${GITHUB_SHA}"
          docker push "${IMAGE_NAME}:sha-${GITHUB_SHA}"
          docker push "${IMAGE_NAME}:dev"
          refs/heads/dev
          sha-${GITHUB_SHA}
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
