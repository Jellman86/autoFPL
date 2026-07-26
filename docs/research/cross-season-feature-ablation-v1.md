# Cross-season feature ablation v1

## Question

Does the pinned prior-season player state improve current-season total-points
prediction beyond `official-temporal-v2` on the same expanding-origin folds?

This evaluator implements the promotion gate; it does not assert that the
answer is yes.

## Cohort and split

Each fold is inherited from the existing temporal-ridge evaluator. Incumbent
ridge/tree models and their cross-season variants use:

- identical training and target Gameweeks;
- identical labelled current-season players;
- each Gameweek's own pre-deadline official replay;
- only prior current-season outcomes available at that replay cutoff; and
- the same prior-season archive, which must have been available by every
  training and target cutoff.

If any required cutoff lacks the archive, the fold is excluded from every
variant. Missing prior identity for an individual current player remains a
feature value and does not remove that player.

## Fixed candidate features

The candidate adds:

- stable-code prior identity and position-change indicators;
- prior-season sample count and trailing zero-minute Gameweeks;
- full-season appearance/start/60-minute rates, minutes and points;
- prior-season points, xG and xA per 90;
- trailing-five appearance, minutes, points, xG/xA/xGI and defensive
  contribution; and
- the equivalent fixed-alpha EWMA summaries where applicable.

Source `xP` and archived final injury status are excluded. Current official
chance remains in the incumbent feature set; historical participation is only
a durability signal.

## Models and preprocessing

The evaluator compares unchanged temporal ridge and histogram-gradient-
boosting models with separately named variants using the appended candidate
features. Ridge median imputation, missing indicators, scaling and fitting
remain training-fold local. Tree missingness handling remains native and
training-fold local. Baseline predictions are retained on the same cohort.

## Interpretation and promotion

The report is deterministic, hash-identified, read-only and refuses output
overwrite. It returns `insufficient-data` until at least one source-complete
current-season fold has the configured training history.

No cross-season feature influences served forecasts. Promotion requires
repeated out-of-time improvement in point and probabilistic metrics, useful
early-season failure slices, stable identity coverage and no material
calibration or decision-utility regression. One favourable fold is not enough.
