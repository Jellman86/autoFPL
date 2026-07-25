# Governance

## Purpose

autoFPL is a private, non-commercial, self-hosted home-lab project. Governance exists to produce reliable code and honest prediction evidence, protect credentials/account boundaries and keep the system maintainable. It is not an enterprise approval structure.

## Responsibility

The maintainer prioritises work, implements/reviews ordinary changes and controls releases. An independent reviewer is used when a change materially affects security/trust boundaries, credentials, deployment/supply chain, authoritative FPL rules, promoted data/models or external actions. One person does not need to impersonate separate project, research, security and release roles for routine work.

## Decision model

- Ordinary product and research decisions live in code, tests, issues and concise PR rationale.
- Write an ADR only for a consequential, hard-to-reverse architecture, security or data decision.
- Exploratory analytics may begin immediately. Literature guides candidate selection; registration and independent review apply before a final holdout is opened or a result is promoted into recommendations.
- Sources and features are judged by point-in-time correctness, reliability and out-of-time predictive value. Private research does not require enterprise vendor admission or written-permission paperwork, but must not bypass login/paid access, collect private session material, create abusive load or republish copyrighted corpora.
- Plans, contracts and documentation must support a working slice rather than become standalone milestones.

## Branches and pull requests

- `dev` is the integration branch and `main` is the supported release branch.
- Work uses short-lived branches and focused PRs.
- Required CI must pass before merge.
- Ordinary low-risk work may merge after author review and green CI.
- Higher-risk work receives the relevant independent review before merge.
- Deployment permission is separate from merge permission; docs/governance-only changes are not deployed.

## Releases

A release promotes a known `dev` revision to `main`, uses a signed SemVer tag and pins deployed images by digest. Release notes, migration/recovery evidence and deployment verification cover the behaviour that actually changed. Do not impose release operations on ordinary development commits.

## Non-overridable boundaries

No exception may permit committed secrets, automatic FPL account actions, temporal leakage in a promoted result, fabricated research evidence, unsafe untrusted-input handling or broader runtime privileges without explicit approval.

Other temporary exceptions are recorded concisely in the issue or PR with scope, reason, owner and removal condition. If an exception becomes permanent or changes a hard-to-reverse decision, update the relevant standard or ADR instead of accumulating waiver paperwork.
