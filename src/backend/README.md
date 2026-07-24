# Backend boundary

.NET 10 code for identity, authorisation, authoritative workflow state, audit, API and MCP tools. It must not contain forecasting algorithms or an FPL account-write client under the current compliance ADR.

The first executable domain slice validates decision-snapshot metadata. Schema version `1.0` is supported, and source type is restricted to `manual` or `synthetic`; unsupported values fail with stable domain error codes.

The wire boundary is deliberately separate from the domain:

- `AutoFpl.Contracts` maps validated domain values to transport documents;
- `AutoFpl.Api` exposes liveness, readiness and fail-closed metadata validation over HTTP;
- `contracts/decision-snapshot/v1/metadata.schema.json` is the versioned JSON Schema Draft 7 contract;
- checked-in manual and synthetic examples are executable fixtures;
- NJsonSchema is pinned in tests only and is not a production dependency.

The API rejects undeclared JSON properties with 400 and returns stable RFC problem responses with 422 for domain-invalid metadata. Kestrel limits request bodies to 16 KiB. The API container is a digest-pinned, non-root .NET chiseled image and is documented in the [container runbook](../../docs/operations/container.md).

The validator and licensing decision is recorded in [ADR-0008](../../docs/adr/0008-versioned-json-contracts.md).

Run the locked .NET test suite from the repository root:

```text
make test-dotnet
```
