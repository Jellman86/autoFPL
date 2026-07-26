# Temporal histogram-tree challenger v1

## Status

`temporal-tabular-v1` adds one fixed nonlinear challenger to the exact
expanding-origin folds used by `temporal-ridge-v1`. It is exploratory, cannot
populate advice, and makes no claim until retained real Gameweeks produce
eligible folds.

## Command

Install the hash-locked analytics environment, then run:

```bash
python3 -m pip install --require-hashes \
  --requirement requirements-analytics.txt
PYTHONPATH=src/analytics python3 -m autofpl_analytics.temporal_tree \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/temporal-tabular-report.json
```

The report contains the ridge challenger, histogram-tree challenger and four
simple point incumbents on identical target players. It remains deterministic,
hash-identified and read-only, refuses output overwrite, and returns exit code
`2` when there is insufficient history.

## Fixed design

The challenger uses scikit-learn 1.9.0
`HistGradientBoostingRegressor` with squared-error loss, learning rate `0.05`,
100 boosting iterations, at most 7 leaves per tree, at least 20 training rows
per leaf, L2 regularisation `10.0`, 63 bins, disabled early stopping and seed
`20260726`. These values are fixed before real scoring and are not selected
from the eventual holdout.

Continuous inputs retain their nulls so the estimator learns missing-value
branches. Position uses the same fixed one-hot fields as ridge. A feature with
no possible split in the training fold is removed inside that fold; target
data never controls this decision. The report lists candidate, retained,
unsplittable and missing training fields for every fold.

## Why this challenger

Ridge tests additive regularised relationships after fold-local imputation and
scaling. Histogram boosting tests nonlinear thresholds and interactions while
handling partial missingness natively. Keeping both on the same input contract,
training chronology and target population makes the comparison meaningful.

No tree hyperparameter, feature, or seed may be chosen using a final holdout.
If the tree does not improve credible rolling evidence, ridge or a simpler
incumbent remains preferable.
