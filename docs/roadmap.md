# autoFPL roadmap

This roadmap describes intended outcomes and the evidence required to advance. It is not a release promise. Priorities can change when compliance, data rights, research results, operational evidence or user needs change.

## In progress

### 1. Runnable private development service

Deliver the smallest deployable API boundary without implying that forecasting or FPL automation exists.

- Expose liveness, readiness and versioned decision-snapshot metadata validation.
- Build, scan and smoke-test one hardened non-root container artifact.
- Publish the exact verified artifact to GHCR from protected CI.
- Deploy a digest-pinned private development instance with no public route.

**Exit evidence:** contract and integration tests pass; malformed and oversized requests fail closed; the image has no HIGH/CRITICAL vulnerability finding; the running digest, health checks and rollback path are verified. Tracked by [issue #9](https://github.com/Jellman86/autoFPL/issues/9).

The API, image and protected publication pipeline are implemented by issue #9. Digest-pinned private deployment and runtime/rollback verification remain outstanding.

## Up next

### 2. Lawful data-source and ingestion foundation

Decide what data autoFPL can collect, retain and derive before building ingestion jobs.

- Inventory candidate official, licensed, open and user-supplied sources.
- Record licence, terms, permitted purposes, attribution, retention and redistribution constraints.
- Define source identities, season validity, `observed_at`, `available_at`, revisions and immutable snapshots.
- Implement ingestion only for approved sources, with provenance and data-quality checks.

**Exit evidence:** each enabled source has an approved review and dataset card; point-in-time reconstruction is tested; no technical-access shortcut bypasses the [FPL terms boundary](compliance/fpl-terms-boundary.md).

### 3. Authoritative FPL state and deterministic rules

Build the authoritative read model before predictive components can influence a decision.

- Version season rules, deadlines, scoring, prices, positions and chip constraints.
- Represent money and time with exact domain types.
- Reconstruct a decision snapshot using only information available at its deadline.
- Check squad and action feasibility with deterministic code and explicit invariants.

**Exit evidence:** contract, property and historical fixture tests cover rule boundaries; small feasibility cases agree with brute force or a trusted reference; corrections create revisions rather than rewriting evidence.

### 4. Baseline forecasts and research pipeline

Establish calibrated baselines before adding complex machine learning.

- Preregister prediction targets, horizons, baselines, metrics and slices.
- Use rolling-origin walk-forward validation across available seasons and regimes.
- Measure calibration and proper scoring rules as well as decision utility.
- Record code, immutable data snapshot, environment, seeds and artefacts for every promoted result.

**Exit evidence:** simple baselines and candidate models are reproducible; leakage checks pass; uncertainty, negative results and material limitations are retained in model cards.

### 5. Simulation and optimisation

Turn versioned forecasts into reproducible feasible plans while keeping deterministic constraints authoritative.

- Simulate uncertainty and relevant gameweek scenarios from versioned forecast inputs.
- Optimise transfers, captaincy, bench and chip plans over bounded horizons.
- Record solver status, objective, gap, runtime, constraints and sensitivity to forecast error.
- Fail safely on infeasibility, timeout or stale evidence.

**Exit evidence:** tiny instances match brute force; every returned plan is rules-feasible; baseline policies and sensitivity analyses show when optimisation adds value and when it does not.

### 6. Evidence-grounded advisory interfaces

Expose the same bounded evidence to the web application, ChatGPT and Hermes without making an AI model authoritative.

- Provide versioned read-only MCP/API tools for facts, forecasts, simulations and feasible plans.
- Let the decision orchestrator compare alternatives and cite tool-generated evidence.
- Persist structured proposals separately from approval and execution state.
- Keep provider adapters optional and quarantine unvalidated model output.

**Exit evidence:** clients receive equivalent versioned evidence; explanations trace to inputs and uncertainty; no conversational client can silently turn a proposal into an external account action.

### 7. Human approval workflow and product experience

Make review, comparison and approval clear before considering any external integration.

- Present assumptions, alternatives, uncertainty, deadlines and expected trade-offs.
- Record proposal revision, evidence versions and explicit approval decisions.
- Support accessible review and rejection without dark patterns or hidden automation.
- Keep all FPL changes advisory/manual unless separately permissioned.

**Exit evidence:** end-to-end tests prove proposals remain unapproved by default and cannot bypass human review; usability evidence shows that the decision and its uncertainty are understandable.

### 8. Release and operational hardening

Promote a known `dev` commit only after the service, evidence and recovery path are supportable.

- Add authoritative persistence, migrations, backup/restore and observability as required by delivered features.
- Generate and check API documentation once a stable public contract exists.
- Verify resource bounds, dependency recovery, secret lifecycle and upgrade/rollback paths.
- Produce a SemVer release PR, human-readable release notes and a digest-pinned deployment record.

**Exit evidence:** the [Definition of Done](standards/definition-of-done.md) is satisfied for the release scope; required checks pass on the exact tag; deployment and rollback are exercised with no unresolved blocking review.

## Standing constraints

These constraints apply to every phase:

- Safety and FPL terms compliance take priority over convenience.
- Human approval is required before any external account action.
- Deterministic facts, rules, money, scoring and feasibility remain authoritative.
- Data and research evidence must be point-in-time correct and reproducible.
- OpenRouter and private model-provider integrations remain optional; core operation must not require a paid provider.
- Deployment permission is separate from merge permission.

Completed implementation belongs in the [changelog](../CHANGELOG.md), durable architectural decisions belong in ADRs, and actionable scope belongs in GitHub issues rather than accumulating here as a progress log.
