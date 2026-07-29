# Historical appearance-hurdle opening-distribution evaluation v1

## Question

Do the appearance-hurdle scenario distributions improve proper probabilistic
scores and appearance calibration at historical opening deadlines, rather than
only improving point means and the selected squad?

This experiment uses the target-outcome-free direct-tree and hurdle scenario
reconstructions for the 2023/24, 2024/25 and 2025/26 opening targets. Only
after both sets of distributions are frozen does it load the first eight
Gameweeks of realised points and appearances.

## Fixed screen

Every target player and Gameweek is evaluated on the identical scenario
support. The primary metric is empirical CRPS over 15,712 player-Gameweeks.
The screen also measures quantile calibration, central interval coverage,
pinball loss, appearance Brier score, appearance log loss and calibration
error.

Before inspecting the result, the hurdle challenger was required to:

1. improve aggregate CRPS by at least 1%;
2. win at least two of the three target seasons;
3. avoid a position CRPS regression above 5%;
4. avoid appearance Brier-score regression; and
5. avoid appearance log-loss regression.

The targets had already been opened by the earlier policy comparison. Passing
can retain the distribution engine for the current prospective challenger but
cannot promote it.

## Result

The hurdle distributions passed every gate:

| Metric | Direct tree | Appearance hurdle | Change |
| --- | ---: | ---: | ---: |
| Mean CRPS | 0.918192 | **0.905546** | **-1.3773%** |
| Appearance Brier | 0.209731 | **0.203814** | -0.005917 |
| Appearance log loss | 0.764873 | **0.609646** | -0.155227 |
| Appearance calibration error | 0.099702 | **0.083004** | -0.016698 |

The challenger won all three targets:

| Target | Direct CRPS | Hurdle CRPS |
| --- | ---: | ---: |
| 2023/24 | 0.909510 | **0.895279** |
| 2024/25 | 0.917061 | **0.907674** |
| 2025/26 | 0.927480 | **0.913436** |

Every position improved: defender CRPS by 1.77%, forward by 0.77%,
goalkeeper by 1.53% and midfielder by 1.21%. The decision is
`retain-hurdle-opening-distributions-prospective-challenger`.

## Calibration interpretation

The retained challenger is better, not perfectly calibrated. Its central 80%
interval covers 87.67% of outcomes, so it is conservative. The large zero
mass in FPL scores also makes low discrete quantiles appear far above their
nominal rates. Each marginal distribution has only 37 or 38 donor samples,
which limits tail resolution.

Those limitations do not invalidate the CRPS comparison because both models
use the same empirical support and exact target cohort. They do show why the
application must avoid describing the current ranges as precise confidence
intervals. Further calibration work should use genuinely new outcomes or a
separately specified distribution family rather than tuning the opened targets.

The result data identity is
`af3e964f3f936b06f542ab6b19ecb996d2f1c97584298a5aff7877aa8837ada1`
and the run identity is
`00808a1062b294785ebb5be506a7787198722598720d736436c969e003fb2234`.
An independent complete rerun was byte-identical; the emitted file SHA-256 was
`1714354be62700a6c9b82518f01fd014357b6e2b35b93e5496a21e43d7e442b2`.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_appearance_hurdle_opening_distribution_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-hurdle-opening-distributions.json
```

The command opens SQLite read-only, refuses to overwrite an existing output
and emits deterministic data and run identities.
