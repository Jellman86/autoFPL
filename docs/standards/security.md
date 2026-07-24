# Security Standard

This project follows least privilege, explicit trust boundaries and fail-closed behaviour. Use the NIST Secure Software Development Framework, OWASP ASVS/API guidance, GitHub secure-use guidance and OpenSSF/SLSA controls proportionately.

## Threat priorities

1. FPL or identity credential theft.
2. Unauthorised external account action.
3. Prompt/tool injection causing data disclosure or mutation.
4. Poisoned research sources or training data.
5. Dependency, Action or container supply-chain compromise.
6. Recommendation manipulation or silent model/data corruption.
7. Exposure of personal squad, account or usage data.
8. Denial of service around deadlines.

## Mandatory controls

- Authenticate users through an approved OIDC provider; do not invent password storage.
- Authorise on the server using deny-by-default scopes.
- Separate read, recommend, approve and execute capabilities. Execute remains absent until explicitly authorised by compliance ADR.
- Treat webpages, documents, model responses, MCP content and uploaded files as untrusted data, never instructions.
- Validate size, type, schema, range and provenance at every ingress.
- Parameterise database queries and constrain file paths/object keys.
- Apply CSRF, SSRF, XSS, injection and rate-limit protections appropriate to the interface.
- Encrypt transport; encrypt sensitive data at rest where the platform supports it.
- Use short-lived credentials and secret-store injection. Rotate and revoke without rebuilding code.
- Keep production databases and admin surfaces off public networks.

## LLM/MCP controls

- ChatGPT-hosted conversation reaches autoFPL only through the documented read-only MCP adapter; no model is embedded in deterministic domain services.
- ChatGPT/Codex OAuth credentials are not autoFPL identity tokens and must never be accepted, copied, logged or stored by the MCP resource server.
- Private MCP data requires autoFPL-issued or approved tokens with exact issuer, audience/resource, expiry and scope validation on every request.
- The LLM may request read-only analysis tools; deterministic services enforce tool arguments and authorisation.
- Application-managed orchestration is bounded by explicit model-turn, time, token, simulation, tool and provider-spend budgets; recursive agents and silent provider switching are prohibited.
- Client-hosted ChatGPT/Hermes MCP sessions are bounded only where autoFPL has control: per-tool scope, argument validation, request/rate/response limits, analytical compute quotas and cancellation. Do not claim visibility into client tokens, spend, retries or complete transcripts.
- Application-managed AI recommendations use a structured proposal schema with evidence/run references, alternatives, assumptions, uncertainty, separately labelled judgement and an enforced initial `unapproved` state. The baseline MCP path returns evidence bundles and persists no client-generated synthesis.
- Tool outputs are bounded, schema validated, provenance labelled and limited to the data required by the selected tool.
- No model output becomes an SQL statement, shell command, URL fetch or external mutation without a constrained non-LLM policy layer.
- Human approval shows the exact proposed action and current data before any future execution.
- OpenAI/Codex cached credentials are never copied into the application or container.
- Optional outbound AI assistance uses only the ADR-approved provider-neutral port; domain services never import provider SDKs or vendor response types.
- OpenRouter and other API credentials are runtime-injected from the deployment secret store and are never persisted, logged or included in prompts.
- Provider/model/routing policy is explicit and allowlisted; no adapter may switch provider, model or fallback silently.
- A private Hermes proxy is supported only on an authenticated private network or loopback boundary and receives no database, FPL-account or approval capability.
- Provider output remains untrusted: extracted claims stay quarantined until source-linked validation/promotion, while provider failure returns the deterministic artefact or leaves a source explicitly unprocessed rather than fabricating a result.

## CI/CD and supply chain

- Workflow token permissions default to `contents: read`.
- Third-party Actions use full commit SHAs with release comments.
- Untrusted PR code does not run with secrets; `pull_request_target` is forbidden without threat-model exception.
- Releases use locked dependencies, SBOMs, provenance/attestations and vulnerability scans when build artefacts begin.
- Deployment images use non-root users, read-only root filesystems where possible, dropped capabilities, no-new-privileges, bounded resources, health checks and immutable digests.
- Never mount the Docker socket into application containers.

## Vulnerability handling

Triage by exploitability and impact, not CVSS alone. Security fixes include a regression test, affected-version analysis, coordinated disclosure, dependency/data revocation where needed and reconciliation across `main` and `dev`.
