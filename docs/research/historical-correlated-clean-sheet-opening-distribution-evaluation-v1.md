# Historical correlated clean-sheet opening-distribution evaluation v1

## Question

Can the validated Dixon–Coles clean-sheet probabilities improve the retained
historical opening player-point distributions when clean sheets are represented
as shared fixture events without changing any accepted player mean?

This is the distribution-only follow-up to the player clean-sheet component
evaluation. It does not retry the rejected clean-sheet point-mean replacement.

## Fixed construction

For each of the three registered opening targets, the reconstruction:

1. trains the appearance, conditional-points and 60-minute components using
   strictly earlier season archives;
2. removes the exact position-specific clean-sheet points from every latest
   prior-season donor player-Gameweek;
3. fits the unchanged, externally replicated time-decayed Dixon–Coles model
   using strictly earlier matches;
4. draws one shared scoreline per fixture and scenario path from its joint
   score matrix;
5. adds clean-sheet points only where that fixture's simulated player state
   reaches 60 minutes; and
6. reconciles integer residual paths so every player-Gameweek column sum is
   exactly equal to the retained appearance-hurdle scenario column sum.

The scoreline draw preserves opponent and teammate clean-sheet dependence,
including the shared 0–0 state. The historical 2023/24 GW7 Burnley–Luton
double Gameweek is simulated as two separate fixture events; its players may
receive zero, one or two clean-sheet awards. The GW2 Burnley/Luton blank
creates no fixture event. Target fixture identity is read before scenario
construction, but target goals, minutes, event fields and points are not.

The fixed screen requires at least 1% aggregate CRPS improvement, wins in two
of three target seasons, no position CRPS regression above 5%, unchanged
appearance Brier/log loss and exact point-mean preservation. Passing would
only admit the challenger to the complete 3/6/8-Gameweek policy screen.

## Result

The challenger fails. It changes dependence and marginal shape, but it does
not improve the player-point distribution:

| Metric | Retained hurdle paths | Correlated clean-sheet paths | Change |
| --- | ---: | ---: | ---: |
| Aggregate mean CRPS | **0.905546** | 0.907367 | 0.20% worse |
| 50% interval coverage | 71.64% | 72.16% | wider overcoverage |
| 80% interval coverage | 87.67% | 87.48% | small improvement |
| 95% interval coverage | 94.62% | 94.15% | worse |
| Appearance Brier | 0.203814 | 0.203814 | unchanged |
| Appearance log loss | 0.609646 | 0.609646 | unchanged |
| Maximum point-mean delta | — | **0.000000** | exactly preserved |

It wins only the latest target:

| Target | Retained CRPS | Challenger CRPS | Winner |
| --- | ---: | ---: | --- |
| 2023/24 | **0.895279** | 0.901251 | retained |
| 2024/25 | **0.907674** | 0.909522 | retained |
| 2025/26 | 0.913436 | **0.911277** | challenger |

By position, defender CRPS worsens 0.14%, goalkeeper CRPS worsens 0.98% and
midfielder CRPS worsens 0.16%; forward CRPS improves only 0.03%. Every
position remains inside the stability ceiling, but the materiality and
target-win gates fail. Quantile calibration changes are mixed rather than
consistently favourable.

## Decision

Do not retain or promote the correlated clean-sheet point distribution. The
served v2 initial squad, accepted player means and retained hurdle paths remain
unchanged. The validated team scoreline probabilities remain available for
future components, but clean-sheet correlation alone has not earned a place in
the decision model.

This is useful negative evidence: a match model can improve clean-sheet Brier
substantially while an explicit player-points representation still loses under
a proper score. The next component experiment should allocate conditional
player goals and assists from shared team scoring state, pass those events
through the exact season-versioned scorer, and use the same untuned opening
CRPS and complete 3/6/8-Gameweek policy gates.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_correlated_clean_sheet_opening_distribution_evaluation \
  --database /path/to/autofpl.db \
  --scoreline-evaluation \
    docs/research/results/historical-scoreline-replication-evaluation-v1.json \
  --output /path/to/historical-correlated-clean-sheet-opening-distribution.json
```

The retained result is
[historical-correlated-clean-sheet-opening-distribution-evaluation-v1.json](results/historical-correlated-clean-sheet-opening-distribution-evaluation-v1.json).
Its data identity is
`f57dda2d4479a7cbf008e241e1f1068d8b8c8b17aae88e38781f53aeaef68583`
and its run identity is
`5755b468d638559037d85c7c51e8ce3c36c433e5981a991d69603044c954bf60`.
The retained file SHA-256 is
`4b82c5a78631a78a6e91946b86813af7da4f3f4dc5f5d1ab3591d26338ffd801`.
