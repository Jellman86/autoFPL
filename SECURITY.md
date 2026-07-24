# Security Policy

## Reporting

Do not open a public issue containing a vulnerability, credential, private dataset or exploitable detail. Use GitHub's private vulnerability reporting when enabled, or contact the repository owner through an agreed private channel.

Include affected component/version, impact, reproducible steps with synthetic data and any suggested mitigation. Never include live credentials or user data.

## Supported versions

Until the first release, only the latest commit on `main` is supported. A formal support matrix will be published with the first release.

## Security invariants

- No FPL, OpenAI or other user credentials are collected by application code without an approved threat model and ADR.
- LLM output cannot directly mutate state or trigger external actions.
- Secrets come from the deployment secret store and are never written to logs, images, source or experiment artefacts.
- Network egress, filesystem writes and service permissions are deny-by-default.
- Authentication and authorisation are enforced server-side on every protected operation.
- Security events are auditable without recording sensitive content.

See `docs/standards/security.md` and `docs/compliance/fpl-terms-boundary.md`.
