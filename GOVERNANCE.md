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
- Required release CI must pass on an up-to-date head SHA.
- Commits require a verified signature; releases use signed SemVer tags once product releases begin.
- History is immutable; force pushes and deletions are forbidden.

### `dev`

- Integration branch for completed vertical slices.
- Changes only by PR from short-lived branches.
- The fast comprehensive `repository-policy` job and Gitleaks are required; this includes locked restore, application tests, governance tests, documentation checks and repository policy.
- CodeQL and dependency review continue to run. Their findings must be resolved when relevant, but those slower jobs are not universal merge blockers for every development PR.
- Commits do not require signatures and the branch need not be rebased solely to repeat already-passing checks after an unrelated `dev` update.
- Review conversations must be resolved before merge.
- Force pushes and deletions are forbidden.

### Emergency changes

A hotfix PR may target `main` only for an active security, data-integrity or availability incident. It still requires a reproducing test, focused review, rollback steps and immediate reconciliation into `dev`.

## Merge and release policy

- Squash merge feature PRs with a Conventional Commit title.
- Do not merge red CI, unresolved blocking review or an expired exception.
- Require independent review for changes to security/trust boundaries, deployment or supply chain, authoritative domain logic, data/research promotion, credentials, or external actions. Ordinary low-risk changes use focused author review plus CI.
- Release PRs promote a known `dev` commit to `main`; they do not introduce unrelated code.
- Versioning, changelog and database compatibility are verified before a tag.
- Deployment is a separate permission from merge and must be reversible.

## Policy changes

Changes to a non-negotiable scientific, security, compliance or human-approval boundary require an ADR, explicit risk analysis and owner approval. Machine-enforced repository policy changes with the executable governance test; GitHub-hosted settings are verified by API read-back and recorded in the policy PR.
