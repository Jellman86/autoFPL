# Baseline evaluation v1

## Status

This is an executable exploratory baseline specification, not a promoted model
or a performance claim. Deterministic fixtures test chronology and mathematics;
they are not research evidence. Real results remain unavailable until autoFPL
has multiple complete official replay/outcome pairs.

## Question and target

For each player present in the latest official capture available before a
Gameweek deadline, predict the player's final official integer FPL points for
that Gameweek.

The population contains only players whose pre-deadline replay identity matches
an official final outcome. Added post-deadline players may exist in the outcome
capture but do not enter that earlier prediction population.

## Point-in-time split

The evaluator uses expanding origins ordered by season and Gameweek. It never
randomly splits player rows.

For target Gameweek `t`:

1. select the latest immutable reference capture with
   `capture.availableAtUtc <= t.deadlineUtc`;
2. use the latest final target outcome as the eventual observed target;
3. admit only earlier-Gameweek outcome revisions with
   `outcome.availableAtUtc <= t.deadlineUtc` into training; and
4. select each earlier Gameweek's latest revision satisfying that cutoff.

This means a later correction may change the eventual target used to score its
own Gameweek, but it cannot enter a training fold whose decision deadline
preceded the correction.

Player IDs are used only within one season. Cross-season evaluation remains
disabled until explicit provider-code identity matching is implemented and
tested.

## Baselines

All baselines produce one deterministic point prediction per eligible player:

- `zero-points` — always predicts zero;
- `position-expanding-mean` — mean prior points for the player's position,
  falling back to the training-wide mean;
- `player-expanding-mean` — mean prior points for the player, falling back to
  the position mean;
- `player-last-points` — the player's most recent eligible prior points,
  falling back to the position mean; and
- `official-running-mean` — cumulative points visible in the pre-deadline
  official capture divided by the number of earlier Gameweeks.

These are deliberately simple. They establish leakage-safe incumbents for
later minutes, team/opponent-strength, market, tree, hierarchical and ensemble
challengers.

## Metrics and slices

The command reports mean absolute error, root mean squared error and signed mean
error over all eligible player/Gameweek predictions, plus the same metrics by
position. Lower MAE/RMSE is better; signed mean error exposes systematic over-
or under-prediction.

The v1 point baselines do not produce a predictive distribution, so CRPS,
interval coverage and calibration are not reported. The first probabilistic
candidate must add proper scores and calibration without removing these point
baseline comparisons.

## Reproducibility and output

Run:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --output /path/to/baseline-report.json
```

The SQLite connection is query-only. The report records:

- evaluator and schema versions;
- exact configuration and availability rule;
- replay/outcome IDs, timestamps and source hashes for every target and
  training fold;
- a canonical data-identity hash;
- metrics and position slices; and
- a canonical run-identity hash.

The command refuses to overwrite an existing report. With no complete pair or
no eligible rolling fold it writes `status: insufficient-data` and exits `2`.
Schema mismatch, incomplete coverage and invalid configuration fail closed.

## Promotion boundary

No final holdout or promotion threshold is registered yet because the live
2026/27 sample contains no completed Gameweeks. Before any challenger influences
advice, register the training window, final holdout, minimum sample, tuning
budget, proper probabilistic metrics, decision-utility evaluation and promotion
rule. Player-card forecasts remain synthetic until that later gate passes.
