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

- The LLM may request read-only analysis tools; deterministic services enforce tool arguments and authorisation.
- Tool outputs are bounded, schema validated and provenance labelled.
- No model output becomes an SQL statement, shell command, URL fetch or external mutation without a constrained non-LLM policy layer.
- Human approval shows the exact proposed action and current data before any future execution.
- OpenAI/Codex cached credentials are never copied into the application or container.

## CI/CD and supply chain

- Workflow token permissions default to `contents: read`.
- Third-party Actions use full commit SHAs with release comments.
- Untrusted PR code does not run with secrets; `pull_request_target` is forbidden without threat-model exception.
- Releases use locked dependencies, SBOMs, provenance/attestations and vulnerability scans when build artefacts begin.
- Deployment images use non-root users, read-only root filesystems where possible, dropped capabilities, no-new-privileges, bounded resources, health checks and immutable digests.
- Never mount the Docker socket into application containers.

## Vulnerability handling

Triage by exploitability and impact, not CVSS alone. Security fixes include a regression test, affected-version analysis, coordinated disclosure, dependency/data revocation where needed and reconciliation across `main` and `dev`.
