from __future__ import annotations

import os
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path, PurePosixPath

_RESEARCH_RECORD = re.compile(
    r"^\s*-?\s*\*\*Research record:\*\*\s*`?([^`\s]+)`?\s*$",
    re.IGNORECASE | re.MULTILINE,
)
_FIELD = re.compile(
    r"^\s*-\s*\*\*(?P<name>[^*]+):\*\*\s*(?P<value>.*?)\s*$",
    re.MULTILINE,
)
_IMMUTABLE_SOURCE = re.compile(
    r"https://doi\.org/[^\s|)]+|https://arxiv\.org/(?:abs|pdf)/\d{4}\.\d{4,5}v\d+",
    re.IGNORECASE,
)
_ACCEPTANCE_RECORD = re.compile(
    r"https://github\.com/Jellman86/autoFPL/(?:pull|issues)/\d+(?:[#/?][^\s]*)?",
    re.IGNORECASE,
)
_REVIEW_PREFIX = PurePosixPath("docs/research/reviews")
_EXEMPT_RATIONALE_PLACEHOLDERS = {"", "not applicable", "n/a", "none", "todo"}


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


def _valid_utc_timestamp(value: str) -> bool:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return False
    return parsed.tzinfo is not None and parsed.utcoffset() is not None


def _review_violations(root: Path, relative_path: str, text: str) -> list[str]:
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

    owner = fields.get("owner", "").casefold()
    accepted_by = fields.get("accepted by", "").casefold()
    if owner and accepted_by and owner == accepted_by:
        violations.append(f"{relative_path}: accepted by must be distinct from the owner")

    accepted_at = fields.get("accepted at (utc)", "")
    if accepted_at and not _valid_utc_timestamp(accepted_at):
        violations.append(f"{relative_path}: accepted at (UTC) must be a timezone-aware ISO timestamp")

    acceptance_record = fields.get("acceptance record", "")
    if acceptance_record and not _ACCEPTANCE_RECORD.search(acceptance_record):
        violations.append(f"{relative_path}: acceptance record must link to an autoFPL PR or issue review record")

    if status == "accepted":
        if classification != "research-required":
            violations.append(f"{relative_path}: accepted research review classification must be research-required")
        if not _IMMUTABLE_SOURCE.search(text):
            violations.append(f"{relative_path}: accepted review needs an immutable DOI or versioned arXiv source")

        experiment = fields.get("experiment", "")
        experiment_path = PurePosixPath(experiment)
        if (
            not experiment
            or not experiment_path.is_relative_to(PurePosixPath("docs/research/experiments"))
            or experiment_path.suffix not in {".yaml", ".yml"}
            or not (root / experiment_path).is_file()
        ):
            violations.append(f"{relative_path}: accepted review must link an existing registered experiment")
        else:
            experiment_text = (root / experiment_path).read_text(encoding="utf-8")
            if not re.search(r"^status:\s*registered\s*(?:#.*)?$", experiment_text, re.MULTILINE):
                violations.append(f"{relative_path}: linked experiment status must be registered")

    if status == "exempt":
        if classification != "deterministic-non-inferential":
            violations.append(
                f"{relative_path}: exempt record classification must be deterministic-non-inferential"
            )
        rationale = fields.get("exemption rationale", "").casefold()
        if rationale in _EXEMPT_RATIONALE_PLACEHOLDERS:
            violations.append(f"{relative_path}: exempt record needs a substantive exemption rationale")

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

    full_path = root / review_path
    if not full_path.is_file():
        return [f"Research record does not exist: {review_path.as_posix()}"]

    return _review_violations(
        root,
        review_path.as_posix(),
        full_path.read_text(encoding="utf-8"),
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


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    changed_paths, violations = _changed_paths(root, os.environ.get("BASE_SHA", ""))
    violations.extend(validate_final_review_artifacts(root))
    violations.extend(
        validate_research_gate(root, changed_paths, os.environ.get("PR_BODY", ""))
    )

    if violations:
        for violation in sorted(set(violations)):
            print(f"- {violation}", file=sys.stderr)
        return 1

    print("Research review policy check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
