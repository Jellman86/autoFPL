# autoFPL product and research roadmap

This roadmap connects the product experience to the data, scientific and
engineering work required to make it trustworthy. It is ordered by dependency,
not calendar date. Working software and measured research results advance the
project; plans and controls support those outcomes.

## North star

autoFPL is a private, self-hosted research application whose overriding goal is
the highest practical Fantasy Premier League prediction accuracy and decision
quality obtainable from useful free or self-hosted data and methods.

Before a deadline, a user should be able to open one Gameweek decision room and:

1. see the recommended starting XI, bench order, captain and vice-captain;
2. inspect every player's predictive distribution, expected minutes, important
   evidence, uncertainty and failure risks;
3. compare the recommendation with safer, higher-ceiling and user-authored
   alternatives;
4. ask an AI about that exact snapshot and selection without allowing generated
   text to replace facts, mathematics or feasibility checks; and
5. make the final FPL change manually.

The product is not an autonomous FPL account bot. It does not collect FPL
credentials or session material and does not submit transfers, activate chips
or change a lineup.

Initial-squad prediction quality is the active development priority because it
drives every downstream comparison, scenario, explanation and AI interaction.
Additional AI, authentication and sharing work does not outrank replacing the
served heuristic with a leakage-safe, calibrated, multi-Gameweek decision
policy.

## Selection ownership and locking

Forecast evidence and the user's FPL decision are separate records. A new
forecast may propose a different squad or selection, and AI may explain or
propose an edit, but neither is allowed to silently change the user's draft.

The decision-room lifecycle is:

1. an immutable forecast artefact produces a recommended squad, XI, bench,
   captain and vice-captain;
2. the user copies that recommendation into an editable draft, can modify it
   directly or ask AI for a proposed change, and must explicitly apply any AI
   proposal;
3. **Lock selection** validates the complete legal selection and creates an
   immutable, user-owned decision revision tied to the exact forecast and
   evidence identities;
4. before the official deadline, a user may unlock into a new draft and lock a
   superseding revision without rewriting the earlier decision; and
5. at the deadline, the latest locked revision becomes permanently frozen.
   Subsequent effective-player and captain changes are calculated only from
   official FPL automatic-substitution and captain-fallback rules against
   observed outcomes.

The UI must always distinguish recommended, draft, locked and deadline-frozen
state. Locking improves auditability and protects intent; it is not a claim that
the underlying forecast is accurate. autoFPL does not currently submit the
locked selection to the user's FPL account, so the UI must retain a clear manual
submission reminder.

## Product shape

```text
public/user data -> point-in-time observations -> immutable SQLite snapshot
                                                       |
                                                       v
                                              calibrated forecasts
                                                       |
                                                       v
                                     GPU/CPU reproducible scenarios
                                                       |
                                                       v
                                  rules-feasible candidate selections
                                                       |
                         +-----------------------------+----------------------+
                         |                                                    |
                         v                                                    v
                Gameweek decision room                              read-only MCP tools
           pitch, cards, evidence, compare                    ChatGPT, Codex and Hermes
                         |                                                    |
                         +-------------------- human decision ----------------+
```

The default deployment remains one .NET application, one SQLite database and
one static web experience. Python analytics run as a bounded module or process
when the scientific ecosystem materially helps. A separate long-running
service, another database or distributed infrastructure requires a measured
need.

## Application-shell visual direction

The implemented interface uses the same navigation grammar as Optimisarr and
YAWAMF: a persistent desktop sidebar, responsive mobile drawer, compact top bar
and breadcrumbs. autoFPL keeps its own focused football-analysis identity. The
pitch remains the primary squad surface; official player portraits, forecast
uncertainty and selection role make each player immediately recognisable.
Dense research detail belongs on a dedicated, deep-linkable player page instead
of a modal, side sheet or repeated card content.

With qualifying official evidence, forecast fields use a plainly labelled,
deliberately wide Baseline v0 built from market, availability,
capture-reported aggregate and fixture inputs. It is useful as an initial
prediction without being confused with an out-of-time validated or promoted
model; identity, prior outcomes and fixtures come from the same cutoff-safe
official capture.

On the pitch and bench, each player card shows:

- an official FPL/Premier League photo when available, with a deterministic
  initials-and-club fallback;
- name, club, position, opponent and home/away state;
- expected points, expected minutes and a compact uncertainty cue;
- captain, vice-captain, bench order and availability status; and
- a clear selected/focus state for mouse, touch and keyboard.

Activating a card opens the player's full evidence page. Breadcrumbs preserve
location and the back action returns to the same squad context. Its sections
are:

1. **Forecast** — point/minutes distributions, starting and 60-minute
   probabilities, freshness, model/run identity and selection rationale;
2. **Recent form** — chronological prior Gameweeks with opponent, home/away,
   minutes, starts, points, goals, assists, clean sheets, saves, bonus and
   cards;
3. **Fixtures** — the upcoming fixture sequence, rest days, congestion and
   home/away context; and
4. **Research tape** — admitted pre-deadline source claims, timestamps,
   agreement, contradictions and linked/dependent reports, visibly quarantined
   from the forecast; and
5. **Decision rationale** — the incumbent model's reasons, risks and the
   specific evidence that materially moved the forecast.

**My squad** is also a dedicated page, not a dashboard panel or edit dialog.
The current revision/lock panel is only a transitional workflow. The complete
builder must let a user construct and revise all 15 places from the eligible
player pool, search and filter replacements, see budget, club and positional
constraints update immediately, arrange XI/bench/captaincy, and save a
validated immutable revision. It compares the user's squad with the model using
the same forecast artifact and shows both expected-points delta and forecast
distribution; it must not imply that the user's squad was submitted to FPL.

Recent form initially uses the existing immutable Gameweek outcome captures.
Rows with one fixture are match-specific; multi-fixture Gameweeks are visibly
labelled as aggregated. Add a bounded, cached per-fixture history collector
only if the product or evaluation shows that the extra granularity is useful.
The dossier never fills unavailable fields with invented values.

The selected player's portrait and chronological forecast-to-outcome ribbon are
the signature interaction: selection on the pitch should visually connect to
the evidence used to judge that player. Motion is limited, respects
reduced-motion preferences and never obscures values. The page must support
breadcrumbs, browser history, focus restoration, deep linking and complete
keyboard operation.

## AI access and identity

autoFPL supports three complementary modes. All modes consume the same versioned
snapshots, forecasts, scenarios and candidate selections.

### 1. ChatGPT and Codex plugin mode

The preferred way to use an existing ChatGPT subscription is to run autoFPL as a
plugin backed by a read-only MCP server. ChatGPT and Codex can call typed tools,
discuss the evidence and optionally render an MCP Apps UI component inside the
OpenAI host. OpenAI documents this as an MCP server plus an optional web
component rendered inside ChatGPT:
[MCP server and UI quickstart](https://developers.openai.com/plugins/build/app-quickstart#introduction).

This uses the user's ChatGPT access in ChatGPT. It does not give the standalone
autoFPL website a transferable ChatGPT inference entitlement.

### 2. Standalone application with a configured model provider

The standalone decision room works without AI. Optional embedded conversation
uses a server-side provider adapter configured by the owner, initially one of:

- OpenAI API through a server-held API key;
- an OpenAI-compatible provider such as OpenRouter;
- a private Hermes model/tool proxy; or
- a future local model that satisfies the same evidence and evaluation
  contract.

Provider secrets never enter browser code, JSON responses, logs, SQLite
snapshots or Git. AI unavailability leaves forecasts, alternatives and
deterministic explanations usable.

### 3. Generic MCP client mode

Hermes and other compatible clients may connect to the same Streamable HTTP MCP
server. Start with read-only tools and no user-specific authentication while the
service remains single-user and private. If it later serves multiple users or a
broader network, implement the MCP OAuth 2.1 resource-server contract using an
established identity provider. OAuth here authenticates access to autoFPL; it
does not convert a ChatGPT subscription into API billing. See
[OpenAI plugin authentication](https://developers.openai.com/plugins/build/auth).

The public OpenAI documentation reviewed for this roadmap does not establish a
general-purpose "Sign in with ChatGPT" mechanism that lets an independently
hosted third-party website consume a user's ChatGPT subscription for embedded
inference. Do not present that option in the standalone UI unless a supported
OpenAI product specifically provides it.

### Integration configuration boundary

Integration configuration is split deliberately between deployment policy and
user interaction:

- **Compose/environment bootstrap:** publish instance-wide, non-secret settings
  such as the public base URL, enabled integration types, authentication mode,
  OAuth/OIDC issuer, provider endpoint allowlist, default model policy and
  resource/cost ceilings. Inject instance credentials through the deployment
  secret store or mounted secret files; never commit them to Compose or a
  tracked `.env`.
- **Integrations settings UI:** show connection health, capabilities, granted
  scopes, model/provider selection, usage limits and last successful test.
  Allow an authorised user to connect, re-authorise, test and disconnect an
  integration. Secrets are accepted only by the backend, are write-only after
  submission and are never returned to browser code, logs or API responses.
- **Policy precedence:** deployment policy defines what may be enabled and its
  maximum privileges. User settings can choose only within that allowlist and
  cannot override network, scope, cost or write-action restrictions.

The ChatGPT/Codex path does not require an OpenAI key in autoFPL. ChatGPT hosts
the model session and connects to autoFPL's OAuth-protected MCP plugin; the
OAuth flow authenticates the user to autoFPL. The standalone embedded-chat path
is separate and requires an explicitly configured API provider. A future
multi-user bring-your-own-key mode requires an encrypted credential store and
per-user lifecycle controls; it must not represent per-user keys as Compose
environment variables or retain them as plaintext SQLite values.

Use established standards rather than provider-specific login plumbing:
OpenID Connect for the autoFPL web session, OAuth 2.1 protected-resource
metadata and PKCE for MCP clients, and OpenAPI plus MCP schemas for the
integration contracts. Prefer an established identity provider over an
autoFPL-authored authorization server.

### Dashboard sharing and owner access

Dashboard sharing is a future product capability, not a reason to expand the
current private single-owner slice. Before any dashboard can be exposed beyond
the trusted private boundary, autoFPL must establish an authenticated owner
session through OpenID Connect and enforce authorisation in the backend rather
than relying on hidden UI controls.

The initial roles and boundaries are deliberately small:

- **Owner:** may configure integrations and provider secrets, manage collection
  and retention, create or revoke shares, edit and lock selections, and invoke
  other consequential instance actions allowed by deployment policy.
- **Viewer:** may inspect only the explicitly shared, read-only dashboard or
  immutable decision snapshot. Possession of a share link never grants owner
  access, write access or a reusable application session.
- **Future collaborator:** requires a separately designed role and audit
  contract. It must not emerge implicitly by giving a viewer selected owner
  routes.

Shares default to a sanitized, read-only decision view and have an explicit
scope, creation time, expiry policy and revocation control. They may use a
high-entropy capability link for bounded low-sensitivity sharing or require an
authenticated invited viewer when the deployment policy demands it. A shared
view may include the selected squad, forecasts, player dossiers, explanations,
uncertainty, evidence citations and freshness. It must exclude integration
secrets, provider configuration, private AI conversations, retained raw source
payloads, infrastructure metadata, administrative controls and every
write-capable route or MCP tool.

Owner authentication and viewer isolation are release gates for sharing.
Automated tests must prove direct-route authorisation, sanitized responses,
expiry and revocation, cache behavior and the absence of privilege gained by
changing client-side state. Relevant owner actions and share lifecycle events
must be auditable without logging secrets or private evidence payloads.

## Scientific programme

Prediction quality is evaluated out of time and at the decision cutoff. Every
candidate must compete with simple baselines, and complexity survives only when
it improves proper scores, calibration or decision utility.

### Prediction targets

The first forecast produces a distribution, not only a point estimate, for:

- probability of starting;
- probability of playing at least 60 minutes;
- expected minutes conditional on availability;
- Gameweek points and important scoring-event components; and
- joint squad outcomes needed for captaincy and lineup decisions.

### Temporal feature set

Every target Gameweek is represented using only observations available before
its deadline. The first richer feature table should add:

- player lags and rolling 1/3/5-Gameweek rates for minutes, starts, points and
  scoring components;
- nullable official xG/xA/xGC, ICT/BPS and defensive-action lags with explicit
  observed sample counts, followed by a same-fold feature ablation before they
  enter an incumbent model;
- exponentially weighted player form, retaining the raw missingness and sample
  count;
- team and opponent attacking/defensive form split by home and away;
- fixture opponent, venue, rest days, fixture count and congestion;
- availability/status/news changes with their actual capture timestamps; and
- explicit promoted/new-player and insufficient-history indicators.

Transforms, weights, imputers and encoders are fitted inside each training
window. Later corrections cannot rewrite an earlier decision row. Current
Gameweek-level outcomes are sufficient for the initial table; match-level
granularity and previous-season joins remain separate challengers until
fixture-level collection and cross-season player identity are reliable.

### Multi-season outcome learning

autoFPL learns through a controlled forecast → outcome → evaluation → retrain
loop, not by allowing a live model to mutate after each match.

The first useful training history is two complete seasons—2024/25 and 2025/26—
plus the accumulating 2026/27 season. Each archived player-Gameweek row must
retain the data that was knowable at its deadline, the later final outcome and
a reviewed cross-season identity. Team changes, promoted clubs, renamed player
records and genuine new players remain explicit rather than being guessed.

After each completed Gameweek the operator workflow should:

1. freeze and pair the final official outcome with its pre-deadline capture;
2. score the incumbent, challengers and user-locked squad without retraining;
3. append the new Gameweek as an untouched temporal fold;
4. rebuild candidate models on the expanding historical window on a scheduled
   cadence; and
5. promote a challenger only when the registered rolling-origin gates improve
   calibration, proper scores and decision utility without unacceptable
   failure-slice regressions.

Older seasons provide sample size and sparse-player priors; recency weighting,
season effects and hierarchical partial pooling are evaluated inside each fold.
They are not assumptions baked into the incumbent. Random splits, post-deadline
news, corrected future values and evaluation on the same rows used for fitting
cannot justify promotion.

### Baselines and challengers

Build in this order:

1. naive historical and market/availability baselines;
2. point-in-time published forecasts as separately scored challengers,
   beginning with official FPL `ep_next` and the existing bounded FPL Form
   collector;
3. regularised and tree-based tabular baselines;
4. hierarchical count, minutes and team/opponent-strength models;
5. calibrated ensembles;
6. richer public component forecasts, news, role, tactical and text-derived
   features only after source admission and same-fold ablation; and
7. neural or agentic methods only where rolling evidence justifies them.

Published forecasts never enter the incumbent merely because they look
plausible. Each is captured before its target deadline, retains content and
player identity, is scored alone against later official outcomes, and is then
tested as a model feature on the same temporal folds. Opta's public player-stat
projections are a promising later component source, but require a bounded
Quark collector and a reviewed availability/retention boundary before
implementation. Paid or credential-gated predictions are not scraped around
their access controls.

Promoted models are judged with rolling-origin evaluation, proper scoring rules
such as log score or CRPS where applicable, Brier scores for discrete events,
calibration error, decision regret/utility and meaningful failure slices.
Random temporal splits and future-known features cannot support promotion.

### Player and source evidence fusion

The forecast target is a player-Gameweek distribution, not one context-free
player score. The model programme separates:

- appearance, start and minutes distributions;
- player event rates conditional on minutes, position, team and opponent;
- shared match state and correlated scoring, clean-sheet, bonus and defensive
  events; and
- the resulting expected FPL points, quantiles and blank/return/haul
  probabilities.

Hierarchical partial pooling is a candidate for sparse and new-player history.
Direct position-specific tabular models remain challengers, and predictive
distributions may be stacked only with weights learned inside temporal
training windows.

News, scout, pundit, elite-manager and social evidence enters only through a
closed typed claim ledger. A claim records source and author, publication,
retrieval and availability times, content and revision identity, player,
Gameweek, claim target, directness, short supporting span, extraction identity
and duplicate cluster. Extraction confidence is not truth probability.
Unvalidated AI or scraper output remains quarantined and cannot alter a
forecast or squad.

Reliability is estimated by source, claim type and lead time, with shrinkage
for sparse sources, time/regime effects and dependence control for copied
reports. Reputation or historic manager rank can select a source for
evaluation but never sets its weight. Explicit source forecasts are first
scored alone, then tested as same-fold features or predictive distributions.
Broad sentiment and repeated mentions are not additive evidence.

The initial squad is a receding-horizon decision. The first registered
candidate horizons are 3, 6 and 8 Gameweeks; the final comparison, rather than
preference, chooses among them. See the focused
[player and source evidence-fusion programme](research/player-source-evidence-fusion-v1.md).

### Mathematical decision layer

The decision layer should progress from exact small cases to stronger methods:

- constrained lineup and captain enumeration;
- Monte Carlo expected utility and downside/ceiling sensitivity;
- mixed-integer optimisation for transfers, budgets, hits and chips;
- stochastic and robust optimisation for forecast uncertainty;
- rolling-horizon planning; and
- game-theoretic or opponent-aware objectives only after simpler policies have
  a measured baseline.

The UI must keep the objective visible. A result is never described as
"optimal" without the objective, constraints, horizon and uncertainty model.

### Reproducible CPU and GPU simulation

Monte Carlo simulation is a first-class product capability.

1. Implement a deterministic, vectorised CPU reference with explicit seeds and
   tiny cases that can be checked exactly.
2. Add a batched GPU backend for the representative scenario workload.
3. Prove distributional parity, reproducible stream ownership and bounded
   numerical tolerance against the CPU reference.
4. Benchmark end-to-end latency, throughput, memory and energy/resource use on
   available home-lab hardware.
5. Use GPU acceleration by default only when the deployed hardware and workload
   show a material benefit; retain the CPU path as the portable fallback.

The analytics process owns accelerator access. The public/API process does not
receive a GPU device merely because a future workload may use one. Candidate
implementations may use mature array/tensor libraries, but the library and
hardware backend are selected through a focused benchmark rather than an
architecture promise.

Correlated simulation should model shared match state, team scoring, player
minutes, substitutions and mutually dependent events instead of sampling every
player independently.

## Current checkpoint

The deterministic foundation on `dev` validates manual squads and complete
Gameweek selections, resolves substitutions and captaincy, and scores a supplied
effective outcome.

The decision room now includes:

- an Optimisarr/YAWAMF-style responsive sidebar and breadcrumbed application
  shell;
- a responsive formation and bench;
- interactive player evidence cards opening full player pages;
- official player portraits and cutoff-aware player dossiers;
- an immutable, capture-linked Baseline v0 expected-points artefact;
- an immutable user-owned forecast draft with an explicit one-time pre-deadline
  lock and computed locked, expired or frozen status;
- a dedicated 15-player My Squad workspace with exact-capture player search,
  live budget, club, composition and formation feedback, captaincy and bench
  controls, immutable revision saving and explicit locking;
- a server-authoritative same-artifact projected-points comparison between the
  model and user squad;
- deterministic explanation and risk sections;
- alternative-strategy summaries; and
- an explicit, unavailable-until-grounded AI composer.

Baseline v0 is a legal initial prediction built from official price, ownership,
availability, capture-reported aggregate and fixture inputs. It is deliberately
labelled as limited preseason evidence and is not a fitted, out-of-time
validated or promoted model. The synthetic fixture remains only as the
fail-closed acceptance fallback when qualifying official evidence is absent.

The pinned 2025/26 archive now has a separately retained preseason evaluation.
The fixed histogram-tree challenger was selected on expanding-origin
development folds and improved MAE by 8.87% against the pre-selected
rolling-three baseline on the untouched Gameweek 31–38 holdout, winning all
eight folds without a position-level regression. This supports building an
explicitly provisional preseason challenger for 2026/27 Gameweek 1; it does
not promote the model or replace Baseline v0.

The corresponding read-only generator now requires that exact evaluated
archive identity, fits the unchanged selected model to all historical
player-Gameweeks and emits fitted GW1 means beside exact-capture Baseline v0
values. Current availability and missing stable-code history stay explicit.
Migration 20 and a strict operator import now validate the fixed model,
evaluation, archive, official capture, Baseline cohort and every eligible
player before immutable persistence. A read-only route exposes the artifact
and the player dossier shows its raw point mean, Baseline delta, holdout result
and availability/identity warnings. It remains unable to influence advice.

The first replacement initial-squad candidate now consumes the complete
current joint player matrix and exact official prices. A zero-gap MILP selects
the full squad, legal XI and captain for a transparent single-Gameweek mean
surrogate, then the CPU FPL scorer measures the choice against Baseline v0 on
all retained joint rows. It is intentionally shadow-only: the underlying
distributions are still prospective and the 3/6/8-Gameweek decision horizon is
not yet implemented.

The opening-decision forecast now extends the unchanged retained two-season
tree across Gameweeks 1–8 from one exact official cutoff and records cumulative
3, 6 and 8-Gameweek player means. It does not propagate Gameweek 1 injury
status through the horizon. Eight per-Gameweek whole-row scenario marginals now
preserve within-week player dependence and are paired into fixed-seed paths
without repeating one historic shock through the horizon. A zero-gap MILP now
solves legal opening squads and weekly roles for every 3/6/8 horizon under
frozen expected-value and worst-20%-CVaR policies, then applies exact FPL
scenario scoring. Historical horizon/risk-policy selection remains the next
decision-quality gate; the current artifact recommends no policy.

The pinned historical archive registry now extends back through 2022/23.
Together with 2023/24, these exact raw archives provide four seasons and three
strict expanding-origin opening-season evaluation targets instead of a
scientifically inadequate single comparison. The 2022/23 archive is the first
training seed, not an evaluation target. The read-only opening-policy data
audit verifies exact raw payload hashes, reconstructs each target's Gameweek 1
cohort and price, joins Gameweek 1–8 outcomes and proves legal-pool feasibility.
Historical `value` is confined to the decision constraint; same-Gameweek `xP`
remains excluded. The matching forecast reconstruction now fits the unchanged
multi-season tree on strictly earlier archives and emits GW1–8 point means for
all 658, 616 and 690 opening players without selecting target point, minute or
event fields. Historical fixture structure is explicitly a final-archive
proxy: the 2023/24 target correctly contains the GW2 postponement and 11-match
GW7 realization. Scenario reconstruction now adds missingness-safe raw
appearance estimates and 37/38/38 latest-prior-season whole-Gameweek donor
paths without opening target performance. Registration
`b9d28cac497af35fc0762b7080db7e369759678872f78d47135e050f1920b675`
freezes all six candidates, common exact-FPL GW1–8 scoring and a stability gate
before target outcomes are opened. The byte-reproducible registered comparison
has now selected the six-Gameweek expected-points reference: the higher-mean
three-Gameweek downside policy failed the frozen worst-season regression gate.
The selected policy remains non-serving and is now frozen for prospective
2026/27 outcome scoring. The current v2 opening artifact now binds that exact
evaluation identity, marks only the six-Gameweek expected-points squad as the
prospective selection and leaves the serving boundary closed. Capturing its
official 2026/27 outcomes without retrospective reselection is the next
decision-quality gate. The selected projection is now an automated immutable
pipeline stage: every new official opening capture regenerates the exact
squad, freezes all eight weekly roles, imports through the private inbox and
appears on a typed read-only OpenAPI route only for the latest capture.
The prospective outcome method is also now preregistered before any target
result: it selects the final predeadline artifact deterministically, holds its
eight role decisions, and compares exact realised FPL scores with the
same-capture served Baseline v0 and single-Gameweek optimiser held unchanged.
Partial Gameweek 1–7 reports cannot pass. After Gameweek 8, a promotion review
requires at least a two-point gain over the served benchmark and no breach of
the frozen preseason cumulative p10; the evaluator itself never promotes.
A first exact transfer-aware challenger has also been tested rather than
assumed better. It jointly optimised the opening squad and up to one free
transfer before each Gameweek through GW6, but lost 21.33 realised points on
average against the fixed incumbent, won only one of three historical targets
and regressed by 74 points in the worst target. The plans repeatedly reversed
the same transfers as weak weekly fixture means alternated. It is rejected.
Do not add banking, hits or richer recourse until the per-fixture player
distribution demonstrates materially better opponent, minutes and role
discrimination; solver expressiveness is not forecast accuracy.
The selected six-week opener now also has a fixed conditional-optimality audit:
it solves the best distinct squad, excludes and globally reoptimises around
each incumbent player, and bootstraps the paired scenario paths. This exposes
small objective margins and unstable picks without confusing exact solver
optimality with empirical prediction quality. The resulting player regret and
selection-frequency fields are intended to ground later card explanations;
they remain non-serving diagnostics.
Because the audit exposes a flat forecast surface, prediction work now returns
to the point model before adding solver complexity. The next fixed ablation
compares the unchanged retained two-season histogram tree with all four pinned
historical seasons on the same eight late-2025/26 folds and exact player
cohorts. It requires material MAE gain, RMSE non-regression, majority fold wins
and position stability before a four-season current shadow may be retained.
The four-season result improved MAE by 0.80%, RMSE, seven of eight folds and
three of four positions, but missed the fixed 1% MAE gate. It is not retained.
Do not tune an age weight against the same opened folds merely to bridge the
0.20-point threshold gap; a separately specified partial-pooling or recency
model needs independent chronological evidence.
The next fixed challenger addresses the forecast's zero-inflated structure
directly. It multiplies a fold-local appearance probability by a point
regression trained only on appearance-positive rows and compares that
unconditional mean with the retained direct tree on identical target players.
This is the first point challenger that makes the already supported
participation mechanism explicit rather than asking one regression to absorb
lineup chance and performance simultaneously. The result passed: MAE improved
by 1.86%, RMSE improved, all eight folds won and every position improved.
Retain the exact appearance-hurdle specification as the next current
Gameweek 1–8 shadow. Do not silently substitute it into the incumbent
scenario or opening-squad lineage; rebuild and compare those artifacts under a
new explicit model identity.
That current path is now complete as a read-only challenger. It replaces
Pickford, Rodon and Anderson with Leno, Tarkowski and Rayan while retaining 12
players, reaches a zero-gap six-week optimum and gains 1.97 mean points on its
38 paired paths. Those three removed players were independently the three
least stable incumbent selections. Treat this as the strongest supported
current opener, but keep the serving handoff explicit and versioned because
current outcomes remain unavailable.
That handoff is now implemented as selected opening-squad v2. The worker
generates it separately, SQLite retains v1 and v2 under their own model
evaluation identities, the current typed route prefers v2 with v1 fallback and
the decision room displays v2's squad, roles and player means. “Use prediction
as draft” copies the shown GW1 choice before any explicit lock. V2 is therefore
the best-supported current product prediction, while its status remains
prospective-unscored and its promotion flag remains false.
The hurdle model has now also passed a complete historical opening-policy
screen under the already selected six-Gameweek expected-points decision rule.
Against the direct-tree incumbent it gained 37, 4 and 21 realised points over
the first eight Gameweeks of the 2023/24, 2024/25 and 2025/26 targets,
respectively. The +20.67 mean gain, three target wins and +4 worst-target gain
clear the fixed materiality and stability gates. This is the strongest
retrospective decision evidence for v2, but the targets were already opened;
only frozen 2026/27 outcomes can provide prospective promotion evidence.
The v2-specific conditional-optimality audit also confirms a zero-gap global
solve over the current 560-player pool. Leno, Tarkowski, Watkins, Bruno and
Truffert are bootstrap-core selections. Roefs is the only fragile pick at
28.5%; the best distinct solution swaps him for Kelleher but loses 0.79 exact
scenario points and never wins a retained path. The 200 resamples still
produce 195 distinct squads, so the model surface remains flat even though the
declared optimisation problem is solved exactly. Surface that distinction in
player explanations and continue improving inputs rather than adding solver
complexity.
The hurdle scenario distributions have now passed their separate proper-score
screen as well. Across 15,712 historical opening player-Gameweeks, CRPS
improves by 1.38%, all three target seasons win and every position improves.
Appearance Brier, log loss and calibration error also improve. Retain the
exact hurdle distributions as the best current scenario input. Their central
80% interval still covers 87.67% and the 37/38-row donor support is coarse, so
describe the ranges as empirical scenarios rather than perfectly calibrated
confidence intervals.
The OpenFPL-inspired official creative-history challenger has also been
screened without weakening those gates. Adding prior BPS, influence,
creativity and threat improved CRPS by 0.62% and won all three season
distribution comparisons, but missed the fixed 1% materiality threshold.
More importantly, its selected squads lost six realised points on average,
won only one target and regressed by 31 points in the worst target. Reject the
feature set and leave v2 unchanged.
The first cutoff-safe team/opponent challenger has now also been tested.
It reconstructed venue-specific attack and defence rates from official
player-summed expected goals, used a 180-day half-life and five-match prior,
and exposed only two matchup features to the conditional-point model.
Appearance scores remained exactly unchanged, but CRPS regressed by 1.125%,
all three season comparisons lost and selected squads lost 31.67 realised
points on average. Reject this representation and leave v2 unchanged. Do not
tune its decay, shrinkage or formula on the same opened targets. A later
team-strength candidate must use a genuinely different point-in-time signal
or registered hierarchical component model and prove standalone temporal
accuracy before the same opening-distribution and complete-policy screen.
Current cutoff-safe availability extraction can proceed independently because
it changes a different forecast component.

The SQLite persistence vertical slice is deployed and survives a managed
container recreate. Operator-triggered fixed-origin official FPL collectors
store exact raw hashes, retrieval-time availability and normalised
player/Gameweek/team/fixture rows. The API selects the newest capture available
before a requested Gameweek's recorded deadline. A second fail-closed command
can retain official per-player outcomes only after the event is finished and
data-checked, every fixture is finished and player coverage exactly matches the
post-event reference capture. The API and decision room can pair that final
outcome with a cutoff-safe replay. No 2026/27 Gameweek has completed yet, so a
real pair and current-season baseline evaluation dataset do not exist. There is
still no promoted model, calibrated scenario engine, promoted full-squad
optimiser, live AI provider or product release. Public read-only MCP
player-dossier, current-prediction and strategy tools are deployed; owner
selection data remains outside that anonymous boundary.

## v0.1 — Evidence-grounded single-Gameweek advisor

**User question**

> Given my current 15-player squad and everything known before the deadline,
> what XI, bench order and captain should I choose, what credible alternatives
> exist, and why?

Transfers and chips are explicit non-goals for v0.1.

### Milestone A — decision-room product contract

- Serve the decision room from the existing application.
- Render the XI, bench, player forecasts, explanations, risks and alternatives
  from one versioned advice document.
- Publish the supported HTTP surface as a tested OpenAPI 3.1 document with
  stable operation IDs and response schemas.
- Prove loading, selection interaction, responsive behavior and honest
  synthetic/real evidence labels.

**Exit:** the current synthetic fixture is visually usable and its API contract
is tested.

### Milestone B — SQLite decision snapshots

Tracked by [#41](https://github.com/Jellman86/autoFPL/issues/41).

**Status:** complete — the database, explicit migrations, cutoff-correct
snapshot API, revision lineage, restart readback, integrity check, online
backup, persistent Quark volume and visibly persisted synthetic UI fixture are
deployed.

- Persist season/Gameweek deadlines, player identity, position and price,
  current squad/selection, source observations and immutable snapshots.
- Reconstruct a snapshot from observations with
  `available_at <= decision_cutoff`.
- Preserve corrections as revisions.
- Read the same state after process restart and verify backup/integrity.
- Replace the demo endpoint's state fields with one persisted acceptance
  fixture while keeping the forecast explicitly synthetic.

**Exit:** the decision room survives restart and visibly identifies its snapshot
and cutoff.

### Milestone C — real point-in-time evidence

**Status:** active — bounded immutable reference capture, cutoff-safe replay,
final per-player outcome capture, complete replay/outcome pairing and
standalone official published-expected-points evaluation are implemented.
Migration 16 also provides an immutable quarantined typed-claim ledger with
official player identity and cutoff-safe reads; it is empty until admitted
source adapters populate it and cannot influence forecasts. The first real
replay/outcome pair awaits a completed 2026/27 Gameweek.

- Implement the smallest useful real historical/current importer alongside the
  fields it actually supplies.
- Preserve source identity, retrieval/publication/availability times, content
  identity, corrections, missingness and player matching.
- Reconstruct at least one historical deadline without future-known values.
- Retain the official bootstrap player photo identifier alongside player code
  and identity. Construct image references only from an allowlisted official
  Premier League asset origin; never accept an arbitrary image URL.
- Expose a cutoff-aware player dossier read model joining identity, prior
  outcomes, fixtures and quarantined research claims without exposing retained
  raw provider JSON. Keep claim dependence, contradictions and the
  non-influence boundary visible in the dossier.
- After the core minutes and points incumbents exist, ingest one bounded public
  predicted-points/minutes source as a timestamped external baseline. Score its
  published forecast alone before testing it as a model feature.
- Retain official FPL's published next-Gameweek expected-points value from the
  same immutable bootstrap capture as the first no-extra-transport challenger.
  Expose it as a comparison, never as the incumbent, until its standalone
  out-of-time evaluation is complete.
- Keep richer news, scout, browser and Byparr sources optional until an ablation
  shows predictive value.
- Shadow-capture admitted typed claims through Quark's existing research
  services. Learn reliability separately by claim target and lead time, cluster
  repeated reports, and retain conflicting/missing evidence without changing
  Baseline v0.

**Exit:** the application can replay a real historical pre-deadline snapshot and
later outcome without manual catalogue entry.

### Milestone D — baseline evaluation

Tracked by [#42](https://github.com/Jellman86/autoFPL/issues/42).

**Status:** active — the read-only baseline command, expanding-origin
selection, correction-time leakage checks, deterministic point baselines,
smoothed probability-of-60-minutes baselines, proper binary scores,
expected-minutes baselines, empirical point/minutes distributions, CRPS,
quantile/interval calibration diagnostics, position slices and reproducibility
identities are implemented against test fixtures. Real evaluation remains
unavailable until completed replay/outcome pairs exist.

- Implement a local Python evaluation command using SQLite snapshots.
- Reproduce naive and credible strong baselines with rolling origins.
- Register targets, windows, metrics, tuning budget and promotion rule before
  opening the final holdout.
- Retain machine-readable results, failure slices and negative findings.

**Exit:** one command produces a leakage-tested comparison report from real
replayable data.

### Milestone E — calibrated player forecasts and player experience

Tracked by [#43](https://github.com/Jellman86/autoFPL/issues/43).

**Status:** active — the immutable all-eligible-player Baseline v0 artifact and
typed read route are implemented with explicit provisional status,
uncalibrated interval-only values and null fitted start/60-minute
probabilities. Calibrated component distributions still require real temporal
folds and promotion evidence.

- Promote the best candidate that satisfies the registered rule, or retain the
  valid baseline.
- Keep the visible Baseline v0 as the honest provisional incumbent while real
  outcome folds accumulate; never relabel it as validated based on plausibility
  or in-sample fit.
- Retain the implemented immutable Baseline v0 artifact linked to its exact
  official capture and content hash; extend it with model/run, code and
  configuration identities before promoting a fitted challenger.
- Emit a versioned forecast artifact for every eligible player, including
  appearance/start/minutes and point-distribution components when supported,
  explicit missing components and provisional/calibrated status. The selected
  15-player advice artifact remains a downstream decision result rather than
  the complete player forecast table.
- Replace the provisional Baseline v0 values on the implemented official-photo
  pitch cards only when a persisted forecast artefact satisfies the promotion
  rule.
- Extend the implemented chronological form and fixture views with model/run
  identity, freshness and material feature evidence.
- Keep the implemented pitch/dossier URL synchronisation while adding editable
  user alternatives.
- Extend the implemented recommended → editable draft → explicit user lock
  lifecycle with transfer-aware squad edits. XI/bench/captaincy edits already
  create immutable superseding pre-deadline revisions, and the latest lock
  becomes permanently frozen at the deadline. AI outputs remain unapplied
  proposals and post-deadline effective changes use only deterministic official
  substitution/captaincy rules.
- Continue proving stale-data and provider-failure states; loading,
  missing-photo and missing-history states already have browser-tested
  fallbacks with keyboard and touch interaction.

**Exit:** the decision room displays the first honest out-of-time-evaluated
forecast on recognisable player cards, and a user can inspect the complete
cutoff-aware evidence trail for any selected player.

### Milestone F — scenarios and feasible alternatives

Tracked by [#44](https://github.com/Jellman86/autoFPL/issues/44) and
[#45](https://github.com/Jellman86/autoFPL/issues/45).

**Status:** active — the exact persisted forecast can now become an immutable
user-owned draft, direct XI/bench/captaincy edits create validated superseding
revisions, and the user explicitly locks the latest choice; transfer-aware squad
editing, scenario distributions and alternative generation remain. The
versioned CPU scoring kernel now resolves complete joint outcome rows with
official auto-substitution and captaincy semantics and compares candidates on
identical rows. A whole-Gameweek residual candidate has passed its retrospective
2025/26 CRPS screen and now emits an exact, content-addressed 2026/27 GW1
prospective shadow. It remains outside the product until genuinely new outcomes
score that frozen shadow. The matrix now crosses a strict private application
boundary through a network-isolated worker, immutable SQLite artifact and
current/stale/missing readiness contract. A deterministic read-only selection
score shadow now binds that matrix to the exact model selection and latest
user-owned revision, preserves all 38 paired score rows and emits distribution
and win/tie/loss summaries. The application now atomically publishes a
standalone integrity-checked SQLite backup into a dedicated read-only worker
mount when relevant source identity changes, removing the live-WAL timing
dependency. Migration 27, the private inbox and a strict importer now persist
that score with exact scenario/forecast/revision lineage; a read-only API
returns it only while all three inputs remain current. It remains shadow
evidence and cannot influence advice. A deterministic three-iteration beam
search now generates balanced, worst-20%-tail and best-20%-tail role strategies
on the fixed 15-player squad. The current 4,008-candidate workload completes in
about two CPU seconds, so GPU acceleration is deferred until transfer-aware or
multi-Gameweek search demonstrates a measured bottleneck. Migration 28 now
persists one exact strategy set per selection-score artifact, the worker
advances through that fourth stage automatically, and a strict read-only
OpenAPI route exposes only the current result.

- Score the frozen matrix prospectively as final outcomes arrive.
- Surface the persisted balanced, safer and higher-ceiling strategies in the
  decision room and let the user copy one into an explicit draft.
- Compare the current user-authored selection distribution with every strategy.
- Show objective, expected gain/loss, uncertainty and the evidence that changes
  between candidates.

**Exit:** tiny cases agree with exact references, all candidates are feasible,
and the UI can compare at least three strategies plus the user's selection.

### Milestone G — grounded ChatGPT, MCP and embedded AI

Tracked initially by [#46](https://github.com/Jellman86/autoFPL/issues/46).

**Status:** active — the application now hosts a stateless Streamable HTTP MCP
endpoint with typed anonymous `get_player_dossier` and
`get_current_prediction` tools over the same cutoff-correct read services as
the decision room. A third typed tool exposes the current balanced, safer and
higher-ceiling role strategies with paired scenario comparisons while
preserving their unpromoted shadow status. All three tools are explicitly
read-only, non-destructive and closed-world; the prediction and strategy tools
return only public model artifacts and none exposes owner selection data. A
validated repo-owned development plugin package now points ChatGPT and Codex
at the production MCP endpoint. Public submission materials, OAuth,
user-specific tools and host prompt tests remain.

- Expose typed read-only tools for snapshots, player forecasts, candidate
  comparison, evidence and data freshness.
- Package the MCP server as a ChatGPT/Codex plugin and test the subscription
  experience in the OpenAI host.
- Support Hermes and generic MCP clients through the same tools.
- Add optional standalone conversation through a server-side provider adapter.
- Add an Integrations settings surface for connection state, scoped linking,
  provider selection, limits, testing and revocation while keeping instance
  policy and secret injection in the Compose/deployment boundary.
- Implement standards-based autoFPL identity and MCP OAuth before exposing
  user-specific data beyond the trusted single-user network.
- Establish the authenticated owner role and backend authorisation boundary
  that future dashboard sharing will depend on; do not expose sharing in this
  milestone.
- Scope every conversation to explicit snapshot and candidate IDs.
- Require citations/tool evidence for factual claims; keep deterministic
  explanations available without AI.
- Turn an AI-suggested change into a visible, unapproved draft that is
  independently validated before comparison.

**Exit:** ChatGPT, Hermes and the standalone provider path receive equivalent
evidence; integrations can be configured, tested and revoked without exposing
secrets; provider failure leaves the core UI usable; and AI cannot mutate
authoritative state or perform FPL actions.

### Milestone H — v0.1 proof and private release

Tracked by [#47](https://github.com/Jellman86/autoFPL/issues/47) and
[#48](https://github.com/Jellman86/autoFPL/issues/48).

The automated acceptance journey must:

1. restore a known historical fixture after application restart;
2. reconstruct the same cutoff-correct snapshot;
3. reproduce the promoted forecast and scenario artefacts;
4. display the recommendation, open a photo-backed player card and inspect its
   forecast, recent form, fixtures and explanation;
5. compare a user-edited selection;
6. answer a grounded question through at least one MCP client;
7. remain useful when every AI provider is unavailable; and
8. deploy and roll back by immutable image digest with SQLite backup/restore.

## v0.2 — Transfer planning

Tracked by [#49](https://github.com/Jellman86/autoFPL/issues/49).

Extend the same decision room to current and proposed 15-player squads. Model
free transfers, selling price, budget, hits and uncertainty over a bounded
horizon. Compare hold, simple transfer and optimiser policies. The UI must show
which players change, immediate and horizon value, budget effects, risk and
forecast sensitivity.

## v0.3 — Multi-Gameweek and chip strategy

Tracked by [#50](https://github.com/Jellman86/autoFPL/issues/50).

Add rolling-horizon transfer planning and season-versioned chip rules. Compare
no-chip and simple heuristic policies before complex stochastic search. Surface
plan fragility and retain negative results when extra planning depth does not
produce reliable value.

## v1.0 — Supported private advisor

Tracked by [#51](https://github.com/Jellman86/autoFPL/issues/51),
[#52](https://github.com/Jellman86/autoFPL/issues/52) and
[#53](https://github.com/Jellman86/autoFPL/issues/53).

v1.0 hardens the proven product rather than introducing its first UI:

- accessible desktop and mobile decision experiences;
- user/proposal history and explicit approval/rejection where useful;
- an OpenID Connect owner session with backend-enforced gates for configuration,
  collection, selection locking and other consequential actions;
- explicit, revocable read-only dashboard or snapshot sharing with sanitized
  viewer responses and tested owner/viewer isolation;
- migrations, backup/restore, retention and observability;
- provider configuration and secret lifecycle;
- signed SemVer release, recovery and exercised rollback; and
- no FPL write client under the current product boundary.

## Immediate implementation order

The next development slices are deliberately product-sized. The responsive
shell, full player page, full-squad revision contract, dedicated My Squad
builder and pinned two-season archive are now implemented and tested. The
2024/25-to-2025/26 audit finds 534 exact stable-code matches; roster turnover
stays explicit and no name fallback is allowed. Final official outcome capture
and replay-pair readiness are also automated in bounded poll cycles; the first
real 2026/27 pair remains pending the first completed Gameweek. A fixed
two-season expanding-origin screen is also complete: the historical tree
improves aggregate MAE slightly over its matched current-season-only ablation,
but wins only three of eight folds and remains a shadow candidate. Its frozen
2026/27 GW1 forecast is generated against an exact official capture, persisted
in its own immutable artifact family and exposed through OpenAPI and player
dossiers. A lightweight readiness contract now distinguishes exact-current,
stale and missing states without serving an older shadow as current. It remains
outside advice. The frozen generator is also packaged as a separately scanned,
non-root analytics image. A private filesystem handoff now keeps its database
mount read-only and preserves the application as the sole strict importer; both
pollers now run in the deployed Quark Compose stack, using a standalone
application-published snapshot and private inbox. The worker progresses through
point forecast, joint scenario, selection score and fixed-squad role-strategy
artifacts without moving Python model work into the web request path. The
decision room now presents the model, balanced, safer and higher-ceiling roles
on their identical retained scenarios; previews are non-mutating and an
explicit copy creates a current-forecast draft before opening the full My Squad
builder. Exact candidate persistence and its read-only comparison surface are
now implemented. The active order is:

1. run the implemented frozen Baseline/component and selected opening-squad
   evaluators as automatically captured 2026/27 results arrive, retaining
   every partial fold without tuning and applying the preregistered decision
   gate only after Gameweek 8;
2. join only the point, participation and minutes components that pass their
   registered gates, publish calibrated player distributions and replace the
   exploratory rows behind the CPU-reference scorer;
3. compare registered 3-, 6- and 8-Gameweek initial-squad policies with exact
   budget, captaincy, bench, transfer and flexibility utility before selecting
   a serving horizon;
4. admit external forecasts, lineup/news claims and richer features only when
   same-fold ablations improve prediction or decision utility;
5. resume the owner-configured OpenAI-compatible provider after the prediction
   and initial-squad path is credible; and
6. add owner authentication and sanitized dashboard sharing before exposing
   consequential actions outside the trusted instance.

Specialist lineups, public forecasts, named experts and social evidence remain
valuable challenger sources. Operate the existing bounded collectors and score
each source alone, then run identical-fold feature ablations. A source enters
the incumbent only if it increases out-of-time accuracy or decision utility.
Reviewed FBref player-history capture is automatically schedulable in bounded
batches; unresolved source identities remain a separate fail-closed review
queue rather than being guessed from names.
The same bounded Byparr boundary now registers Championship playing-time
populations from 2021/22 through 2025/26. The earlier four populations are the
source side of an expanding-season promoted-player evaluation: 2021/22 trains
the first translation, 2022/23–2024/25 precede registered historical opening
targets, and 2025/26 remains the current cohort. Historical rows stay
identity-free until deterministically matched within each target FPL roster.
The first fixed appearance translation now passes: an equal-weight pool of the
retained appearance probability and a regularised Championship participation
model improves Brier score by 23.12%, wins all three scored target seasons and
all four positions, and keeps its player-cluster bootstrap interval wholly
favourable. Its full follow-on propagation improved affected-player CRPS by
8.94% and did not regress the all-player distribution, but the unchanged
globally constrained six-Gameweek policy gained only one realised point per
target on average with one win. That misses the fixed +2 mean and two-win
decision gates. The feature remains retrospective and non-serving; neither its
blend nor its gate may be retuned on these opened outcomes. Preserve it for
current sensitivity and genuinely prospective 2026/27 scoring. The current
sensitivity is now complete: none of the 60 reviewed Coventry, Hull and
Ipswich players is selected by either v2 or the translated challenger, and the
global solve returns the same 15 players and identical path scores. Aggregate
promoted-player playing time therefore leaves the active initial-squad path;
work advances to higher-decision-impact temporal, availability and registered
robust-horizon challengers that can affect selected or near-boundary players.
The first selected-squad lineup boundary audit is now complete as well. The
latest exact-capture FFScout revision covers all 15 v2 players, predicts 13 to
start and omits Enzo and Van Hecke. Those claims arrived 87.57 seconds after
the frozen official forecast capture and remain quarantined. They are genuine
pre-deadline risk evidence, but a predicted-XI omission is not a zero-appearance
probability. The next active forecast challenger therefore decomposes start,
substitute appearance and zero minutes, scores source reliability by lead time,
and must pass the unchanged proper-score and full 3/6/8-Gameweek policy screens
before changing the served squad.
The first fixed start-state point challenger has now tested that decomposition.
Its coherence rule improves start Brier and log loss, but the complete point
mean improves MAE by only 0.0811%, regresses RMSE and wins 4/8 folds. It is
rejected without tuning. This closes another aggregate-tree variation: the
fixture-level scoring boundary is now proven instead. A deterministic,
season-versioned scorer reproduces all 113,260 player-fixture totals across
four pinned seasons exactly from minutes, goals, assists, clean sheets, goals
conceded, saves, penalties, cards, own goals, bonus and the applicable
defensive-contribution rule. The active accuracy slice can therefore model
components conditional on participation and shared team/opponent state, pass
their simulated events through an exact scorer and then apply the unchanged
distribution and initial-squad policy screens. The unchanged time-decayed
Dixon–Coles model now passes a broader three-target replication: over 247
matches it improves joint-score NLL by 3.24%, clean-sheet Brier by 6.36%,
wins 17/24 clean-sheet folds and does not regress a target season. Retain that
shared scoreline state only where the player-level evidence supports it. The
first clean-sheet factorization improves player clean-sheet Brier by 2.70%,
log loss, calibration and 14/24 folds, but its reconstructed point mean improves
MAE by only 0.0975%, wins 12/24 point folds and loses MAE to the league-rate
variant. Reject the mean replacement and preserve v2. The active challenger is
therefore tested as a mean-preserving point distribution that injects shared
Dixon–Coles scorelines and fixture-level 60-minute clean-sheet awards. It
preserves every incumbent player-Gameweek column mean exactly, including the
registered blank and double Gameweeks, but worsens aggregate opening CRPS by
0.20%, wins only one of three targets and regresses goalkeeper CRPS by 0.98%.
Reject it without running a policy screen and preserve the retained hurdle
paths. The active component slice is now conditional player goal/assist
allocation from shared team scoring state. That marked-Poisson allocation
improves goal and assist NLL/Brier, calibration, every position and 15/24
folds, while beating both league-rate and position-share controls. Its combined
NLL gain is nevertheless only 0.53%, below the fixed 1% materiality gate.
Reject it without reconstructing opening points; preserve it only for
prospective evidence. The active initial-squad slice is now the registered
historical 3/6/8-Gameweek decision-horizon comparison on the retained
distributions. That solve is now complete: three-Gameweek expected points leads
the retrospective mean by 5.33 points but wins only one target, so it fails the
original stability gate. Three-Gameweek downside-balanced is stable at +2.33
mean points, two wins and no regression, but it is not the ranked leader and
the preregistered rule does not search passing runners-up after its leader
fails. Preserve the six-Gameweek expected-points policy without post-hoc rule
changes. The active current-squad slice is exact optimality and robustness
around the selected six-week squad under near-boundary exclusions, availability
evidence and bounded input perturbations. That bounded perturbation audit is
now complete. It exhaustively force-screens all 545 unselected players and
globally re-solves the closest forecast boundaries. Roefs/Kelleher is an
effective tie at a 0.73/0.74% decrease/uplift; Rayan/Anderson is next at
1.28/1.30%. Enzo, Szoboszlai, Van Hecke and Thiago also leave after at most a
5% coherent forecast reduction, while Watkins and Bruno tolerate 22.99% and
16.49%. Preserve the exact squad, but prioritize fresh lineup, health, role and
qualified external-forecast evidence for those near-boundary players. The next
accuracy slice should turn that priority into cutoff-safe player-specific
participation evidence and re-run the frozen distribution/policy gates; lineup
sources remain participation evidence, not an arbitrary points adjustment.
The first transport defect in that slice is now closed. Spider's wait path
returns an empty synthetic 525 result on Quark, so the official Premier League
injury page now reuses the already deployed isolated Playwright MCP. The fixed
collector requires all 20 club sections and structurally valid player rows,
retains compact club/player/injury/optional-update-link evidence plus the
rendered widget hash, and rejects the application shell or partial content. The
typed extractor now resolves within the official team and creates only
quarantined `doubtful` claims without inventing probabilities. The next slice
audits those resolved and unresolved rows against the selected and
near-boundary opening-squad players before any forecast-effect evaluation.
Do not add another standalone governance, universal contract, infrastructure
or AI-orchestrator project ahead of these slices.

## Conditional enhancements

- News/scout/pundit source evaluation
  [#55](https://github.com/Jellman86/autoFPL/issues/55) and claim extraction,
  corroboration and reliability [#56](https://github.com/Jellman86/autoFPL/issues/56)
  are now an explicit shadow-research workstream. The immutable claim-ledger
  foundation is implemented; source admission, extraction and scoring remain.
  Each source remains a challenger until an out-of-time ablation shows gain.
  The fixed-URL Byparr connector
  [#57](https://github.com/Jellman86/autoFPL/issues/57) is implemented for the
  FBref prior-competition capture and remains restricted to sources where it
  improves measured coverage.
- Accelerator investigation [#30](https://github.com/Jellman86/autoFPL/issues/30)
  is activated by the representative Monte Carlo workload and deployed hardware,
  not by unused device availability.
- Game-theoretic methods [#31](https://github.com/Jellman86/autoFPL/issues/31)
  follow a measured single-manager decision baseline.

## Standing constraints

- Prediction accuracy, calibration, decision utility and reproducibility are
  the primary optimisation goals.
- User action remains manual.
- Deterministic rules, money, scoring and feasibility remain authoritative.
- Every predictive input is point-in-time correct or excluded.
- OpenViking stores research and agent context, never authoritative squad,
  snapshot, forecast or approval state.
- Public-source collection remains bounded, non-abusive and outside private,
  login or paid access.
- Dynamic pages, bounded article extraction and search reuse Quark's existing
  Playwright MCP, Spider MCP and SearXNG services respectively; autoFPL does not
  deploy a parallel scraping/browser stack.
- Every interface sees the same versioned evidence; AI cannot manufacture a
  more favourable answer by bypassing the analytical pipeline.
