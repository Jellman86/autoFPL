# FPL Form feature ablation v1

## Status and command

`fpl-form-feature-ablation-v1` is an executable, exploratory test of whether
retained FPL Form forecasts improve autoFPL's official-data tabular models. It
does not promote a model or populate advice.

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.fpl_form_ablation \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/fpl-form-ablation.json
```

The command is deterministic, opens SQLite read-only, refuses to overwrite an
existing report and returns exit code `2` when no source-complete temporal fold
exists.

## Question and cohort

The test asks one bounded question: on identical labelled players and
expanding-origin folds, does adding the captured public forecast improve
Gameweek total-points error over official-data-only ridge or histogram
boosting?

A target fold is eligible only when every historical training Gameweek and the
target Gameweek has an FPL Form capture available no later than that
Gameweek's official feature decision cutoff. One missing source capture
excludes the entire fold from every variant. This avoids comparing a
source-enhanced model on an easier or different population.

Strict direct player and fixture identity, declared row coverage, club,
position, participation and London-local kickoff checks are inherited from
`fpl-form-temporal-feature-v1`. Identity disagreement is an error, not a
missing value.

## Fixed variants

The report recomputes these models on each admitted cohort:

- official-data ridge and fixed histogram gradient boosting;
- each official model plus published conditional predicted points and an
  explicit forecast-present indicator; and
- each official model plus the conditional fields, appearance-adjusted points
  and an explicit complete-appearance-probability indicator.

The four simple point incumbents are recomputed on the same cohort. Ridge
imputation, missing indicators and scaling are fitted only on the training
rows. The tree retains missing values natively and decides which fields are
splittable using training rows only. Target rows never choose preprocessing,
features or hyperparameters.

Published conditional points and appearance-adjusted points remain separately
named because the latter changes the meaning of the provider value. A missing
player forecast or incomplete appearance probability is retained as null plus
an indicator; it is never silently converted to zero.

## Interpretation and limits

MAE, RMSE and mean error are descriptive until enough real folds accumulate.
A favourable synthetic test, one Gameweek or one season cannot support
promotion. The current 2026/27 database may correctly report
`no-source-complete-expanding-origin-folds` while captures accumulate.

Later promotion requires a registered multi-fold rule, failure slices,
calibration and decision-utility evidence. If neither source variant produces
repeatable out-of-time gain, the official-only incumbent remains preferable
and the negative result should be retained.
