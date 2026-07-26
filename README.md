# autoFPL

A rigorous private home-research platform for **high-quality, human-approved Fantasy Premier League predictions and decision support**.

> [!IMPORTANT]
> autoFPL's north star is prediction quality using useful free or self-hosted methods. Public-source search, scraping, browser rendering, public read-only endpoints and bounded Byparr-assisted collection are in scope. The project does not collect FPL credentials/session material, bypass login or paid access, or submit team changes; the user acts manually.

## Status

**Decision room, SQLite snapshots, real-source replay/outcome capture and the rolling research commands are implemented.** When qualifying official evidence exists, the private application persists and shows an explicitly unvalidated Baseline v0 squad, XI, bench and captaincy derived from price, ownership, availability, capture-reported totals and fixture context; otherwise its acceptance fixture remains visibly synthetic. Each forecast artifact is linked to the exact official capture and has an immutable content hash. A separate durable player-Gameweek artifact covers every eligible official player and explicitly labels its current intervals and expected-minutes proxy as uncalibrated while fitted start/60-minute probabilities remain null. Background official collection produces both artifacts, while “Refresh prediction” only reloads the latest persisted result and never starts collection. Read-only evaluators compare total-points, expected-minutes, probability-of-60-minutes and empirical point/minutes distribution baselines with correction-safe expanding origins, score identity-gated FPL Form forecasts, and compare fold-local ridge and fixed histogram-tree challengers over cutoff-safe player, fixture and team features. Public-page collection reuses Quark's existing hardened Playwright/Spider research stack rather than deploying another scraper. A fixed, deliberately diverse shadow inventory can be refreshed automatically at a bounded interval to retain private immutable captures from official injury, specialist predicted-lineup and dependent-consensus pages without exposing their text or influencing a forecast. Deterministic FFScout adapters resolve predicted-XI entries through embedded official Premier League photo codes and resolve `Out`/percentage-bearing `Doubts` only through unique team-scoped official names. The decision room exposes exact-snapshot classification coverage for every official club, keeping gaps unknown rather than presenting missing players as non-starters. A separate strAIghtred adapter retains displayed consensus start probabilities, requires unique team-scoped identity and assigns the same player/target duplicate cluster as FFScout so dependent agreement is not counted twice. A read-only evaluator now scores those pre-deadline start claims against exact official starts by source and lead time, while refusing to substitute appearance for availability truth. All derived claims remain immutable and quarantined; unmatched or ambiguous identities remain explicit and no claim influences a forecast. A commit-pinned 2025/26 archive importer normalizes prior-season match performance through stable official player codes while excluding known look-ahead `xP`; a deterministic cross-season state artifact joins it to every current player with rolling performance/durability summaries and makes current official health authoritative. An identical-fold ablation now compares those candidates with unchanged ridge/tree incumbents, but Baseline v0 cannot consume them until real out-of-time folds justify promotion. The 2026/27 season has not yet produced a completed Gameweek pair or active FPL Form forecast, so Baseline v0 has no real out-of-time evaluation result and is not a promoted model.

A locked-holdout historical preseason evaluation has separately shown that the
fixed histogram-tree challenger improves next-Gameweek point MAE over its
pre-selected rolling-three baseline on the pinned 2025/26 archive. That result
now feeds a deterministic GW1 player-artifact generator tied to the exact
evaluated archive and current official capture. The fitted means are comparison
evidence only: current availability is explicit, no distribution is invented,
and the model does not silently replace Baseline v0. A strict operator import
persists the artifact immutably, a read-only OpenAPI route exposes it and each
player dossier presents the comparison with its evidence boundary.

The [delivery roadmap](docs/roadmap.md) defines the dependency-ordered route from the current foundation through v0.1 single-Gameweek advice, transfer and chip planning, and the v1.0 human-approved advisor. See the [documentation index](docs/index.md) and [changelog](CHANGELOG.md) for maintained guidance and implemented changes.

## Intended home-lab architecture

- **One .NET 10 application** — product API, workflow, authoritative state, static decision-room UI and MCP tools.
- **OpenAPI 3.1 contract** — the supported HTTP surface is generated from runtime metadata at `/openapi/v1.json`.
- **SQLite** — authoritative local storage on a persistent home-lab volume.
- **Python when needed** — research, forecasting, reproducible CPU/GPU Monte Carlo simulation and optimisation as a module/process or bounded worker.
- **Existing Quark research services** — isolated Playwright MCP handles dynamic structured pages, Spider MCP handles bounded public text extraction and SearXNG handles discovery; autoFPL adds typed evidence adapters rather than another crawler/browser stack.
- **Gameweek decision room** — an evidence-rich formation, player-card, explanation, comparison and grounded-conversation experience served by the application.
- **OpenViking** — versioned research and unstructured context, never authoritative squad state.
- **ChatGPT/Codex plugin and MCP** — subscription-backed use inside the OpenAI host through read-only autoFPL tools and an optional MCP Apps UI; autoFPL does not hold ChatGPT credentials.
- **Hermes MCP client** — private conversational access to the same versioned prediction tools, using Hermes' independently configured model provider.
- **Optional model-provider adapters** — OpenRouter API or a private Hermes proxy may perform bounded extraction/classification and generate explanations behind a provider-neutral boundary; outputs are quarantined or non-authoritative and neither provider is required for core operation.
- **Evidence-grounded AI decision orchestrator** — the strategic “mind” retrieves relevant data and memory, asks forecasting/simulation/optimisation tools for evidence and compares feasible plans. Application-managed mode persists a structured unapproved proposal; client-hosted ChatGPT/Hermes returns an evidence-grounded advisory synthesis without proposal persistence.

The current code-first boundary is recorded in [ADR-0010](docs/adr/0010-code-first-home-lab-architecture.md), together with the [prediction-quality decision](docs/adr/0009-prediction-quality-first-private-research.md), [read-only MCP boundary](docs/adr/0005-chatgpt-mcp-interface.md) and [FPL access boundary](docs/compliance/fpl-terms-boundary.md).

## Non-negotiable quality principles

1. Maximise leakage-free out-of-time prediction quality and decision utility.
2. Build working vertical slices with focused tests; do not substitute governance for implementation.
3. Point-in-time-correct data and rolling/walk-forward evaluation only.
4. Calibrate probabilistic forecasts and compare them with simple and incumbent baselines.
5. Require every source, feature or model promoted into advice to justify itself through ablation and failure-slice evidence.
6. Keep optimiser outputs feasible, reproducible and independently checked.
7. Link every material result to code, data snapshot, environment, seed and evidence.
8. Keep credentials, private data and account actions outside the analytical pipeline.
9. Label claims as observed fact, sourced evidence, assumption or judgement.
10. Apply research-promotion, migration and release gates only when work reaches those stages.

## Branch and release flow

```text
feature/* or fix/* -> pull request -> dev -> release PR -> main -> signed/tagged release
```

- `main` is releasable and protected.
- `dev` is the integration branch and protected.
- Work starts from `dev` on a short-lived branch.
- Squash merges use Conventional Commit titles.
- Hotfixes into `main` must be reconciled back into `dev` immediately.

See [GOVERNANCE.md](GOVERNANCE.md) and [CONTRIBUTING.md](CONTRIBUTING.md).

## Local governance checks

Install the hash-locked governance dependency, then run the same checks as CI:

```bash
python3 -m pip install --require-hashes --requirement requirements-governance.txt
make verify
```

`make verify` runs the Python governance tests, locked .NET tests, repository policy and documentation-link validation used by CI.

## Repository map

- `docs/adr/` — durable architecture decisions, including explicit supersession.
- `docs/standards/` — enforceable engineering and scientific standards.
- `docs/compliance/` — legal and product-operation boundaries.
- `docs/research/` — evidence base and reproducibility templates.
- `docs/operations/` — build, deployment, verification and rollback runbooks.
- `docs/index.md` — maintained routing index for repository documentation.
- `docs/roadmap.md` — prioritised future outcomes and evidence gates.
- `contracts/` — versioned stable external and persisted-data boundaries where useful.
- `src/` — product and analytics implementation boundaries.
- `src/backend/AutoFpl.Api/wwwroot/` — the current single-application Gameweek decision room.
- `tools/governance/` — executable repository policy.
- `tests/` — governance, domain and contract tests.
- `CHANGELOG.md` — implemented notable changes, with current work under **Unreleased**.

## Licensing and data rights

autoFPL source code is licensed under **GNU AGPL-3.0-only**. See [LICENSE](LICENSE). If a modified version is offered for users to interact with over a network, the licence requires an opportunity for those users to receive the corresponding source code.

Runtime and user data are not licensed by the source-code licence. Public-source research inputs require technical provenance, decision-time availability and quality evidence; they do not require direct written permission as a general project gate. Third-party article/data corpora stay private and are not republished. Personal squad history, research snapshots, credentials and deployment configuration belong in ignored local/runtime storage—not this public repository.
