# Temporal ridge challenger v1

## Status and purpose

`temporal-ridge-v1` is an executable exploratory total-points challenger. It
tests whether the implemented point-in-time player, fixture and team features
add out-of-time signal beyond auditable incumbents. It is not a promoted
forecast, does not populate advice or player cards, and makes no performance
claim before real folds exist.

## Command

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.temporal_ridge \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/temporal-ridge-report.json
```

The command reads SQLite in query-only mode, emits deterministic data and run
identities, and refuses to overwrite a report. It exits `2` with an
`insufficient-data` report until at least one expanding-origin fold has three
earlier complete feature/outcome pairs.

## Registered exploratory design

- Target: official per-player total points for one Gameweek.
- Split: expanding Gameweek origins within one season; no random split or
  cross-season identity matching.
- Challenger: linear ridge regression with a fixed penalty of `10.0`.
- Comparators: zero points, expanding position mean, player last points and the
  pre-deadline official running mean.
- Metrics: MAE, RMSE and mean error overall and by position.
- Tuning: none. The penalty and feature set are fixed before real evaluation.
- Promotion: prohibited by this command. A later promotion experiment must
  register its validation/final windows and decision-utility rule before
  opening a final holdout.

## Temporal and preprocessing boundary

For target Gameweek `t`, features come from its latest retained official replay
available before its deadline. Training labels are earlier Gameweek outcome
corrections that were already available when that exact target replay was
captured. Each training row's features are independently reconstructed from
that earlier Gameweek's own latest pre-deadline replay. Missing historical
replays are excluded rather than reconstructed from future data.

Within every fold, and never before it:

1. continuous missing values receive the training-column median;
2. every continuous input receives an explicit missing-value indicator;
3. continuous inputs, indicators and fixed position one-hot fields are
   standardised using training means and population standard deviations; and
4. the ridge coefficients and intercept are fitted only to training rows.

The report records imputation counts, zero-variance fields, coefficient norm
and the largest standardised coefficients for diagnosis and future player
explanations.

## Fixed feature set

The challenger consumes price, selection, official availability chance,
fixture count and venue mix, rest/congestion gaps, cumulative rates, prior
sample count, rolling-three and exponentially weighted player output and
appearance form, rolling-three team form, the same opponent form averaged over
target fixtures, and fixed position indicators. Null history remains null
until the fold-local transform; it is not silently replaced in the feature
artefact.

The current inputs deliberately omit richer news, role, tactical, scraped and
cross-season information. Those sources become separately measurable
challengers or ablations only after their timestamps and identities are
retained.

## Interpretation

This small regularised model is a bridge between the deterministic incumbents
and more flexible tabular methods. A negative result is useful: it shows that
the present temporal inputs or sample size do not yet justify added model
complexity. Tree models, alternative penalties and feature selection must not
be chosen on the eventual final holdout.
