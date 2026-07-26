# Historical participation evaluation v1

## Question

Can fixed prior-performance models improve next-Gameweek appearance, start,
60-minute and total-minutes forecasts over simple player and position history
baselines on the pinned 2025/26 archive?

This is a prerequisite for adjusting the provisional GW1 point mean. It does
not promote a model or establish current-season calibration.

## Targets and cohort

The evaluator reuses the historical preseason point evaluator's exact stable
player cohort, aggregation and feature contract. Fixture rows are aggregated to
one player-Gameweek before targets are defined:

- `appearance`: total Gameweek minutes greater than zero;
- `start`: at least one official start in the Gameweek;
- `played-60`: total Gameweek minutes at least 60; and
- `minutes`: uncapped total Gameweek minutes, including double Gameweeks.

Target fixture count and home-fixture rate are known before kickoff. Every
other feature uses only numerically earlier Gameweeks. Source `xP`, settled
final health, target outcomes and future Gameweeks remain excluded.

## Fixed models and split

Appearance, start and 60-minute probabilities use a fixed
histogram-gradient-boosting classifier with log-loss and the same depth,
regularisation, binning, iteration and seed contract as the selected point
tree. Missing values and unsplittable feature removal are learned within each
training fold. Comparators are smoothed global, position, player-last,
player-rolling-three and player-expanding event rates.

Minutes uses the fixed histogram-gradient-boosting regressor against zero,
position mean, player last, player rolling-three and player expanding-mean
baselines.

Development uses expanding Gameweek origins before Gameweek 31. Each target
selects its comparator on development data. Gameweeks 31–38 remain the locked
holdout and are opened only after that selection. Because the challenger and
its configuration are fixed before this evaluation, development folds fit only
the inexpensive comparators; the challenger is neither selected nor tuned
there. Challenger fitting begins only after the comparator is locked, avoiding
work that cannot affect the decision while preserving the identical holdout
gate.

## Metrics and gate

Binary forecasts report Brier score, natural-log loss, ten-bin expected
calibration error, mean probability and event rate overall and by position.
Minutes reports MAE, RMSE, mean error and position slices.

A binary target supports a provisional bridge only when the fixed candidate:

1. improves holdout Brier score by at least 1% over its pre-selected baseline;
2. does not regress log loss;
3. keeps calibration error within 0.02 of the comparator; and
4. wins a majority of holdout Gameweeks by Brier score; and
5. has no position whose Brier score regresses by more than 0.02.

Minutes requires at least 1% holdout MAE improvement, no RMSE regression and a
majority of fold wins, with no position whose MAE regresses by more than 5%.
Every target must pass before the report can mark the combined bridge
supported. Passing still does not promote the result or alter served advice.

## First retained result

The first real run used historical capture `1`, source revision
`f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a`, 841 stable players and 29,747
fixture rows. Every target selected its simple comparator on 25 development
folds before the untouched Gameweek 31–38 holdout was opened.

| Target | Challenger | Comparator | Challenger score | Comparator score | Improvement | Fold wins | Result |
| --- | --- | --- | ---: | ---: | ---: | ---: | --- |
| Appearance | Histogram classifier | Rolling-three rate | Brier 0.084772 | Brier 0.108087 | 21.5706% | 8/8 | Supported |
| Start | Histogram classifier | Rolling-three rate | Brier 0.081821 | Brier 0.104625 | 21.7959% | 8/8 | Supported |
| 60+ minutes | Histogram classifier | Rolling-three rate | Brier 0.083342 | Brier 0.102468 | 18.6653% | 8/8 | Supported |
| Total minutes | Histogram tree | Player-last minutes | MAE 13.470439 | MAE 12.280442 | -9.6902% | 1/8 | Not supported |

All three probability models also improved log loss, calibration and every
position slice. Their mean holdout probabilities were within 0.0026 of the
observed event rates. They therefore support separately labelled provisional
current-player probability artifacts.

The minutes tree lowered RMSE from 28.954555 to 23.369100 but worsened MAE,
lost seven of eight holdout Gameweeks and regressed every position, including
20.2002% for goalkeepers. It is rejected. Exact minutes must retain the
transparent player-last comparator until a replacement passes the same locked
gate. The combined four-target bridge is therefore not supported.

The retained machine-readable summary is
[`historical-participation-evaluation-2025-26-v1.json`](results/historical-participation-evaluation-2025-26-v1.json).
The complete report is reproducible from the immutable archive with the
command documented in `src/analytics/README.md`.

## Limitations and next action

The archive is a settled export and lacks historical decision-time injury
state. Within-season evaluation cannot prove that probabilities remain
calibrated across promoted clubs, transfers, tactical changes or the new
season. Gameweek-level binary targets deliberately mean “at least one” for
double Gameweeks.

The next action is to fit the unchanged supported appearance, start and
60-minute classifiers to current players. Current official availability
remains authoritative and scout evidence stays a separately evaluated
challenger. Exact minutes retains the player-last baseline while a new
hierarchical or two-stage minutes challenger is evaluated on the identical
folds.
