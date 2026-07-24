# Backend boundary

.NET 10 code for identity, authorisation, authoritative workflow state, audit, API and MCP tools. It must not contain forecasting algorithms or an FPL account-write client under the current compliance ADR.

The first executable domain slice validates decision-snapshot metadata. Schema version `1.0` is supported, and source type is restricted to `manual` or `synthetic`; unsupported values fail with stable domain error codes.

Run the locked .NET test suite from the repository root:

```text
make test-dotnet
```
