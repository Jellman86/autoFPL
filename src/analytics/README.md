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
stable official code, keeps current official availability separate and reports
any event-nesting incoherence rather than silently clipping independently
evaluated probabilities. The artifact is deterministic, read-only, refuses
overwrite and cannot influence advice. See the
[provisional participation forecast specification](../../docs/research/preseason-participation-forecast-v1.md).

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
