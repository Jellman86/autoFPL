# autoFPL documentation

Use this index to find the maintained source for each topic. The deterministic private-development foundation, decision room and SQLite snapshots are deployed. Fixed-origin official FPL reference and final-outcome capture plus cutoff-safe pairing are implemented; the first completed 2026/27 Gameweek pair, forecasting, simulation, optimisation, advisory MCP tools and proposal persistence do not yet exist.

## Start here

- [Project overview](../README.md) — purpose, current status, architecture and non-negotiable boundaries.
- [Roadmap](roadmap.md) — prioritised future outcomes and their evidence gates.
- [Architecture overview](architecture/README.md) — approved system boundaries and decision records.
- [Contributing](../CONTRIBUTING.md) — branch, test and review workflow.

## Standards

- [Documentation standard](standards/documentation.md) — how documentation is structured, grounded and validated.
- [Definition of Done](standards/definition-of-done.md) — evidence required before a change is complete.
- [Engineering standard](standards/engineering.md) — architecture, language, testing and dependency gates.
- [OpenAPI standard](standards/openapi.md) — HTTP contract generation, metadata, compatibility, errors and security.
- [Research standard](standards/research.md) — point-in-time evaluation, baselines, calibration and reproducibility.
- [Data-quality and provenance standard](standards/data-governance.md) — point-in-time collection, source quality, reproducibility and derived-feature controls.
- [Security standard](standards/security.md) — trust boundaries, secret handling and CI controls.

## Architecture and contracts

- [Architecture decision records](adr/) — accepted and superseded durable decisions.
- [Versioned contracts](../contracts/README.md) — machine-readable service and data boundaries.
- [Backend boundary](../src/backend/README.md) — .NET product and contract projects.
- [Analytics boundary](../src/analytics/README.md) — future Python forecasting, simulation and optimisation ownership.
- [Web boundary](../src/web/README.md) — future presentation-layer ownership.

## Product boundary, security and governance

- [FPL access boundary](compliance/fpl-terms-boundary.md) — proportionate private-research and account-action boundaries.
- [Threat model](security/threat-model.md) — assets, trust boundaries and mitigations.
- [Security policy](../SECURITY.md) — reporting and supported security posture.
- [Governance](../GOVERNANCE.md) — roles, branch policy, decisions and release control.

## Data-source provenance

- [Provenance register](data/sources/README.md) — source status, timing, quality and collection-method records.
- [Source-record v1 contract](../contracts/data-source/v1/source-record.schema.json) — machine-readable source timing, identity, hash and correction envelope; not yet a runtime API.
- [Manual evidence timing v1](data/manual-evidence-timing-v1.md) — exact current POST-field inventory, evidence classes and leakage-safe replay semantics.
- [Manual user input v1](data/sources/manual-user-input-v1.md) — versioned direct-entry scope, privacy and point-in-time constraints.
- [Repository synthetic fixtures v1](data/sources/repository-synthetic-fixtures-v1.md) — versioned fictional test/evidence boundary.
- [Official FPL read-only API v1](data/sources/official-fpl-api-v1.md) — fixed-origin player, Gameweek, team, fixture and outcome capture semantics.
- [FPL Form public forecast v1](data/sources/fpl-form-public-forecast-v1.md) — fixed-origin conditional predicted-points capture and evaluation boundary.

## Research and evidence

- [Evidence base](research/evidence-base.md) — durable sources supporting the research approach.
- [Baseline evaluation v4](research/baseline-evaluation-v4.md) — executable point, expected-minutes, availability and empirical distribution baselines with rolling chronology, proper scores and calibration diagnostics.
- [FPL Form external evaluation v1](research/fpl-form-external-evaluation-v1.md) — cutoff- and identity-gated scoring of published conditional points and the separately named appearance-adjusted challenger.
- [Temporal feature table v2](research/temporal-feature-table-v2.md) — cutoff-safe player, underlying-outcome and team match-leading features with explicit missingness and correction chronology.
- [Temporal ridge challenger v1](research/temporal-ridge-v1.md) — fold-local regularised total-points challenger over the cutoff-safe temporal feature table.
- [Temporal histogram-tree challenger v1](research/temporal-tree-v1.md) — fixed nonlinear comparison on the ridge evaluator's identical expanding-origin folds.
- [FPL Form temporal feature v1](research/fpl-form-temporal-feature-v1.md) — exact-cutoff, strict-direct-identity bridge from retained public forecasts into model-ready player features.
- [FPL Form feature ablation v1](research/fpl-form-feature-ablation-v1.md) — same-cohort official-only versus public-forecast ridge/tree comparison with source-complete temporal folds.
- [Predictive research-review template](research/literature-review-template.md) — optional working note for method selection and promotion registration.
- [Dataset-card template](research/dataset-card-template.md) — provenance, rights and quality record.
- [Model-card template](research/model-card-template.md) — intended use, evaluation, limitations and monitoring.
- `docs/research/experiment-template.yaml` — preregistered experiment metadata.

## Operations

- [Development container](operations/container.md) — build, scan, smoke-test, publish and private deployment of the bounded validation API.

Additional runbooks must be added with the runnable services they support and include verification and recovery rather than documenting an unmerged deployment.

## Project history

- [Changelog](../CHANGELOG.md) — implemented changes, with current work under **Unreleased**.
- GitHub issues — actionable planned work, acceptance criteria and reproducible defects.

If a maintained reader-facing page is added, link it here in the same change.
