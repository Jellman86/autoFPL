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
      API / workflow / MCP / web
                 |
     Python analytics when needed
 forecast / simulate / optimise
                 |
   Gameweek decision room + clients
                 |
        human-reviewed advice
```

OpenViking may supply unstructured research and personal context, but it is never authoritative squad, snapshot or forecast state. The Gameweek decision room is now part of the v0.1 journey and is served by the same deployable application.

SQLite runs on a persistent local volume with foreign keys, WAL mode, migrations and consistent backups. Another database, object store, broker or separate service requires measured evidence that the current design is insufficient.

## Trust boundaries

- External data, retrieved text and model output are untrusted.
- Deterministic FPL rules, arithmetic and feasibility remain authoritative.
- Predictive results are candidates until point-in-time validation and promotion evidence pass.
- SQLite is authoritative for structured application state; OpenViking and generated artefacts are context or derived outputs.
- No FPL write boundary exists. The user applies advice manually.

## Interfaces

The .NET application exposes the product API and later read-only MCP tools.
Stable external, MCP and persisted-data formats are versioned. Private v0.x
module and API boundaries may evolve with tests and migrations.

The supported HTTP integration surface is generated as OpenAPI 3.1 at
`/openapi/v1.json`. OpenAPI describes HTTP consumers and generated clients; it
does not replace the separate MCP tool contract or expose internal research
formats by default.

Python is used where its scientific ecosystem improves forecasting, simulation or optimisation. Start with the least operationally expensive integration that works: an invoked module/process or bounded worker. Introduce a long-running independent analytics service only for a measured isolation or performance need.

Monte Carlo simulation starts with a vectorised, seeded CPU reference. A bounded
analytics process may use a GPU backend after parity tests and a representative
benchmark prove a material benefit on deployed hardware. The web/API process
does not receive accelerator access by default.

ChatGPT and Codex use the user's OpenAI-hosted experience through an autoFPL
plugin backed by read-only MCP tools. Hermes and other MCP clients call the same
tools. The standalone web application works without AI and may optionally use a
server-side OpenAI API key, compatible provider or private Hermes proxy behind a
bounded adapter. A ChatGPT subscription is not treated as a transferable API
credential for the standalone site.

AI output cannot alter authoritative facts, approve a proposal or act on an FPL
account. An AI-suggested lineup becomes a visible draft, receives deterministic
validation and is compared through the same forecast/scenario path.

## Repository boundaries

- `src/backend/` — current .NET application.
- `src/analytics/` — Python research and promoted analytics when implemented.
- `src/web/` — optional future presentation code for a concrete journey.
- `contracts/` — stable external and persisted-data schemas where they add interoperability or safety.

[ADR-0010](../adr/0010-code-first-home-lab-architecture.md) supersedes the earlier mandatory PostgreSQL, separate-service and universal-contract assumptions. Do not add Kubernetes, a broker, service mesh, object store, GPU dependency or extra database before a measured requirement.
