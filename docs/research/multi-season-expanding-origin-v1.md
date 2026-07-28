# Multi-season expanding-origin evaluation v1

## Question

Does carrying the pinned 2024/25 archive into 2025/26 training improve fixed
ridge or histogram-tree FPL-point prediction beyond the same models trained on
2025/26 alone?

This is a retrospective source-and-feature ablation. Both seasons were already
observed when it was designed, so it can retain a specification for prospective
testing but cannot promote a model or replace Baseline v0.

## Cohort and chronology

The evaluator requires the exact 2024/25 and 2025/26 immutable captures. It
aggregates fixture rows to one player-Gameweek target and orders origins by
season then Gameweek. Every 2025/26 target uses:

- all player-Gameweek rows from earlier seasons and earlier current-season
  Gameweeks for the multi-season candidates;
- only earlier 2025/26 rows for the current-season-only ablation;
- the same target players and final point labels for every model; and
- only exact official player codes across seasons, with no name fallback.

The feature row separates prior-season from current-season history, carries
exact-code identity and position-change indicators, preserves the offseason
gap and includes full-history, rolling-three, rolling-five and fixed-alpha
EWMA participation and performance summaries. Legacy 2024/25 defensive
contribution is null, not zero-filled; each window includes both a nullable
mean and an observed-count feature.

Transforms, median imputation, missing indicators and scaling are fitted inside
each training fold. A test changes a later outcome and verifies that an earlier
feature row remains byte-for-byte equal. The SQLite source is opened read-only,
the report is deterministic and the CLI refuses output overwrite.

## Fixed comparison

Gameweeks 31–38 of 2025/26 are evaluated with expanding origins. Five fixed
models receive identical target cohorts:

1. player rolling-three points;
2. current-season-only ridge;
3. current-season-only histogram tree;
4. multi-season ridge; and
5. multi-season histogram tree.

The ridge penalty and tree configuration are inherited unchanged from the
existing historical evaluators. There is no tuning against these folds.

## First retained result

The real run used 27,283 2024/25 fixture rows for 784 stable codes and 29,747
2025/26 fixture rows for 841 stable codes. The eight target folds contained
6,252 player-Gameweeks.

| Model | MAE | RMSE |
| --- | ---: | ---: |
| Multi-season histogram tree | 0.963833 | 1.909170 |
| Current-season-only histogram tree | 0.966352 | 1.923032 |
| Multi-season ridge | 1.032849 | 1.948337 |
| Current-season-only ridge | 1.036365 | 1.954180 |
| Rolling-three baseline | 1.060919 | 2.216491 |

The multi-season tree improved MAE by 9.1511% against rolling three, but only
0.2607% against the matched current-season-only tree. It reduced aggregate
RMSE by 0.013862 versus that tree, while winning only three of eight folds and
slightly regressing goalkeeper and midfielder MAE. The multi-season ridge
showed the same small aggregate pattern.

The evidence therefore supports retaining the two-season specification as a
prospective shadow challenger. It does not support promotion or a claim that
older history is uniformly beneficial. The retained machine-readable summary
is
[`multi-season-expanding-origin-2024-25-2025-26-v1.json`](results/multi-season-expanding-origin-2024-25-2025-26-v1.json).

## Limitations and next action

Both archives are settled end-of-season exports. They do not reconstruct
historical injury/news publication times, prices, ownership or every fixture
reschedule as it was known at the deadline. The result also informed this
written interpretation, so 2026/27 folds must remain untouched prospective
tests.

The next product-safe step is a separately labelled, read-only 2026/27 shadow
forecast using the frozen two-season specification. Baseline v0 remains the
served decision model. Automatic final-outcome pairing will score both without
retraining when real Gameweeks complete.
