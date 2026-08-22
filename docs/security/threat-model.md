# Threat Model

- **Status:** fixed-route optional model-provider boundary
- **Reviewed:** 2026-08-01
- **Owners:** Jellman86
- **Method:** assets, trust boundaries, misuse cases and STRIDE-informed analysis

Update this model when a PR materially changes a trust boundary: identity, MCP/external action capability, secret exposure, public network access, untrusted upload, model provider or database class.

## Protected assets

- User identity, squad state, purchase prices, preferences and recommendation history.
- Any future autoFPL identity-provider credentials, access tokens, signing keys and optional model-provider API keys; the baseline MCP path holds no OpenAI/ChatGPT OAuth token.
- Licensed football data, source contracts and private research artefacts.
- Immutable historical snapshots and experiment lineage.
- Forecast/model artefacts, optimiser constraints, application-managed AI recommendation proposals and recommendation audit.
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
5. Application to its local SQLite store and any bounded analytics process.
6. Analytics code to permitted source data and model artefacts.
7. Ingestion to quarantine/curated data.
8. LLM/OpenViking research context to deterministic application logic.
9. GitHub pull request to CI runner and release artefact.
10. Optional outbound AI-assistance port to OpenRouter or an authenticated private Hermes proxy for bounded extraction/classification and explanation.
11. Deployment control plane to runtime secrets and production services.

## Principal threats and controls

| Threat | Example | Required controls |
|---|---|---|
| Identity spoofing | Stolen session accesses private squad | OIDC, secure session cookies/tokens, MFA-capable provider, server-side authorisation, revocation |
| Credential confusion | ChatGPT/Codex or Hermes model-provider token is accepted as an autoFPL token or copied into the service | Separate issuers and audiences, OAuth resource binding, exact token validation, no model-provider token ingestion or storage |
| MCP confused deputy | Prompt requests excessive private data or invokes an undeclared capability | Read-only tool allowlist, least-privilege scopes, per-tool server checks, bounded responses, user-visible purpose and audit |
| Orchestration manipulation | Prompt or memory causes an application-managed unbounded loop, or a client-hosted response hides alternatives/labels judgement as model output | Mode-labelled audit; complete loop/token/spend budgets only in application-managed mode; per-tool authorization/compute limits in MCP mode; structured application proposal; separate judgement and enforced unapproved state |
| Memory poisoning | Generated summaries or stale context become authoritative facts | Authoritative-store separation, source/version/time labels, explicit memory writes, correction/expiry policy and point-in-time retrieval |
| Provider data exposure | OpenRouter route or private proxy receives unnecessary personal/source data | Disabled-by-default adapters, data minimisation, explicit model/routing allowlist, retention review, private authenticated proxy, no credentials in prompts |
| Provider output substitution | Generated prose changes a forecast, or an extracted claim bypasses quarantine/validation | Structured artefact remains authoritative and visible, source-linked candidate state, immutable values, output schema separation, response provenance and failure isolation |
| Unauthorised action | Prompt induces a transfer or chip action | No FPL write client; separate capability scopes; deterministic policy; exact human approval if future permission exists |
| Prompt/tool injection | Article tells agent to disclose data or call a tool | Treat content as data, source labels, strict MCP schemas, tool allowlists, no model-authoritative mutation |
| SSRF/data exfiltration | User URL reaches internal service | The first importer has compile-time HTTPS origins, no redirects, no caller URL/proxy/header/cookie input, bounded responses and no ambient secrets; future general fetchers require destination and resolved-IP controls |
| Data poisoning | Manipulated injury report changes recommendation | Source evaluation, corroboration, provenance, confidence/expiry, anomaly detection, reversible snapshots |
| Temporal leakage | Corrected post-match data enters backtest | `available_at`, immutable pre-deadline snapshots, embargoes, walk-forward tests and independent review |
| Model/solver tampering | Artefact or constraint silently replaced | Content hashes, signed provenance, immutable IDs, feasibility checks and append-only audit |
| Supply-chain compromise | Mutable Action/tag becomes malicious | Full-SHA Action pins, lockfiles, Dependabot review, SBOM/provenance, restricted workflow permissions |
| Secret disclosure | Token committed or logged | Secret scanning, deployment secret store, redaction, no cached Codex credentials in runtime, no provider keys in persisted configuration, rotation runbook |
| Injection | Malicious input reaches SQL/shell/template | Exact JSON binding, domain/range checks, parameterised SQLite commands, no shell construction, output encoding |
| Denial of service | Deadline traffic or expensive simulation exhausts service | Quotas, bounded jobs, timeouts, cancellation, cached versioned results, resource limits, graceful degradation |
| Repudiation | Recommendation changed without trace | Immutable audit with actor, time, code/data/model versions, objective and approval state |
| Privacy overcollection | Research stores unnecessary user history | Data minimisation, separation, retention/deletion policy, access audit and consent boundaries |
| Access or legal breach | Collector accesses private/login/paid data, misuses credentials or creates harmful load | Public-source boundary, no ambient credentials, bounded rate/concurrency, source-specific review and a kill switch |

## Abuse cases that must fail closed

- A webpage or retrieved paper contains instructions to reveal secrets or alter policy.
- A user asks the conversational model to bypass squad constraints or mark a proposal approved.
- A ChatGPT or private Hermes MCP client presents a token for the wrong issuer, resource, audience or scope.
- A private Hermes MCP client attempts direct database/provider access or sends Hermes model credentials instead of using the scoped read-only MCP boundary.
- Model output in ChatGPT or Hermes attempts to discover a write capability or retrieve more private data than the selected tool requires.
- Retrieved memory or a generated summary claims to be authoritative, current or permission to change policy without a source/versioned record.
- An application-managed orchestrator exceeds its model-turn, time, token, simulation, tool or provider-spend budget; recursively starts agents; or omits alternatives and uncertainty from its proposal.
- A client-hosted MCP session exceeds per-tool analytical quotas or is reported as fully replayable/fully budget-controlled despite autoFPL not owning its model loop.
- OpenRouter or a private Hermes proxy is unavailable, over quota or returns malformed/generated values that conflict with the structured prediction artefact.
- Provider routing would select a model or upstream provider outside the configured allowlist.
- The analytics service returns malformed, infeasible or unversioned output.
- A model times out or lacks required inputs near a deadline.
- A source silently revises historical statistics.
- CI on an untrusted pull request attempts to access secrets or obtain write permission.
- A dependency update changes data semantics or solver results without test changes.

## Residual foundation risks

The private application has unauthenticated decision-state write routes for snapshots and selection draft/edit/lock actions, intended only for its trusted internal network. It must not receive a public route before identity and authorisation are implemented. SQLite state is authoritative and therefore requires a persistent volume, protected file access, consistent backups before destructive migration or rollback, and retention/deletion work before storing real personal history.

The optional evidence-review adapter adds fixed-route outbound model access.
It is disabled by default, accepts only an owner-allowlisted endpoint host and
exact `/chat/completions` path, refuses redirects, sends no user squad or
account state, caps request, response, output tokens and time, and never stores
or logs its bearer key or raw response. Source spans remain prompt-injection
material: a system instruction labels them untrusted, a strict context-derived
schema constrains identifiers, and the independent application importer still
validates every citation, source, scenario, target, cutoff and coverage value.
One immutable success, refusal or unavailable receipt suppresses repeated spend
for the same context. Residual risks are provider retention, semantic bias,
owner misconfiguration and private-Hermes transport exposure; no review is
promoted or connected to forecast mutation.

The operator-triggered official FPL importer adds outbound public HTTPS and
untrusted provider JSON. Its origins are compile-time constants; redirects,
non-JSON responses, oversized responses, malformed schemas and broken entity
references fail closed before an atomic write. It sends no credentials and
exposes no web import route. Residual risks are silent provider semantic
changes, source downtime, operator over-collection and growth from repeated
private raw captures. Captures do not influence advice until point-in-time
evaluation admits specific fields.

There is still no forecasting, simulation, optimiser or account-action
runtime. Source behaviour, access conditions and data availability can change,
so collection must remain observable and operator-disableable.

## Review triggers

- First non-fixed-origin ingestion, forecasting, AI/advisory service or public endpoint.
- Authentication, user upload or MCP implementation.
- First external data provider or automated retrieval.
- First model that influences a recommendation.
- Any capability capable of external mutation.
- Material dependency/runtime or deployment change.
- Security incident, terms change or annually before season start.
