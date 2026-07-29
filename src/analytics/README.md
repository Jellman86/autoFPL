# Analytics boundary

Python owns point-in-time feature generation, forecasting, simulation,
backtesting and optimisation where the scientific ecosystem is useful. It
returns versioned candidate artefacts; the backend validates and records any
artefact promoted into product state.

Install the hash-locked scientific dependencies before running tabular
challengers:

```bash
python3 -m pip install --require-hashes \
  --requirement requirements-analytics.txt
```

## Historical preseason evaluation v1

The preseason bridge evaluates fixed ridge and histogram-tree point
challengers against simple baselines on the pinned historical archive:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_preseason_evaluation \
  --database /path/to/autofpl.db \
  --season 2025-26 \
  --output /path/to/historical-preseason-report.json
```

Model selection uses expanding-origin development folds through Gameweek 30.
The locked Gameweek 31–38 holdout runs only the selected challenger alongside
the simple baselines, while the gate remains tied to the baseline selected on
development data. The report is deterministic, read-only and refuses output
overwrite. A passing result supports a separately labelled preseason
challenger; it cannot promote or silently replace Baseline v0.

The design, limitations and first retained result are recorded in the
[historical preseason specification](../../docs/research/historical-preseason-evaluation-v1.md).

## Multi-season expanding-origin evaluation v1

The multi-season evaluator asks whether carrying exact-code 2024/25 history
improves the fixed 2025/26 ridge and histogram-tree candidates. It compares
multi-season and current-season-only variants on the same late-season target
players:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.multi_season_evaluation \
  --database /path/to/autofpl.db \
  --season 2024-25 \
  --season 2025-26 \
  --output /path/to/multi-season-report.json
```

Legacy defensive metrics remain explicitly missing with observed-count
features, transforms fit inside each fold and cross-season identity is exact
official player code only. The result is a retrospective shadow-candidate
screen, never a promotion decision. See the
[multi-season specification](../../docs/research/multi-season-expanding-origin-v1.md).

## Multi-season preseason shadow forecast v1

The frozen leading two-season tree can emit a current GW1 comparison artifact
without changing served advice:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.multi_season_player_forecast \
  --database /path/to/autofpl.db \
  --output /path/to/two-season-shadow.json
```

Both evaluated archives must have been available by the exact official target
cutoff. The artifact reports raw point means beside Baseline v0, preserves
official availability and exact-code identity gaps, has no calibrated
distribution and cannot influence selection. See the
[shadow forecast specification](../../docs/research/multi-season-preseason-shadow-forecast-v1.md).

The same fixed command is packaged as the non-root
`ghcr.io/jellman86/autofpl-analytics` companion image. Its default invocation
polls the read-only `/data/autofpl.db`, does nothing while the exact shadow is
current, and atomically writes one capture-named JSON artifact to
`/analytics-inbox` when the latest supported target is missing. Once the point
shadow is present, the same worker creates the separately named joint scenario
handoff; it never skips the point prerequisite or combines the two import
states. Mount the database read-only and a separate private inbox writable by
UID `1654`:

```bash
docker run \
  --read-only \
  --volume /private/autofpl:/data:ro \
  --volume autofpl-analytics-inbox:/analytics-inbox \
  ghcr.io/jellman86/autofpl-analytics:dev
```

The worker never writes SQLite, exposes no port and supports only the frozen
2026/27 GW1 target. The `.NET` application remains the only strict product
import boundary: when its bounded inbox poll is enabled, it imports at most one
file of each type per cycle and renames it `.imported` or `.rejected`. The
worker treats either marker as final for that capture, avoiding a regeneration
loop.

## CPU joint-scenario reference v1

The first simulation kernel scores a complete selection over supplied joint
points/participation rows:

```python
from autofpl_analytics.scenario_reference import (
    sample_joint_scenarios,
    score_selection_scenarios,
)
```

Sampling uses an explicit PCG64 seed and selects whole support rows, preserving
their cross-player dependence. Scoring applies the same goalkeeper/outfield
auto-substitution, formation and captaincy order as the deterministic domain.
Candidate comparisons are paired on identical rows. The kernel is
research-only: it does not manufacture a distribution from the current
uncalibrated intervals and cannot influence advice until calibrated,
point-in-time scenario inputs are registered. See the
[CPU reference specification](../../docs/research/cpu-joint-scenario-reference-v1.md).

## Joint player-Gameweek scenario shadow v1

The read-only expanding-origin evaluator compares one whole-Gameweek residual
row per eligible training Gameweek with degenerate tree, player-empirical and
position-empirical distributions:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_joint_scenario_evaluation \
  --database /path/to/autofpl.db \
  --season 2025-26 \
  --output /path/to/historical-joint-scenario.json
```

After that frozen candidate passes its retrospective screen, the current
command binds the exact point, participation and source-archive artifacts and
emits a complete matrix for the supported target:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_joint_scenario_forecast \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 1 \
  --output /path/to/current-joint-scenario.json
```

The current artifact contains integer point and played/not-played rows,
source-Gameweek provenance, player-column identities, availability-adjusted
point means, matrix diagnostics and a content hash. It is prospectively
unscored and cannot influence advice. The application imports it only through
the strict private handoff described in the operations guide. See the
[scenario research note](../../docs/research/joint-player-gameweek-scenario-shadow-v1.md).

## Historical participation evaluation v1

The companion evaluator tests fixed appearance, start, 60-minute and uncapped
total-minutes challengers on the same pinned archive and locked temporal split:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_participation_evaluation \
  --database /path/to/autofpl.db \
  --season 2025-26 \
  --output /path/to/historical-participation-report.json
```

Binary targets use Brier score, log loss and calibration error; minutes uses
MAE and RMSE. Comparator selection occurs only on expanding-origin development
folds before the Gameweek 31–38 holdout opens. The report is deterministic,
read-only and cannot alter the preseason artifact or served advice. See the
[historical participation specification](../../docs/research/historical-participation-evaluation-v1.md).
The first retained result supports provisional appearance, start and
60-minute classifiers, while rejecting the exact-minutes tree in favour of
the player-last baseline.

## Historical participation coherence diagnostic v1

The coherence evaluator applies one fixed equal-weight Euclidean projection to
the independently fitted appearance, start and 60-minute probabilities on the
same historical folds:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_participation_coherence_evaluation \
  --database /path/to/autofpl.db \
  --season 2025-26 \
  --output /path/to/historical-participation-coherence.json
```

It requires appearance probability to be at least start and 60-minute
probability, leaves already coherent vectors unchanged and compares raw with
projected Brier, log-loss, calibration, fold and position results. Because the
underlying holdout had already been opened before this correction was
motivated, the output is always a secondary diagnostic rather than a new
promotion test and can never authorize product import. See the
[historical participation coherence specification](../../docs/research/historical-participation-coherence-evaluation-v1.md).
The first real run removed all 55 historical nesting violations but failed the
fixed predictive-quality gate: appearance Brier and log loss regressed, while
60-minute Brier was non-worse in only three of eight folds. The projection is
therefore rejected for the current artifact.

## Historical joint participation evaluation v1

The next evaluator fits one five-state classifier and derives coherent
appearance, start and 60-minute marginals from its probability distribution:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_joint_participation_evaluation \
  --database /path/to/autofpl.db \
  --raw-report ../../docs/research/results/historical-participation-coherence-2025-26-v1.json \
  --season 2025-26 \
  --output /path/to/historical-joint-participation.json
```

It verifies and reuses the retained raw comparator identities instead of
refitting 24 unchanged classifiers. Because this model was designed after the
historical holdout was opened, its result is an exploratory candidate screen
only; the exact model must face genuinely new 2026/27 folds before product use.
See the
[historical joint participation specification](../../docs/research/historical-joint-participation-evaluation-v1.md).
The first real screen produced zero coherence violations and improved start
and 60-minute Brier/log loss in seven of eight folds, but appearance regressed
on aggregate Brier, log loss and the fold gate. The full joint model is
therefore rejected; a raw-appearance plus conditional-child factorization is
next.

## Historical conditional participation evaluation v1

The conditional challenger preserves the fixed raw appearance classifier,
fits start and 60-minute classifiers only on historical appearance-positive
rows and multiplies each conditional probability by raw appearance. This
guarantees coherent child marginals without sacrificing appearance:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_conditional_participation_evaluation \
  --database /path/to/autofpl.db \
  --raw-report ../../docs/research/results/historical-participation-coherence-2025-26-v1.json \
  --output /path/to/historical-conditional-participation.json
```

The historical screen is exploratory because its model family was chosen
after the holdout was opened. See the
[historical conditional participation specification](../../docs/research/historical-conditional-participation-evaluation-v1.md).
The first real screen preserved appearance and improved aggregate start and
60-minute Brier/log loss, but start was non-worse in only four of eight folds.
The fixed gate therefore rejects the combined candidate. Raw, joint and
factorized variants are now frozen for prospective 2026/27 comparison.

## Current official availability ceiling v1

The current participation artifact carries a prospective comparison variant
that treats official chance as an appearance ceiling, then re-derives start
and 60-minute marginals from the frozen conditional rates. It does not
multiply two unproven independent probabilities, does not alter the raw
fields and cannot influence advice before real 2026/27 scoring. Unknown or
inconsistent official state fails closed. See the
[availability ceiling specification](../../docs/research/current-official-availability-ceiling-v1.md).

## Provisional preseason participation forecast v1

The current-player generator fits only the three supported classifiers and
retains baseline-labelled exact minutes:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.preseason_participation_forecast \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 1 \
  --output /path/to/gw1-preseason-participation.json
```

It requires the exact retained evaluation identities, joins prior history by
stable official code and reports raw event-nesting incoherence. Artifact v1.2
also retains coherent-factorized and official-ceiling-factorized comparison
variants without replacing the raw fields. The artifact is deterministic,
read-only, refuses overwrite, carries a fail-closed product-import readiness
decision and cannot influence advice. See the
[provisional participation forecast specification](../../docs/research/preseason-participation-forecast-v1.md).
Its first v1.1 current-player run is retained there. Product import remains
blocked by unsupported coherence candidates, unscored prospective
availability evidence and promoted/new-player coverage gaps.
The complete first v1.2 per-player artifact is retained before outcomes; its
official ceiling changed 32 of 557 players while both factorized variants
remained coherent for every player.

## Provisional preseason player forecast v1

After the locked holdout supports the fixed candidate, the current-player
generator fits that unchanged histogram-tree configuration to every archived
2025/26 player-Gameweek sample and compares its GW1 point means with the exact
Baseline v0 player artifact:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.preseason_player_forecast \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 1 \
  --output /path/to/gw1-preseason-challenger.json
```

The command requires the exact source revision and content hashes that passed
the retained evaluation. Current players join prior history only by stable
official code; missing identities remain explicit. Current official
availability accompanies every prediction but does not silently alter the
fitted point mean. The artifact is deterministic, refuses overwrite and cannot
influence served advice.

See the
[provisional preseason forecast specification](../../docs/research/preseason-player-forecast-v1.md).

## Baseline evaluation v4

The first local command reads the authoritative SQLite database in read-only
mode and evaluates deterministic total-points and expected-minutes baselines
plus smoothed probability-of-60-minutes and empirical point/minutes
distribution baselines with expanding Gameweek origins:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --output /path/to/baseline-report.json
```

It exits `2` with a machine-readable `insufficient-data` report until at least
one target Gameweek has the configured number of prior complete outcomes that
were available by that target deadline. Existing output files are never
overwritten. Reports remain explicitly exploratory and cannot populate player
cards or influence advice.

The target, temporal split, metrics, baselines and current limitations are
recorded in the
[baseline evaluation specification](../../docs/research/baseline-evaluation-v4.md).

## Temporal feature table v2

The read-only feature command materialises player and team information that was
actually available with one selected official pre-deadline capture:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.feature_table \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 2 \
  --output /path/to/features-2026-27-gw2.json
```

It includes player outcome lags, rolling 1/3/5-Gameweek statistics,
exponentially weighted form, explicit missingness, target fixtures and rest
gaps, plus rolling team attack/defence/result form split by venue. Official
xG/xA/xGC, ICT/BPS and defensive outcomes retain null legacy values and expose
per-metric sample counts instead of inventing zeroes. Earlier outcome
corrections are admitted only when they were available by the selected replay's
actual capture time. Output is deterministic, hash-identified, exploratory and
never overwrites an existing file.

The exact boundary and limitations are recorded in the
[temporal feature table specification](../../docs/research/temporal-feature-table-v2.md).

## Cross-season player state v1

The read-only cross-season command joins the pinned 2025/26 archive to one
current official target through stable player codes:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.cross_season_player_state \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 1 \
  --output /path/to/cross-season-state.json
```

Every current player remains present. Missing prior identity is explicit;
prior-season 1/3/5/10-Gameweek and EWMA performance/durability summaries never
override current official health. The artifact is deterministic, read-only,
hash-identified and explicitly does not influence a forecast. See the
[cross-season state specification](../../docs/research/cross-season-player-state-v1.md).

The prior-competition coverage audit turns the remaining current-player
history gaps into a source-integration target:

```text
python -m autofpl_analytics.prior_competition_coverage \
  --forecast /path/to/preseason-participation-v1.2.json \
  --prior-competition-club "Coventry City" \
  --prior-competition-club "Hull City" \
  --prior-competition-club "Ipswich Town" \
  --output /path/to/prior-competition-coverage.json
```

It verifies the complete frozen source artifact, preserves official stable
codes, distinguishes prior-competition squads from other new/transferred
players and emits the exact bounded capture fields needed from Quark's existing
research services. Names are never an automatic identity fallback and the
audit cannot influence a forecast. See the
[coverage specification](../../docs/research/prior-competition-player-history-coverage-v1.md).

## Cross-season feature ablation v1

The promotion-gate command compares unchanged ridge/tree models with
cross-season-state variants on identical source-complete folds:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.cross_season_ablation \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/cross-season-ablation.json
```

Missing prior player identity remains explicit; a prior-season archive missing
at any fold cutoff excludes that fold from every variant. Archived final health
and source `xP` are not candidate features. The report remains unpromoted and
returns exit code `2` until a source-complete fold exists. See the
[cross-season ablation specification](../../docs/research/cross-season-feature-ablation-v1.md).

## Official underlying feature ablation v1

The official feature ablation compares the unchanged ridge/tree contract with
fixed xG/xA/xGC, ICT/BPS and defensive-contribution additions on identical
expanding-origin folds:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.official_underlying_ablation \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/official-underlying-ablation.json
```

Rolling-three and exponentially weighted means carry observed sample counts.
Ridge preprocessing and tree feature removal remain training-fold local. The
report is deterministic, read-only and unpromoted, and returns exit code `2`
until an eligible official fold exists.

See the
[official underlying feature ablation specification](../../docs/research/official-underlying-feature-ablation-v1.md).

## Temporal ridge challenger v1

The first feature-consuming challenger fits a fixed regularised linear model
inside honest expanding Gameweek origins:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.temporal_ridge \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/temporal-ridge-report.json
```

All median imputation, missing indicators, standardisation and fitting are
training-fold local. The deterministic report compares ridge with four simple
point baselines, remains explicitly exploratory, and exits `2` when too few
complete historical feature/outcome pairs exist.

The fixed feature and evaluation design are recorded in the
[temporal ridge specification](../../docs/research/temporal-ridge-v1.md).

## Temporal histogram-tree challenger v1

The nonlinear comparison reuses the ridge evaluator's exact folds, target
players and incumbents:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.temporal_tree \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/temporal-tabular-report.json
```

Its scikit-learn implementation and transitive scientific dependencies are
version- and hash-locked. Missing branches and unsplittable feature removal are
learned inside each training fold. The model remains exploratory and fixed
before real evaluation.

See the
[temporal tree specification](../../docs/research/temporal-tree-v1.md).

## Cutoff-safe FPL Form features v1

The first scraped-forecast modelling bridge joins a retained FPL Form capture
only when it was available by the official feature replay's exact capture
time:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.fpl_form_feature_table \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 4 \
  --output /path/to/fpl-form-features-gw4.json
```

It validates direct player and fixture identities again, aggregates
fixture-level conditional and appearance-adjusted values, preserves missing
players/probabilities, and remains an unpromoted feature artefact. See the
[FPL Form temporal feature specification](../../docs/research/fpl-form-temporal-feature-v1.md).

## FPL Form model-feature ablation v1

The source ablation recomputes official-only and FPL-Form-enhanced ridge/tree
models on identical, source-complete expanding-origin folds:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.fpl_form_ablation \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/fpl-form-ablation.json
```

Every training and target Gameweek must have a forecast captured by its own
official feature cutoff. Missing source history excludes the fold from every
variant; missing player predictions and appearance probabilities remain
explicit model inputs. The report is read-only, deterministic, unpromoted and
returns exit code `2` until at least one source-complete fold exists. See the
[FPL Form feature ablation specification](../../docs/research/fpl-form-feature-ablation-v1.md).
