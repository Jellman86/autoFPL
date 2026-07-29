# Fixture-strength expanding-origin evaluation v1

## Question

Do cutoff-correct team and opponent form features improve the retained
two-season histogram-tree forecast enough to enter the opening-squad forecast
as a prospective shadow challenger?

The experiment is deliberately narrow. It compares the same model class,
training rows, target players and chronological folds with and without two
fixture-strength features. It is a retrospective ablation over already-seen
2025/26 outcomes and cannot promote a model.

## Scientific basis

Dixon and Coles modelled football scores with dynamic team strengths, Poisson
regression and explicit home advantage. Their result motivates treating the
opponent and venue path as predictive state rather than assuming a player's
recent output transfers unchanged to every fixture
([Dixon and Coles, 1997](https://doi.org/10.1111/1467-9876.00065)).

Forecast distributions will subsequently be judged by calibration and
sharpness using proper scoring rules, following
[Gneiting, Balabdaoui and Raftery (2007)](https://doi.org/10.1111/j.1467-9868.2007.00587.x)
and
[Gneiting and Raftery (2007)](https://doi.org/10.1198/016214506000001437).
This slice evaluates point means first because it is an input-feature ablation,
not yet a distributional promotion experiment.

Recent FPL-specific operations research also supports separating uncertain
performance forecasts from exact constrained squad selection, then evaluating
the combined decision out of sample
([Venter and Van Vuuren, 2024](https://doi.org/10.5784/40-1-753)).
autoFPL therefore improves and validates forecast inputs before extending its
exact squad optimizer to multiple Gameweeks.

## Registered comparison

The incumbent is `multi-season-histogram-tree` with its frozen feature and
hyperparameter contract. The challenger adds:

- the target player's team's mean FPL points for the player's position over
  the previous three Gameweeks; and
- the target opponent's mean FPL points allowed to that position over the
  previous three Gameweeks, averaged across target fixtures.

Both values use only numerically earlier Gameweeks. They reset at the season
boundary because historical team IDs and promoted-club membership are not
assumed stable. Missing history remains null and is handled by the tree's
fold-local missing branch. No bookmaker odds, final health state, future result
or end-of-season ranking enters a feature.

Gameweeks 31–38 of 2025/26 are evaluated on expanding origins. The challenger
passes the screen only if it:

1. improves aggregate MAE by at least 1%;
2. does not regress aggregate RMSE;
3. wins a strict majority of Gameweek folds; and
4. does not regress any position's MAE by more than 5%.

Passing retains the exact feature contract for a 2026/27 prospective shadow.
It does not alter served advice. Failing discards the feature pair.

## Reproduction

```shell
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.fixture_strength_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/fixture-strength-report.json
```

The evaluator opens SQLite read-only, emits a deterministic identity hash and
refuses to overwrite an existing report. Tests verify identical target cohorts
and that modifying a later outcome cannot change an earlier feature row.

## Next boundary

If retained, the same cutoff-correct feature contract must be mapped to current
official team and fixture identities and scored prospectively. A later
distributional experiment will combine point, appearance and minutes
components and use CRPS plus calibration diagnostics. Only then can the
registered 3/6/8-Gameweek optimizers compare opening-squad policies.
