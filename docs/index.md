# autoFPL documentation

Use this index to find the maintained source for each topic. The repository is in its foundation phase and currently includes a versioned decision-snapshot metadata contract plus a bounded private-development validation service. Pages for end-user workflows that do not yet exist are intentionally absent.

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
- [Data-governance standard](standards/data-governance.md) — rights, provenance, retention and derived-data controls.
- [Security standard](standards/security.md) — trust boundaries, secret handling and CI controls.

## Architecture and contracts

- [Architecture decision records](adr/) — accepted and superseded durable decisions.
- [Versioned contracts](../contracts/README.md) — machine-readable service and data boundaries.
- [Backend boundary](../src/backend/README.md) — .NET product and contract projects.
- [Analytics boundary](../src/analytics/README.md) — future Python forecasting, simulation and optimisation ownership.
- [Web boundary](../src/web/README.md) — future presentation-layer ownership.

## Compliance, security and governance

- [FPL terms boundary](compliance/fpl-terms-boundary.md) — prohibited and permission-gated FPL access.
- [Threat model](security/threat-model.md) — assets, trust boundaries and mitigations.
- [Security policy](../SECURITY.md) — reporting and supported security posture.
- [Governance](../GOVERNANCE.md) — roles, branch policy, decisions and release control.

## Data-source admission

- [Admission register](data/sources/README.md) — source statuses, universal prohibitions and review requirements.
- [Source-record v1 contract](../contracts/data-source/v1/source-record.schema.json) — machine-readable admitted-source, rights, timing, hash and correction envelope; not yet a runtime API.
- [Manual user input v1](data/sources/manual-user-input-v1.md) — admitted direct-entry scope, privacy and point-in-time constraints.
- [Repository synthetic fixtures v1](data/sources/repository-synthetic-fixtures-v1.md) — admitted fictional test/evidence boundary.

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
