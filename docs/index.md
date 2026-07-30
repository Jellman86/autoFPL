# autoFPL documentation

Use this index to find the maintained source for each topic. The deterministic
private-development foundation, decision room, SQLite snapshots and persisted,
explicitly unvalidated Baseline v0 selection and all-player forecast artifacts
are deployed. Fixed-origin
official FPL reference and final-outcome capture plus cutoff-safe pairing are
implemented; the first completed 2026/27 Gameweek pair, validated forecasting,
simulation, optimisation and proposal persistence do not yet exist. One public
read-only MCP player-dossier and current-prediction tools are deployed;
user-specific advisory tools
still require owner authentication.

## Start here

- [Project overview](../README.md) — purpose, current status, architecture and non-negotiable boundaries.
- [Roadmap](roadmap.md) — prioritised future outcomes and their evidence gates.
- [Architecture overview](architecture/README.md) — approved system boundaries and decision records.
- [Contributing](../CONTRIBUTING.md) — branch, test and review workflow.

## Standards

- [Documentation standard](standards/documentation.md) — how documentation is structured, grounded and validated.
- [Definition of Done](standards/definition-of-done.md) — evidence required before a change is complete.
- [Engineering standard](standards/engineering.md) — architecture, language, testing and dependency gates.
- [OpenAPI standard](standards/openapi.md) — HTTP contract generation, metadata, compatibility, errors and security.
- [Research standard](standards/research.md) — point-in-time evaluation, baselines, calibration and reproducibility.
- [Data-quality and provenance standard](standards/data-governance.md) — point-in-time collection, source quality, reproducibility and derived-feature controls.
- [Security standard](standards/security.md) — trust boundaries, secret handling and CI controls.

## Architecture and contracts

- [Architecture decision records](adr/) — accepted and superseded durable decisions.
- [Versioned contracts](../contracts/README.md) — machine-readable service and data boundaries.
- [Backend boundary](../src/backend/README.md) — .NET product and contract projects.
- [Analytics boundary](../src/analytics/README.md) — future Python forecasting, simulation and optimisation ownership.
- [Web boundary](../src/web/README.md) — future presentation-layer ownership.

## Product boundary, security and governance

- [FPL access boundary](compliance/fpl-terms-boundary.md) — proportionate private-research and account-action boundaries.
- [Threat model](security/threat-model.md) — assets, trust boundaries and mitigations.
- [Security policy](../SECURITY.md) — reporting and supported security posture.
- [Governance](../GOVERNANCE.md) — roles, branch policy, decisions and release control.

## Data-source provenance

- [Provenance register](data/sources/README.md) — source status, timing, quality and collection-method records.
- [Source-record v1 contract](../contracts/data-source/v1/source-record.schema.json) — machine-readable source timing, identity, hash and correction envelope; not yet a runtime API.
- [Manual evidence timing v1](data/manual-evidence-timing-v1.md) — exact current POST-field inventory, evidence classes and leakage-safe replay semantics.
- [Manual user input v1](data/sources/manual-user-input-v1.md) — versioned direct-entry scope, privacy and point-in-time constraints.
- [Repository synthetic fixtures v1](data/sources/repository-synthetic-fixtures-v1.md) — versioned fictional test/evidence boundary.
- [Official FPL read-only API v1](data/sources/official-fpl-api-v1.md) — fixed-origin player, Gameweek, team, fixture and outcome capture semantics.
- [FPL Form public forecast v1](data/sources/fpl-form-public-forecast-v1.md) — fixed-origin conditional predicted-points capture and evaluation boundary.
- [Public research shadow sources v1](data/sources/public-research-shadow-sources-v1.md) — fixed-registry Spider capture, timing, retention and fail-closed boundary for diverse public evidence.
- [FBref Championship playing time v1](data/sources/fbref-championship-playing-time-v1.md) — fixed-URL 2021/22–2025/26 Byparr populations plus stable-match-ID schedule boundaries for current promoted-player history.

## Research and evidence

- [Evidence base](research/evidence-base.md) — durable sources supporting the research approach.
- [Player and source evidence fusion v1](research/player-source-evidence-fusion-v1.md) — probabilistic player state, typed pundit/news claims, reliability learning, aggregation and multi-Gameweek evaluation plan.
- [Research source portfolio v1](research/research-source-portfolio-v1.md) — deliberately diverse official, specialist, quantitative, market and named-expert shadow candidates plus prior-season and health-state treatment.
- [Baseline evaluation v4](research/baseline-evaluation-v4.md) — executable point, expected-minutes, availability and empirical distribution baselines with rolling chronology, proper scores and calibration diagnostics.
- [FPL Form external evaluation v1](research/fpl-form-external-evaluation-v1.md) — cutoff- and identity-gated scoring of published conditional points and the separately named appearance-adjusted challenger.
- [Official FPL published expected-points evaluation v1](research/official-fpl-published-expected-points-evaluation-v1.md) — standalone deadline-correct scoring of retained `ep_next` values against later official outcomes.
- [Temporal feature table v2](research/temporal-feature-table-v2.md) — cutoff-safe player, underlying-outcome and team match-leading features with explicit missingness and correction chronology.
- [Official underlying feature ablation v1](research/official-underlying-feature-ablation-v1.md) — same-fold ridge/tree comparison of the unchanged official contract with fixed xG/xA/xGC, ICT/BPS and defensive additions.
- [Temporal ridge challenger v1](research/temporal-ridge-v1.md) — fold-local regularised total-points challenger over the cutoff-safe temporal feature table.
- [Cross-season player state v1](research/cross-season-player-state-v1.md) — stable-code prior-season performance and durability state with current-health precedence.
- [Prior-competition player-history coverage v1](research/prior-competition-player-history-coverage-v1.md) — exact-code audit and bounded source-intake target for promoted, transferred and new-player history gaps.
- [Cross-season feature ablation v1](research/cross-season-feature-ablation-v1.md) — identical-fold incumbent comparison for prior-season performance and durability candidates.
- [Historical preseason evaluation v1](research/historical-preseason-evaluation-v1.md) — fixed expanding-origin development and locked-holdout evidence for a provisional archive-trained GW1 bridge.
- [Multi-season expanding-origin evaluation v1](research/multi-season-expanding-origin-v1.md) — exact-code 2024/25 plus 2025/26 feature table and matched current-only versus multi-season ridge/tree ablation.
- [Historical training-window evaluation v1](research/historical-training-window-evaluation-v1.md) — fixed identical-fold screen of the retained two-season point model against an otherwise unchanged four-season challenger.
- [Historical appearance-hurdle points evaluation v1](research/historical-appearance-hurdle-points-evaluation-v1.md) — fixed two-part comparison of direct point regression with appearance probability times conditional points.
- [Historical appearance-hurdle opening-policy evaluation v1](research/historical-appearance-hurdle-opening-policy-evaluation-v1.md) — identical-policy comparison showing whether the hurdle model improves complete legal opening-squad decisions across historical seasons.
- [Historical appearance-hurdle opening-distribution evaluation v1](research/historical-appearance-hurdle-opening-distribution-evaluation-v1.md) — proper-score and appearance-calibration comparison on target-outcome-free historical opening reconstructions.
- [Historical appearance-hurdle multi-horizon policy evaluation v1](research/historical-appearance-hurdle-multi-horizon-policy-evaluation-v1.md) — registered 3/6/8-Gameweek expected-points and downside policy comparison on the retained hurdle paths, confirming the six-week reference.
- [Historical correlated clean-sheet opening-distribution evaluation v1](research/historical-correlated-clean-sheet-opening-distribution-evaluation-v1.md) — rejected mean-preserving shared-fixture clean-sheet representation tested on opening CRPS, calibration and position stability.
- [Historical player attacking-component evaluation v1](research/historical-player-attacking-component-evaluation-v1.md) — rejected near-miss allocating shared team goal intensity to player goals and assists under Poisson NLL, Brier, fold and position gates.
- [Historical official-creative opening evaluation v1](research/historical-official-creative-opening-evaluation-v1.md) — rejected OpenFPL-inspired BPS/influence/creativity/threat enrichment tested on both proper scores and complete opening-squad outcomes.
- [Historical team-fixture-strength opening evaluation v1](research/historical-team-fixture-strength-opening-evaluation-v1.md) — rejected cutoff-safe player-summed-xG matchup enrichment tested on proper scores and complete opening-squad outcomes.
- [Current appearance-hurdle opening squad v1](research/current-appearance-hurdle-opening-squad-v1.md) — current GW1–8 hurdle means, availability-coherent paths and the new zero-gap six-week opening-squad challenger.
- [Current best-supported opening squad v2](research/current-best-supported-opening-squad-v2.md) — versioned product handoff, frozen model lineage, eight-week roles and the exact squad now shown by the decision room.
- [Multi-season preseason shadow forecast v1](research/multi-season-preseason-shadow-forecast-v1.md) — exact-capture 2026/27 GW1 raw point comparison from the frozen two-season tree, unable to influence advice.
- [CPU joint-scenario reference v1](research/cpu-joint-scenario-reference-v1.md) — seeded whole-row sampling and exact auto-substitution, formation and captaincy scoring for paired strategy comparisons.
- [Current multi-horizon player forecast v1](research/current-multi-horizon-player-forecast-v1.md) — cutoff-bound Gameweek 1–8 point means and registered 3/6/8 opening-squad horizons.
- [Current multi-horizon joint scenarios v1](research/current-multi-horizon-joint-scenarios-v1.md) — whole-Gameweek dependent residual rows paired into fixed-seed opening-horizon paths.
- [Current multi-horizon initial squad v1](research/current-multi-horizon-initial-squad-v1.md) — zero-gap legal 3/6/8-week expected-value and lower-tail-CVaR opening-squad policies with exact FPL rescoring.
- [Historical transfer-aware opening challenger v1](research/historical-transfer-aware-opening-challenger-v1.md) — rejected zero-gap planned-transfer challenger whose repeated fixture-driven churn lost 21.33 points on average against the retained fixed squad.
- [Current opening-squad optimality audit v1](research/current-opening-squad-optimality-audit-v1.md) — conditional player-exclusion regret and paired-path bootstrap stability for the zero-gap selected opener.
- [Current appearance-hurdle opening optimality audit v2](research/current-appearance-hurdle-opening-optimality-audit-v2.md) — exact global optimality, distinct-squad regret and paired-path player stability for the best-supported v2 opener.
- [Current selected opening-squad shadow v1](research/current-selected-opening-squad-shadow-v1.md) — exact historical-policy binding with all eight preseason role decisions frozen for prospective scoring.
- [Selected opening-squad prospective outcome evaluation v1](research/selected-opening-squad-prospective-outcome-evaluation-v1.md) — preregistered incremental Gameweek 1–8 exact-FPL comparison with frozen same-capture benchmarks and a non-automatic promotion-evidence gate.
- [Joint player-Gameweek scenario shadow v1](research/joint-player-gameweek-scenario-shadow-v1.md) — expanding-origin CRPS screen, whole-Gameweek residual rows and the exact 2026/27 GW1 prospective shadow matrix.
- [Current selection scenario score shadow v1](research/current-selection-scenario-score-shadow-v1.md) — exact model and user revision scoring on identical joint rows through the strict product handoff.
- [Current selection role strategies shadow v1](research/current-selection-role-strategies-shadow-v1.md) — bounded balanced, lower-tail and upper-tail XI, bench and captaincy search on the fixed 15-player squad.
- [Historical participation evaluation v1](research/historical-participation-evaluation-v1.md) — fixed locked-holdout appearance, start, 60-minute and uncapped-minutes challengers with proper probability and calibration scores.
- [Historical participation coherence evaluation v1](research/historical-participation-coherence-evaluation-v1.md) — fixed Euclidean probability-nesting projection on identical historical folds, explicitly limited to a secondary diagnostic.
- [Historical joint participation evaluation v1](research/historical-joint-participation-evaluation-v1.md) — five-state coherent classifier screened against immutable raw metrics, explicitly requiring new-season evidence for promotion.
- [Provisional preseason participation forecast v1](research/preseason-participation-forecast-v1.md) — exact-evaluation-identity current-player appearance, start and 60-minute probabilities with baseline-labelled minutes and explicit coherence.
- [Current official availability ceiling v1](research/current-official-availability-ceiling-v1.md) — fail-closed official status/chance constraints and a registered prospective comparison variant.
- [Provisional preseason player forecast v1](research/preseason-player-forecast-v1.md) — exact-evaluation-identity fit of the selected archive model to current GW1 players without changing served advice.
- [Temporal histogram-tree challenger v1](research/temporal-tree-v1.md) — fixed nonlinear comparison on the ridge evaluator's identical expanding-origin folds.
- [FPL Form temporal feature v1](research/fpl-form-temporal-feature-v1.md) — exact-cutoff, strict-direct-identity bridge from retained public forecasts into model-ready player features.
- [FPL Form feature ablation v1](research/fpl-form-feature-ablation-v1.md) — same-cohort official-only versus public-forecast ridge/tree comparison with source-complete temporal folds.
- [Predictive research-review template](research/literature-review-template.md) — optional working note for method selection and promotion registration.
- [Dataset-card template](research/dataset-card-template.md) — provenance, rights and quality record.
- [Model-card template](research/model-card-template.md) — intended use, evaluation, limitations and monitoring.
- `docs/research/experiment-template.yaml` — preregistered experiment metadata.

## Operations

- [Development container](operations/container.md) — build, scan, smoke-test, publish and private deployment of the bounded validation API.

Additional runbooks must be added with the runnable services they support and include verification and recovery rather than documenting an unmerged deployment.

## Project history

- [Changelog](../CHANGELOG.md) — implemented changes, with current work under **Unreleased**.
- GitHub issues — actionable planned work, acceptance criteria and reproducible defects.

If a maintained reader-facing page is added, link it here in the same change.
