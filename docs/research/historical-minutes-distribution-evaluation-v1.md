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

## Result

The Quark run at application revision
`fd87ea4f79ecf902fc401176c53b3bdc15e826c3` completed all eight folds and
scored 6,252 player-Gameweeks:

| Model | Mean CRPS | Central 80% coverage | Mean width |
|---|---:|---:|---:|
| Appearance-hurdle conditional empirical | **8.666169** | 0.936020 | 34.598049 |
| Player empirical | 10.469246 | 0.929623 | 37.356206 |
| Player-last point mass | 12.205374 | 0.689699 | 0.000000 |
| Position empirical | 18.196291 | 0.986564 | 90.000000 |

The candidate improved CRPS by 17.2226% against player empirical, 28.9971%
against player-last and 52.3740% against position empirical. It won all eight
folds against every reference and improved goalkeeper, defender, midfielder
and forward CRPS in every comparison. The fixed screen therefore passes.

The exact specification is retained for a current prospective
minutes-distribution shadow. It remains unpromoted and cannot alter served
expected minutes, point forecasts or squad advice until genuinely new 2026/27
outcomes score the frozen artifact.

Coverage above the nominal 80% level shows that the retained intervals are
conservative. This is visible diagnostic evidence to carry into prospective
scoring; the already-opened folds must not be used to narrow support or tune
the appearance probability.

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
