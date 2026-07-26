# Baseline evaluation v4

## Status

This is an executable exploratory baseline specification, not a promoted model
or a performance claim. Deterministic fixtures test chronology and mathematics;
they are not research evidence. Real results remain unavailable until autoFPL
has multiple complete official replay/outcome pairs.

## Questions and targets

For each player present in the latest official capture available before a
Gameweek deadline:

1. predict the player's final official integer FPL points for that Gameweek;
2. predict the probability that the player's final official Gameweek minutes
   are at least 60; and
3. predict the player's final official Gameweek-total minutes; and
4. produce empirical predictive distributions for Gameweek-total points and
   minutes.

The population contains only players whose pre-deadline replay identity matches
an official final outcome. Added post-deadline players may exist in the outcome
capture but do not enter that earlier prediction population.

Both minutes targets use the official Gameweek-total minutes field. In a double
Gameweek, reaching 60 minutes across either or both fixtures is therefore a
positive outcome and expected minutes may exceed 90. This baseline does not yet
model individual fixture appearances.

## Point-in-time split

The evaluator uses expanding origins ordered by season and Gameweek. It never
randomly splits player rows.

For target Gameweek `t`:

1. select the latest immutable reference capture with
   `capture.availableAtUtc <= t.deadlineUtc`;
2. use the latest final target outcome as the eventual observed target;
3. admit only earlier-Gameweek outcome revisions with
   `outcome.availableAtUtc <= t.deadlineUtc` into training; and
4. select each earlier Gameweek's latest revision satisfying that cutoff.

This means a later correction may change the eventual target used to score its
own Gameweek, but it cannot enter a training fold whose decision deadline
preceded the correction.

Player IDs are used only within one season. Cross-season evaluation remains
disabled until explicit provider-code identity matching is implemented and
tested.

## Point baselines

The point baselines produce one deterministic prediction per eligible player:

- `zero-points` — always predicts zero;
- `position-expanding-mean` — mean prior points for the player's position,
  falling back to the training-wide mean;
- `player-expanding-mean` — mean prior points for the player, falling back to
  the position mean;
- `player-last-points` — the player's most recent eligible prior points,
  falling back to the position mean; and
- `official-running-mean` — cumulative points visible in the pre-deadline
  official capture divided by the number of earlier Gameweeks.

These are deliberately simple. They establish leakage-safe incumbents for
later minutes, team/opponent-strength, market, tree, hierarchical and ensemble
challengers.

## Probability baselines

The probability baselines predict `P(official Gameweek minutes >= 60)`:

- `global-played60-rate` — add-one-smoothed rate over all eligible prior
  player/Gameweek outcomes;
- `position-played60-rate` — add-one-smoothed prior rate for the player's
  position, falling back to the global rate;
- `player-played60-rate` — add-one-smoothed prior rate for the player, falling
  back to the position rate; and
- `official-start-rate` — add-one-smoothed cumulative official starts visible
  before the deadline divided by elapsed prior Gameweeks, capped at one for
  double-Gameweek histories.

Add-one smoothing is the posterior mean under a uniform Beta(1,1) prior. It
keeps small-sample forecasts away from unjustified zero and one probabilities.
These are audit-friendly incumbents, not claims that starting and reaching 60
minutes are equivalent.

## Expected-minutes baselines

The expected-minutes baselines produce one unconditional Gameweek-total
minutes prediction per eligible player:

- `minutes-zero` — always predicts zero;
- `minutes-global-expanding-mean` — mean prior minutes across all players;
- `minutes-position-expanding-mean` — mean prior minutes for the player's
  position, falling back to the global mean;
- `minutes-player-expanding-mean` — mean prior minutes for the player, falling
  back to the position mean;
- `minutes-player-last` — the player's most recent eligible prior minutes,
  falling back to the position mean; and
- `minutes-official-running-mean` — cumulative minutes visible in the
  pre-deadline official capture divided by elapsed prior Gameweeks.

These forecasts intentionally include zero-minute outcomes. A later
availability model may also report conditional minutes, but simulation needs
the unconditional expectation and its uncertainty rather than silently
assuming a player is available.

## Distribution baselines

Points and minutes each have four distributional incumbents:

- a degenerate distribution at zero;
- the empirical distribution of all eligible prior player/Gameweek outcomes;
- the empirical distribution for the player's position, falling back to the
  global distribution; and
- the empirical distribution for the player, falling back to the position
  distribution.

Every empirical sample is drawn from the correction-safe training window for
that target deadline. The distributions are discrete and intentionally
dependency-free. They preserve multimodality and double-Gameweek totals without
assuming a Gaussian shape, but early player histories are necessarily
degenerate or very sparse.

## Metrics and slices

For points and expected minutes, the command reports mean absolute error, root
mean squared error and signed mean error over all eligible player/Gameweek
predictions, plus the same metrics by position. Lower MAE/RMSE is better;
signed mean error exposes systematic over- or under-prediction.

For the binary probability target it reports:

- Brier score and natural-log loss, both proper scores where lower is better;
- observed event rate and mean predicted probability;
- expected calibration error using fixed-width descriptive bins;
- non-empty calibration bins with count, mean forecast, observed rate and
  absolute gap; and
- the same aggregate probability metrics by position.

Calibration bins are descriptive, especially in the tiny early sample. They
must not be read as proof of calibration.

For the point and minutes distributions, the command reports:

- mean continuous ranked probability score (CRPS), where lower is better;
- median absolute error and mean pinball loss across fixed quantiles;
- observed quantile-at-or-below rates and absolute calibration gaps;
- observed coverage and mean width for central 50%, 80% and 95% intervals;
- mean empirical sample count, exposing sparse distributions; and
- the same diagnostics by position.

Quantiles use deterministic linear interpolation. Coverage and calibration
diagnostics are descriptive until enough real rolling folds exist. No fitted
calibrator is applied yet, and no fixture result is evidence of calibration.

## Reproducibility and output

Run:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --output /path/to/baseline-report.json
```

The SQLite connection is query-only. The report records:

- evaluator and schema versions;
- exact configuration and availability rule;
- replay/outcome IDs, timestamps and source hashes for every target and
  training fold;
- a canonical data-identity hash;
- point, expected-minutes, binary-probability and distributional metrics,
  calibration diagnostics and position slices; and
- a canonical run-identity hash.

The command refuses to overwrite an existing report. With no complete pair or
no eligible rolling fold it writes `status: insufficient-data` and exits `2`.
Schema mismatch, incomplete coverage and invalid configuration fail closed.

## Promotion boundary

No final holdout or promotion threshold is registered yet because the live
2026/27 sample contains no completed Gameweeks. Before any challenger influences
advice, register the training window, final holdout, minimum sample, tuning
budget, decision-utility evaluation and promotion rule. Add calibrated minutes
and points challengers only by fitting any calibrator inside each training
window and comparing it with these empirical incumbents. Player-card forecasts
remain synthetic until that later gate passes.
