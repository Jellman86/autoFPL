# ADR-0003: Protected main and dev branches

- **Status:** Accepted
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** architecture

## Context

The project needs a stable release branch and an integration boundary without a complex long-lived GitFlow model. Development controls should maximise correctness and research validity rather than add ceremony that slows evidence-producing iterations. A GitHub-generated verified commit proves account authorisation, not human review, and is therefore a weak development control compared with tests, leakage prevention and reproducible evaluation.

## Decision

- `main` is releasable and receives release/hotfix PRs only.
- `dev` receives feature/fix/research PRs from short-lived branches.
- Both branches require pull requests, prohibit force push/deletion and require resolved conversations where GitHub plan capabilities permit.
- `dev` requires the comprehensive repository-policy job and Gitleaks. CodeQL and dependency review still run but are not universal blockers. `dev` does not require signed commits or strict stale-base reruns.
- `main` remains the strict release boundary: it requires up-to-date release checks and verified commit signatures; releases use signed tags.
- Squash merge is standard; release PRs promote an already validated `dev` state.
- Independent review is risk-based and mandatory for security, deployment/supply-chain, authoritative logic, data/research promotion, credential and external-action changes. Ordinary low-risk work relies on focused author review and CI.
- Once a second eligible reviewer exists, code-owner approval becomes required.

## Consequences

The two long-lived branches add promotion overhead but isolate integrated development from release state. Fast `dev` gates preserve engineering and scientific quality without making routine iteration depend on signatures, stale-base reruns or unrelated slow analyses. Stronger release controls remain concentrated on `main`. Branch settings must not deadlock a single maintainer into routine administrative bypasses.
