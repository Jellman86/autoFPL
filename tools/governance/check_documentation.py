from __future__ import annotations

import re
import sys
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import unquote, urlsplit

from markdown_it import MarkdownIt
from markdown_it.token import Token

_EXCLUDED_PARTS = {
    ".git",
    ".hermes",
    ".mypy_cache",
    ".pytest_cache",
    ".ruff_cache",
    ".venv",
    "bin",
    "node_modules",
    "obj",
    "__pycache__",
}
_GITHUB_SLUG_UNSUPPORTED = re.compile(r"[^\w\- ]", re.UNICODE)
_PARSER = MarkdownIt("commonmark")


@dataclass(frozen=True)
class MarkdownDocument:
    links: tuple[str, ...]
    fragments: frozenset[str]


def _markdown_files(root: Path) -> list[Path]:
    return sorted(
        path
        for path in root.rglob("*")
        if path.is_file()
        and path.suffix.lower() == ".md"
        and not _EXCLUDED_PARTS.intersection(path.relative_to(root).parts)
    )


def _heading_text(token: Token) -> str:
    parts: list[str] = []
    for child in token.children or []:
        if child.type in {"text", "code_inline", "image"}:
            parts.append(child.content)
        elif child.type in {"softbreak", "hardbreak"}:
            parts.append(" ")
    return "".join(parts)


def _github_slug(text: str, occurrences: dict[str, int]) -> str:
    base = _GITHUB_SLUG_UNSUPPORTED.sub("", text.strip().lower()).replace(" ", "-")
    duplicate_index = occurrences.get(base, 0)
    occurrences[base] = duplicate_index + 1
    return base if duplicate_index == 0 else f"{base}-{duplicate_index}"


def _parse_document(path: Path) -> MarkdownDocument:
    tokens = _PARSER.parse(path.read_text(encoding="utf-8-sig"))
    links: list[str] = []
    fragments: set[str] = set()
    slug_occurrences: dict[str, int] = {}

    for index, token in enumerate(tokens):
        if token.type == "heading_open" and index + 1 < len(tokens):
            inline = tokens[index + 1]
            if inline.type == "inline":
                fragments.add(_github_slug(_heading_text(inline), slug_occurrences))

        if token.type != "inline":
            continue
        for child in token.children or []:
            if child.type == "link_open":
                href = child.attrGet("href")
                if href:
                    links.append(href)
            elif child.type == "image":
                source = child.attrGet("src")
                if source:
                    links.append(source)

    return MarkdownDocument(tuple(links), frozenset(fragments))


def documentation_violations(root: Path) -> list[str]:
    """Return broken, repository-escaping or fragment-invalid Markdown links."""
    root = root.resolve()
    violations: list[str] = []
    documents: dict[Path, MarkdownDocument] = {}

    for source in _markdown_files(root):
        resolved_source = source.resolve()
        try:
            resolved_source.relative_to(root)
        except ValueError:
            violations.append(f"{source.relative_to(root)} (source outside repository)")
            continue
        documents[resolved_source] = _parse_document(source)

    for source, document in sorted(documents.items()):
        for target in document.links:
            parsed = urlsplit(target)
            if parsed.scheme or parsed.netloc:
                continue

            local_target = unquote(parsed.path)
            destination = (
                source
                if not local_target
                else (
                    root / local_target.lstrip("/")
                    if local_target.startswith("/")
                    else source.parent / local_target
                ).resolve()
            )

            try:
                destination.relative_to(root)
            except ValueError:
                violations.append(
                    f"{source.relative_to(root)} -> {target} (outside repository)"
                )
                continue

            if not destination.exists():
                violations.append(f"{source.relative_to(root)} -> {target} (missing)")
                continue

            fragment = unquote(parsed.fragment)
            if fragment:
                destination_document = documents.get(destination)
                if destination_document is None or fragment not in destination_document.fragments:
                    violations.append(
                        f"{source.relative_to(root)} -> {target} (missing fragment)"
                    )

    return violations


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    violations = documentation_violations(root)
    if violations:
        print("Documentation link violations:", *violations, sep="\n  ")
        return 1

    print("Local documentation links and fragments are valid.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
