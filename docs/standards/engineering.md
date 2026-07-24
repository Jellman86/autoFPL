# Engineering Standard

## Architecture

Use a modular-monolith-first product boundary with one independently deployable analytics worker. Add services only when a measured scaling, isolation or ownership requirement justifies the operational cost.

- C# owns product workflow, identity, authorisation, authoritative state and audit.
- Python owns forecast training/inference, simulation and optimisation.
- TypeScript owns presentation, never business authority.
- Contracts are versioned OpenAPI/JSON Schema or Protobuf artefacts.
- Postgres is authoritative; OpenViking and model artefacts are derived/context stores.

Domain code cannot depend directly on wall clocks, global random state, network clients or mutable process environment. Inject these at boundaries.

## Language gates

### .NET/C#

When introduced, pin .NET 10 LTS and NuGet dependencies. Enable nullable references, implicit usings, deterministic builds, warnings-as-errors, built-in analyzers and locked restore. Required gates: `dotnet format --verify-no-changes`, `dotnet build --locked-mode`, `dotnet test`, coverage threshold, architecture tests and package-vulnerability audit.

### Python

Use Python 3.14 when the full scientific stack supports it; otherwise record a temporary Python 3.13 constraint. Manage environments with `uv`, commit `uv.lock`, require `ruff format --check`, `ruff check`, strict `mypy`, `pytest`, coverage, import-boundary tests and dependency audit. Notebooks are exploratory only; promoted logic moves into tested modules.

### TypeScript

Use current Active/Maintenance LTS Node as pinned by `.tool-versions` or equivalent and commit the package-manager lockfile. Enable TypeScript strict mode, ESLint, formatting, unit/component tests, accessibility checks and Playwright smoke tests. Browser code never receives server secrets.

## Testing pyramid

- **Unit/property tests:** domain calculations, constraints and transformations.
- **Contract tests:** C#↔Python and API schemas.
- **Integration tests:** real Postgres and service boundaries using disposable containers.
- **End-to-end tests:** a few critical human approval journeys.
- **Backtests:** separate scientific evaluation; never substitute for software tests.

No snapshot-only test may approve financial, rule, scoring or recommendation behaviour. Critical calculations require explicit expected values and invariants.

## Data and time types

- Use IDs with documented source namespace and season validity.
- Represent FPL money as integer tenths of a million, not binary floating point.
- Store timestamps as UTC instants and preserve the source timezone/offset when material.
- Store `observed_at`, `available_at`, source and revision for every decision feature.
- Randomness uses recorded seeds and isolated generators.

## Database changes

- Migrations are reviewed source artefacts, never generated at application startup in production.
- Prefer expand/migrate/contract for breaking schema changes.
- Test migration from the latest released schema using realistic volumes.
- Destructive changes require backup verification and a separate approval.
- Historical forecasts, recommendations and experiments are append-only; corrections create revisions.

## Observability

Instrument every scheduled job and recommendation with correlation ID, code version, data snapshot, model version and outcome. Use OpenTelemetry-compatible structured logs, metrics and traces. Never log credentials, raw tokens, user-uploaded source files or unnecessary personal data.

## Dependency policy

- Direct dependencies need a documented purpose, maintained status, compatible licence and vulnerability review.
- Commit lockfiles and use frozen/locked restore in CI.
- Pin GitHub Actions to full SHAs and container images to digests before deployment.
- Dependabot proposes updates; CI and human review decide whether to merge.
- Remove unused dependencies promptly.

## Performance

Set budgets only after measuring representative workloads. Correctness, calibration and reproducibility precede micro-optimisation. Any performance claim records dataset size, hardware class, warm-up, repetitions and variance.
