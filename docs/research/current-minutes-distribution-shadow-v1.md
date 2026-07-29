# Current minutes-distribution shadow v1

## Purpose

This generator freezes the retained appearance-hurdle conditional empirical
minutes distribution for the exact 2026/27 Gameweek 1 official snapshot. It is
a prospective component shadow, not a served expected-minutes value, complete
points forecast or squad recommendation.

## Inputs and variants

The artifact requires the exact current participation artifact and pinned
2025/26 archive. Stable official player code joins current players to their
positive-minutes support; players without a prior match use an explicit
same-position fallback.

Each player receives two immutable variants:

1. `rawSupported` uses the appearance probability whose model family passed
   the historical holdout; and
2. `officialCeilingProspective` applies the separately registered official
   availability ceiling before assigning probability mass.

Both expose zero-minutes probability, expected minutes, p10/median/p90 and the
complete finite weighted support. Neither influences advice. The official
variant was not available in the historical archive and must remain separate
until genuinely new outcomes score it.

## Reproduction

```shell
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_minutes_distribution_forecast \
  --database /path/to/autofpl.db \
  --output /path/to/current-minutes-distribution.json
```

The generator opens SQLite read-only, binds the retained historical screen and
source participation run identities, is deterministic and refuses output
overwrite.

## Next boundary

This artifact is not yet imported into the application. The next forecast
slice combines the frozen minutes distribution with conditional player event
rates and shared match state to produce a complete FPL-points distribution.
That complete component set, rather than a minutes mean alone, can then feed
the registered 3-, 6- and 8-Gameweek opening-squad scenarios.
