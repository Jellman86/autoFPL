from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Callable

_RESEARCH_RECORD = re.compile(
    r"^\s*-?\s*\*\*Research record(?:\s*\([^*]*\))?:\*\*\s*`?([^`\s]+)`?\s*$",
    re.IGNORECASE | re.MULTILINE,
)
_FIELD = re.compile(
    r"^\s*-\s*\*\*(?P<name>[^*]+):\*\*\s*(?P<value>.*?)\s*$",
    re.MULTILINE,
)
_IMMUTABLE_SOURCE = re.compile(
    r"https://doi\.org/[A-Za-z0-9./()_:;-]+|https://arxiv\.org/(?:abs|pdf)/\d{4}\.\d{4,5}v\d+",
    re.IGNORECASE,
)
_ACCEPTANCE_RECORD = re.compile(
    r"https://github\.com/Jellman86/autoFPL/(?P<area>pull|issues)/(?P<number>\d+)"
    r"#(?P<kind>issuecomment|pullrequestreview)-(?P<id>\d+)",
    re.IGNORECASE,
)
_REVIEW_PREFIX = PurePosixPath("docs/research/reviews")
_EXPERIMENT_PREFIX = PurePosixPath("docs/research/experiments")
_EXEMPT_RATIONALE_PLACEHOLDERS = {"", "not applicable", "n/a", "none", "todo"}
_GITHUB_LOGIN = re.compile(
    r"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?(?:\[bot\])?$"
)


@dataclass(frozen=True)
class Acceptance:
    actor: str
    created_at: str
    body: str


TextLoader = Callable[[str], str | None]
AcceptanceResolver = Callable[[str], Acceptance | None]
SourceResolver = Callable[[str], bool]


def _is_research_sensitive(path: str) -> bool:
    candidate = PurePosixPath(path)
    if not candidate.is_relative_to(PurePosixPath("src/analytics")):
        return False
    return candidate.suffix.lower() not in {".md", ".txt"}


def _fields(text: str) -> dict[str, str]:
    return {
        match.group("name").strip().casefold(): match.group("value").strip()
        for match in _FIELD.finditer(text)
    }


def _parse_timestamp(value: str) -> datetime | None:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        return None
    return parsed.astimezone(timezone.utc)


def _read_current(root: Path, relative_path: str) -> str | None:
    path = root / PurePosixPath(relative_path)
    if not path.is_file():
        return None
    return path.read_text(encoding="utf-8")


def _review_violations(
    root: Path,
    relative_path: str,
    text: str,
    *,
    experiment_loader: TextLoader | None = None,
    acceptance_resolver: AcceptanceResolver | None = None,
    source_resolver: SourceResolver | None = None,
    expected_owner: str | None = None,
    require_live_verification: bool = False,
) -> list[str]:
    violations: list[str] = []
    fields = _fields(text)
    status = fields.get("status", "").casefold()
    classification = fields.get("classification", "").casefold()

    if status not in {"accepted", "exempt"}:
        return [f"{relative_path}: referenced research record status must be accepted or exempt"]

    for name in (
        "review id",
        "feature/decision",
        "issue",
        "owner",
        "accepted by",
        "accepted at (utc)",
        "acceptance record",
        "classification",
    ):
        if not fields.get(name):
            violations.append(f"{relative_path}: missing required field {name}")

    owner = fields.get("owner", "")
    accepted_by = fields.get("accepted by", "")
    if owner and not _GITHUB_LOGIN.fullmatch(owner):
        violations.append(f"{relative_path}: owner must be a GitHub login")
    if accepted_by and not _GITHUB_LOGIN.fullmatch(accepted_by):
        violations.append(f"{relative_path}: accepted by must be a GitHub login")
    if owner and accepted_by and owner.casefold() == accepted_by.casefold():
        violations.append(f"{relative_path}: accepted by must be distinct from the owner")
    if require_live_verification:
        if not expected_owner:
            violations.append(f"{relative_path}: pull request author verification is unavailable")
        elif owner and owner.casefold() != expected_owner.casefold():
            violations.append(f"{relative_path}: owner does not match the pull request author")

    accepted_at_text = fields.get("accepted at (utc)", "")
    accepted_at = _parse_timestamp(accepted_at_text) if accepted_at_text else None
    if accepted_at_text and accepted_at is None:
        violations.append(f"{relative_path}: accepted at (UTC) must be a timezone-aware ISO timestamp")

    acceptance_url = fields.get("acceptance record", "")
    if acceptance_url and not _ACCEPTANCE_RECORD.fullmatch(acceptance_url):
        violations.append(
            f"{relative_path}: acceptance record must link directly to an autoFPL issue comment or PR review"
        )

    if status == "accepted":
        if classification != "research-required":
            violations.append(f"{relative_path}: accepted research review classification must be research-required")

        source_urls = sorted(set(_IMMUTABLE_SOURCE.findall(text)))
        if not source_urls:
            violations.append(f"{relative_path}: accepted review needs an immutable DOI or versioned arXiv source")
        elif require_live_verification:
            if source_resolver is None:
                violations.append(f"{relative_path}: live immutable-source verification is unavailable")
            else:
                for source_url in source_urls:
                    try:
                        resolved = source_resolver(source_url)
                    except Exception:
                        resolved = False
                    if not resolved:
                        violations.append(f"{relative_path}: immutable source could not be resolved: {source_url}")

        experiment = fields.get("experiment", "")
        experiment_path = PurePosixPath(experiment)
        loader = experiment_loader or (lambda value: _read_current(root, value))
        experiment_text = None
        if (
            experiment
            and experiment_path.is_relative_to(_EXPERIMENT_PREFIX)
            and experiment_path.suffix in {".yaml", ".yml"}
        ):
            experiment_text = loader(experiment_path.as_posix())
        if experiment_text is None:
            violations.append(f"{relative_path}: accepted review must link an existing registered experiment")
        elif not re.search(r"^status:\s*registered\s*(?:#.*)?$", experiment_text, re.MULTILINE):
            violations.append(f"{relative_path}: linked experiment status must be registered")

    if status == "exempt":
        if classification != "deterministic-non-inferential":
            violations.append(
                f"{relative_path}: exempt record classification must be deterministic-non-inferential"
            )
        rationale = fields.get("exemption rationale", "").casefold()
        if rationale in _EXEMPT_RATIONALE_PLACEHOLDERS:
            violations.append(f"{relative_path}: exempt record needs a substantive exemption rationale")

    if require_live_verification and acceptance_url and _ACCEPTANCE_RECORD.fullmatch(acceptance_url):
        if acceptance_resolver is None:
            violations.append(f"{relative_path}: live GitHub acceptance verification is unavailable")
        else:
            try:
                acceptance = acceptance_resolver(acceptance_url)
            except Exception:
                acceptance = None
            if acceptance is None:
                violations.append(f"{relative_path}: acceptance record could not be verified on GitHub")
            else:
                if accepted_by and acceptance.actor.casefold() != accepted_by.casefold():
                    violations.append(f"{relative_path}: acceptance actor does not match Accepted by")
                actual_timestamp = _parse_timestamp(acceptance.created_at)
                if accepted_at and actual_timestamp != accepted_at:
                    violations.append(f"{relative_path}: acceptance timestamp does not match GitHub")
                review_id = fields.get("review id", "")
                token_kind = "EXEMPTION" if status == "exempt" else "REVIEW"
                expected_token = f"ACCEPT-RESEARCH-{token_kind} {review_id}"
                if expected_token.casefold() not in acceptance.body.casefold():
                    violations.append(f"{relative_path}: GitHub record lacks the explicit acceptance token")

    return violations


def _safe_review_path(value: str) -> PurePosixPath | None:
    path = PurePosixPath(value)
    if path.is_absolute() or ".." in path.parts:
        return None
    if not path.is_relative_to(_REVIEW_PREFIX) or path.suffix != ".md":
        return None
    return path


def validate_research_gate(
    root: Path,
    changed_paths: list[str],
    pull_request_body: str,
    *,
    base_text_loader: TextLoader | None = None,
    acceptance_resolver: AcceptanceResolver | None = None,
    source_resolver: SourceResolver | None = None,
    expected_owner: str | None = None,
) -> list[str]:
    sensitive = sorted(path for path in changed_paths if _is_research_sensitive(path))
    if not sensitive:
        return []

    match = _RESEARCH_RECORD.search(pull_request_body)
    if match is None or not match.group(1).strip():
        return [
            "Research record is required for executable src/analytics changes; "
            "link an accepted review or independently accepted deterministic exemption"
        ]

    value = match.group(1).strip()
    review_path = _safe_review_path(value)
    if review_path is None:
        return ["Research record must be a Markdown file under docs/research/reviews/"]

    current_path = root / review_path
    if not current_path.is_file():
        return [f"Research record does not exist: {review_path.as_posix()}"]
    if base_text_loader is None:
        return ["Research record base-branch verification is unavailable"]

    base_text = base_text_loader(review_path.as_posix())
    if base_text is None:
        return [
            f"{review_path.as_posix()}: accepted research record must predate implementation "
            "and exist on the pull request base branch"
        ]

    return _review_violations(
        root,
        review_path.as_posix(),
        base_text,
        experiment_loader=base_text_loader,
        acceptance_resolver=acceptance_resolver,
        source_resolver=source_resolver,
        expected_owner=expected_owner,
        require_live_verification=True,
    )


def validate_final_review_artifacts(root: Path) -> list[str]:
    review_root = root / _REVIEW_PREFIX
    if not review_root.is_dir():
        return []

    violations: list[str] = []
    for path in sorted(review_root.glob("*.md")):
        text = path.read_text(encoding="utf-8")
        status = _fields(text).get("status", "").casefold()
        if status in {"accepted", "exempt"}:
            relative = path.relative_to(root).as_posix()
            violations.extend(_review_violations(root, relative, text))
    return violations


def _changed_paths(root: Path, base_sha: str) -> tuple[list[str], list[str]]:
    if not base_sha:
        return [], []
    result = subprocess.run(
        ["git", "diff", "--name-only", "--diff-filter=ACMR", base_sha, "HEAD"],
        cwd=root,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        return [], ["Unable to determine changed files for research-review enforcement"]
    return [line for line in result.stdout.splitlines() if line], []


def _git_text_loader(root: Path, base_sha: str) -> TextLoader:
    def load(relative_path: str) -> str | None:
        result = subprocess.run(
            ["git", "show", f"{base_sha}:{relative_path}"],
            cwd=root,
            check=False,
            capture_output=True,
            text=True,
        )
        return result.stdout if result.returncode == 0 else None

    return load


def _resolve_github_acceptance(url: str, token: str) -> Acceptance | None:
    match = _ACCEPTANCE_RECORD.fullmatch(url)
    if match is None or not token:
        return None

    record_id = match.group("id")
    if match.group("kind").casefold() == "issuecomment":
        endpoint = f"https://api.github.com/repos/Jellman86/autoFPL/issues/comments/{record_id}"
        timestamp_field = "created_at"
    else:
        if match.group("area").casefold() != "pull":
            return None
        endpoint = (
            "https://api.github.com/repos/Jellman86/autoFPL/pulls/"
            f"{match.group('number')}/reviews/{record_id}"
        )
        timestamp_field = "submitted_at"

    request = urllib.request.Request(
        endpoint,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "User-Agent": "autoFPL-research-policy",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            payload = json.load(response)
    except (OSError, ValueError, urllib.error.HTTPError):
        return None

    actor = payload.get("user", {}).get("login")
    created_at = payload.get(timestamp_field)
    body = payload.get("body") or ""
    if not actor or not created_at:
        return None
    return Acceptance(actor=actor, created_at=created_at, body=body)


def _source_exists(url: str) -> bool:
    if url.casefold().startswith("https://doi.org/"):
        doi = url[len("https://doi.org/") :]
        endpoint = "https://doi.org/api/handles/" + urllib.parse.quote(doi, safe="")
    else:
        endpoint = url

    request = urllib.request.Request(
        endpoint,
        headers={"Accept": "application/json,text/html", "User-Agent": "autoFPL-research-policy"},
    )
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            if response.status != 200:
                return False
            payload = response.read(64_000)
    except (OSError, urllib.error.HTTPError):
        return False

    if url.casefold().startswith("https://doi.org/"):
        try:
            return json.loads(payload).get("responseCode") == 1
        except (UnicodeDecodeError, ValueError):
            return False
    return bool(payload)


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    base_sha = os.environ.get("BASE_SHA", "")
    changed_paths, violations = _changed_paths(root, base_sha)
    violations.extend(validate_final_review_artifacts(root))

    sensitive = any(_is_research_sensitive(path) for path in changed_paths)
    loader = _git_text_loader(root, base_sha) if sensitive and base_sha else None
    token = os.environ.get("GITHUB_TOKEN", "")
    acceptance_resolver = (
        lambda url: _resolve_github_acceptance(url, token)
        if sensitive
        else None
    )
    source_resolver = _source_exists if sensitive else None
    violations.extend(
        validate_research_gate(
            root,
            changed_paths,
            os.environ.get("PR_BODY", ""),
            base_text_loader=loader,
            acceptance_resolver=acceptance_resolver,
            source_resolver=source_resolver,
            expected_owner=os.environ.get("PR_AUTHOR", ""),
        )
    )

    if violations:
        for violation in sorted(set(violations)):
            print(f"- {violation}", file=sys.stderr)
        return 1

    print("Research review policy check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
