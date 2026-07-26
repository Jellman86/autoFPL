# Historical preseason evaluation v1

## Question

Can a fixed model of prior performance and participation predict next-Gameweek
FPL points better than simple baselines within the pinned 2025/26 archive,
strongly enough to justify an explicitly provisional 2026/27 preseason
challenger?

This is a bridge for the period before current-season outcome folds exist. It
does not establish cross-season validity and cannot promote a model.

## Cohort and temporal split

Historical fixture rows are aggregated to one player-Gameweek target. Each
sample uses:

- target fixture count and home-fixture rate, which are known before kickoff;
- only player outcomes from numerically earlier Gameweeks;
- full-history, trailing-three, trailing-five and fixed-alpha EWMA summaries;
- appearance, start, 60-minute, minutes, points, xG, xA, xGI and defensive
  contribution history; and
- the season-fixed FPL position.

Source `xP`, final archived health, target outcomes, current-season facts and
future historical Gameweeks are excluded. New or mid-season players remain in
the cohort with explicit missing history.

Candidate selection uses expanding Gameweek origins before Gameweek 31. The
Gameweek 31–38 holdout is opened only after selecting:

- one challenger by development MAE from fixed ridge and histogram-gradient
  boosting models; and
- one comparator by development MAE from zero, position mean, player last,
  player trailing-three and player expanding-mean baselines.

The holdout runs only the selected challenger plus the simple baselines. The
gate remains tied to the comparator selected on development data; other
baselines are reported only as context. No hyperparameter, feature, model or
comparator is changed after reading it.

## Fixed gate

A provisional preseason bridge is supported only when the selected challenger:

1. improves holdout MAE over the pre-selected baseline by at least 1%;
2. does not regress holdout RMSE;
3. wins a majority of holdout Gameweeks; and
4. has no position whose MAE regresses by more than 5%.

Passing permits a separately named challenger artifact. It does not replace
Baseline v0, claim calibration, or satisfy the current-season promotion rule.

## First retained result

The first real run used historical capture `1`, source revision
`f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a`, 841 stable players and 29,747
fixture rows. Development selected
`historical-preseason-histogram-tree` and the
`player-rolling3-points` comparator.

On the untouched eight-Gameweek holdout:

| Model | Rows | MAE | RMSE | Mean error |
| --- | ---: | ---: | ---: | ---: |
| Historical histogram tree | 6,252 | 0.966813 | 1.921273 | 0.013921 |
| Pre-selected rolling-three baseline | 6,252 | 1.060919 | 2.216491 | 0.011655 |

The challenger improved MAE by 8.8702%, won all eight holdout Gameweeks and
improved MAE in every position. The fixed gate therefore returned
`supported-for-provisional-preseason-bridge`.

The retained machine-readable summary is
[`historical-preseason-evaluation-2025-26-v1.json`](results/historical-preseason-evaluation-2025-26-v1.json).
The complete report is reproducible from the immutable archive with the command
documented in `src/analytics/README.md`.

## Limitations and next action

The historical archive is a settled end-of-season export, not a sequence of
decision-time captures. It lacks trustworthy historical injury chronology,
price, ownership and publication-time state. Within-season performance may not
survive transfers, promoted clubs, tactical changes or other cross-season
concept drift.

The next implementation fits the unchanged selected specification on all
eligible 2025/26 rows and emits an immutable, visibly provisional GW1
challenger joined to current players only through stable official codes.
Current official availability remains authoritative. Current-season
rolling-origin evaluation supersedes this bridge as soon as eligible folds
exist.
