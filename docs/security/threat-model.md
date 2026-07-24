# Threat Model

- **Status:** foundation baseline
- **Reviewed:** 2026-07-24
- **Owners:** Jellman86
- **Method:** assets, trust boundaries, misuse cases and STRIDE-informed analysis

This model must be updated whenever a PR introduces a new data source, identity flow, MCP tool, external network boundary, model provider, upload, database class or action capability.

## Protected assets

- User identity, squad state, purchase prices, preferences and recommendation history.
- Any future autoFPL identity-provider credentials, access tokens, signing keys and optional model-provider API keys; the baseline MCP path holds no OpenAI/ChatGPT OAuth token.
- Licensed football data, source contracts and private research artefacts.
- Immutable historical snapshots and experiment lineage.
- Forecast/model artefacts, optimiser constraints and recommendation audit.
- Repository, CI credentials, release provenance and deployment configuration.

## Actors

- Authenticated user.
- Repository maintainer/reviewer.
- Product, analytics and web services.
- ChatGPT MCP client and hosted model.
- Private Hermes MCP client.
- Optional private Hermes explanation proxy and external model providers.
- Data/news providers and OpenViking.
- Benign but malformed source.
- Malicious user, source publisher, dependency maintainer or compromised CI account.

## Trust boundaries

1. Browser client to the public product API.
2. ChatGPT MCP client to the read-only MCP adapter using an autoFPL-issued/approved token and least-privilege scopes.
3. Private Hermes MCP client to the same read-only adapter using its own autoFPL token and scopes, with no direct database access and no transfer of Hermes model-provider credentials.
4. Untrusted ChatGPT- or Hermes-hosted model output to MCP tool selection and arguments; deterministic application services remain authoritative.
5. Product API to Postgres and analytics service.
6. Analytics service to object/model stores and permitted external data.
7. Ingestion to quarantine/curated data.
8. LLM/OpenViking research context to deterministic application logic.
9. GitHub pull request to CI runner and release artefact.
10. Optional outbound explanation-provider port to OpenRouter or an authenticated private Hermes proxy.
11. Deployment control plane to runtime secrets and production services.

## Principal threats and controls

| Threat | Example | Required controls |
|---|---|---|
| Identity spoofing | Stolen session accesses private squad | OIDC, secure session cookies/tokens, MFA-capable provider, server-side authorisation, revocation |
| Credential confusion | ChatGPT/Codex or Hermes model-provider token is accepted as an autoFPL token or copied into the service | Separate issuers and audiences, OAuth resource binding, exact token validation, no model-provider token ingestion or storage |
| MCP confused deputy | Prompt requests excessive private data or invokes an undeclared capability | Read-only tool allowlist, least-privilege scopes, per-tool server checks, bounded responses, user-visible purpose and audit |
| Provider data exposure | OpenRouter route or private proxy receives unnecessary personal/source data | Disabled-by-default adapters, data minimisation, explicit model/routing allowlist, retention review, private authenticated proxy, no credentials in prompts |
| Provider output substitution | Generated prose changes a forecast or hides uncertainty | Structured artefact remains authoritative and visible, immutable values, output schema separation, response provenance and failure isolation |
| Unauthorised action | Prompt induces a transfer or chip action | No FPL write client; separate capability scopes; deterministic policy; exact human approval if future permission exists |
| Prompt/tool injection | Article tells agent to disclose data or call a tool | Treat content as data, source labels, strict MCP schemas, tool allowlists, no model-authoritative mutation |
| SSRF/data exfiltration | User URL reaches internal service | Destination allowlists, DNS/IP validation, bounded fetcher, network egress policy, no ambient secrets |
| Data poisoning | Manipulated injury report changes recommendation | Source admission, corroboration, provenance, confidence/expiry, anomaly detection, reversible snapshots |
| Temporal leakage | Corrected post-match data enters backtest | `available_at`, immutable pre-deadline snapshots, embargoes, walk-forward tests and independent review |
| Model/solver tampering | Artefact or constraint silently replaced | Content hashes, signed provenance, immutable IDs, feasibility checks and append-only audit |
| Supply-chain compromise | Mutable Action/tag becomes malicious | Full-SHA Action pins, lockfiles, Dependabot review, SBOM/provenance, restricted workflow permissions |
| Secret disclosure | Token committed or logged | Secret scanning, deployment secret store, redaction, no cached Codex credentials in runtime, no provider keys in persisted configuration, rotation runbook |
| Injection | Malicious input reaches SQL/shell/template | Boundary schema/range checks, parameterised access, no shell construction, output encoding |
| Denial of service | Deadline traffic or expensive simulation exhausts service | Quotas, bounded jobs, timeouts, cancellation, cached versioned results, resource limits, graceful degradation |
| Repudiation | Recommendation changed without trace | Immutable audit with actor, time, code/data/model versions, objective and approval state |
| Privacy overcollection | Research stores unnecessary user history | Data minimisation, separation, retention/deletion policy, access audit and consent boundaries |
| Licence/terms breach | Undocumented FPL endpoint becomes production feed | Source admission gate, compliance ADR and supported licensed interface requirement |

## Abuse cases that must fail closed

- A webpage or retrieved paper contains instructions to reveal secrets or alter policy.
- A user asks the conversational model to bypass squad constraints or mark a proposal approved.
- A ChatGPT or private Hermes MCP client presents a token for the wrong issuer, resource, audience or scope.
- A private Hermes MCP client attempts direct database/provider access or sends Hermes model credentials instead of using the scoped read-only MCP boundary.
- Model output in ChatGPT or Hermes attempts to discover a write capability or retrieve more private data than the selected tool requires.
- OpenRouter or a private Hermes proxy is unavailable, over quota or returns malformed/generated values that conflict with the structured prediction artefact.
- Provider routing would select a model or upstream provider outside the configured allowlist.
- The analytics service returns malformed, infeasible or unversioned output.
- A model times out or lacks required inputs near a deadline.
- A source silently revises historical statistics.
- CI on an untrusted pull request attempts to access secrets or obtain write permission.
- A dependency update changes data semantics or solver results without test changes.

## Residual foundation risks

No application code or runtime exists yet. Later PRs must convert these controls into automated tests and operational evidence. FPL terms can change each season; compliance review is therefore time-bounded. Licensed-data availability and permitted commercial use remain unresolved product prerequisites.

## Review triggers

- First deployed service or public endpoint.
- Authentication, user upload or MCP implementation.
- First external data provider or automated retrieval.
- First model that influences a recommendation.
- Any capability capable of external mutation.
- Material dependency/runtime or deployment change.
- Security incident, terms change or annually before season start.
