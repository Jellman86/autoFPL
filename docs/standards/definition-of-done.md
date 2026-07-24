# Definition of Done

A change is complete only when every applicable gate below is evidenced. “Not applicable” requires a reason in the PR.

## Scope and behaviour

- Acceptance criteria and explicit non-goals are satisfied.
- Behaviour is delivered as the smallest complete vertical slice.
- No unrelated refactor, integration, deployment or data collection is included.
- User-facing limitations and uncertainty are clear.

## Tests and quality

- Every new behaviour was introduced by a failing test first.
- Focused, unit, contract, integration and end-to-end tests pass as applicable.
- Failure paths, boundaries and malformed inputs are covered.
- Formatting, linting, static analysis and type checking pass with no new warnings.
- Tests are deterministic; flakes are fixed, not retried into invisibility.
- Mutation/property tests are used for critical domain invariants where valuable.

## Research validity

- The hypothesis, decision rule, baselines, metrics and evaluation window were registered before final evaluation.
- Data is point-in-time correct and leakage checks pass.
- Walk-forward results cover multiple seasons/regimes where the data permits.
- Calibration, proper scoring rules, decision utility and material slices are reported.
- Uncertainty, limitations, negative results and sensitivity analyses are retained.
- The result is reproducible from code SHA, immutable data snapshot, lockfile, seeds and command.

## Optimisation validity

- Every returned squad/action satisfies all season-versioned rules and budget constraints.
- The solver status, objective, gap, runtime, input forecast version and constraints are recorded.
- Small instances are checked against brute force or a trusted reference.
- Infeasible/timeout cases fail safely and never masquerade as recommendations.
- Sensitivity to forecast error, horizon and risk objective is reported.

## Security, privacy and compliance

- Threat model and abuse cases are updated for changed trust boundaries.
- No secret, credential, private data or unauthorised FPL-derived data is committed.
- Dependencies and Actions are immutable/locked and licence-reviewed.
- Least privilege, validation, audit and retention/deletion requirements are met.
- The change remains inside the FPL terms boundary and human-approval invariant.

## Operations

- Structured logs, metrics and traces reveal success/failure without sensitive data.
- Health/readiness checks cover real dependencies.
- Database changes are forward compatible, idempotent where applicable and rollback/roll-forward tested.
- Deployment and data migrations have explicit rollback or recovery steps.
- Runbooks and alerts are updated.

## Documentation and review

- Documentation follows the [documentation standard](documentation.md), is linked from the index where reader-facing, and passes the documentation checker.
- User, API, architecture, ADR, model/dataset card and runbook changes are included where applicable.
- User- or operator-relevant implemented behaviour is recorded under **Unreleased** in `CHANGELOG.md`; future work remains in the roadmap or GitHub issues.
- PR contains commands and real outputs used for verification.
- For changes to security/trust boundaries, deployment or supply chain, authoritative domain logic, data/research promotion, credentials or external actions, independent review found no unresolved blocking issue. Ordinary low-risk changes receive focused author review.
- Required GitHub checks pass on the PR head being merged.
