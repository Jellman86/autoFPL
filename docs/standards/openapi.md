# OpenAPI standard

autoFPL uses OpenAPI for intentional HTTP integration boundaries. The generated
document is a consumer contract for the decision-room UI, tests, future MCP
adapters and independently implemented clients; it is not a dump of internal
domain or research objects.

## Published contract

- The application serves OpenAPI 3.1 JSON at `/openapi/v1.json`.
- OpenAPI 3.1 uses the JSON Schema 2020-12 model supported by ASP.NET Core 10.
- API routes carry their major version in the path, currently `/api/v1`.
- `info.version` is a semantic contract revision, currently `1.0.0`.
- The runtime-generated document is authoritative for the deployed HTTP
  surface. Do not maintain a divergent hand-written copy.
- A documentation UI is optional. The machine-readable document must remain
  usable without one.

## Endpoint metadata

Every supported API operation must have:

- a stable, unique operation ID;
- a short summary and a coherent tag;
- request-body and successful-response schemas;
- every intentionally returned error status documented as Problem Details; and
- tests that cover the route's important runtime behavior as well as its
  contract presence.

Health and readiness probes may appear under an `Operations` tag. Static web
assets, implementation helpers, analytics internals and unpromoted experiment
formats do not become public contracts merely because they exist.

## JSON and HTTP conventions

- JSON property names are camel case and case-sensitive.
- Requests reject undeclared members where the runtime contract requires exact
  input.
- Timestamps are UTC ISO 8601 values; money remains integer tenths of a million.
- Success responses use the narrowest appropriate 2xx status.
- Client and domain failures use `application/problem+json` following
  [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457.html), with stable
  application error codes where callers need to branch.
- Pagination, filtering and idempotency are added only when a delivered
  collection or mutation requires them, then specified consistently across the
  relevant API major version.

## Compatibility and versioning

Backward-compatible additions may increment the OpenAPI `info.version` minor
version. Compatible corrections may increment the patch version. Removing a
field or operation, changing a field's meaning or type, making an optional input
required, or otherwise invalidating a conforming client requires a new API major
path.

Operation IDs remain stable within an API major version because generated
clients may use them as method names. Deprecation must be represented in the
document and changelog before removal in a later major version.

Private v0.x implementation boundaries may still evolve quickly, but once a
route is used by the shipped web UI, MCP adapter or another client, compatibility
is deliberate and tested.

## Security

The document and examples must never contain API keys, authorization headers,
cookies, FPL credentials, private source material or user-specific data. OpenAPI
describes an authentication scheme when one exists; it never embeds a
credential.

If the service moves beyond its current private single-user network, access to
the document follows the same exposure and authentication review as the API
itself. An interactive documentation UI must not be added to production by
default without evaluating its script, content-security-policy and
authentication implications.

## Verification

CI must:

1. restore locked, vulnerability-free OpenAPI dependencies;
2. request the generated document from the real application;
3. assert OpenAPI 3.1 and expected versioned paths/operation IDs;
4. assert important response schemas are present; and
5. reject representative secret-bearing terms in the generated document.

References:

- [OpenAPI Specification 3.1](https://spec.openapis.org/oas/v3.1.2.html)
- [JSON Schema 2020-12](https://json-schema.org/draft/2020-12)
- [ASP.NET Core 10 OpenAPI support](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0)
- [Semantic Versioning 2.0.0](https://semver.org/)
