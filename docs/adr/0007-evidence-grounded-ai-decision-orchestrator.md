# ADR-0007: Evidence-grounded AI decision orchestrator

- **Status:** Accepted
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** architecture, AI governance, security

## Context

autoFPL's intended experience is an AI “mind” that understands the user's goal, retrieves relevant current and historical evidence, asks specialist mathematical services for analysis, compares feasible plans and explains an advisory recommendation. The AI should do more than rephrase one finished expected-points table.

That role must not collapse evidence, calculations, memory and generated judgement into an unverifiable chat response. A language model has a finite context window, may hallucinate, and cannot be the database, source-rights policy, forecasting engine, optimiser or approval authority.

The architecture therefore needs a useful decision orchestrator with strong separation between sourced facts, tool-generated evidence, memory, mathematical results and generated judgement.

## Decision drivers

- Make the AI the strategic reasoning and conversational layer.
- Let it request additional forecasts, simulations and constrained alternatives when evidence is incomplete.
- Preserve authoritative facts, calculations, rules and approval outside the model.
- Keep provider choice replaceable across ChatGPT, Hermes, OpenRouter and future approved models.
- Make application-managed recommendations inspectable and replayable, while describing client-hosted evidence and audit guarantees accurately.
- Prevent prompt injection, unbounded agents, silent memory corruption and unauthorised FPL actions.

## Considered options

### A. AI as explanation formatter only

The application would compute one plan and ask a model to describe it. This is safe but rejected as the product goal because the model cannot investigate uncertainty, compare candidate strategies, use relevant memory or ask specialist tools for additional scenarios.

### B. AI as autonomous source of facts, forecasts and actions

The model would browse, calculate, select and execute with few constraints. This is rejected because model responses are not reproducible evidence, unrestricted retrieval can breach source terms, generated arithmetic can be wrong, and autonomous FPL actions are outside the approved compliance boundary.

### C. Evidence-grounded AI decision orchestration

The model receives an appropriately bounded context and an allowlist of typed tools. It can iteratively retrieve evidence and request analyses, then separate cited results from its own judgement. Deterministic services, policy and human approval remain external. Enforcement differs according to who hosts the model loop: a client-hosted ChatGPT/Hermes session versus an application-managed standalone provider loop.

This option is selected.

## Decision

1. The AI is the **decision orchestrator** and conversational strategy layer. It interprets the user's objective, assembles relevant evidence, requests specialist analysis, compares feasible plans, identifies uncertainty and produces reasoned advisory judgement.
2. Two orchestration modes are supported and must be identified in configuration, responses and audit:
   - **Client-hosted MCP mode:** ChatGPT or private Hermes owns the model session and orchestration loop and calls autoFPL's MCP tools under [ADR-0005](0005-chatgpt-mcp-interface.md).
   - **Application-managed mode:** autoFPL owns the orchestration loop and calls an explicitly enabled provider adapter such as OpenRouter or a private Hermes proxy under [ADR-0006](0006-optional-model-provider-adapters.md).
3. In client-hosted MCP mode, autoFPL sees and audits only the MCP requests it receives and the analytical artefacts it creates/returns. It enforces token scope, per-tool authorization, argument validation, rate/response limits and analytical compute budgets. It does not claim to control or know the client's upstream model turns, hidden retries, context, token usage, provider spend or complete conversation transcript.
4. In application-managed mode, autoFPL assembles the model **working context**, authorizes and executes every tool call, bounds the complete loop and records the observable prompt-template/input hashes, model/provider provenance, tool transcript, final response hash and proposal. Hidden chain-of-thought is neither requested nor required.
5. The model is not given an indiscriminate dump of every database row, document or conversation. Application-managed context assembly and MCP tool responses both minimise authorised, point-in-time records and version-addressed artefacts; more data requires another scoped tool request.
6. Inputs may include current squad and user constraints, approved curated data/features, source-linked news claims, user preferences, prior decision outcomes, predictive distributions, calibrated uncertainty, simulation summaries, optimiser candidates, rules and policy status.
7. PostgreSQL and immutable source/analytical artefacts are **authoritative memory** for structured state and audit. OpenViking or another retrieval store may hold unstructured research, summaries and contextual memory, but retrieved text remains non-authoritative evidence until linked to an approved source or record.
8. The orchestrator may use allowlisted read-only advisory tools to retrieve evidence, run or fetch forecast artefacts, request scenario simulations, ask the optimiser for feasible candidates and compare sensitivity. It cannot issue arbitrary SQL, shell, URL-fetch or provider calls. The application validates and executes tools in both modes; the client/provider never receives ambient service credentials.
9. **Bounded orchestration** is mode-specific. Application-managed mode enforces maximum model turns, elapsed time, token allowance, tool cost, simulation budget, response size and provider spend. Client-hosted MCP mode enforces only the controls autoFPL owns: per-tool authorization, request/rate/response limits, analytical compute quotas and cancellation. Recursive application-managed agents, silent provider switching and indefinite application retries are prohibited.
10. Collection remains outside the model. The AI may request only admitted, permissioned source connectors and may extract candidate claims only after immutable capture under the data-governance standard. It cannot browse around a failed control, choose an unapproved source or bypass authentication, robots, anti-bot, paywall, rate-limit, licence or terms restrictions.
11. Mathematical services remain responsible for predictive distributions, calibration, simulation and constraint-valid optimisation. The orchestrator can request changed assumptions or new scenarios but cannot silently edit returned values, solver feasibility, FPL rules or provenance.
12. The orchestrator may rank or select among validated candidate plans and may consider qualitative evidence or user preference not fully represented in the objective. Any such generated judgement is labelled separately from model/solver output, cites its evidence and states material assumptions and uncertainty.
13. A completed application-managed run creates a structured `RecommendationProposal` containing a proposal ID, orchestration mode, decision deadline, selected candidate and alternatives, forecast/simulation/optimiser run IDs, evidence and memory references, assumptions, user objective, uncertainty/sensitivity, conflicts or missing data, explicit AI judgement and an initial `unapproved` state.
14. Client-hosted MCP mode returns schema-validated evidence bundles and analytical run IDs but does not persist the client's generated recommendation or claim a complete replayable proposal. ChatGPT/Hermes may present its own advisory synthesis with citations. Persisting that synthesis would require a future explicit, separately authorized write contract; it is absent from the baseline MCP surface.
15. Neither mode can mark a proposal approved, mutate authoritative memory, alter an FPL account or trigger another external action. **Human approval** remains exact, explicit and separate; no FPL write client exists under the current compliance decision.
16. Durable memory changes are explicit application operations with provenance, scope, retention and correction rules. The model cannot silently convert conversation, generated summaries or its own conclusions into authoritative facts or training labels.
17. Prompt, source and memory content are treated as data rather than instructions. Tool authorization, argument validation, source admission, policy and proposal schemas are enforced outside the model.
18. If the orchestrator or every provider is unavailable, the dashboard and APIs still expose existing validated forecasts, simulations and optimiser candidates. The system reports that AI synthesis is unavailable rather than inventing a recommendation.

## Consequences

### Positive

- The AI can act as the useful strategic “mind” of the product rather than a cosmetic narrator.
- Forecasting, simulation and optimisation remain specialist tools that the mind can invoke and interrogate.
- Facts, calculations, memory and generated judgement remain distinguishable and auditable.
- ChatGPT, Hermes and standalone providers can provide the same strategic role through shared evidence contracts, with explicit mode-specific enforcement and audit guarantees.
- The user can inspect alternatives and uncertainty before making the final decision.

### Negative and risks

- Multi-step application-managed orchestration adds latency, token cost, tool coordination and deadline-capacity requirements.
- Client-hosted MCP sessions cannot provide autoFPL with complete model-loop, token, spend or transcript observability; only received tool calls and returned artefacts are audited.
- A persuasive explanation may still overstate weak evidence; structured citations, uncertainty and alternatives must remain visible.
- Relevant-context retrieval can omit information or surface stale memory, requiring temporal filters, source ranking and explicit missing-data reporting.
- Qualitative AI judgement is harder to backtest than a fixed objective and must be logged and evaluated separately.

### Reversibility/migration

The application-managed orchestrator consumes versioned contracts and creates non-authoritative proposal records. It can be replaced, disabled or changed between approved providers without migrating forecasts, source snapshots, simulations, optimiser artefacts or approval records. Client-hosted MCP sessions remain replaceable clients and create no AI proposal record in the baseline.

## Verification

Before enabling an orchestrator:

- shared contract tests prove both modes can access only the declared evidence and analysis tools and never receive ambient credentials;
- deterministic fixtures prove additional scenario requests preserve inputs, versions, seeds and deadlines;
- application-managed proposal-schema tests require orchestration mode, run IDs, evidence, alternatives, assumptions, uncertainty, conflicts and `unapproved` state;
- MCP contract tests prove client-hosted sessions receive evidence bundles/run IDs but have no proposal-persistence, approval, memory-write or FPL-action tool;
- adversarial tests prove prompt/source/memory instructions cannot grant tools, change policy, rewrite returned mathematics or approve/execute a proposal;
- context tests prove point-in-time filtering, minimisation, authorization and source/memory provenance;
- application-managed orchestration tests enforce model-turn, time, token, simulation, tool and provider-spend budgets and reject recursive loops;
- MCP-mode tests enforce per-tool scope, request/rate/response and analytical-compute limits without asserting knowledge of client tokens, spend, hidden retries or complete transcript;
- evaluation separately scores factual support, citation correctness, candidate-plan feasibility, preference alignment, uncertainty communication and unsupported claims;
- application-managed replay tests reconstruct observable model inputs, tool transcript and all versioned artefacts without requiring retained hidden model reasoning or claiming bit-identical model output;
- provider-outage tests leave the deterministic product usable and clearly report missing AI synthesis;
- separate end-to-end ChatGPT and private Hermes MCP tests ask for a gameweek recommendation, cause justified tool calls when needed and preserve evidence/run fields without creating a proposal record or exposing write capability;
- an application-managed OpenRouter/private-Hermes test produces the structured unapproved proposal under complete orchestration budgets and audit.

## References

- [ADR-0005: ChatGPT MCP conversational interface](0005-chatgpt-mcp-interface.md)
- [ADR-0006: Optional model-provider adapters](0006-optional-model-provider-adapters.md)
- [Research and model validation standard](../standards/research.md)
- [Data governance standard](../standards/data-governance.md)
- [Security standard](../standards/security.md)
- [Threat model](../security/threat-model.md)
