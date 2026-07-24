#!/usr/bin/env python3
"""Minimal semantic checks for the two source-record contract examples."""

from __future__ import annotations

import json
import sys
from collections.abc import Mapping
from datetime import datetime
from pathlib import Path
from typing import Final

EXPECTED_EXAMPLES: Final = ("manual.json", "synthetic.json")


def check_source_record_document(document: object) -> list[str]:
    """Check chronology that Draft-07 cannot express."""
    if not isinstance(document, Mapping):
        return ["source record must be a JSON object"]

    violations: list[str] = []
    parsed: dict[str, datetime] = {}
    for field in ("retrievedAt", "availableAt"):
        value = _parse_utc(document.get(field))
        if value is None:
            violations.append(f"{field} must be an RFC 3339 UTC timestamp ending in Z")
        else:
            parsed[field] = value

    for field in ("observedAt", "publishedAt"):
        raw = document.get(field)
        if raw is None:
            continue
        value = _parse_utc(raw)
        if value is None:
            violations.append(f"{field} must be null or an RFC 3339 UTC timestamp ending in Z")
        else:
            parsed[field] = value

    observed = parsed.get("observedAt")
    published = parsed.get("publishedAt")
    retrieved = parsed.get("retrievedAt")
    available = parsed.get("availableAt")

    if observed is not None and published is not None and observed > published:
        violations.append("observedAt must not follow publishedAt")
    if observed is not None and retrieved is not None and observed > retrieved:
        violations.append("observedAt must not follow retrievedAt")
    if published is not None and available is not None and published > available:
        violations.append("availableAt must not precede publishedAt")
    if retrieved is not None and available is not None and retrieved > available:
        violations.append("availableAt must not precede retrievedAt")

    record_id = document.get("recordId")
    supersedes = document.get("supersedesRecordId")
    if supersedes is not None and supersedes == record_id:
        violations.append("supersedesRecordId must differ from recordId")

    return violations


def check_source_record_examples(root: Path) -> list[str]:
    """Run semantic checks for the two contract examples."""
    directory = root / "contracts/data-source/v1/examples"
    violations: list[str] = []
    for name in EXPECTED_EXAMPLES:
        path = directory / name
        if not path.is_file():
            violations.append(f"missing source-record example: {name}")
            continue
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            violations.append(f"{name}: invalid JSON: {error}")
            continue
        violations.extend(
            f"{name}: {violation}"
            for violation in check_source_record_document(document)
        )
    return violations


def _parse_utc(value: object) -> datetime | None:
    if not isinstance(value, str) or not value.endswith("Z") or "T" not in value:
        return None
    try:
        return datetime.fromisoformat(f"{value[:-1]}+00:00")
    except ValueError:
        return None


def main() -> int:
    violations = check_source_record_examples(Path.cwd())
    for violation in violations:
        print(violation, file=sys.stderr)
    if violations:
        return 1
    print("Source-record examples satisfy timestamp policy.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
