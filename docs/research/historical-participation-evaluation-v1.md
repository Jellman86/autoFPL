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
holdout and are opened only after that selection.

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

## Limitations and next action

The archive is a settled export and lacks historical decision-time injury
state. Within-season evaluation cannot prove that probabilities remain
calibrated across promoted clubs, transfers, tactical changes or the new
season. Gameweek-level binary targets deliberately mean “at least one” for
double Gameweeks.

The next action is to run this fixed report against the exact retained Quark
archive. Only target models that pass their locked gate may be fitted to
current players, where current official availability remains authoritative and
scout evidence stays a separately evaluated challenger.
