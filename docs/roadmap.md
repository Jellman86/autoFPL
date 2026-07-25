# autoFPL delivery roadmap

This roadmap defines the product destination, release horizons, dependency order and evidence required to advance. It is not a date promise: research results, prediction evidence, operational constraints and user needs can change the route. Actionable acceptance criteria live in the linked GitHub issues; implemented behaviour belongs in the [changelog](../CHANGELOG.md).

## Product north star

autoFPL is a private home-research system whose overriding north star is to make FPL predictions as close to reality as possible using useful free or self-hosted methods.

Before a deadline, the user should be able to ask what to do. The product reconstructs a point-in-time decision snapshot, produces calibrated forecasts, simulates uncertainty, generates rules-feasible alternatives and lets an AI decision orchestrator compare the evidence. The result explains assumptions, alternatives, uncertainty and trade-offs. It remains advisory: the user decides and performs any FPL change manually.

The intended supported interfaces are:

- an evidence-rich web experience for proposal comparison and explicit approval or rejection;
- versioned read-only API/MCP tools used by ChatGPT and Hermes;
- an optional application-managed AI orchestrator behind a provider-neutral boundary;
- the same authoritative state, forecasts, scenarios and feasible-plan artefacts beneath every interface.

The end state is not an autonomous FPL bot. Public-source scraping, search, browser rendering, public read-only endpoints and bounded Byparr-assisted collection are valid research tools. FPL credential/session collection and account actions remain outside the product scope; changing that requires a deliberate owner decision, security design and new ADR under the [FPL access boundary](compliance/fpl-terms-boundary.md).

## Current checkpoint

The deterministic foundation is implemented on `dev` and privately deployed as a hardened, digest-pinned development service. It currently supports:

- versioned decision-snapshot metadata validation;
- versioned manual and synthetic source records plus manual-evidence timing rules;
- manual squad, starting-XI and complete bench-order feasibility;
- effective captaincy and automatic substitutions from manual play evidence;
- one composed effective outcome and deterministic effective-XI scoring from complete manual points;
- strict malformed/domain-invalid API handling and immutable snapshots;
- protected build, scan, publication and Git-backed private deployment.

This is not yet a forecasting product. There is no production ingestion pipeline, authoritative database, promoted forecast, simulation, optimiser, MCP advisory surface, proposal workflow, dashboard or product release.

## Release horizons

### v0.1 — Evidence-grounded single-Gameweek advice

**First usable question:**

> Given my point-in-time squad and the evidence available before this deadline, what starting XI and captain should I choose for this Gameweek, what credible alternatives exist, and how uncertain is the advice?

**Required input classes:**

- versioned season/Gameweek/deadline metadata and the deterministic rules required for lineup and captain feasibility;
- the user's 15-player squad, starting selection, bench order, captain/vice-captain and private risk/preferences;
- free point-in-time player observations and historical outcome targets selected for coverage, reliability, temporal correctness and measured predictive value;
- source, revision, timing and content-identity metadata sufficient to prove that every input was available before the decision deadline.

Public news, scout and pundit features are valuable enhancement candidates, not blockers to the first forecast. Add them when they can be reconstructed point in time and their out-of-time ablation improves the baseline.

**Required output artefacts:**

- one immutable decision-snapshot identifier;
- one versioned predictive distribution for each eligible squad player;
- one reproducible bounded scenario artefact;
- multiple rules-feasible lineup/captain candidates with objective and sensitivity evidence;
- one versioned read-only evidence bundle containing assumptions, alternatives, uncertainty, limitations and all upstream identifiers.

**Required journey:**

1. Persist the minimum free point-in-time data and user state in SQLite.
2. Reconstruct a decision snapshot using only evidence available before its cutoff.
3. Implement simple and research-backed forecast candidates, then register the final comparison before opening the holdout.
4. Promote only a leakage-safe model that improves on declared baselines out of time.
5. Produce bounded scenarios and rules-feasible lineup/captain candidates.
6. Expose the evidence, alternatives and uncertainty through read-only API/MCP tools.
7. Prove the journey end to end and privately deploy v0.1.
8. Add news or other richer sources in parallel when measured ablations show value.

**Acceptance scenario:**

The acceptance fixture describes a 15-player squad, one decision cutoff, pre-cutoff observations and a later outcome used only for evaluation. The application must recreate the same snapshot, forecast, scenario and feasible candidates through API/MCP. Real-world prediction claims require rolling point-in-time evaluation; synthetic success proves software behaviour only.

**Completion evidence:**

- the same immutable fixture can reconstruct the snapshot and reproduce the promoted forecast, scenario and feasible-candidate artefacts;
- temporal leakage, calibration, proper-score, decision-utility and failure-slice gates pass according to a preregistered rule;
- tiny simulation and optimisation cases match exact, brute-force or trusted references;
- ChatGPT and Hermes receive equivalent evidence and cannot access FPL actions;
- the release passes required checks and deploys by immutable digest;
- limitations and uncertainty remain visible, and the user enacts any decision manually.

**Explicit non-goals:** transfers, chips, opponent modelling, public exposure, application-managed AI, paid-provider dependence and FPL account access.

**Critical path:**

1. [#38 — dependency-ordered delivery roadmap](https://github.com/Jellman86/autoFPL/issues/38)
2. [#39 — v0.1 user journey and acceptance scenario](https://github.com/Jellman86/autoFPL/issues/39)
3. [#54 — align deployed manual-evidence contracts with reproducible timing semantics](https://github.com/Jellman86/autoFPL/issues/54)
4. [#40 — select and validate the minimum free v0.1 evidence package](https://github.com/Jellman86/autoFPL/issues/40)
5. [#41 — SQLite-backed season rules and decision-snapshot persistence](https://github.com/Jellman86/autoFPL/issues/41)
6. [#42 — register the first baseline comparison](https://github.com/Jellman86/autoFPL/issues/42)
7. [#43 — calibrated baseline forecast artefacts](https://github.com/Jellman86/autoFPL/issues/43)
8. [#44 — reproducible single-Gameweek scenarios](https://github.com/Jellman86/autoFPL/issues/44)
9. [#45 — feasible lineup/captain candidates](https://github.com/Jellman86/autoFPL/issues/45)
10. [#46 — versioned read-only API/MCP evidence](https://github.com/Jellman86/autoFPL/issues/46)
11. [#47 — end-to-end human-controlled advisory proof](https://github.com/Jellman86/autoFPL/issues/47)
12. [#48 — v0.1 release and private deployment](https://github.com/Jellman86/autoFPL/issues/48)

News-source research [#55](https://github.com/Jellman86/autoFPL/issues/55), claim extraction [#56](https://github.com/Jellman86/autoFPL/issues/56) and a hardened Byparr connector [#57](https://github.com/Jellman86/autoFPL/issues/57) run as optional enhancement work and do not block the first baseline forecast.

### v0.2 — Transfer planning

Extend the proven evidence loop to bounded transfer decisions. Version free transfers, points hits, budget and selling-price rules; compare hold/simple-transfer baselines; evaluate decision utility over time; return feasible alternatives and sensitivity to forecast error and horizon.

**Exit evidence:** historical point-in-time transfer cases and tiny exact instances agree with trusted references; every proposal is feasible; hold/simple baselines are reproduced; uncertainty and horizon sensitivity are reported; all actions remain manual.

Tracked by [#49](https://github.com/Jellman86/autoFPL/issues/49).

### v0.3 — Multi-Gameweek and chip strategy

Extend transfer planning to bounded multi-Gameweek horizons and season-versioned chips. Compare no-chip and simple heuristic policies before complex search, and retain negative results where longer-horizon optimisation adds no reliable value.

**Exit evidence:** current-season chip rules are versioned and tested; rolling-origin evaluation covers material regimes; plan feasibility, solver evidence and sensitivity are reproducible; advice remains manual.

Tracked by [#50](https://github.com/Jellman86/autoFPL/issues/50).

### v1.0 — Human-approved FPL advisor

Deliver the supported product experience rather than only analytical endpoints:

- an accessible web interface for evidence, alternatives and uncertainty;
- structured proposal revisions with an `unapproved` default state;
- explicit approval/rejection and audit separated from execution;
- the existing client-hosted ChatGPT/Hermes mode;
- optional bounded application-managed AI proposals without making a paid provider or AI synthesis essential;
- authoritative persistence, migrations, backup/restore, observability, retention/deletion, upgrade and rollback evidence;
- a signed SemVer release and supported private deployment boundary.

**Exit evidence:** end-to-end and adversarial tests prove facts, mathematics, AI judgement, proposal state and approval remain separate; accessibility/usability evidence shows that decisions and uncertainty are understandable; provider failure leaves deterministic evidence available; recovery and rollback are exercised; no FPL write client exists under the current terms decision.

Tracked by:

- [#51 — proposal comparison and explicit approval experience](https://github.com/Jellman86/autoFPL/issues/51)
- [#52 — optional bounded application-managed AI proposals](https://github.com/Jellman86/autoFPL/issues/52)
- [#53 — v1.0 release](https://github.com/Jellman86/autoFPL/issues/53)

## Dependency map

```text
current deterministic contracts -> align evidence timing/provenance (#54) --+
                                                                     |
v0.1 user journey ---------------------------------------------------+-> select/validate minimum data (#40)

#40 -> SQLite authoritative snapshot (#41)
#40 + #41 -> baseline comparison registration (#42)
#41 + #42 -> calibrated forecast artefact (#43)
                     -> scenario artefact
                         -> feasible candidates
                             -> read-only API/MCP evidence
                                 -> end-to-end proof
                                     -> v0.1 release
                                         -> transfer planning (v0.2)
                                             -> multi-Gameweek/chips (v0.3)
                                                 -> web proposal/approval
                                                     +-> v1.0 release
                                                     `-> optional managed AI --(if included)--> v1.0 release

#55 -> optional news-source evidence -> #56 optional news features -> ablation against #43
#57 only when it materially improves that optional source coverage
```

A later stage must not bypass an unmet predecessor by replacing missing evidence with LLM output, future-known data, an undocumented feed or an unvalidated model.

## Delivery workstreams and evidence gates

### 1. Runnable private service — completed development foundation

The API, protected publication pipeline and digest-pinned private Dockhand deployment are operational. Health, hardening and representative 200/400/422 behaviour are verified. Formal release rollback remains a v0.1 release gate once release state is reconciled.

### 2. Prediction-quality data and ingestion

- Inventory fields and sources that could improve the declared prediction target.
- Record source identity, collection-code SHA/method, coverage, season validity, `published_at`, `retrieved_at`, `available_at`, content identity, corrections, latency, missingness and known bias.
- Use useful free public sources through RSS/Atom, APIs/feeds, search, scraping, browser rendering or public read-only endpoints. Direct written permission is not a general gate for this private research project.
- Permit a dedicated hardened Byparr connector when it materially improves coverage or reliability; keep it source/domain-bounded and separate from the ARR-stack instance.
- Record official statements, journalism, scout analysis and pundit opinion as distinct evidence classes; preserve author/source, directness, source span, publication/correction times, conflicts and expiry without republishing full copyrighted content.
- Preserve private source snapshots or durable content identities plus timing metadata as needed for reproducibility. Reject future-known or temporally unreconstructable evidence.
- Promote each source and feature only after rolling/walk-forward reliability measurement and ablation against the incumbent data set.

**Exit evidence:** each enabled source has a technical provenance/dataset card; point-in-time reconstruction and correction-by-revision are tested; added data shows reproducible predictive or coverage value.

### 3. Authoritative state and deterministic rules

- Version season rules, deadlines, scoring, prices, positions, transfers and chips when their release requires them.
- Keep money and time exact.
- Persist authoritative workflow state in SQLite; keep OpenViking non-authoritative.
- Reconstruct snapshots using only evidence available at the relevant deadline.
- Check every proposed action with deterministic invariants.

**Exit evidence:** focused domain, migration and historical-fixture tests cover delivered behaviour; corrections create revisions; a consistent SQLite backup can be restored.

### 4. Forecasting and research

- Use literature and strong existing implementations to select worthwhile methods; exploratory analytics may begin immediately.
- Before the final holdout, register target, horizon, baselines, metrics, slices and promotion threshold.
- Use rolling-origin evaluation, training-only transforms and leakage controls.
- Add news or other rich features only after the baseline works, and promote them only when point-in-time ablation shows repeatable value.
- Measure calibration, proper scoring rules and decision utility.
- Record reproducibility details for promoted results and retain negative findings.

**Exit evidence:** promoted forecasts beat the declared simple baselines under the preregistered rule and remain reproducible with documented limitations.

### 5. Simulation and optimisation

- Simulate only from versioned forecast inputs and justified dependencies.
- Preserve reproducible random streams and bounded compute.
- Generate alternatives for explicit objectives without weakening deterministic rules.
- Record solver status, objective, gap, runtime, inputs, constraints and sensitivity.
- Fail safely on stale data, infeasibility, timeout or cancellation.

**Exit evidence:** tiny cases match exact/brute-force references; every returned plan is feasible; baseline policies and sensitivity show when complexity adds value and when it does not.

### 6. Advisory interfaces and AI orchestration

- Expose the same versioned evidence through API, web and read-only MCP tools.
- Keep ChatGPT/Hermes client-hosted synthesis distinct from application-managed proposals.
- Minimise context, allowlist typed tools and quarantine unvalidated model output.
- Treat retrieved articles, feeds and transcripts as hostile content: never follow embedded instructions, fetch linked resources or let source text grant tool, memory, approval or execution authority.
- Label generated judgement separately from facts and analytical results.
- Preserve deterministic availability if every AI provider is unavailable.

**Exit evidence:** clients receive equivalent evidence; citations trace to inputs and uncertainty; adversarial tests cannot grant tools, rewrite mathematics, mutate memory, approve or execute.

### 7. Human approval and product experience

- Present assumptions, alternatives, uncertainty, deadlines and trade-offs accessibly.
- Persist proposal/evidence revisions and an `unapproved` default state where the application owns the workflow.
- Require exact approval/rejection without dark patterns or implicit conversational approval.
- Keep approval separate from external execution; current FPL changes remain manual.

**Exit evidence:** end-to-end tests prevent bypass; usability evidence shows the decision and uncertainty are understandable; audit and revocation semantics are verified.

### 8. Release and operations

- Reconcile protected branches before release and promote a known `dev` state without unrelated changes.
- Add persistence, migrations, observability, backup/restore and retention only as required by delivered behaviour.
- Verify resource bounds, secret lifecycle, dependency recovery, upgrades and rollback.
- Produce signed SemVer tags, human-readable release notes and digest-pinned deployment records.

**Exit evidence:** the [Definition of Done](standards/definition-of-done.md) is satisfied for the exact release scope; required checks pass on the tag; deployment, recovery and rollback are exercised with no unresolved blocking review.

## Conditional research tracks

These do not block the critical path unless a later promoted method creates the stated need:

- [#30 — CPU, Intel iGPU and NPU execution](https://github.com/Jellman86/autoFPL/issues/30): CPU is the supported path. Revisit iGPU only for a measured representative workload; revisit NPU only for a supported neural-inference workload. Never attach unused devices to the API.
- [#31 — game-theoretic decision models](https://github.com/Jellman86/autoFPL/issues/31): optional research after simple forecast, simulation and planning baselines exist. Papers and opponent models do not bypass local point-in-time evidence.
- [#57 — hardened Byparr connector](https://github.com/Jellman86/autoFPL/issues/57): use when browser-challenge handling materially improves public-source coverage. Keep a dedicated source-bounded instance with SSRF, cookie, proxy and resource controls; the ARR-stack instance remains isolated.

## Standing constraints

- Prediction quality, scientific integrity and reproducibility are the primary optimisation goals.
- Human approval is required before any external account action.
- Deterministic facts, rules, money, scoring and feasibility remain authoritative.
- Data and research evidence must be point-in-time correct and reproducible.
- Use any useful free or self-hosted public-source method—including search, scraping, browser rendering and Byparr—when technically bounded and evaluated out of time. Do not bypass login or paid access, collect private/session data, create abusive traffic or break applicable law.
- OpenRouter and private model-provider integrations remain optional; core operation must not require a paid provider.
- Deployment permission is separate from merge permission.
- A phase is complete only when its linked issue evidence and applicable [Definition of Done](standards/definition-of-done.md) gates pass.
