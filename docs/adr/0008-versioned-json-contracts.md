# ADR-0008: Versioned JSON contracts and test validator

- **Status:** Accepted
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** architecture, security, dependency governance

## Context

autoFPL needs stable machine-readable boundaries between its domain, future HTTP/MCP services, Python analytics and external clients. Those boundaries must reject ambiguous values, remain testable without a running service and avoid coupling domain objects directly to transport serialization.

A standards-compliant validator is useful in tests. The selected dependency must be free to use, permissively licensed, version-pinned and absent from production runtime projects unless runtime validation is later justified.

## Decision drivers

- Publish explicit, versioned and fail-closed JSON contracts.
- Keep transport serialization out of the domain layer.
- Test checked-in examples and domain-to-document mappings against the same schema.
- Minimise production dependencies.
- Avoid package terms that may create future fees or deployment ambiguity.

## Considered options

### JsonSchema.Net 9.3.0

JsonSchema.Net directly supports Draft 2020-12 and has a small dependency surface. Its source repository reports MIT, but the 9.3.0 NuGet binary declares and contains `OSMFEULA.txt`. That agreement applies a maintenance fee to use of the published binary in revenue-generating activity above a stated revenue threshold. Self-compiling the MIT source would avoid the binary agreement, but maintaining a private build is not justified for test-only validation.

Rejected for the published NuGet binary.

### NJsonSchema 11.6.1

NJsonSchema is MIT-licensed, actively maintained and validates the strict object, required-property and enumeration keywords needed by the first contract. It brings more test-only transitive dependencies and a broader generation/OpenAPI feature surface than required.

Selected for test-only validation.

### Custom validation or transport attributes in the domain

Hand-written schema evaluation would duplicate a standard and risk semantic gaps. Adding JSON converters or attributes to domain enums would couple the domain to one wire representation.

Rejected.

## Decision

1. Versioned JSON Schemas live under `contracts/<contract>/vN/`. Existing published versions are immutable except for corrections that do not change accepted instances; behavioural changes require a new version.
2. The first decision-snapshot metadata contract uses JSON Schema Draft 7 because its stable keywords fully express the current boundary.
3. Contract objects reject undeclared properties and use exact case-sensitive enumerations.
4. Checked-in examples are executable fixtures and must pass the schema validator.
5. Domain objects map through explicit contracts-layer documents before serialization. The domain project has no JSON transport attributes or validator dependency.
6. `NJsonSchema` 11.6.1 is pinned only in the test project and remains covered by the repository lock file, vulnerability scan, dependency review and Dependabot.
7. The JsonSchema.Net 9.3.0 published binary is not used because of its packaged maintenance-fee agreement. A future switch requires a fresh exact-version licence and dependency audit.
8. Runtime services may validate at ingress, but adding a runtime validator is a separate decision based on measured need; the current production assemblies remain free of third-party JSON Schema dependencies.

## Consequences

### Positive

- Services and clients can share a stable contract independent of implementation language.
- Schema examples and .NET mapping are tested together.
- Domain logic remains transport-independent.
- The only new third-party dependency is test-only and permissively licensed.

### Negative and risks

- NJsonSchema has a larger transitive test dependency surface than the rejected alternative.
- Draft 7 omits newer schema features; future contracts may require a reviewed validator change.
- Schema validation does not replace semantic domain validation or cross-field policy checks.

### Reversibility/migration

The schema files are validator-neutral. NJsonSchema can be replaced by another conforming validator without changing accepted documents. Existing contract versions remain stable; incompatible changes use a new version directory.

## Verification

- canonical manual and synthetic examples pass schema validation;
- unsupported versions, non-canonical source values, missing fields and extra fields fail;
- both domain source variants map to documents that pass;
- locked restore and NuGet vulnerability/deprecation scans pass;
- dependency review verifies the selected package set on every pull request.

## References

- [JSON Schema Draft 7](https://json-schema.org/draft-07)
- [NJsonSchema 11.6.1 on NuGet](https://www.nuget.org/packages/NJsonSchema/11.6.1)
- [NJsonSchema 11.6.1 package manifest](https://api.nuget.org/v3-flatcontainer/njsonschema/11.6.1/njsonschema.nuspec)
- [NJsonSchema 11.6.1 source commit](https://github.com/RicoSuter/NJsonSchema/tree/ac2ba4ab9faaea3fb5adff784b99d73d70f6bdb1)
- [JsonSchema.Net 9.3.0 package manifest](https://api.nuget.org/v3-flatcontainer/jsonschema.net/9.3.0/jsonschema.net.nuspec)
- [JsonSchema.Net 9.3.0 package archive containing `OSMFEULA.txt`](https://api.nuget.org/v3-flatcontainer/jsonschema.net/9.3.0/jsonschema.net.9.3.0.nupkg)
