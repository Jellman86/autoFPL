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

If the historical screen is poor, reject this candidate. If it is useful, fix
the exact contract before any new-season outcomes are inspected, then score it
alongside raw classifiers on genuinely new 2026/27 folds. Availability fusion
and evaluated sparse/new-player coverage remain independent product-import
gates.
