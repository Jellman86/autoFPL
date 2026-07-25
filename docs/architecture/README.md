# Architecture

## Default home-lab shape

autoFPL should remain one simple deployable application until measured needs justify more:

```text
public data / user input / research
                 |
       validation + available_at
                 |
              SQLite
     authoritative local state
                 |
         .NET application
      API / workflow / MCP
                 |
     Python analytics when needed
 forecast / simulate / optimise
                 |
        human-reviewed advice
```

OpenViking may supply unstructured research and personal context, but it is never authoritative squad, snapshot or forecast state. A small web interface may be added for a concrete journey; it is not a prerequisite for useful API/MCP advice.

SQLite runs on a persistent local volume with foreign keys, WAL mode, migrations and consistent backups. Another database, object store, broker or separate service requires measured evidence that the current design is insufficient.

## Trust boundaries

- External data, retrieved text and model output are untrusted.
- Deterministic FPL rules, arithmetic and feasibility remain authoritative.
- Predictive results are candidates until point-in-time validation and promotion evidence pass.
- SQLite is authoritative for structured application state; OpenViking and generated artefacts are context or derived outputs.
- No FPL write boundary exists. The user applies advice manually.

## Interfaces

The .NET application exposes the product API and later read-only MCP tools. Stable external, MCP and persisted-data formats are versioned. Private v0.x module and API boundaries may evolve with tests and migrations.

Python is used where its scientific ecosystem improves forecasting, simulation or optimisation. Start with the least operationally expensive integration that works: an invoked module/process or bounded worker. Introduce a long-running independent analytics service only for a measured isolation or performance need.

ChatGPT and Hermes may call the same read-only MCP tools and explain evidence. Optional application-managed model providers remain behind a bounded adapter. AI output cannot alter authoritative facts, approve a proposal or act on an FPL account.

## Repository boundaries

- `src/backend/` — current .NET application.
- `src/analytics/` — Python research and promoted analytics when implemented.
- `src/web/` — optional future presentation code for a concrete journey.
- `contracts/` — stable external and persisted-data schemas where they add interoperability or safety.

[ADR-0010](../adr/0010-code-first-home-lab-architecture.md) supersedes the earlier mandatory PostgreSQL, separate-service and universal-contract assumptions. Do not add Kubernetes, a broker, service mesh, object store, GPU dependency or extra database before a measured requirement.
