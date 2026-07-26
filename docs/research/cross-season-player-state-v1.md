# Cross-season player state v1

## Purpose

`cross-season-player-state-v1` is the first deterministic bridge from the
pinned 2025/26 archive to a current official FPL target. It gives every current
player an explicit prior-season performance and durability state before any
cross-season feature is allowed into a forecast.

It is an exploratory data artifact, not a fitted model and not a claim that
prior-season information improves prediction accuracy.

## Target and identity

The command selects the latest official capture for the requested season and
Gameweek that:

- recorded that Gameweek as next;
- was available no later than its recorded deadline; and
- contains its declared current-player coverage.

The 2025/26 archive must itself have been available by that exact official
capture time. Current and historical players join only through the official
stable player `code`; season-local element IDs and names are not identity
fallbacks. Missing prior players stay in the output with explicit missing
history. Duplicate current stable codes fail closed.

## State

Historical fixture rows are summed to one player-Gameweek total so double
Gameweeks remain one temporal step. The artifact includes:

- full-season and trailing 1/3/5/10-Gameweek summaries;
- a fixed exploratory EWMA with alpha `0.25`;
- minutes, starts, points, xG/xA/xGI/xGC and defensive actions;
- appearance, start and 60-minute rates;
- points, xG and xA per 90 where minutes are positive;
- last appearance and consecutive trailing zero-minute Gameweeks;
- current-season official accumulated minutes, starts and points; and
- explicit position and normalized team-name change signals.

The EWMA and windows are candidate state summaries. Their values are not fitted
or selected using a holdout and cannot be promoted without identical-fold
ablation against the current feature/model contract.

## Health precedence

Current official status, chance and news time are authoritative for the target.
Archived final status/chance/news identity and prior participation describe
long-run durability only. The artifact cannot replace current health with an
old status, even when current news is sparse.

The archive has no trustworthy decision-time injury chronology for every
2025/26 deadline. Therefore trailing zero minutes are labelled participation,
not diagnosed injury, and final archived health is not retrospectively injected
into historical folds.

## Leakage and promotion boundary

- Source `xP` is absent.
- The archive is used only for targets after its recorded repository/runtime
  availability.
- Exact source and official capture hashes identify every artifact.
- Generation is deterministic, read-only and refuses to overwrite output.
- `influencesForecast=false` and `isPromoted=false` remain mandatory in v1.

The next experiment adds separately named cross-season candidates to the
existing expanding-origin evaluator. Baseline v0 remains unchanged unless
repeated out-of-time scoring and failure-slice evidence justify promotion.
