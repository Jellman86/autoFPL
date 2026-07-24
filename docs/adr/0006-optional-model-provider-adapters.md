# ADR-0006: Optional model-provider adapters

- **Status:** Accepted
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** architecture, security

## Context

autoFPL may need generated explanations or bounded conversational assistance outside ChatGPT. The personal deployment should support an explicitly supplied API key, initially OpenRouter, and may use a private Hermes proxy. These integrations must not change the deterministic authority established for forecasts, simulations, optimisation and approval.

Inbound conversational clients and outbound model providers are different boundaries. ChatGPT or Hermes can call autoFPL through MCP under [ADR-0005](0005-chatgpt-mcp-interface.md) without autoFPL calling a model. This ADR governs the separate case where autoFPL itself requests generated text from a configured provider.

## Decision drivers

- Keep model-provider choice replaceable and optional.
- Support an OpenAI-compatible API provider such as OpenRouter without leaking its key.
- Permit a private Hermes deployment without making Hermes mandatory.
- Keep forecasts and recommendations reproducible when no provider is available.
- Make model, routing, data exposure, cost and failure behavior explicit.
- Prevent model output from becoming an action or source of analytical truth.

## Considered options

### A. Provider SDKs inside domain services

Each feature could call a vendor SDK directly. This is rejected because it spreads vendor types, credentials, retries and failure semantics through authoritative code.

### B. One provider-neutral port with isolated adapters

Application code submits a bounded explanation request containing a completed, validated prediction artefact. An adapter maps that request to an enabled provider and returns an untrusted generated explanation plus non-secret provenance.

This preserves domain isolation and allows deterministic fake adapters in tests.

### C. No outbound models

This is the safest and remains the default runtime mode. It is insufficient if a user explicitly wants generated explanations in the standalone application, so the capability is supported but disabled until configured.

## Decision

1. Outbound generation is accessed only through a provider-neutral port owned by the application boundary. Analytics, optimiser, approval and persistence domains do not import provider SDKs or vendor response types.
2. The first supported API adapter is OpenRouter through its documented HTTPS API. Its API key represents separate provider billing and authorization; it is not a ChatGPT-subscription credential.
3. A private Hermes proxy is a supported optional adapter for a personal deployment. Hermes retains ownership of its model-provider credentials; autoFPL receives no underlying OAuth refresh token or provider API key.
4. Every adapter is disabled by default. Enabling one requires an explicit provider, model, endpoint policy, timeout, request-size limit, rate limit and cost/usage ceiling where the provider supports one.
5. Credentials are injected at runtime from the deployment secret store. They are never committed, written to application configuration, persisted in PostgreSQL, returned by an API or included in logs, traces, prompts, exceptions or recommendation artefacts.
6. OpenRouter routing must be intentional. The configuration records the requested model and any permitted upstream-provider or fallback policy; autoFPL must not silently broaden providers or models beyond that policy.
7. Provider requests use data minimisation. They contain only the fields needed to explain the selected, already-computed artefact and never include FPL credentials, browser sessions, unrelated personal history or raw secret-bearing source material.
8. Provider output is untrusted presentation text and never authoritative. It cannot alter a forecast, uncertainty value, solver constraint, proposed action, approval state, database query or external account.
9. Deterministic services continue when every model provider is disabled, exhausted, rate-limited, unavailable or returns malformed output. The product returns the validated prediction artefact with a clear “generated explanation unavailable” state.
10. Retries are bounded and idempotent. Provider failures cannot recursively invoke agents, switch providers silently or create an unbounded background loop.
11. Non-secret provenance includes provider adapter, requested model, actual model/provider when reported, routing-policy version, prompt-template version, request time, latency, outcome and response hash. Sensitive prompt and response bodies are not retained by default.
12. A private Hermes proxy binds only to an authenticated private network or loopback boundary, is never exposed as a public unauthenticated inference endpoint and is not granted database, FPL-account or approval capabilities.
13. Provider-specific privacy, retention, data-processing, regional, model-license and terms behavior must be reviewed before production enablement. “OpenAI-compatible” describes protocol shape, not equivalent security or policy.

## Consequences

### Positive

- The standalone application can use OpenRouter with a user-controlled API key.
- A personal deployment can reuse Hermes as an optional inference boundary without copying its OAuth credentials.
- Hermes can also remain purely an MCP client, which is the simpler path for “give me the predictions”.
- Model failures and vendor changes cannot break or silently change the analytical core.
- Provider behavior is attributable without storing secrets.

### Negative and risks

- Each enabled provider adds cost, privacy, retention, availability and supply-chain considerations.
- OpenRouter may route to different upstream providers unless configuration constrains that behavior.
- A Hermes proxy adds a privileged service and operational dependency when enabled.
- Generated explanations may still hallucinate or omit qualifications, so the structured artefact remains visible and authoritative.

### Reversibility/migration

Adapters can be added or removed behind the same port. Disabling an adapter requires no data migration. Stored forecast and recommendation records do not contain provider-native response objects and remain valid independently.

## Verification

Before enabling an adapter:

- contract tests use deterministic fakes to prove identical application behavior with the adapter disabled, failing or malformed;
- boundary tests prove generated text cannot mutate analytical or approval fields;
- configuration tests fail closed on missing provider/model/limits and reject secrets in tracked files;
- log-capture tests prove keys, authorization headers and sensitive payloads are redacted;
- timeout, retry, rate-limit and cost-ceiling behavior is tested;
- provenance tests record the selected adapter/model/routing policy without secret material;
- an opt-in OpenRouter integration test runs only with an injected test key and controlled spend;
- an opt-in private Hermes integration test verifies authentication, private-network exposure and graceful unavailability;
- the complete core suite passes with no provider credentials configured.

## References

- [OpenRouter quickstart](https://openrouter.ai/docs/quickstart)
- [OpenRouter provider routing](https://openrouter.ai/docs/features/provider-routing)
- [OpenRouter privacy policy](https://openrouter.ai/privacy)
- [Hermes Agent provider documentation](https://hermes-agent.nousresearch.com/docs/integrations/providers)
- [ADR-0005: ChatGPT MCP conversational interface](0005-chatgpt-mcp-interface.md)
- [Security standard](../standards/security.md)
- [Threat model](../security/threat-model.md)
