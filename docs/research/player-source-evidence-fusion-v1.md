# Player and source evidence fusion v1

Reviewed 26 July 2026. This is a candidate programme, not evidence that a
source or model improves autoFPL. Promotion still requires registered,
point-in-time rolling evaluation.

## Decision

autoFPL will predict a player-Gameweek distribution rather than assign one
context-free player rating. The intended state is:

- probability of appearing, starting and reaching relevant minutes bands;
- a minutes distribution conditional on squad role and availability;
- player event rates conditional on minutes, team and opponent state;
- fixture-correlated clean-sheet, scoring, assist, save, card, bonus and
  defensive-contribution events; and
- the resulting FPL points distribution with expected value, quantiles,
  blank/return/haul probabilities and explicit missingness.

Hierarchical partial pooling across player, position, team and season is a
candidate for sparse and new-player histories. Direct tabular and position
ensembles remain strong challengers rather than being excluded by the
generative design.

Durable player ability does not reset with the official new-season totals.
Cutoff-safe prior-season match histories, current-season matches, transfers,
team/manager changes and injury/return-to-play chronology are candidate inputs.
Rolling 1/3/5/10-match, exponentially decayed and long-run partially pooled
states are compared inside temporal training windows; no fixed lookback is
assumed best.

## Human and published evidence

Public prose, selections and forecasts enter as immutable typed claims, never
as instructions or direct changes to expected points. The closed initial
targets are availability, likely start, probable minutes and role. Each claim
retains source, canonical URL, author when available, publication/retrieval/
availability timing, source content identity and revision, player and target
Gameweek, directness, short supporting span, extraction identity/confidence and
duplicate-cluster identity.

Extraction confidence describes whether the text was parsed correctly; it is
not the probability that the football claim is true. New claims remain
quarantined until deterministic identity, chronology and schema checks pass.
LLM extraction remains untrusted and cannot select sources, navigate, invoke
tools, promote features or change a squad.

Source contribution is learned by:

`source × claim type × lead time × recent regime`

The reliability model should estimate bias, calibration, resolution, coverage,
contradiction and incremental value, with hierarchical shrinkage for sparse
sources and dependence control for copied reports. An expert's FPL rank or
reputation is a source-selection hypothesis, not a forecast weight. Published
teams and transfers are constrained decisions and cannot be treated as
unconditional player point forecasts.

## Candidate aggregation

The structured-data player model is the prior/reference prediction. Typed
claims may become likelihood-style updates to their matching component, such
as start or minutes probability, only after calibrated outcome evidence exists.
Explicit published predictive distributions can instead be combined with
model distributions using regularised predictive stacking fitted inside each
temporal training window.

Broad sentiment scores and repeated mentions are not additive evidence.
Duplicate/corroboration clusters prevent one report echoed by many accounts
from being counted as independent observations. Missing or conflicting claims
remain visible and may cause abstention.

## Evaluation

All evaluation uses evidence available no later than the target decision
cutoff and fits reliability, calibration and aggregation weights using
training windows only.

| Target | Primary evaluation |
|---|---|
| availability/start/60+ minute events | log loss, Brier score, reliability and resolution |
| minutes and FPL points distributions | CRPS, quantile/interval coverage and calibration |
| explicit expected points | distributional score where available; MAE/RMSE/bias as secondary diagnostics |
| squad policy | legal-feasibility checks, expected utility and realised decision regret |

Source-only scoring precedes a same-player, same-fold ablation against the
structured incumbent. Promotion requires useful incremental out-of-time gain,
calibration, failure slices and uncertainty on the paired difference. Negative
and inconclusive results are retained.

## Planning horizon

The initial squad is a rolling-horizon decision, not a ranking of one-Gameweek
point estimates. The first candidate horizons are 3, 6 and 8 Gameweeks, fixed
before final comparison. Joint scenarios include match state, player minutes,
correlated scoring events, bench/captain outcomes, future transfers, hits,
budget and squad flexibility. The chosen horizon is re-optimised each
Gameweek; no model assumes preseason knowledge remains accurate for the whole
season.

## Implementation order

1. Retain and evaluate the existing simple distribution, minutes and
   availability baselines as real outcome folds accumulate.
2. **Implemented provisionally:** emit a versioned player-Gameweek forecast
   artifact for every eligible player, with explicit provisional status,
   uncalibrated interval-only distribution and missing fitted components.
3. **Implemented as a quarantine boundary:** persist immutable typed claims
   with official player identity and cutoff-safe reads. Start-claim outcome
   scoring is now implemented; availability/minutes/role truth and promotion
   remain.
4. **Implemented for three diverse public pages:** inventory and shadow-capture
   official availability, specialist predicted-lineup and dependent-consensus
   sources through Quark's existing Spider service. A deterministic first
   adapters now resolve FFScout predicted-XI entries by embedded official photo
   code and `Out`/percentage-bearing `Doubts` by unique team-scoped official
   names, writing separately versioned quarantined start and availability
   claims. Bans remain excluded. A separate deterministic adapter retains the
   strAIghtred consensus percentages under the same player/target duplicate
   clusters as FFScout, making dependence explicit. Official-injury extraction,
   quantitative reproduction and named experts remain.
5. **Implemented for start truth:** score pre-deadline categorical and
   probabilistic start claims against exact official Gameweek starts by source
   and fixed lead-time bucket. Availability is not proxied by appearance;
   reliability shrinkage and same-fold no-source versus source-feature
   ablations remain.
6. Add Bayesian component updates and/or predictive-distribution stacking only
   when their registered comparison supports them.
7. Feed the full player distributions into a correlated CPU reference
   simulation, multi-Gameweek optimiser and only then a parity-tested GPU
   backend.
8. **Player-dossier presentation implemented:** expose cutoff-safe source
   claims, source counts, categorical conflicts and linked evidence clusters as
   a quarantined research tape. Reliability learning, material forecast
   movement and the same boundary in AI tools remain.

## Research basis

- [Whitaker et al., *A Bayesian Approach for Determining Player Abilities in
  Football*](https://doi.org/10.1111/rssc.12454) — interpretable hierarchical
  player event abilities and team scoring.
- [Kahn, *A Generative Bayesian Model for Aggregating Experts'
  Probabilities*](https://arxiv.org/abs/1207.4144) — expert bias, calibration,
  accuracy and dependence.
- [Yao et al., *Using Stacking to Average Bayesian Predictive
  Distributions*](https://doi.org/10.1214/17-BA1091) — regularised combination
  of predictive distributions using proper scoring rules.
- [Gneiting and Raftery, *Strictly Proper Scoring Rules, Prediction, and
  Estimation*](https://doi.org/10.1198/016214506000001437) — evaluation that
  rewards honest probabilistic forecasts.
- [O'Brien, Gleeson and O'Sullivan, *Identification of skill in an online
  game: The case of Fantasy Premier
  League*](https://doi.org/10.1371/journal.pone.0246698) — persistent skill,
  long-term planning and observed herding.
- [Bhatt et al., *Who Should Be the Captain This
  Week?*](https://doi.org/10.1609/icwsm.v13i01.3213) — diversity-aware FPL
  crowd evidence for captain choice.
- [Groos, *OpenFPL*](https://arxiv.org/abs/2508.09992) — a useful,
  version-pinned public-data position-ensemble and multi-horizon challenger;
  local reproduction remains required.
