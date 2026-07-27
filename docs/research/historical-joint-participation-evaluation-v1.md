# Historical joint participation evaluation v1

## Question and status

Can one fixed joint classifier produce coherent appearance, start and
60-minute probabilities while matching or improving the independently fitted
raw classifiers?

This evaluator was designed after the 2025/26 Gameweek 31–38 holdout and the
Euclidean projection diagnostic had both been opened. Its historical result is
therefore an **exploratory reused-holdout candidate screen, not a promotion
test**. It may help reject an unsuitable model before the season, but only
genuinely new 2026/27 temporal folds can support product use.

## Five feasible states

One multiclass probability distribution represents every logically feasible
combination of the three binary outcomes:

| State | Appearance | Start | 60+ minutes |
| --- | ---: | ---: | ---: |
| No appearance | 0 | 0 | 0 |
| Substitute, under 60 | 1 | 0 | 0 |
| Substitute, 60+ | 1 | 0 | 1 |
| Starter, under 60 | 1 | 1 | 0 |
| Starter, 60+ | 1 | 1 | 1 |

The model deliberately permits a non-starting player to total 60 minutes and a
starter to leave before 60. Marginal probabilities are derived by summing
states:

```text
P(appearance) = 1 - P(no appearance)
P(start) = P(starter under 60) + P(starter 60+)
P(60+) = P(substitute 60+) + P(starter 60+)
```

This guarantees both child probabilities are no greater than appearance
probability without changing forecasts after fitting.

## Fixed model and temporal design

The candidate is one histogram-gradient-boosting multiclass classifier with
the unchanged feature contract used by the historical participation
classifiers. Its learning rate, 100-iteration limit, seven-leaf trees,
minimum leaf size, L2 regularisation, bin limit, missing-value behavior and
random seed are fixed in code.

Each Gameweek 31–38 model trains only on numerically earlier Gameweeks. Rather
than refitting the three raw classifiers, the evaluator requires the retained
coherence report, verifies its archive/configuration/data/run identities and
uses its immutable raw metrics as the comparator. This preserves the exact
raw evidence while avoiding 24 redundant model fits.

The five-state distribution reports multiclass Brier score—the mean per-row
sum of five squared class-probability errors—and log loss. Derived marginals
report Brier score, log loss, ten-bin calibration and position slices.

## Exploratory gate

For each marginal target, the joint model must:

1. have no aggregate Brier-score regression;
2. have no aggregate log-loss regression;
3. keep calibration error within 0.01 of the raw comparator;
4. avoid Brier regression in a majority of folds; and
5. avoid a position Brier regression greater than 0.02.

It must also produce zero coherence violations. Passing marks only
`historicalScreenSupportsCurrentSeasonRegistration`; every report keeps
`isPromoted`, `canReplaceCurrentRawProbabilities` and `productImportReady`
false.

## First retained result

The real screen used the same 6,252 player-Gameweek rows across Gameweeks
31–38 as the retained raw report and produced zero coherence violations.

| Target | Raw Brier | Joint Brier | Brier change | Raw log loss | Joint log loss | Non-worse folds | Gate |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Appearance | 0.084772 | 0.085054 | +0.000282 | 0.277871 | 0.278517 | 3/8 | Not supported |
| Start | 0.081821 | 0.081033 | -0.000788 | 0.260154 | 0.257732 | 7/8 | Supported screen |
| 60+ minutes | 0.083342 | 0.082580 | -0.000762 | 0.261876 | 0.259586 | 7/8 | Supported screen |

The joint distribution's multiclass Brier score was 0.255300 and log loss was
0.492793. Its mean state probabilities closely tracked observed state rates.
Calibration remained within tolerance and no position crossed the material
regression threshold.

The child targets improved both aggregate proper scores and seven of eight
folds, but appearance regressed on Brier, log loss and the majority-fold
check. The combined screen therefore rejects the full five-state model for the
current artifact. This result motivates a narrower coherent factorization:
retain the supported raw appearance model, learn start and 60-minute
probabilities conditional on appearance, then multiply each conditional by
raw appearance probability.

The retained machine-readable report is
[`historical-joint-participation-2025-26-v1.json`](results/historical-joint-participation-2025-26-v1.json).
Its file SHA-256 is
`28a2dc6fbf60306eb1474b471b95f829dba8f30346fb25a41e0d0476e8b7941c`.

## Reproduce

Run against an application-native database backup and the retained raw report:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_joint_participation_evaluation \
  --database /path/to/autofpl.db \
  --raw-report docs/research/results/historical-participation-coherence-2025-26-v1.json \
  --season 2025-26 \
  --output /path/to/historical-joint-participation.json
```

The command is deterministic, reads SQLite in read-only mode and refuses to
overwrite an existing output. Missing or changed comparator evidence fails
closed.

## Limitations and next evidence

The historical archive lacks decision-time injury status and cannot evaluate
current official availability fusion. The five-state target also models
Gameweek-level outcomes rather than fixture-specific states in a double
Gameweek.

The full joint candidate failed because its appearance marginal regressed,
despite useful start and 60-minute gains. It must not replace the current raw
artifact. The next candidate should preserve raw appearance exactly and fit
`P(start | appearance)` and `P(60+ | appearance)`, producing each coherent
child marginal by multiplication. Its exact contract must be fixed before
new-season outcomes are inspected, then scored on genuinely new 2026/27
folds. Availability fusion and evaluated sparse/new-player coverage remain
independent product-import gates.
