# ADR-0006: Optional model-provider adapters

- **Status:** Accepted
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** architecture, security

## Context

autoFPL may need bounded AI assistance outside ChatGPT for structured extraction/classification from permitted unstructured sources, evidence-grounded decision orchestration and generated explanations. The personal deployment should support an explicitly supplied API key, initially OpenRouter, and may use a private Hermes proxy. These integrations must not bypass data admission or change the deterministic authority established for forecasts, simulations, optimisation and approval.

Inbound conversational clients and outbound model providers are different boundaries. ChatGPT or Hermes can call autoFPL through MCP under [ADR-0005](0005-chatgpt-mcp-interface.md) without autoFPL calling a model. This ADR governs the separate case where autoFPL requests candidate structured extraction, a bounded decision-orchestration turn or generated text from a configured provider. Source extraction occurs only after an authorised source has been collected and snapshotted; decision orchestration follows [ADR-0007](0007-evidence-grounded-ai-decision-orchestrator.md).

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

Application code submits one of a small set of schema-defined requests: extract claims from a retained source snapshot, classify a bounded item, run one decision-orchestration turn over an approved working context/tool transcript or explain a completed validated prediction artefact. An adapter maps that request to an enabled provider and returns untrusted candidate data/text plus non-secret provenance. The application—not the provider adapter—authorises and executes each tool call and enforces ADR-0007's orchestration budget.

This preserves domain isolation, keeps collection separate from interpretation and allows deterministic fake adapters in tests.

### C. No outbound models

This is the safest and remains the default runtime mode. It is insufficient if a user explicitly wants AI-assisted extraction or generated explanations in the standalone application, so those capabilities are supported but disabled until configured. Deterministic parsers, manually reviewed data and the structured prediction artefact remain valid fallbacks.

## Decision

1. Outbound AI assistance is accessed only through a provider-neutral port owned by the application boundary. Typed operations are limited to bounded extraction/classification, one application-controlled decision-orchestration turn and explanation of validated artefacts. Analytics, optimiser, approval and persistence domains do not import provider SDKs or vendor response types.
2. The first supported API adapter is OpenRouter through its documented HTTPS API. Its API key represents separate provider billing and authorization; it is not a ChatGPT-subscription credential.
3. A private Hermes proxy is a supported optional adapter for a personal deployment. Hermes retains ownership of its model-provider credentials; autoFPL receives no underlying OAuth refresh token or provider API key.
4. Every adapter is disabled by default. Enabling one requires an explicit provider, model, allowed operation set, endpoint policy, timeout, request-size limit, rate limit and cost/usage ceiling where the provider supports one.
5. Credentials are injected at runtime from the deployment secret store. They are never committed, written to application configuration, persisted in SQLite, returned by an API or included in logs, traces, prompts, exceptions or recommendation artefacts.
6. OpenRouter routing must be intentional. The configuration records the requested model and any permitted upstream-provider or fallback policy; autoFPL must not silently broaden providers or models beyond that policy.
7. Provider requests use data minimisation. Extraction requests reference a retained, authorised source snapshot and include only the permitted text required by the schema. Explanation requests contain only fields needed to explain a selected, already-computed artefact. Neither may include FPL credentials, browser sessions, unrelated personal history or raw secret-bearing material.
8. Provider output is untrusted candidate data or presentation text and never authoritative. A candidate structured extraction remains quarantined with source span, temporal metadata, model/prompt/schema versions, confidence and expiry until deterministic validation plus required corroboration or human review promotes it. Generated output cannot directly alter a forecast, solver constraint, proposed action, approval state, database query or external account.
9. Deterministic services continue when every model provider is disabled, exhausted, rate-limited, unavailable or returns malformed output. Extraction failure leaves the source explicitly unprocessed and uses only already-approved features; explanation failure returns the validated prediction artefact with a clear “generated explanation unavailable” state.
10. Retries are bounded and idempotent. Provider failures cannot recursively invoke agents, switch providers silently or create an unbounded background loop.
11. Non-secret provenance includes operation type, source snapshot or prediction run ID, provider adapter, requested model, actual model/provider when reported, routing-policy version, prompt/schema version, request time, latency, outcome and response hash. Sensitive prompt and response bodies are not retained by default beyond governed source/candidate records required for audit.
12. A private Hermes proxy binds only to an authenticated private network or loopback boundary, is never exposed as a public unauthenticated inference endpoint and is not granted database, FPL-account or approval capabilities.
13. Provider-specific privacy, retention, data-processing, regional, model-license and terms behavior must be reviewed before production enablement. “OpenAI-compatible” describes protocol shape, not equivalent security or policy.

## Consequences

### Positive

- The standalone application can use OpenRouter with a user-controlled API key for bounded extraction, decision orchestration and explanation.
- A personal deployment can reuse Hermes as an optional inference boundary without copying its OAuth credentials.
- Hermes can also remain purely an MCP client, which is the simpler path for “give me the predictions”.
- Model failures and vendor changes cannot break or silently change the analytical core.
- Provider behavior is attributable without storing secrets.

### Negative and risks

- Each enabled provider adds cost, privacy, retention, availability and supply-chain considerations.
- OpenRouter may route to different upstream providers unless configuration constrains that behavior.
- A Hermes proxy adds a privileged service and operational dependency when enabled.
- Generated explanations may hallucinate or omit qualifications, and extracted claims may be wrong or temporally ambiguous, so quarantine, source evidence and the structured prediction artefact remain authoritative.

### Reversibility/migration

Adapters can be added or removed behind the same port. Disabling an adapter requires no data migration. Stored forecast and recommendation records do not contain provider-native response objects and remain valid independently.

## Verification

Before enabling an adapter:

- contract tests use deterministic fakes to prove identical core behavior with the adapter disabled, failing or malformed;
- extraction tests use retained source fixtures and assert source spans, temporal metadata, schema validation, quarantine and promotion decisions;
- boundary tests prove candidate claims or generated text cannot directly mutate analytical or approval fields;
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
- [ADR-0007: Evidence-grounded AI decision orchestrator](0007-evidence-grounded-ai-decision-orchestrator.md)
- [Security standard](../standards/security.md)
- [Threat model](../security/threat-model.md)
