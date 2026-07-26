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

## Temporal feature table v1

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
gaps, plus rolling team attack/defence/result form split by venue. Earlier
outcome corrections are admitted only when they were available by the selected
replay's actual capture time. Output is deterministic, hash-identified,
exploratory and never overwrites an existing file.

The exact boundary and limitations are recorded in the
[temporal feature table specification](../../docs/research/temporal-feature-table-v1.md).

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
