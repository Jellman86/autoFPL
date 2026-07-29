# Historical appearance-hurdle opening-policy evaluation v1

## Question

Does the retained appearance-hurdle player model improve the realised quality
of the complete opening-squad decision, rather than only improving average
player-point error?

The experiment reconstructs the incumbent direct-tree and appearance-hurdle
joint distributions at three historical opening deadlines. It globally solves
the same registered six-Gameweek expected-points policy for both models, holds
each resulting squad for eight Gameweeks and scores the frozen XI, bench and
captaincy decisions with the exact FPL rules.

## Fixed comparison

The target seasons are 2023/24, 2024/25 and 2025/26. Every target uses only
strictly earlier season archives for model fitting:

- the incumbent fits the retained direct histogram tree;
- the challenger estimates `P(appearance)` on all earlier rows and
  `E(points | appearance)` on appearance-positive rows;
- the challenger point mean is their product;
- whole-Gameweek residual donors come only from the latest strictly earlier
  season; and
- both candidates use the same zero-gap six-Gameweek expected-points
  optimiser and exact eight-Gameweek realised scorer.

Target point, minute and event fields are not read until both scenario
reconstructions are complete. Historical opening-day injury captures do not
exist, and the registered final-archive fixture proxy remains explicit.

Before inspecting this run, the challenger was required to:

1. improve mean realised eight-Gameweek points by at least two;
2. win at least two of three target seasons; and
3. avoid a target-season regression worse than two points.

These are the incumbent opening-policy stability thresholds. The outcomes had
already been opened by the earlier horizon experiment, so this screen can
retain a prospective challenger but cannot promote one.

## Result

The hurdle policy passed every fixed gate:

| Target | Incumbent | Hurdle | Difference | Shared players |
| --- | ---: | ---: | ---: | ---: |
| 2023/24 | 424 | 461 | +37 | 10/15 |
| 2024/25 | 388 | 392 | +4 | 7/15 |
| 2025/26 | 357 | 378 | +21 | 11/15 |
| **Mean** | **389.67** | **410.33** | **+20.67** | — |

The hurdle policy won all three targets. Its worst improvement was four points,
so it also cleared the no-material-regression gate. The decision is
`retain-hurdle-opening-policy-prospective-challenger`.

The result data identity is
`75a4094bec13ae6226f80547344787ad4241405d5b4234720b9f2ba226acd210`
and the run identity is
`ad66dca436de3520b9c73f65440a652d42248ef6eb050e52c4968d86c8a62f04`.
An independent complete rerun was byte-identical; the emitted file SHA-256 was
`7491db242e73dd4f9c3dcf209eeee0e616391bdb3f3927e7d01a97dd66cf5a36`.

## Interpretation

This is stronger decision evidence than the late-2025/26 player-MAE screen:
the gain survives legal squad construction, weekly formation, bench order,
captaincy, substitutions and equal weighting across three opening seasons.
It supports the appearance-hurdle model as the best current opening-policy
challenger and independently agrees with the current v2 replacement of the
incumbent's least stable players.

It is not proof that the current 2026/27 squad is empirically optimal. The
three historical outcomes were already available, historical health state is
missing, and current outcomes have not begun. The frozen 2026/27 v2 squad must
still be scored prospectively without reselection.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_appearance_hurdle_opening_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-hurdle-opening-evaluation.json
```

The command opens SQLite read-only, refuses to overwrite an existing output
and emits deterministic data and run identities.
