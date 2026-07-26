# Backend boundary

.NET 10 code for identity, authorisation, authoritative workflow state, audit, API and MCP tools. It must not contain forecasting algorithms or an FPL account-write client under the current compliance ADR.

The executable backend validates decision-snapshot metadata, deterministic manual squad, starting-XI and complete gameweek-selection feasibility, effective captaincy, automatic substitutions, a composed effective gameweek outcome, and manual point totals for that effective XI. It owns validated SQLite squad/selection state, append-only observations and immutable cutoff-correct snapshots. Operator-triggered fixed-origin importers capture official FPL player/Gameweek/team/fixture/outcome reference data and a public FPL Form external predicted-points baseline. Squad, lineup and bench state remain user-supplied, and no path performs FPL account actions.

The wire boundary is deliberately separate from the domain:

- `AutoFpl.Contracts` maps validated domain values to transport documents;
- `AutoFpl.Api` exposes liveness, SQLite-aware readiness, source-capture metadata, a cutoff-aware official player dossier, immutable decision-snapshot persistence/readback and the deterministic validation/outcome routes over HTTP;
- `contracts/decision-snapshot/v1/metadata.schema.json` is the versioned JSON Schema Draft 7 contract;
- `contracts/manual-evidence/v1/current-post-routes.json` inventories every accepted manual request field and its point-in-time interpretation;
- checked-in manual and synthetic examples are executable fixtures;
- NJsonSchema is pinned in tests only and is not a production dependency.

The API rejects undeclared, duplicate, missing or malformed request data with 400 and returns stable RFC problem responses with 422 for domain-invalid values. Kestrel limits request bodies to 16 KiB and request bodies are not logged. Validation/outcome routes remain stateless; `/api/v1/decision-snapshots` is the explicit persisted exception and is private-network-only until authentication exists. The [manual evidence timing contract](../../docs/data/manual-evidence-timing-v1.md) defines its cutoff and revision semantics. The API container is documented in the [container runbook](../../docs/operations/container.md).

The backend also owns read-only source evaluation where authoritative capture
and identity joins are the primary concern. Run the official published
expected-points baseline with:

```text
dotnet AutoFpl.Api.dll \
  --evaluate-official-fpl-expected-points [season-code]
```

It emits deterministic exploratory JSON, returns exit `2` until one complete
deadline-correct forecast/outcome pair exists, and never promotes the provider
value into product advice.

The validator and licensing decision is recorded in [ADR-0008](../../docs/adr/0008-versioned-json-contracts.md).

Run the locked .NET test suite from the repository root:

```text
make test-dotnet
```
