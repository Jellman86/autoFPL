# Historical scoreline replication evaluation v1

## Question

Does the already fixed time-decayed Dixon–Coles match model replicate across
three season targets strongly enough to supply shared scoreline and clean-sheet
state to a player-level event-distribution ablation?

The original evaluation used only 2025/26 Gameweeks 31–38. It improved
clean-sheet Brier score but missed its primary joint-score gate. The immutable
2022/23 and 2023/24 archives were added later, creating two earlier replication
targets without changing the model.

## Fixed replication

The model and parameters are unchanged from
[time-decayed Dixon–Coles expanding-origin v1](time-decayed-dixon-coles-expanding-origin-v1.md):

- a 180-day exponential half-life;
- fixed L2 and identifiability penalties;
- separate team attack and defence effects with home advantage;
- a bounded Dixon–Coles low-score correction; and
- a league home/away independent-Poisson comparator.

The evaluation runs three expanding season prefixes:

1. 2022/23 trains 2023/24 Gameweeks 31–38;
2. 2022/23–2023/24 train 2024/25 Gameweeks 31–38; and
3. 2022/23–2024/25 train 2025/26 Gameweeks 31–38.

Every target contains the same fixed eight Gameweek origins. The aggregate
gate inherits the original 1% joint-score negative-log-likelihood threshold
and non-regression rules. Because clean sheets are now an explicit scoring
component, it additionally requires at least 1% clean-sheet Brier improvement,
a strict majority of clean-sheet fold wins and no clean-sheet Brier regression
in any target season.

This model follows the dynamic team-strength and low-score likelihood of
[Dixon and Coles (1997)](https://doi.org/10.1111/1467-9876.00065).
Negative log likelihood and Brier score are proper scoring rules; the
evaluation does not select a single most-likely score.

## Result

The unchanged challenger passes the broader replication:

| Metric | League Poisson | Dixon–Coles | Improvement |
| --- | ---: | ---: | ---: |
| Joint-score NLL | 3.049069 | 2.950261 | 3.24% |
| Clean-sheet Brier | 0.183589 | 0.171909 | 6.36% |
| Result NLL | 1.057607 | 0.967091 | 8.56% |
| Goal RMSE | 1.208519 | 1.146614 | 5.12% |

It wins 16 of 24 joint-score folds and 17 of 24 clean-sheet folds across 247
matches. Clean-sheet Brier improves in all three target seasons:

| Target | Matches | Baseline | Challenger | Clean-sheet fold wins |
| --- | ---: | ---: | ---: | ---: |
| 2023/24 | 87 | 0.178173 | 0.167812 | 5/8 |
| 2024/25 | 81 | 0.189726 | 0.176188 | 6/8 |
| 2025/26 | 79 | 0.183261 | 0.172032 | 6/8 |

## Decision

Retain the scoreline model for a player-component ablation. It may provide
correlated team goals, opponent goals and clean-sheet state, but it does not
yet influence the served forecast or squad.

The next test must combine these fixture states with cutoff-safe player
participation and position, reconstruct the resulting clean-sheet and
goals-conceded point components through the exact scorer, and compare complete
player point distributions and squad decisions against the retained
incumbents. A match-level gain alone cannot promote a player forecast.

Previously unseen promoted clubs currently receive league-average latent
effects. Prior-competition team strength is a separate new-club challenger and
must be evaluated before it can replace that explicit fallback.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_scoreline_replication_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-scoreline-replication-evaluation.json
```

The retained result is
[historical-scoreline-replication-evaluation-v1.json](results/historical-scoreline-replication-evaluation-v1.json).
Its data identity is
`f6aecac9568d720248fe5e1078be1f0396f970dad3de82a7cc269b79dc90b25e`
and its run identity is
`2c10b68b1b0732a268002109c5bf7e4e5b266bc4df46ea16cf42b617bcdf6599`.
The retained file SHA-256 is
`0e07e5aa8d01388856d09ccd439f96f024db9b4ede54472981f06edad8e1a9c9`.
