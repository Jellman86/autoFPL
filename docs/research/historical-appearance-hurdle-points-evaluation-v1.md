# Historical appearance-hurdle points evaluation v1

## Question

Does explicitly separating the probability that a player appears from their
expected points conditional on appearing improve unconditional FPL point
forecasts?

For a player-Gameweek, official points are zero when the player does not
appear. The law of total expectation therefore gives:

```text
E[points] = P(appearance) × E[points | appearance]
```

The retained direct point tree can learn this relationship implicitly, but a
single regression must simultaneously model zero inflation, lineup chance,
minutes and scoring output. The hurdle candidate makes the participation
mechanism explicit.

## Fixed models

The incumbent is the unchanged two-season `multi-season-histogram-tree`.

The challenger fits two models on every expanding-origin fold:

- the fixed histogram-gradient-boosting classifier estimates Gameweek
  appearance from all chronologically available player rows; and
- the unchanged histogram-gradient-boosting regressor estimates total points
  using only training rows where minutes were greater than zero.

Both components use the incumbent's exact two-season feature contract,
hyperparameters and stable official player-code identities. Their target
predictions are multiplied without using target minutes or points.

## Fixed evaluation and gate

Gameweeks 31–38 of 2025/26 use the same immutable target archive, player
cohorts and expanding training origins as the retained point model. The report
includes unconditional point MAE, RMSE and mean error, position slices,
per-Gameweek comparisons, appearance Brier/log loss/calibration, and
conditional-point error on target players who appeared.

The hurdle candidate is retained only if all four point-forecast gates pass:

1. aggregate MAE improves by at least 1%;
2. aggregate RMSE does not regress;
3. the challenger wins a strict majority of eight Gameweek folds; and
4. no position's MAE regresses by more than 5%.

The component diagnostics explain failure modes but do not override the
unconditional point gate. These target outcomes were already opened by prior
experiments, so a pass can retain only a prospective current shadow and cannot
promote advice.

## Result

The hurdle model passed every fixed gate:

| Metric | Direct point tree | Appearance hurdle | Change |
| --- | ---: | ---: | ---: |
| MAE | 0.963833 | 0.945900 | -1.86% |
| RMSE | 1.909170 | 1.903293 | -0.005877 |
| Mean error | 0.025308 | 0.012287 | -0.013021 |
| Gameweek fold wins | — | 8/8 | — |

Every position improved:

| Position | Direct MAE | Hurdle MAE | Change |
| --- | ---: | ---: | ---: |
| Goalkeeper | 0.601694 | 0.574642 | -4.50% |
| Defender | 1.089739 | 1.074662 | -1.38% |
| Midfielder | 0.924676 | 0.905557 | -2.07% |
| Forward | 1.132993 | 1.121025 | -1.06% |

The appearance component scored Brier `0.084587`, log loss `0.277726` and
ten-bin calibration error `0.008710`. Its mean probability was `0.365305`
against an observed appearance rate of `0.365323`. The conditional point
component's appeared-player cohort contained 2,284 rows.

The decision is `retain-appearance-hurdle-prospective-shadow`. It is the first
candidate in the current sequence to clear the complete point-mean gate. The
next implementation step is a separately versioned current Gameweek 1–8
shadow; this historical evaluator does not itself replace the existing point
artifact or opening squad.

The result data identity is
`1ef395d722844b1833fb059d606606e0bf1f1d42732377474d671f1a3f17b16e`
and run identity is
`852b3728150cba9ad3bdb46ba24d66e9ac77262b2cf4dc9aae38f0df5cdc7e71`.
An independent second complete run was byte-identical.

## Boundaries

The factorization models at least one Gameweek appearance, not a complete
fixture-level minutes and event process. The archive lacks historical
decision-time injury and specialist lineup state. A retained point mean would
still require current distribution construction, opening-squad optimisation
and prospective outcome scoring.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_appearance_hurdle_points_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-appearance-hurdle-points.json
```

The evaluator is deterministic, read-only and refuses output overwrite.
