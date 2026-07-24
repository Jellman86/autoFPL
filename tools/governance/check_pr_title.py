from __future__ import annotations

import os
import re
import sys

_TITLE = re.compile(
    r"^(?:feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)"
    r"(?:\([a-z0-9][a-z0-9._/-]*\))?!?: [a-z0-9].{2,72}$"
)


def is_valid_pr_title(title: str) -> bool:
    """Return whether ``title`` follows the repository's PR title convention."""
    return bool(_TITLE.fullmatch(title)) and len(title) <= 100


def main() -> int:
    title = os.environ.get("PR_TITLE", "")
    if is_valid_pr_title(title):
        print("Pull request title policy passed.")
        return 0
    print(
        "Pull request title must use Conventional Commits, begin its description "
        "with lowercase text and be no more than 100 characters.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
