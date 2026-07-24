# ADR-0005: ChatGPT MCP conversational interface

- **Status:** Accepted
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** architecture, security

## Context

autoFPL needs optional conversational interfaces without making forecasts, simulations, optimisation, audit or the standalone dashboard depend on an LLM. The personal deployment should be usable from ChatGPT with the user's subscription and from a private Hermes Agent conversation, while avoiding duplicated domain logic and privileged database access.

Three credentials have different meanings and must not be conflated:

1. a ChatGPT/Codex OAuth credential authorises a supported client to use ChatGPT-managed Codex entitlements;
2. an OpenAI Platform API key authorises separately billed API calls;
3. an autoFPL access token authorises ChatGPT or another client to read user-specific autoFPL data.

A ChatGPT login is not a general autoFPL service credential. autoFPL must not copy browser sessions, cached Codex credentials or OAuth refresh tokens from another application.

## Decision drivers

- Keep deterministic calculations and authoritative state independent of model availability.
- Use the documented ChatGPT Apps SDK / MCP integration boundary.
- Keep model-provider credentials out of autoFPL when ChatGPT hosts the conversation.
- Minimise privileged runtime dependencies and credential owners.
- Preserve a normal dashboard and API for users who do not use ChatGPT.
- Keep every FPL recommendation advisory and human-approved.
- Permit future model providers without coupling domain logic to one vendor.

## Considered options

### A. Read-only MCP tools for ChatGPT and Hermes

ChatGPT hosts its conversation and model session. A private Hermes Agent hosts its own conversation and selected provider. Either client calls the same narrowly scoped autoFPL MCP tools over the approved transport. autoFPL validates its own access token, executes deterministic application services and returns versioned evidence.

Benefits:

- uses the user's ChatGPT subscription within ChatGPT;
- lets a Hermes MCP client answer requests such as “give me the predictions” without generating those predictions itself;
- follows a standard tool boundary rather than giving either client database access;
- avoids storing OpenAI credentials in autoFPL;
- keeps calculation authority in tested application services;
- keeps the same services usable by the dashboard.

Costs and risks:

- conversational access depends on ChatGPT availability and product limits;
- an authenticated MCP endpoint and tool-level authorisation are required for private data;
- untrusted prompts and model output create confused-deputy and injection risks.

### B. Private Hermes OAuth proxy

A private autoFPL deployment could send OpenAI-compatible requests to a Hermes proxy that owns ChatGPT/Codex OAuth credentials.

This is technically credible for a trusted personal deployment. Hermes is not considered inherently unsafe. Its proxy can reduce duplicated authentication code and keep OAuth credentials in one owner.

However, it adds an always-available privileged service, a private network boundary, operational coupling and another failure mode. It also makes the standalone autoFPL conversational path depend on Hermes. These costs are unnecessary when the primary subscription-backed experience can run inside ChatGPT.

### C. Embed or reproduce Direct Codex OAuth in autoFPL

This would make autoFPL a Codex OAuth client and credential owner.

It is rejected as the baseline because OpenAI documents ChatGPT sign-in for named Codex product clients, not as a general model API authentication mechanism for arbitrary application backends. It would expand autoFPL's secret-handling scope, couple it to Codex-specific behavior and create avoidable refresh, revocation and compatibility obligations.

### D. Call a model API or local model from the standalone application

A provider adapter could use an OpenAI Platform API key, another supported API or a local model. This remains a valid optional extension when a real standalone conversational requirement justifies its cost, privacy and operational trade-offs.

It is not required for the foundation or for ChatGPT-hosted conversations.

## Decision

1. The supported inbound conversational interface is the autoFPL MCP surface. ChatGPT Apps and a private Hermes MCP client may both use it.
2. ChatGPT owns its model session. autoFPL does not receive or store OpenAI/ChatGPT OAuth tokens and does not exchange a ChatGPT subscription for general API access.
3. Hermes owns its selected model provider and credentials. autoFPL does not receive them when Hermes is acting as an MCP client.
4. The initial MCP surface exposes read-only advisory tools only. Tools may retrieve versioned squad state, forecasts, simulations, optimiser proposals, uncertainty and evidence; they cannot mutate an FPL account or mark a proposal human-approved.
5. A prediction response includes a run identifier, generation and decision-deadline timestamps, data/model/solver versions, assumptions, calibrated uncertainty and evidence references. Conversational clients summarize this artefact rather than inventing missing values.
6. The MCP adapter calls the same deterministic application services used by the dashboard. The model is an untrusted presenter/orchestrator and is never the source of forecast, constraint, optimisation or approval truth.
7. Private user data requires autoFPL's own OAuth 2.1-compatible authorisation boundary, resource binding, least-privilege scopes and server-side checks. Each MCP client acts as a client of that boundary.
8. The standalone dashboard and API remain fully functional when ChatGPT, Hermes and every model provider are unavailable.
9. Outbound model-provider support is governed separately by [ADR-0006](0006-optional-model-provider-adapters.md); no domain service may depend directly on an LLM SDK or vendor response type.
10. Hermes proxy is not a baseline runtime dependency. A private Hermes proxy may be enabled through the optional provider adapter only under ADR-0006's controls.
11. Embedding Direct Codex OAuth remains prohibited unless current OpenAI documentation explicitly supports the intended third-party application use and a security/compliance review approves the credential lifecycle.

## Consequences

### Positive

- Subscription usage remains inside the supported ChatGPT experience.
- autoFPL has no OpenAI credential to leak, rotate or revoke for the MCP path.
- The conversational interface cannot bypass deterministic validation.
- The dashboard, API and research pipeline remain useful independently.
- Hermes remains available as an optional integration rather than an architectural dependency.

### Negative and risks

- ChatGPT-hosted conversation is unavailable when ChatGPT or the user's allowance is unavailable.
- MCP authentication and tool authorization still require careful implementation.
- Prompt injection can attempt to misuse legitimate read tools or exfiltrate excess data.
- A separate provider is required if the standalone dashboard later needs generative chat.

### Reversibility/migration

The MCP adapter is a replaceable inbound interface. A later LLM provider adapter can be introduced behind a provider-neutral port without changing forecast, simulation, optimisation or approval services. Choosing a private Hermes proxy later does not require migrating authoritative data.

## Verification

Before an MCP release:

- contract tests prove every tool calls an existing deterministic application service;
- the tool inventory and schemas prove the surface is read-only and least-privilege;
- authorization tests deny missing, wrong-resource, expired and insufficient-scope tokens;
- prompt-injection tests prove model text cannot change policy, approval state or optimiser constraints;
- responses include data, model, solver and recommendation version identifiers;
- the dashboard and core verification suite pass with all LLM integrations disabled;
- a ChatGPT developer-mode test verifies discovery, authentication, tool calls, citations and failure messages;
- a private Hermes integration test proves “give me the predictions” resolves through MCP and preserves the returned run/version/uncertainty fields;
- logs redact tokens and avoid unnecessary squad or personal data.

## References

- [OpenAI Apps SDK](https://developers.openai.com/apps-sdk/)
- [OpenAI Apps SDK authentication](https://developers.openai.com/apps-sdk/build/auth)
- [OpenAI Codex authentication](https://developers.openai.com/codex/auth)
- [Hermes Agent provider documentation](https://hermes-agent.nousresearch.com/docs/integrations/providers)
- [ADR-0002: Human-in-the-loop compliance boundary](0002-human-in-the-loop-compliance-boundary.md)
- [Threat model](../security/threat-model.md)
