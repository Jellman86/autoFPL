from __future__ import annotations

import json
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
    ".github/workflows/container.yml",
    ".github/workflows/analytics-container.yml",
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
    "docs/adr/0009-prediction-quality-first-private-research.md",
    "docs/adr/0010-code-first-home-lab-architecture.md",
    "contracts/data-source/v1/source-record.schema.json",
    "contracts/data-source/v1/examples/manual.json",
    "contracts/data-source/v1/examples/synthetic.json",
    "contracts/manual-evidence/v1/manual-evidence-catalog.schema.json",
    "contracts/manual-evidence/v1/current-post-routes.json",
    "tools/governance/check_data_source_policy.py",
    "tools/governance/check_pr_title.py",
    "tools/governance/check_documentation.py",
)

REQUIRED_CONTENT_MARKERS = {
    "Makefile": (
        "python3 tools/governance/check_data_source_policy.py",
    ),
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
        "private, non-commercial, self-hosted home-lab project",
        "prediction quality",
        "Byparr-assisted collection",
        "smallest end-to-end slice",
        "Prefer a single deployable application and SQLite",
        "Random train/test splits cannot support temporal FPL claims",
        "available_at",
        "Never select a model",
        "full 40-character SHAs",
        "Lock dependencies",
    ),
    "docs/compliance/fpl-terms-boundary.md": (
        "private, non-commercial home research project",
        "Direct written permission is not a general project requirement",
        "passwords, authentication tokens, cookies or sessions",
        "Do not automate transfers",
        "Publicly reachable read-only pages and endpoints",
    ),
    "docs/standards/research.md": (
        "Evidence-led implementation",
        "final holdout",
        "point-in-time",
        "rolling-origin evaluation",
        "`available_at <= decision_cutoff`",
        "proper scoring",
        "calibration",
        "brute force",
        "Research-backed methods",
    ),
    "docs/standards/engineering.md": (
        "SQLite",
        "one deployable .NET application",
        "persistent local volume",
        "online-backup API",
        "deployed images to digests",
    ),
    "docs/standards/security.md": (
        "`pull_request_target` is forbidden",
        "full commit SHAs",
        "OpenAI/Codex cached credentials",
    ),
    "docs/standards/data-governance.md": (
        "prediction quality",
        "do not require direct written permission",
        "Byparr connector",
        "content hash",
        "AI-assisted extraction",
    ),
    "docs/standards/definition-of-done.md": (
        "Every product slice",
        "Predictive promotion",
        "point-in-time correct",
        "checked against brute force",
        "Development work does not need release",
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
        "actions/setup-dotnet@",
        "dotnet restore src/backend/AutoFpl.slnx --locked-mode",
        "dotnet test src/backend/AutoFpl.slnx --no-restore",
        "python3 tools/governance/check_data_source_policy.py",
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
        "package-ecosystem: docker",
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
    ".github/workflows/container.yml": (
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
    ),
    ".github/workflows/analytics-container.yml": (
        "persist-credentials: false",
        "Dockerfile.analytics",
        "scripts/ci_analytics_container_smoke.sh",
        "--input /scan/autofpl-analytics.tar",
        "IMAGE_NAME: ghcr.io/jellman86/autofpl-analytics",
        "docker tag \"${LOCAL_IMAGE}\" \"${IMAGE_NAME}:sha-${GITHUB_SHA}\"",
        "docker push \"${IMAGE_NAME}:sha-${GITHUB_SHA}\"",
        "docker push \"${IMAGE_NAME}:dev\"",
        "packages: write",
        "refs/heads/dev",
        "sha-${GITHUB_SHA}",
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
    "docs/adr/0009-prediction-quality-first-private-research.md": (
        "prediction quality",
        "without direct written permission",
        "Byparr",
        "rolling/walk-forward evaluation",
        "private, non-commercial home research project",
    ),
    "docs/adr/0010-code-first-home-lab-architecture.md": (
        "Code-first home-lab",
        "SQLite",
        "smallest working vertical slice",
        "no separate acceptance is required before exploratory implementation",
    ),
    "contracts/manual-evidence/v1/current-post-routes.json": (
        '"sourceType": "manual"',
        '"persistence": "mixed"',
        '"requestBodyLogging": "disabled"',
        '"derivedRequestValuesAccepted": false',
        '"replayAvailabilityPolicy": "not-before-receipt"',
    ),
}

_FULL_SHA = re.compile(r"^[0-9a-f]{40}$")
_USES = re.compile(r"^\s*-?\s*uses:\s*([^\s#]+)", re.MULTILINE)
_DANGEROUS_PIPE = re.compile(
    r"(?:curl|wget)\b[^\n|]*\|\s*(?:ba)?sh\b",
    re.IGNORECASE,
)
_REQUEST_BODY_LOGGING = re.compile(
    r"\b(?:AddHttpLogging|UseHttpLogging|HttpLoggingFields|ILogger|ILoggerFactory|"
    r"Serilog|NLog)\b"
    r"|\.\s*(?:Log(?:Trace|Debug|Information|Warning|Error|Critical)?|BeginScope)\s*\("
    r"|\bConsole\s*\.\s*Write(?:Line)?\s*\(",
    re.IGNORECASE,
)
_MAP_POST_ROUTE = re.compile(r'app\s*\.\s*MapPost\s*\(\s*"([^"]+)"')
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
            is_container_publish = (
                scope == "packages"
                and (
                    (
                        relative == Path(".github/workflows/container.yml")
                        and location == "job container-publish"
                    )
                    or (
                        relative
                        == Path(".github/workflows/analytics-container.yml")
                        and location == "job analytics-container-publish"
                    )
                )
            )
            if not (is_codeql_upload or is_container_publish):
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


def _request_body_logging_violations(content: str, relative: Path) -> list[str]:
    if _REQUEST_BODY_LOGGING.search(content):
        return [
            f"{relative}: request-body logging boundary forbids application logging APIs "
            "in backend application sources; use framework metadata logs only"
        ]
    return []


def _manual_evidence_route_catalog_violations(root: Path) -> list[str]:
    program = root / "src/backend/AutoFpl.Api/Program.cs"
    catalog_path = root / "contracts/manual-evidence/v1/current-post-routes.json"
    if not program.is_file() or not catalog_path.is_file():
        return []

    actual_routes = _MAP_POST_ROUTE.findall(program.read_text(encoding="utf-8"))
    try:
        catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as error:
        return [f"manual evidence route catalog is unreadable: {error}"]

    routes = catalog.get("routes") if isinstance(catalog, dict) else None
    if not isinstance(routes, list):
        return ["manual evidence route catalog must contain a routes array"]

    catalog_routes = [
        route.get("path")
        for route in routes
        if isinstance(route, dict) and route.get("method") == "POST"
    ]
    if len(catalog_routes) != len(routes) or not all(
        isinstance(path, str) for path in catalog_routes
    ):
        return ["manual evidence route catalog contains an invalid POST route"]

    actual_set = set(actual_routes)
    catalog_set = set(catalog_routes)
    if len(actual_set) != len(actual_routes) or len(catalog_set) != len(catalog_routes):
        return ["manual evidence route catalog or API contains duplicate POST routes"]
    if actual_set != catalog_set:
        missing = sorted(actual_set - catalog_set)
        stale = sorted(catalog_set - actual_set)
        return [
            "manual evidence route catalog differs from API POST routes "
            f"(missing={missing}, stale={stale})"
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

    backend_sources = sorted((root / "src/backend").rglob("*.cs"))
    for source in backend_sources:
        relative = source.relative_to(root)
        content = source.read_text(encoding="utf-8")
        violations.extend(_request_body_logging_violations(content, relative))
    violations.extend(_manual_evidence_route_catalog_violations(root))

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
