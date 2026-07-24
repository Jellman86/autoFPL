# autoFPL

A rigorous, evidence-driven research platform for **human-approved Fantasy Premier League decision support**.

> [!IMPORTANT]
> autoFPL is not an autonomous FPL bot. Until the Premier League grants written permission and supported data/write access, the project must not scrape or automate FPL, collect Premier League credentials, replay user sessions, or submit team changes.

## Status

**Foundation phase.** This repository currently establishes the research, engineering, security, data-governance and release standards that every later feature must satisfy.

## Intended architecture

- **.NET 10 / C#** — product API, workflow control, authentication, audit and MCP tools.
- **Python 3.14 (3.13 fallback when package compatibility requires it)** — forecasting, simulation, backtesting and mathematical optimisation.
- **TypeScript / Next.js** — evidence-rich dashboard.
- **PostgreSQL** — authoritative transactional and analytical metadata.
- **OpenViking** — versioned research and unstructured context, never authoritative squad state.
- **ChatGPT Apps SDK / MCP** — optional conversational interface; a ChatGPT subscription is not treated as a general application API entitlement.

The approved boundaries are recorded in [ADR-0001](docs/adr/0001-hybrid-modular-architecture.md) and the [FPL terms boundary](docs/compliance/fpl-terms-boundary.md).

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

The foundation uses only the Python standard library:

```bash
python3 -m unittest discover -s tests -p 'test_*.py' -v
python3 tools/governance/check_repository.py
```

As application projects are added, the canonical commands will be exposed through `make verify` and run identically in CI.

## Repository map

- `docs/adr/` — immutable architecture decision records.
- `docs/standards/` — enforceable engineering and scientific standards.
- `docs/compliance/` — legal and product-operation boundaries.
- `docs/research/` — evidence base and reproducibility templates.
- `src/` — future product and analytics implementation boundaries.
- `tools/governance/` — executable repository policy.
- `tests/` — policy tests now; product tests later.

## Licensing and data rights

This private repository does not currently grant an open-source licence. All third-party code, datasets, papers, model artefacts and derived fields require documented provenance and compatible rights before inclusion or distribution. No absence of a technical access control implies permission to collect or reuse data.
