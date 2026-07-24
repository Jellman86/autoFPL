# ADR-0003: Protected main and dev branches

- **Status:** Accepted
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** architecture

## Context

The project needs a stable release branch and an integration boundary without a complex long-lived GitFlow model.

## Decision

- `main` is releasable and receives release/hotfix PRs only.
- `dev` receives feature/fix/research PRs from short-lived branches.
- Both branches require pull requests and CI, prohibit force push/deletion and require resolved conversations where GitHub plan capabilities permit.
- Squash merge is standard; release PRs promote an already validated `dev` state.
- Once a second eligible reviewer exists, code-owner approval becomes required.

## Consequences

The two long-lived branches add promotion overhead but isolate integrated development from release state. Branch settings must not deadlock a single maintainer into routine administrative bypasses.
