# autoFPL documentation

Use this index to find the maintained source for each topic. The deterministic private-development foundation is deployed and the next milestone is the v0.1 evidence-grounded single-Gameweek advice loop. Forecasting, simulation, optimisation, advisory MCP tools, proposal workflow and end-user dashboard do not yet exist.

## Start here

- [Project overview](../README.md) — purpose, current status, architecture and non-negotiable boundaries.
- [Roadmap](roadmap.md) — prioritised future outcomes and their evidence gates.
- [Architecture overview](architecture/README.md) — approved system boundaries and decision records.
- [Contributing](../CONTRIBUTING.md) — branch, test and review workflow.

## Standards

- [Documentation standard](standards/documentation.md) — how documentation is structured, grounded and validated.
- [Definition of Done](standards/definition-of-done.md) — evidence required before a change is complete.
- [Engineering standard](standards/engineering.md) — architecture, language, testing and dependency gates.
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

## Research and evidence

- [Evidence base](research/evidence-base.md) — durable sources supporting the research approach.
- [Predictive feature evidence-review template](research/literature-review-template.md) — structured research required before predictive, statistical, simulation or optimisation implementation.
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
