# ADR-0001: Hybrid modular architecture

- **Status:** Superseded in part by ADR-0010
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** architecture

## Context

The product requires strong web/API workflow, reproducible scientific computing, mathematical optimisation and an evidence-rich UI. A single language would either constrain the research ecosystem or weaken the typed product boundary.

## Decision

The original foundation selected a modular-monolith-first architecture:

- .NET 10/C# for identity, authorisation, workflow, audit, API and MCP tools;
- Python for forecasting, simulation, backtesting and optimisation;
- TypeScript/Next.js for the web experience;
- PostgreSQL as the authoritative store;
- versioned HTTP/gRPC/schema contracts between C# and Python;
- OpenViking as a separate unstructured research-context service.

ADR-0010 supersedes the mandatory language, database and service shape. Current work defaults to one deployable application backed by SQLite, with Python, a web UI or separate services added only for a concrete measured need.

Begin with one product service and one analytics service. No further service extraction occurs without measured scaling/isolation needs and an ADR.

## Consequences

The design uses each ecosystem where it is strongest and gives AI coding agents compiler/type feedback. It introduces a cross-language contract, duplicated runtime tooling and more CI gates. Contract tests and schema generation are therefore mandatory.

## Reversibility

The explicit contract lets analytics be moved in-process or to another implementation without changing authoritative product state.
