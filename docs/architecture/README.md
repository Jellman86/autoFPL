# Architecture

## Context

autoFPL converts permissioned football information, user-supplied state and versioned research into probabilistic forecasts and human-approved FPL recommendations.

```text
permissioned feeds / user input / research sources
                         |
                validation + provenance
                         |
          +--------------+---------------+
          |                              |
   PostgreSQL/object store          OpenViking
   authoritative/versioned          unstructured context
          |                              |
          +--------------+---------------+
                         |
              Python analytics boundary
        forecast -> simulate -> optimise -> verify
                         |
                   .NET product API
       auth -> workflow -> audit -> notifications
             /              |               \
            /               |                \
 dashboard / API     read-only MCP adapter   optional explanation port
            |               |                |
            |        ChatGPT / Hermes     OpenRouter API or
            |           MCP clients       private Hermes proxy
            |               |                |
            +---------------+----------------+
                            |
                       human decision
```

## Trust boundaries

- External data and research content are untrusted.
- The LLM is untrusted and non-authoritative.
- Python outputs are candidate analytical artefacts until schema and feasibility validation succeeds.
- The .NET service owns user authorisation and authoritative workflow state.
- PostgreSQL is authoritative; caches, OpenViking and object-store artefacts are reconstructable or version-addressed.
- No FPL write boundary exists under the current compliance decision.

## Conversational boundary

The primary conversational integration is the [read-only MCP interface](../adr/0005-chatgpt-mcp-interface.md). ChatGPT owns its model session; Hermes owns whichever provider is selected in Hermes. Both call the same versioned autoFPL prediction tools without direct database access. The MCP adapter is an inbound interface to existing application services, not a second source of business logic, and autoFPL receives neither client's underlying model credentials.

Hermes is not classified as inherently unsafe. Its preferred role is an MCP client, allowing a request such as “give me the predictions” to retrieve the deterministic prediction artefact and preserve its run, version, deadline and uncertainty fields.

When generated text is required inside the standalone product, [ADR-0006](../adr/0006-optional-model-provider-adapters.md) permits an explicitly configured OpenRouter API adapter or private Hermes proxy behind a provider-neutral port. That optional path adds cost, privacy and availability boundaries but cannot affect analytical truth or core availability. Reimplementing Codex OAuth inside autoFPL is a separate, higher-risk option and remains prohibited without documented upstream support and review.

## Initial module boundaries

- `src/backend/` — .NET product/control plane.
- `src/analytics/` — Python forecasting, simulation and optimisation.
- `src/web/` — TypeScript presentation.
- `contracts/` — versioned inter-component schemas.
- `docs/research/` — experiment, data and model provenance templates.

Detailed choices are established through ADRs. Do not add a broker, service mesh, Kubernetes, GPU dependency or extra database before a measured requirement and ADR.
