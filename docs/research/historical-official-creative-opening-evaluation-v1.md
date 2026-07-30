# Historical official-creative opening evaluation v1

## Question

Do prior BPS, influence, creativity and threat summaries improve the retained
appearance-hurdle opening forecast enough to change autoFPL's initial squad?

This is a fixed retrospective feature ablation. It was specified before the
challenger result was opened and uses the same three expanding-season opening
targets, exact six-Gameweek selection policy and eight-Gameweek FPL scorer as
the retained v2 model.

## Why this candidate

[OpenFPL](https://arxiv.org/abs/2508.09992) reports a position-specific
XGBoost/Random Forest ensemble using public FPL and Understat histories over
one, three, five, 10 and 38 matches. Its public repository at revision
`ad055ba7270145ce8ecc1f19581423493b1357e7` contains MIT-licensed inference
weights and a notebook, but no training or feature-construction pipeline.
Its prospective test covers only Gameweeks 32–38 of 2024/25, not an opening
Gameweek.

The published model therefore cannot be treated as a leakage-safe Gameweek 1
drop-in. autoFPL instead tests one bounded, reproducible part of its feature
portfolio using data already present in the pinned official archives.

The challenger adds prior-history means for:

- BPS;
- influence;
- creativity; and
- threat.

Each metric is summarized over full prior history, rolling three, rolling five
and the retained fixed-alpha EWMA. All 16 values are calculated from strictly
earlier player-Gameweeks. The model class, hurdle factorization, donor paths,
opening cohorts and optimizer are unchanged.

## Fixed decision rule

The candidate must pass every distribution and policy gate:

- at least 1% aggregate CRPS improvement;
- lower CRPS in at least two of three target seasons;
- no position CRPS regression above 5%;
- no appearance Brier or log-loss regression;
- at least two realised eight-Gameweek squad points gained on average;
- at least two target-season squad wins; and
- no target-season squad regression above the registered tolerance.

Already-opened outcomes can reject or retain a prospective shadow only. They
cannot promote a model.

## Result

The run covered 15,712 player-Gameweeks:

| Measure | Retained hurdle | Enriched hurdle | Difference |
|---|---:|---:|---:|
| Mean CRPS | 0.905546 | 0.899914 | 0.62% improvement |
| Appearance Brier | 0.203814 | 0.203660 | -0.000154 |
| Appearance log loss | 0.609646 | 0.609477 | -0.000169 |
| Mean realised squad points | 410.33 | 404.33 | -6.00 |

The enriched distributions won all three season CRPS comparisons and improved
defenders, midfielders and forwards. Goalkeeper CRPS regressed by 0.55%, inside
the stability tolerance. The aggregate gain nevertheless missed the fixed 1%
materiality gate.

The complete decision result was materially worse. The enriched squad won one
of three target seasons, lost six realised points on average and regressed by
31 points in the worst target. It failed all three policy gates.

The decision is
`do-not-retain-official-creative-opening-challenger`. The served v2 squad and
its forecast lineage remain unchanged.

Data identity:
`d8dfbb4107f180246bf26417174353fcd578f903d2613250af8a04e8b188287b`.

Run identity:
`5fe3e49f35b6cf0afe64026da9e90d4a9cdbfcb126bd095d72c87127179333aa`.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_official_creative_opening_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-official-creative-opening-evaluation.json
```

The evaluator opens SQLite read-only, refuses output overwrite, rebuilds both
target-outcome-free scenario families and emits deterministic content
identities. Tests cover the feature contract, prior-history boundary, fixed
screen and unchanged incumbent reconstruction.

## Next boundary

Do not tune feature subsets or lower the gate on these opened targets. The
next opening-model challenger should address a different missing mechanism:
cutoff-safe team and opponent strength attached to each target fixture. It
must be tested on the same distribution and complete-squad decision metrics
before it can affect the current prediction.
