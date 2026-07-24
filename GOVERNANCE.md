# Governance

## Roles

The repository owner is the final steward of product scope, compliance and releases. Contributors and agents may propose changes but cannot override protected-branch, security, research-integrity or human-approval boundaries.

`CODEOWNERS` records accountable review ownership. A code-owner review requirement should be enabled once a second eligible human reviewer exists; a single maintainer must not create a control that can only be bypassed administratively.

## Decision classes

| Decision | Required record |
|---|---|
| Product behaviour | Issue and acceptance tests |
| Architecture or external integration | ADR |
| Data source or derived dataset | Data-source review and dataset card |
| Model/forecast promoted to decision use | Registered experiment and model card |
| Security/privacy boundary | Threat-model update and security review |
| FPL automation or commercial data use | Written permission plus compliance ADR |
| Release | Release PR, changelog and rollback evidence |

## Branch policy

### `main`

- Always releasable.
- Changes only by release or emergency hotfix PR.
- Required CI must pass on the exact head SHA.
- History is immutable; force pushes and deletions are forbidden.
- Releases are tagged from `main` using SemVer once product releases begin.

### `dev`

- Integration branch for completed vertical slices.
- Changes only by PR from short-lived branches.
- Required CI must pass and review conversations must be resolved.
- Force pushes and deletions are forbidden.

### Emergency changes

A hotfix PR may target `main` only for an active security, data-integrity or availability incident. It still requires a reproducing test, focused review, rollback steps and immediate reconciliation into `dev`.

## Merge and release policy

- Squash merge feature PRs with a Conventional Commit title.
- Do not merge red CI, unresolved blocking review or an expired exception.
- Release PRs promote a known `dev` commit to `main`; they do not introduce unrelated code.
- Versioning, changelog and database compatibility are verified before a tag.
- Deployment is a separate permission from merge and must be reversible.

## Policy changes

Changes that weaken a non-negotiable standard require an ADR, explicit risk analysis and owner approval. The executable governance test must change in the same PR as the policy it enforces.
