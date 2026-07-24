# autoFPL

A rigorous, evidence-driven research platform for **human-approved Fantasy Premier League decision support**.

> [!IMPORTANT]
> autoFPL is not an autonomous FPL bot. Until the Premier League grants written permission and supported data/write access, the project must not scrape or automate FPL, collect Premier League credentials, replay user sessions, or submit team changes.

## Status

**Foundation phase.** This repository currently establishes the research, engineering, security, data-governance and release standards that every later feature must satisfy.

See the [documentation index](docs/index.md), [roadmap](docs/roadmap.md) and [changelog](CHANGELOG.md) for maintained guidance, planned outcomes and implemented changes.

## Intended architecture

- **.NET 10 / C#** — product API, workflow control, authentication, audit and MCP tools.
- **Python 3.14 (3.13 fallback when package compatibility requires it)** — predictive machine learning, probabilistic forecasting, simulation, backtesting and mathematical optimisation.
- **TypeScript / Next.js** — evidence-rich dashboard.
- **PostgreSQL** — authoritative transactional and analytical metadata.
- **OpenViking** — versioned research and unstructured context, never authoritative squad state.
- **ChatGPT Apps SDK / MCP** — subscription-backed conversational client using read-only autoFPL tools; autoFPL does not hold ChatGPT credentials.
- **Hermes MCP client** — private conversational access to the same versioned prediction tools, using Hermes' independently configured model provider.
- **Optional model-provider adapters** — OpenRouter API or a private Hermes proxy may perform bounded extraction/classification and generate explanations behind a provider-neutral boundary; outputs are quarantined or non-authoritative and neither provider is required for core operation.
- **Evidence-grounded AI decision orchestrator** — the strategic “mind” retrieves relevant data and memory, asks forecasting/simulation/optimisation tools for evidence and compares feasible plans. Application-managed mode persists a structured unapproved proposal; client-hosted ChatGPT/Hermes returns an evidence-grounded advisory synthesis without proposal persistence.

The approved boundaries are recorded in [ADR-0001](docs/adr/0001-hybrid-modular-architecture.md), [ADR-0005](docs/adr/0005-chatgpt-mcp-interface.md), [ADR-0006](docs/adr/0006-optional-model-provider-adapters.md), [ADR-0007](docs/adr/0007-evidence-grounded-ai-decision-orchestrator.md), [ADR-0008](docs/adr/0008-versioned-json-contracts.md) and the [FPL terms boundary](docs/compliance/fpl-terms-boundary.md).

## Non-negotiable quality principles

1. Safety and terms compliance before convenience.
2. Human approval before any external account action.
3. Tests first for every behaviour change.
4. Point-in-time-correct data and walk-forward evaluation only.
5. Probabilistic forecasts must be calibrated and compared with simple baselines.
6. Optimiser outputs must be feasible, reproducible and independently checked.
7. Every material result must link code, data snapshot, environment, seed and evidence.
8. No secrets, mutable CI dependencies or unreviewed direct pushes.
9. Claims must be labelled as observed fact, sourced evidence, assumption or judgement.
10. A feature is not complete until the repository's [Definition of Done](docs/standards/definition-of-done.md) is satisfied.

## Branch and release flow

```text
feature/* or fix/* -> pull request -> dev -> release PR -> main -> signed/tagged release
```

- `main` is releasable and protected.
- `dev` is the integration branch and protected.
- Work starts from `dev` on a short-lived branch.
- Squash merges use Conventional Commit titles.
- Hotfixes into `main` must be reconciled back into `dev` immediately.

See [GOVERNANCE.md](GOVERNANCE.md) and [CONTRIBUTING.md](CONTRIBUTING.md).

## Local governance checks

Install the hash-locked governance dependency, then run the same checks as CI:

```bash
python3 -m pip install --require-hashes --requirement requirements-governance.txt
make verify
```

`make verify` runs the Python governance tests, locked .NET tests, repository policy and documentation-link validation used by CI.

## Repository map

- `docs/adr/` — immutable architecture decision records.
- `docs/standards/` — enforceable engineering and scientific standards.
- `docs/compliance/` — legal and product-operation boundaries.
- `docs/research/` — evidence base and reproducibility templates.
- `docs/operations/` — build, deployment, verification and rollback runbooks.
- `docs/index.md` — maintained routing index for repository documentation.
- `docs/roadmap.md` — prioritised future outcomes and evidence gates.
- `contracts/` — immutable, versioned machine-readable service and data boundaries.
- `src/` — product and analytics implementation boundaries.
- `tools/governance/` — executable repository policy.
- `tests/` — governance, domain and contract tests.
- `CHANGELOG.md` — implemented notable changes, with current work under **Unreleased**.

## Licensing and data rights

autoFPL source code is licensed under **GNU AGPL-3.0-only**. See [LICENSE](LICENSE). If a modified version is offered for users to interact with over a network, the licence requires an opportunity for those users to receive the corresponding source code.

Runtime and user data are not licensed by the source-code licence. Third-party datasets, papers, model artefacts, source snapshots and derived fields require documented provenance and compatible collection, use and redistribution rights before inclusion. Personal squad history, private research snapshots, credentials and deployment configuration belong in ignored local/runtime storage—not this public repository. No absence of a technical access control implies permission to collect or reuse data.
