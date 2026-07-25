# Architecture

## Context

autoFPL converts public football information, user-supplied state and versioned research into probabilistic forecasts and human-approved FPL recommendations.

```text
public feeds/web / user input / research sources
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
 dashboard / API     read-only MCP adapter   optional AI-assistance port
            |               |                |
            |        ChatGPT / Hermes     OpenRouter API or
            |           MCP clients       private Hermes proxy
            |               |          extraction / explanation
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
- No FPL write boundary exists under the current product decision.

## Conversational boundary

The primary conversational integration is the [read-only MCP interface](../adr/0005-chatgpt-mcp-interface.md). ChatGPT owns its model session; Hermes owns whichever provider is selected in Hermes. Both call the same versioned autoFPL prediction tools without direct database access. The MCP adapter is an inbound interface to existing application services, not a second source of business logic, and autoFPL receives neither client's underlying model credentials.

Hermes is not classified as inherently unsafe. Its preferred role is an MCP client, allowing a request such as “give me the predictions” to retrieve the deterministic prediction artefact and preserve its run, version, deadline and uncertainty fields.

When AI-assisted extraction, classification or generated text is required inside the standalone product, [ADR-0006](../adr/0006-optional-model-provider-adapters.md) permits an explicitly configured OpenRouter API adapter or private Hermes proxy behind a provider-neutral port. Collection occurs through versioned, technically bounded connectors before model processing; the provider never decides what becomes trusted evidence. Candidate extracted facts remain quarantined until validated and promoted. This optional path adds cost, privacy and availability boundaries but cannot directly affect analytical truth or core availability. Reimplementing Codex OAuth inside autoFPL remains a separate, higher-risk option requiring documented upstream support and review.

## AI decision orchestration

[ADR-0007](../adr/0007-evidence-grounded-ai-decision-orchestrator.md) defines the AI as the evidence-grounded strategic “mind” rather than only an explanation formatter. Two modes share the same analytical/evidence contracts but have different enforcement:

- In **client-hosted MCP mode**, ChatGPT or Hermes owns the model loop. autoFPL serves scoped evidence and analytical tools and audits only the calls it receives; it cannot control the client's model turns, token/provider spend, hidden retries or full transcript, and it does not persist the client's generated synthesis.
- In **application-managed mode**, autoFPL builds the minimal point-in-time working context, calls OpenRouter or a private Hermes proxy, authorizes every tool step, enforces complete loop/cost budgets and persists a structured `unapproved` proposal with the observable transcript and provenance.

In both modes the AI may retrieve approved data and memory, request forecasts or new scenarios, compare feasible optimiser candidates and expose conflicts. The model does not receive an unrestricted database/document dump and cannot directly collect arbitrary URLs, edit analytical values, change rules, mutate authoritative memory, approve a proposal or act on an FPL account. PostgreSQL and immutable artefacts remain authoritative; OpenViking is unstructured context; predictive models, simulations and the optimiser remain specialist analytical services. AI judgement is labelled separately from their outputs, and the user remains the final decision-maker.

## Initial module boundaries

- `src/backend/` — .NET product/control plane.
- `src/analytics/` — Python forecasting, simulation and optimisation.
- `src/web/` — TypeScript presentation.
- `contracts/` — versioned inter-component schemas.
- `docs/research/` — experiment, data and model provenance templates.

Detailed choices are established through ADRs. Do not add a broker, service mesh, Kubernetes, GPU dependency or extra database before a measured requirement and ADR.
