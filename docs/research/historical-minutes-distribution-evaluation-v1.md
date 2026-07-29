# Historical minutes-distribution evaluation v1

## Question and boundary

Does explicitly separating appearance risk from minutes conditional on
appearing produce a better player-Gameweek minutes distribution than empirical
and last-value references?

The 2025/26 Gameweek 31–38 folds are already open. This is an exploratory
candidate screen that may freeze a prospective 2026/27 shadow but cannot
promote a model or alter advice.

## Fixed distributions

The candidate uses the unchanged supported appearance classifier. Its
conditional support is the player's prior positive-minutes outcomes, with a
same-position positive-minutes fallback when the player has none. It assigns
probability `1 - P(appearance)` to zero and spreads `P(appearance)` equally
over the conditional support.

Three references use the identical training rows and target players:

1. the player's empirical minutes distribution, including zeros;
2. a point mass at the player's last minutes outcome; and
3. the position's empirical minutes distribution.

Player references fall back to position history only when the player has no
prior row. Every distribution is scored exactly as weighted finite support;
there is no Monte Carlo or probability quantisation in the evaluator.

## Registered screen

CRPS is the primary proper distributional score. Central 80% interval coverage,
width and distribution-mean error remain diagnostics. Against every reference
independently, the candidate must improve aggregate CRPS by at least 1%, win a
strict majority of folds and avoid position CRPS regression above 5%.

Passing retains the exact model only for prospective shadow generation.
Promotion and product import remain prohibited.

## Reproduction

```shell
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_minutes_distribution_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-minutes-distribution.json
```

The evaluator is deterministic, opens SQLite read-only and refuses output
overwrite. Tests verify weighted CRPS against exact small references and prove
that changing a future minutes outcome cannot alter an earlier fold.

## Next boundary

A retained minutes distribution still needs to combine with conditional player
event rates and shared match state to form the complete FPL-points
distribution. Only that complete distribution can feed the registered 3-, 6-
and 8-Gameweek opening-squad policy comparison.
