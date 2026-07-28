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

## Decision-room visual direction

The implemented decision-room foundation feels like a focused football analysis
desk rather than a generic administration dashboard. The pitch remains the
primary selection surface; official player portraits, forecast uncertainty and
selection role make each player immediately recognisable. Dense research detail
belongs in one selected-player dossier instead of being repeated across every
card. With qualifying official evidence, forecast fields now use a plainly
labelled, deliberately wide Baseline v0 built from market, availability,
capture-reported aggregate and fixture inputs. It is useful as an initial prediction without
being confused with an out-of-time validated or promoted model; identity, prior
outcomes and fixtures come from the same cutoff-safe official capture.

On the pitch and bench, each player card shows:

- an official FPL/Premier League photo when available, with a deterministic
  initials-and-club fallback;
- name, club, position, opponent and home/away state;
- expected points, expected minutes and a compact uncertainty cue;
- captain, vice-captain, bench order and availability status; and
- a clear selected/focus state for mouse, touch and keyboard.

Activating a card opens the same player dossier as a persistent side sheet on
wide screens and a full-screen sheet on mobile. Its sections are:

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

Recent form initially uses the existing immutable Gameweek outcome captures.
Rows with one fixture are match-specific; multi-fixture Gameweeks are visibly
labelled as aggregated. Add a bounded, cached per-fixture history collector
only if the product or evaluation shows that the extra granularity is useful.
The dossier never fills unavailable fields with invented values.

The selected player's portrait and chronological forecast-to-outcome ribbon are
the signature interaction: selection on the pitch should visually connect to
the evidence used to judge that player. Motion is limited to this transition,
respects reduced-motion preferences and never obscures values. The sheet must
support Escape, focus trapping/restoration, deep linking and complete keyboard
operation.

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

- a responsive formation and bench;
- interactive player evidence cards;
- official player portraits and cutoff-aware player dossiers;
- an immutable, capture-linked Baseline v0 expected-points artefact;
- an immutable user-owned forecast draft with an explicit one-time pre-deadline
  lock and computed locked, expired or frozen status;
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
still no promoted model, scenario engine, optimiser, live AI provider or
product release. A public read-only MCP player-dossier tool is deployed; owner
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
editing, scenario distributions and alternative generation remain.

- Implement reproducible CPU Monte Carlo and the GPU parity/benchmark path.
- Generate recommended, safer and higher-ceiling legal selections.
- Let the user create a draft selection and compare its forecast distribution.
- Show objective, expected gain/loss, uncertainty and the evidence that changes
  between candidates.

**Exit:** tiny cases agree with exact references, all candidates are feasible,
and the UI can compare at least three strategies plus the user's selection.

### Milestone G — grounded ChatGPT, MCP and embedded AI

Tracked initially by [#46](https://github.com/Jellman86/autoFPL/issues/46).

**Status:** active — the application now hosts a stateless Streamable HTTP MCP
endpoint with one typed anonymous `get_player_dossier` tool over the same
cutoff-correct read service as the decision room. It is explicitly read-only,
non-destructive and closed-world, and exposes no owner selection data. Plugin
packaging, OAuth, user-specific tools and host integration tests remain.

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

The next development slices are:

1. resolve the implemented current participation artifact's observed import
   blockers before adding backend or dossier fields: keep the rejected
   Euclidean projection and full five-state joint model out of the current
   artifact; run the implemented coherent factorization that preserves the
   supported raw appearance model and learns start and 60-minute probability
   conditional on appearance; keep that rejected factorization out after its
   start probability was non-worse in only four of eight folds, freeze raw,
   joint and factorized variants for genuinely new current-season temporal
   folds and stop selecting coherence methods on the opened holdout; define a
   separately labelled implemented current-official-availability ceiling and
   prospective three-variant evaluation contract, preserve the first complete
   current artifact and score it only after real outcomes; use the implemented
   exact-code coverage audit and fixed-URL Riker Byparr capture of FBref's
   2025/26 Championship playing-time page to drive the 78 promoted-club and 26
   other new/transferred player gaps; use the implemented bounded extractor's
   944 rows and 894 stable FBref identities; use the implemented,
   snapshot-hash-bound bridge for 60 reviewed exact matches while keeping the
   other 25 promoted-club players unresolved; use the implemented official-code
   operator boundary to capture only those reviewed players' fixed match-log
   URLs, use the implemented coverage API and bounded batch operator to expand
   the 60-player capture cohort, and use the implemented deterministic parser
   to retain bounded match chronology; use the three implemented fixed
   promoted-club schedule sources and deterministic stable-match-ID parser to
   define every prior-season match opportunity; next left-join player rows to
   those schedules, form explicitly missing-aware rolling features and add
   that history only after identical-fold evaluation;
   keep exact minutes on the
   retained player-last baseline, evaluate a two-stage or hierarchical minutes
   challenger and add the empirical point-distribution loop before any raw
   preseason value may influence advice;
2. run the implemented identical-fold cross-season feature ablation as real
   current-season folds accumulate, then promote only stable out-of-time gains
   from prior-season match performance, minutes, starts, underlying events and
   participation-derived durability; archived final health remains excluded
   from the candidate and current official availability always dominates it;
3. join the implemented provisional point and participation artifacts by exact
   capture/player identity, with supported appearance/start/60-minute
   probabilities, explicitly baseline-labelled minutes and only distribution
   components that pass their rolling gates;
4. operate the implemented bounded background Spider refresh across the fixed
   official-availability, specialist-lineup and dependent-consensus inventory,
   using the decision-room club board to audit FFScout gaps; then add
   reproducible quantitative, market/team-strength and attributable
   named-expert challengers without treating correlated reports as independent
   votes;
5. operate the implemented deterministic FFScout adapters, which resolve
   predicted-XI photo codes with a unique team-scoped fallback, derive
   non-starter claims only from complete identity-resolved XIs, and retain
   unique team-scoped `Out` and percentage-bearing `Doubts` as separately
   versioned quarantined claims; operate the implemented dependent strAIghtred
   consensus adapter under the same player/target duplicate clusters and the
   implemented read-only start-claim evaluator by source and lead time; then
   add a fail-closed official-injury adapter plus defensible availability truth
   before any feature use;
6. run the final-outcome command after the first completed, data-checked
   Gameweek and verify the resulting real replay/outcome pair;
7. run the baseline command as complete pairs accumulate and retain the
   machine-readable reports;
8. retain the point, probability-of-60-minutes, expected-minutes and empirical
   distribution reports as real folds accumulate;
9. operate the Playwright-MCP-backed bounded FPL Form public predicted-points
   adapter once its 2026/27 active forecast is available, preserve retrieval,
   availability, transport and extraction identities, run the implemented
   fail-closed player/fixture identity coverage report, and run the implemented
   external evaluator over its conditional published values and separately
   named probability-adjusted challenger before using any value as a feature;
10. keep exercising the implemented official-photo decision room and
   cutoff-aware dossiers against live captures as prior outcomes accumulate;
11. run the implemented fold-local ridge and fixed histogram-tree challengers
   as real folds accumulate, then run the implemented identical-fold official
   underlying-feature ablation before changing the incumbent feature contract;
12. run the implemented exact-cutoff, source-complete FPL Form feature ablation
   comparing official-only, conditional-points and appearance-adjusted variants
   on identical folds;
13. retain the implemented, persisted and explicitly unvalidated Baseline v0 on
   player cards while gathering enough real folds to promote or replace it
   through the registered out-of-time rule.

Do not add another standalone governance, universal contract, infrastructure or
AI-orchestrator project ahead of those slices.

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
