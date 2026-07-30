# Historical player clean-sheet component evaluation v1

## Question

Does the retained team scoreline model improve player FPL forecasts when its
clean-sheet probability is combined with cutoff-safe participation and the
exact scoring rules?

The match-model replication is not sufficient evidence on its own. This screen
tests the component at the player level while separating a replacement point
mean from a probabilistic scenario input.

## Fixed factorization

The evaluation uses all three registered historical targets and their
Gameweeks 31–38. It keeps zero-minute players but excludes double-fixture
player-Gameweeks because one Gameweek-level 60-minute probability cannot
identify the fixture in which the threshold was reached.

For each single-fixture row, observed clean-sheet points are subtracted from
the total. The unchanged appearance-hurdle method forecasts the residual. The
candidate then reconstructs:

```text
E(points) =
  P(appearance) × E(non-clean-sheet points | appearance)
  + min(P(appearance), P(60 minutes))
    × P(team clean sheet)
    × position clean-sheet points
```

Two otherwise identical component models use either a time-decayed league
home/away Poisson rate or the retained time-decayed Dixon–Coles team rate.
The incumbent is the unchanged appearance-hurdle total-point mean.

The point-mean gate requires at least 1% MAE improvement, RMSE
non-regression, a strict majority of 24 fold wins, no position MAE regression
above 5% and no loss to the simpler league component. Separately, a clean-sheet
probability may advance to a distribution-only screen with at least 1% Brier
improvement, log-loss and calibration non-regression and a strict majority of
fold wins.

## Result

The probability component passes, while the point mean does not.

Across 15,788 goalkeeper, defender and midfielder rows:

| Metric | League component | Dixon–Coles component | Change |
| --- | ---: | ---: | ---: |
| Brier score | 0.053805 | 0.052352 | 2.70% improvement |
| Log loss | 0.180390 | 0.174722 | 3.14% improvement |
| Calibration error | 0.004781 | 0.003419 | improvement |
| Fold wins | — | 14/24 | strict majority |

Across all 17,931 player rows:

| Point model | MAE | RMSE |
| --- | ---: | ---: |
| Appearance-hurdle incumbent | 0.888789 | 1.835569 |
| Residual + league clean sheet | **0.886663** | 1.835218 |
| Residual + Dixon–Coles clean sheet | 0.887922 | **1.827378** |

The Dixon–Coles mean improves incumbent MAE by only 0.0975%, wins 12/24
folds and loses MAE to the league component. Defender, goalkeeper and forward
MAE regress slightly; midfielder MAE improves. All position changes remain
inside the stability limit, but the materiality, fold-win and league-component
gates fail.

## Decision

Do not replace the served or retained player point mean. Retain the
Dixon–Coles clean-sheet probability only for a point-distribution screen.

That next challenger should inject one shared clean-sheet event per simulated
team fixture, preserving cross-player correlation, while centering the
remaining player-point component so the rejected mean is not smuggled into
advice. It must improve CRPS/calibration and then the complete 3/6/8-Gameweek
squad policy before it can affect the initial prediction.

Goals-conceded deductions, attacking events, saves, cards, bonus and defensive
contributions remain inside the residual in this slice. They require their own
measured event candidates rather than assuming the clean-sheet result
generalises.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_player_clean_sheet_component_evaluation \
  --database /path/to/autofpl.db \
  --scoreline-evaluation \
    docs/research/results/historical-scoreline-replication-evaluation-v1.json \
  --output /path/to/historical-player-clean-sheet-component-evaluation.json
```

The retained result is
[historical-player-clean-sheet-component-evaluation-v1.json](results/historical-player-clean-sheet-component-evaluation-v1.json).
Its data identity is
`81fe251e33e7abf91bc7772be8a501f478f4dc20bd50def90ae5e724ced0ca5a`
and its run identity is
`fba247502980ae3bba7880af3d30d215bae4082a545c4835f9a0f0c190143212`.
The retained file SHA-256 is
`3678a4da96257f708ce9e4356e40e633b82a29995e79fef144c1bcf9b5f69d17`.
