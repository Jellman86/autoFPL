from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path
from typing import Any

try:
    import yaml
except ModuleNotFoundError:  # Fail closed in _workflow_permission_violations.
    yaml = None

REQUIRED_PATHS = (
    "LICENSE",
    "README.md",
    "CHANGELOG.md",
    "AGENTS.md",
    "CONTRIBUTING.md",
    "GOVERNANCE.md",
    "SECURITY.md",
    "requirements-governance.txt",
    ".github/CODEOWNERS",
    ".github/PULL_REQUEST_TEMPLATE.md",
    ".github/dependabot.yml",
    ".github/workflows/ci.yml",
    ".github/workflows/security.yml",
    ".github/workflows/codeql.yml",
    ".github/workflows/dependency-review.yml",
    "docs/standards/definition-of-done.md",
    "docs/standards/documentation.md",
    "docs/standards/engineering.md",
    "docs/standards/research.md",
    "docs/standards/security.md",
    "docs/standards/data-governance.md",
    "docs/compliance/fpl-terms-boundary.md",
    "docs/architecture/README.md",
    "docs/research/evidence-base.md",
    "docs/research/literature-review-template.md",
    "docs/research/experiment-template.yaml",
    "docs/research/model-card-template.md",
    "docs/research/dataset-card-template.md",
    "docs/security/threat-model.md",
    "docs/index.md",
    "docs/roadmap.md",
    "docs/adr/0005-chatgpt-mcp-interface.md",
    "docs/adr/0006-optional-model-provider-adapters.md",
    "docs/adr/0007-evidence-grounded-ai-decision-orchestrator.md",
    "tools/governance/check_pr_title.py",
    "tools/governance/check_research_review.py",
    "tools/governance/check_documentation.py",
)

REQUIRED_CONTENT_MARKERS = {
    "LICENSE": (
        "GNU AFFERO GENERAL PUBLIC LICENSE",
        "Version 3, 19 November 2007",
        "remotely through a computer network",
    ),
    "README.md": (
        "AGPL-3.0-only",
        "Runtime and user data are not licensed",
    ),
    "requirements-governance.txt": (
        "PyYAML==6.0.3",
        "markdown-it-py==4.2.0",
        "mdurl==0.1.2",
    ),
    "CHANGELOG.md": (
        "## Unreleased",
        "Keep a Changelog",
    ),
    "AGENTS.md": (
        "human-in-the-loop",
        "written Premier League permission",
        "random train/test splits are forbidden",
        "available_at",
        "Never select a model",
        "No production code before a failing automated test",
        "full 40-character SHAs",
        "Lock every dependency set",
    ),
    "docs/compliance/fpl-terms-boundary.md": (
        "automated access to or extraction",
        "passwords, cookies or sessions",
        "automatic transfers",
        "written permission or contract",
        "No “experimental”, “personal use” or feature flag bypass",
    ),
    "docs/standards/research.md": (
        "evidence review before implementation",
        "frontier methods are challengers, not defaults",
        "immutable paper version",
        "local out-of-time evidence",
        "rolling-origin walk-forward validation",
        "`available_at <= decision_deadline`",
        "final test period once",
        "proper scoring rules",
        "calibration",
        "tiny brute-force instances",
        "predictive machine learning",
    ),
    "docs/standards/engineering.md": (
        "locked restore",
        "uv.lock",
        "package-manager lockfile",
        "container images to digests",
    ),
    "docs/standards/security.md": (
        "`pull_request_target` is forbidden",
        "full commit SHAs",
        "OpenAI/Codex cached credentials",
    ),
    "docs/standards/data-governance.md": (
        "licence/contract and permitted purposes",
        "explicit authorisation",
        "Technical accessibility is not permission",
        "content hash",
        "AI-assisted extraction",
    ),
    "docs/standards/definition-of-done.md": (
        "failing test first",
        "point-in-time correct",
        "checked against brute force",
        "human-approval invariant",
    ),
    "docs/standards/documentation.md": (
        "## Source of truth",
        "## Information architecture",
        "## Safety and evidence requirements",
        "## Validation checklist",
    ),
    ".github/workflows/security.yml": (
        "contents: read",
        "pull-requests: read",
        "persist-credentials: false",
    ),
    ".github/workflows/ci.yml": (
        "fetch-depth: 0",
        "issues: read",
        "pull-requests: read",
        "actions/setup-dotnet@",
        "dotnet restore src/backend/AutoFpl.slnx --locked-mode",
        "dotnet test src/backend/AutoFpl.slnx --no-restore",
        "BASE_SHA:",
        "GITHUB_TOKEN:",
        "PR_AUTHOR:",
        "PR_BODY:",
        "python3 tools/governance/check_research_review.py",
    ),
    ".github/workflows/codeql.yml": (
        "actions: read",
        "contents: read",
        "security-events: write",
        "persist-credentials: false",
        "queries: security-extended",
        "actions/setup-dotnet@",
        "language: [python, csharp]",
        "github/codeql-action/autobuild@",
    ),
    ".github/dependabot.yml": (
        "package-ecosystem: nuget",
        '"/src/backend"',
        '"/tests/backend"',
    ),
    ".github/workflows/dependency-review.yml": (
        "contents: read",
        "fail-on-severity: moderate",
        "vulnerability-check: true",
        "license-check: true",
        "allow-licenses:",
        "AGPL-3.0-only",
    ),
    "docs/adr/0005-chatgpt-mcp-interface.md": (
        "ChatGPT Apps SDK / MCP",
        "does not receive or store OpenAI/ChatGPT OAuth tokens",
        "Hermes proxy is not a baseline runtime dependency",
        "Direct Codex OAuth",
        "read-only advisory tools",
        "Hermes MCP client",
    ),
    "docs/adr/0006-optional-model-provider-adapters.md": (
        "OpenRouter",
        "provider-neutral port",
        "never authoritative",
        "secret store",
        "private Hermes proxy",
        "deterministic services continue",
        "candidate structured extraction",
    ),
    "docs/adr/0007-evidence-grounded-ai-decision-orchestrator.md": (
        "decision orchestrator",
        "working context",
        "tool-generated evidence",
        "authoritative memory",
        "bounded orchestration",
        "human approval",
    ),
}

_FULL_SHA = re.compile(r"^[0-9a-f]{40}$")
_USES = re.compile(r"^\s*-?\s*uses:\s*([^\s#]+)", re.MULTILINE)
_DANGEROUS_PIPE = re.compile(
    r"(?:curl|wget)\b[^\n|]*\|\s*(?:ba)?sh\b",
    re.IGNORECASE,
)
_FORBIDDEN_TRACKED_NAMES = {
    ".env",
    "auth.json",
    "credentials.json",
    "id_rsa",
    "id_ed25519",
}


def _tracked_files(root: Path) -> list[Path]:
    """Return Git-index files, or a safe filesystem fallback outside Git repos."""
    if (root / ".git").exists():
        result = subprocess.run(
            ["git", "ls-files", "-z"],
            cwd=root,
            check=False,
            capture_output=True,
        )
        if result.returncode == 0:
            return [
                root / raw.decode("utf-8", errors="surrogateescape")
                for raw in result.stdout.split(b"\0")
                if raw
            ]

    excluded = {".git", ".venv", "node_modules", "__pycache__"}
    return [
        path
        for path in root.rglob("*")
        if path.is_file() and not excluded.intersection(path.relative_to(root).parts)
    ]


def _permission_value_violations(
    value: Any,
    *,
    location: str,
    relative: Path,
) -> list[str]:
    if value == "write-all":
        return [f"{relative}: {location} permissions: write-all is forbidden"]
    if value == "read-all":
        return [
            f"{relative}: {location} permissions: read-all is broader than necessary"
        ]
    if not isinstance(value, dict):
        return [f"{relative}: {location} permissions must be an explicit mapping"]

    violations: list[str] = []
    for scope, access in value.items():
        if access == "write":
            is_codeql_upload = (
                relative == Path(".github/workflows/codeql.yml")
                and location == "workflow"
                and scope == "security-events"
            )
            if not is_codeql_upload:
                violations.append(
                    f"{relative}: {location} {scope} write permission requires "
                    "a reviewed policy exception"
                )
        elif access not in {"read", "none"}:
            violations.append(
                f"{relative}: {location} {scope} has invalid permission: {access}"
            )
    return violations


def _dependency_review_trigger_violations(content: str, relative: Path) -> list[str]:
    if relative != Path(".github/workflows/dependency-review.yml"):
        return []
    if re.search(r"(?m)^[ \t]*workflow_dispatch[ \t]*:", content):
        return [
            f"{relative}: workflow_dispatch is forbidden because dependency review "
            "requires an explicit base-ref and head-ref outside pull_request events"
        ]
    return []


def _workflow_permission_violations(content: str, relative: Path) -> list[str]:
    if yaml is None:
        return [f"{relative}: PyYAML is required for fail-closed permission checks"]
    try:
        document = yaml.safe_load(content)
    except yaml.YAMLError as error:
        return [f"{relative}: invalid workflow YAML: {error}"]
    if not isinstance(document, dict):
        return [f"{relative}: workflow must be a YAML mapping"]
    if "permissions" not in document:
        return [f"{relative}: workflow must declare explicit permissions"]

    violations = _permission_value_violations(
        document["permissions"],
        location="workflow",
        relative=relative,
    )
    jobs = document.get("jobs", {})
    if not isinstance(jobs, dict):
        violations.append(f"{relative}: jobs must be a YAML mapping")
        return violations
    for job_name, job in jobs.items():
        if isinstance(job, dict) and "permissions" in job:
            violations.extend(
                _permission_value_violations(
                    job["permissions"],
                    location=f"job {job_name}",
                    relative=relative,
                )
            )
    return violations


def _required_ci_step_violations(content: str, relative: Path) -> list[str]:
    if relative != Path(".github/workflows/ci.yml"):
        return []
    if yaml is None:
        return [f"{relative}: PyYAML is required for fail-closed CI step checks"]
    try:
        document = yaml.safe_load(content)
    except yaml.YAMLError as error:
        return [f"{relative}: invalid workflow YAML: {error}"]

    expected = "python3 tools/governance/check_documentation.py"
    invalid_candidates: list[str] = []
    if isinstance(document, dict):
        jobs = document.get("jobs", {})
        if isinstance(jobs, dict):
            for job_name, job in jobs.items():
                if not isinstance(job, dict):
                    continue
                steps = job.get("steps", [])
                if not isinstance(steps, list):
                    continue
                for step_index, step in enumerate(steps):
                    if not isinstance(step, dict) or not isinstance(step.get("run"), str):
                        continue
                    if not any(
                        line.strip() == expected for line in step["run"].splitlines()
                    ):
                        continue

                    reasons: list[str] = []
                    if "if" in job:
                        reasons.append("job is conditional")
                    if (
                        "continue-on-error" in job
                        and job["continue-on-error"] is not False
                    ):
                        reasons.append("job may continue on error")
                    if "if" in step:
                        reasons.append("step is conditional")
                    if (
                        "continue-on-error" in step
                        and step["continue-on-error"] is not False
                    ):
                        reasons.append("step may continue on error")
                    if not reasons:
                        return []
                    invalid_candidates.append(
                        f"job {job_name} step {step_index + 1}: {', '.join(reasons)}"
                    )

    if invalid_candidates:
        return [
            f"{relative}: documentation validation run step must be unconditional "
            f"and failure-enforcing ({'; '.join(invalid_candidates)})"
        ]
    return [f"{relative}: missing executable documentation validation run step"]


def check_repository(root: Path) -> list[str]:
    """Return deterministic repository-policy violations for ``root``."""
    violations: list[str] = []

    for relative_path in REQUIRED_PATHS:
        if not (root / relative_path).is_file():
            violations.append(f"missing required governance file: {relative_path}")

    for relative_path, markers in REQUIRED_CONTENT_MARKERS.items():
        policy_path = root / relative_path
        if not policy_path.is_file():
            continue
        content = policy_path.read_text(encoding="utf-8").casefold()
        for marker in markers:
            if marker.casefold() not in content:
                violations.append(
                    f"{relative_path}: missing mandatory policy marker: {marker}"
                )

    workflows = sorted((root / ".github" / "workflows").glob("*.y*ml"))
    for workflow in workflows:
        content = workflow.read_text(encoding="utf-8")
        relative = workflow.relative_to(root)

        violations.extend(_workflow_permission_violations(content, relative))
        violations.extend(_dependency_review_trigger_violations(content, relative))
        violations.extend(_required_ci_step_violations(content, relative))

        if "pull_request_target:" in content:
            violations.append(
                f"{relative}: pull_request_target requires an approved threat-model exception"
            )

        if _DANGEROUS_PIPE.search(content):
            violations.append(f"{relative}: piping downloaded content to a shell is forbidden")

        for match in _USES.finditer(content):
            reference = match.group(1)
            if reference.startswith("./"):
                continue
            if "@" not in reference:
                violations.append(f"{relative}: action reference lacks a revision: {reference}")
                continue
            action, revision = reference.rsplit("@", 1)
            if not _FULL_SHA.fullmatch(revision):
                violations.append(
                    f"{relative}: {action} must be pinned to a full commit SHA"
                )

    for path in _tracked_files(root):
        if path.is_file() and path.name in _FORBIDDEN_TRACKED_NAMES:
            violations.append(
                f"forbidden credential-shaped file present: {path.relative_to(root)}"
            )
        if path.is_file() and path.suffix.lower() in {".pem", ".p12", ".pfx"}:
            violations.append(
                f"forbidden private-key/certificate bundle present: {path.relative_to(root)}"
            )

    return sorted(set(violations))


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    violations = check_repository(root)
    if violations:
        print("Repository governance check failed:")
        for violation in violations:
            print(f"- {violation}")
        return 1
    print("Repository governance check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
