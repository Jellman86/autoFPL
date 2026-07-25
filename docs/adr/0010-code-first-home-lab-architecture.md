# ADR-0010: Code-first home-lab architecture and research promotion

- **Status:** Accepted
- **Date:** 2026-07-25
- **Owners:** Jellman86
- **Decision class:** product, architecture, research
- **Supersedes:** ADR-0001's mandatory PostgreSQL/separate-service shape, ADR-0004's pre-implementation acceptance gate and ADR-0008 where it implies schemas for private internal boundaries

## Context

autoFPL is a single-user, self-hosted home-lab project. Earlier decisions applied release-grade research and enterprise architecture controls before useful prediction code existed. That consumed effort without changing application behaviour.

## Decision

1. Build the smallest working vertical slice before expanding contracts, governance or infrastructure.
2. Use one deployable application and SQLite as the default authoritative store. Add another database or service only after a measured need.
3. Use Python when its research ecosystem materially helps forecasting, simulation or optimisation; it may run as a module or bounded worker. Add a web frontend only for a concrete user journey.
4. Version stable external, MCP and persisted-data boundaries. Private internal v0.x interfaces may evolve with tests and migrations.
5. Research literature guides candidate selection, but no separate acceptance is required before exploratory implementation.
6. Before opening a final holdout or promoting a result into recommendations, register the target, temporal split, baselines, metrics and promotion rule. Promotion still requires leakage-free rolling/walk-forward evidence, calibration, decision utility and reproducibility.
7. Apply independent review, recovery, observability and release evidence according to actual risk and lifecycle stage rather than to every development task.
8. Preserve the existing hard boundaries: no FPL account writes, no secrets/private session collection, deterministic rules remain authoritative and the user acts manually.

## Consequences

Working application and research code can begin sooner, deployment stays small, and scientific rigour is concentrated where it affects prediction claims. SQLite or the process layout can be replaced later through normal migrations if measurements justify it.
