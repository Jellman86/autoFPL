# FPL Form temporal feature v1

## Status and command

`fpl-form-temporal-feature-v1` is the first executable bridge from retained
public-page forecasts into the Python modelling boundary. It is exploratory,
does not alter the official feature table or advice, and is not evidence that
the source improves accuracy.

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.fpl_form_feature_table \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 4 \
  --output /path/to/fpl-form-features-gw4.json
```

The read-only command refuses output overwrite. Exit code `2` means that no
forecast was available by the selected official replay's exact capture time;
this is a normal pre-source state rather than an error.

## Temporal boundary

The command first builds `official-temporal-v2`, then selects the latest FPL
Form capture satisfying:

```text
forecast.availableAtUtc <= officialFeature.decisionCutoffUtc
```

Using the replay capture time rather than merely the later Gameweek deadline
prevents a forecast retrieved after the rest of the decision row from entering
that row. Later source corrections never rewrite an earlier feature artefact.

## Identity and interpretation

This first Python join accepts only direct source player and fixture IDs. Every
row must also agree with the official replay on normalized player name, club,
position, team participation and unambiguous Europe/London kickoff. Any
disagreement fails the whole artefact closed. The broader .NET identity report
can document unique fallback matches, but Python will not duplicate that
fallback algorithm; a later persisted resolution boundary can admit them.

Fixture conditional predicted points are summed to one player-Gameweek feature.
The separately named appearance-adjusted feature is emitted only when every
fixture prediction for that player contains a probability. Players absent from
the public forecast remain explicit nulls with `hasForecast: false`.

## Next evaluation

The next command will compare official-only ridge/tree folds with the same
models plus these two features. Only folds with source evidence available at
every training and target cutoff can support that ablation. No scraped value
may influence product forecasts until the out-of-time comparison beats the
retained incumbent under a registered promotion rule.
