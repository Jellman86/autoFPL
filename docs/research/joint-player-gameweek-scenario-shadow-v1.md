# Joint player-Gameweek scenario shadow v1

## Decision

autoFPL now has a frozen, point-in-time candidate for generating joint
player-Gameweek outcome rows. It passed a retrospective 2025/26 screen and has
therefore been used to create a 2026/27 Gameweek 1 prospective shadow. Neither
result is promoted and neither can influence squad advice.

The model is deliberately a small empirical bridge between the existing player
forecasts and the exact selection scorer. It establishes measurable,
reproducible distribution quality before match-state simulation or GPU work is
considered.

## Historical method

For each target in 2025/26 Gameweeks 31–38, the evaluator uses only numerically
earlier settled Gameweeks. Fold-local histogram trees estimate unconditional
point means and the fixed appearance classifier estimates appearance
probabilities.

One scenario row is created for every eligible training Gameweek:

1. the same source Gameweek is retained across every target player, preserving
   common temporal conditions in the empirical row;
2. a player's stable-code outcome supplies its donor when present;
3. a deterministic same-position donor is used when that code has no row;
4. appearance counts are quantised over the available rows;
5. played point values are shifted to the fold-local unconditional point mean,
   rounded to integers and bounded to the registered range; and
6. non-playing players always receive zero points.

The generator never reads a later target when constructing an earlier fold.
Its SQLite connection is read-only, output is deterministic and the CLI refuses
to overwrite an existing report.

## Registered comparison

The candidate is compared on identical 6,252 player-Gameweek outcomes with:

- a degenerate distribution at the histogram-tree point mean;
- the player's expanding empirical point distribution; and
- the position's expanding empirical point distribution.

Continuous ranked probability score (CRPS) is the primary distribution metric.
The screen requires at least 1% aggregate CRPS improvement over the strongest
empirical baseline, a majority of fold wins and no position CRPS regression
above 5%. Appearance probabilities are also checked with Brier score, log loss
and calibration error.

## First retained result

The candidate achieved mean CRPS 0.639806 versus 0.689157 for the selected
player-empirical baseline: a 7.1611% improvement. It won all eight folds, and
CRPS improved for goalkeeper, defender, midfielder and forward slices. The
quantised appearance rows achieved Brier score 0.084686 versus 0.084772 for the
unquantised classifier and 0.111694 for the player-empirical comparator.

The screen passes, but it is retrospective: the evaluator was written after
the 2025/26 outcomes were already visible. This result can freeze a
prospective challenger, not promote it. The compact machine-readable result is
[`historical-joint-scenario-evaluation-2025-26-v1.json`](results/historical-joint-scenario-evaluation-2025-26-v1.json).

## Current shadow

The supported current generator binds:

- official capture 16 and its decision cutoff;
- the exact 2026/27 GW1 two-season point forecast;
- the exact provisional participation forecast and official availability
  ceiling;
- the exact retained 2025/26 source archive; and
- the passing historical screen identities.

The current worker binds the retained screen's exact evaluator, data, run,
metric and fold identities. It does not recompute the already-opened
retrospective comparison on every prospective capture; the standalone
historical evaluator remains the deterministic reproduction command. This
keeps current generation bounded without weakening the import identity.
The worker also reads the already validated exact point artifact from SQLite
and fits participation in an isolated child process. That child exits before
matrix construction, preventing independent model-fitting heaps from
accumulating in the long-lived polling process. OpenMP and BLAS thread counts
are bounded to the worker's two-CPU allocation; the retained matrix remains
byte-for-byte identical.

The point forecast is unconditional and deliberately does not model current
availability. For coherence, the shadow multiplies that mean by the ratio of
official-ceiling appearance probability to raw appearance probability. The
pre- and post-adjustment values remain in every player record. This
availability rule is itself prospectively unscored and cannot be treated as a
promoted calibration.

The resulting artifact contains 38 complete rows for 560 current players.
Every row retains its source Gameweek, integer point vector and appearance
vector. A separate content hash covers the exact player columns, source
Gameweeks and both matrices. The compact retained record is
[`current-joint-scenario-shadow-2026-27-gw1-v1.json`](results/current-joint-scenario-shadow-2026-27-gw1-v1.json).

## Limits and next action

Whole-Gameweek rows preserve observed common shocks, but do not explicitly
simulate fixtures, team scorelines, bonus allocation or substitution events.
Only 38 source rows are available, and missing stable-code histories use
counted same-position donors. Integer and appearance quantisation leave a
small finite-row difference between requested and generated marginal means.

The bounded private application handoff is now implemented. Migration 26
revalidates the exact official target, source archive, point artifact, player
columns, matrix dimensions, domain bounds and canonical matrix hash before an
immutable insert. Separate latest/readiness routes remain read-only, and the
network-isolated worker cannot write SQLite.

The next slice can feed that persisted matrix into the existing CPU selection
scorer for model and user-owned candidates. Promotion still requires scoring
the frozen artifact against genuinely new 2026/27 outcomes. GPU implementation
remains conditional on exact CPU parity and a measured representative
workload.
