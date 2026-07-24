# Threat Model

- **Status:** foundation baseline
- **Reviewed:** 2026-07-24
- **Owners:** Jellman86
- **Method:** assets, trust boundaries, misuse cases and STRIDE-informed analysis

This model must be updated whenever a PR introduces a new data source, identity flow, MCP tool, external network boundary, model provider, upload, database class or action capability.

## Protected assets

- User identity, squad state, purchase prices, preferences and recommendation history.
- Any future provider credentials, tokens and signing keys.
- Licensed football data, source contracts and private research artefacts.
- Immutable historical snapshots and experiment lineage.
- Forecast/model artefacts, optimiser constraints and recommendation audit.
- Repository, CI credentials, release provenance and deployment configuration.

## Actors

- Authenticated user.
- Repository maintainer/reviewer.
- Product, analytics and web services.
- ChatGPT/MCP client and external model providers.
- Data/news providers and OpenViking.
- Benign but malformed source.
- Malicious user, source publisher, dependency maintainer or compromised CI account.

## Trust boundaries

1. Browser/ChatGPT client to public product API.
2. Product API to Postgres and analytics service.
3. Analytics service to object/model stores and permitted external data.
4. Ingestion to quarantine/curated data.
5. LLM/OpenViking research context to deterministic application logic.
6. GitHub pull request to CI runner and release artefact.
7. Deployment control plane to runtime secrets and production services.

## Principal threats and controls

| Threat | Example | Required controls |
|---|---|---|
| Identity spoofing | Stolen session accesses private squad | OIDC, secure session cookies/tokens, MFA-capable provider, server-side authorisation, revocation |
| Unauthorised action | Prompt induces a transfer or chip action | No FPL write client; separate capability scopes; deterministic policy; exact human approval if future permission exists |
| Prompt/tool injection | Article tells agent to disclose data or call a tool | Treat content as data, source labels, strict MCP schemas, tool allowlists, no model-authoritative mutation |
| SSRF/data exfiltration | User URL reaches internal service | Destination allowlists, DNS/IP validation, bounded fetcher, network egress policy, no ambient secrets |
| Data poisoning | Manipulated injury report changes recommendation | Source admission, corroboration, provenance, confidence/expiry, anomaly detection, reversible snapshots |
| Temporal leakage | Corrected post-match data enters backtest | `available_at`, immutable pre-deadline snapshots, embargoes, walk-forward tests and independent review |
| Model/solver tampering | Artefact or constraint silently replaced | Content hashes, signed provenance, immutable IDs, feasibility checks and append-only audit |
| Supply-chain compromise | Mutable Action/tag becomes malicious | Full-SHA Action pins, lockfiles, Dependabot review, SBOM/provenance, restricted workflow permissions |
| Secret disclosure | Token committed or logged | Secret scanning, secret store, redaction, no cached Codex credentials in runtime, rotation runbook |
| Injection | Malicious input reaches SQL/shell/template | Boundary schema/range checks, parameterised access, no shell construction, output encoding |
| Denial of service | Deadline traffic or expensive simulation exhausts service | Quotas, bounded jobs, timeouts, cancellation, cached versioned results, resource limits, graceful degradation |
| Repudiation | Recommendation changed without trace | Immutable audit with actor, time, code/data/model versions, objective and approval state |
| Privacy overcollection | Research stores unnecessary user history | Data minimisation, separation, retention/deletion policy, access audit and consent boundaries |
| Licence/terms breach | Undocumented FPL endpoint becomes production feed | Source admission gate, compliance ADR and supported licensed interface requirement |

## Abuse cases that must fail closed

- A webpage or retrieved paper contains instructions to reveal secrets or alter policy.
- A user asks the conversational model to bypass squad constraints or mark a proposal approved.
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
