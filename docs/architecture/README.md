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
       auth -> workflow -> audit -> MCP -> notifications
                         |
               dashboard / ChatGPT app
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

## Initial module boundaries

- `src/backend/` — .NET product/control plane.
- `src/analytics/` — Python forecasting, simulation and optimisation.
- `src/web/` — TypeScript presentation.
- `contracts/` — versioned inter-component schemas.
- `docs/research/` — experiment, data and model provenance templates.

Detailed choices are established through ADRs. Do not add a broker, service mesh, Kubernetes, GPU dependency or extra database before a measured requirement and ADR.
