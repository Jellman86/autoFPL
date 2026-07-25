# Engineering Standard

## Home-lab architecture

Prefer the simplest system that delivers a useful prediction:

- one deployable .NET application for API, workflow, authoritative state and MCP;
- SQLite on a persistent local volume as the authoritative store;
- Python modules or a bounded worker when forecasting, simulation or optimisation needs the scientific ecosystem;
- a web frontend only when it delivers a useful user journey; and
- OpenViking only for unstructured context, never authoritative squad or prediction state.

Add a service, broker, object store or different database only after a measured scaling, isolation or reliability need. Keep boundaries replaceable without pre-building infrastructure for hypothetical scale.

## Contracts and code

Stable external, MCP and persisted-data formats are versioned and tested. Private internal v0.x APIs and module boundaries may evolve with their consumers; they do not require a new schema or ADR for every change.

Supported HTTP integration boundaries follow the
[OpenAPI standard](openapi.md). The deployed document is generated from runtime
endpoint metadata so it cannot quietly diverge from the application.

Domain rules cannot depend directly on wall clocks, global random state or network clients. Store money as integer tenths of a million, timestamps as UTC instants and predictive inputs with source/revision/`available_at` metadata.

## Language quality

Use the compiler, formatter, type checker and focused tests appropriate to code that exists. Keep dependencies locked for CI and deployment. Exploratory notebooks/scripts are allowed; logic used by the application moves into tested modules.

## Testing

- Unit/property tests cover domain calculations and important invariants.
- Integration tests use real SQLite files and real module boundaries where practical.
- Contract tests cover stable external or cross-process formats.
- End-to-end tests cover a few valuable user journeys.
- Backtests establish scientific evidence and do not replace software tests.

Do not create test categories merely to satisfy a pyramid. Add the smallest test that catches a realistic failure.

## SQLite

- Enable foreign keys, WAL mode and a bounded busy timeout.
- Apply reviewed migrations explicitly during deployment/startup control, not ad hoc from request handling.
- Test migrations from the latest schema.
- Use the SQLite online-backup API or `VACUUM INTO` for consistent backups.
- Destructive changes require a verified backup/recovery path.
- Keep historical snapshots, forecasts and experiments append-only; corrections create revisions.

Move away from SQLite only after measurements show that concurrent writers, availability requirements or data volume make it insufficient.

## Operations and dependencies

Add structured logs and health checks for behaviour that actually runs. Add metrics, tracing, alerts and runbooks when operational experience shows they are useful, not as prerequisites for unwritten features.

Dependencies need a purpose, compatible licence and vulnerability review. Pin GitHub Actions to full SHAs and deployed images to digests. Measure before optimising performance.
