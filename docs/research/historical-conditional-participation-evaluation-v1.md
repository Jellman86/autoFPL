# Historical conditional participation evaluation v1

## Question and status

Can the supported raw appearance classifier be preserved while start and
60-minute probabilities are made coherent through conditional models?

This evaluator was designed after the 2025/26 Gameweek 31–38 holdout,
Euclidean projection diagnostic and five-state challenger had been opened.
Its historical result is therefore an **exploratory reused-holdout candidate
screen, not a promotion test**. Only genuinely new 2026/27 temporal folds can
support product use.

## Fixed factorization

The appearance classifier is unchanged. Two classifiers are trained on
appearance-positive rows:

```text
P(start) = P(appearance) × P(start | appearance)
P(60+) = P(appearance) × P(60+ | appearance)
```

Both child marginals are consequently no greater than appearance probability.
The evaluator regenerates the raw appearance prediction on each fold and
requires its complete rounded metric and position-slice document to reproduce
the cryptographically validated retained comparator. A mismatch fails closed.

The two conditional classifiers use the unchanged participation feature
contract and fixed histogram-gradient-boosting configuration. Each target
Gameweek trains only on numerically earlier Gameweeks. Conditional training
rows are selected using observed historical appearance outcomes; target
probabilities do not use the target Gameweek's appearance outcome.

The separate child classifiers define coherent marginals but not a joint
start/60-minute distribution. This is sufficient for the current prediction
contract, which exposes those marginals independently.

## Comparator and exploratory gate

The evaluator requires the retained raw coherence report and validates its
archive, configuration, data and run identities. It compares the factorized
probabilities with the retained raw classifiers on the identical rows and
folds.

For each target, the factorized candidate must:

1. have no aggregate Brier-score regression;
2. have no aggregate log-loss regression;
3. keep calibration error within 0.01 of the raw comparator;
4. avoid Brier regression in a majority of folds; and
5. avoid a position Brier regression greater than 0.02.

It must also reproduce appearance and produce zero coherence violations.
Passing marks only
`historicalScreenSupportsCurrentSeasonRegistration`; every report keeps
`isPromoted`, `canReplaceCurrentRawProbabilities` and `productImportReady`
false.

## First retained result

The real screen used 6,252 player-Gameweek rows across Gameweeks 31–38,
reproduced raw appearance exactly and produced zero coherence violations.

| Target | Raw Brier | Factorized Brier | Brier change | Raw log loss | Factorized log loss | Non-worse folds | Gate |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Appearance | 0.084772 | 0.084772 | 0.000000 | 0.277871 | 0.277871 | 8/8 | Supported screen |
| Start | 0.081821 | 0.081659 | -0.000162 | 0.260154 | 0.258314 | 4/8 | Not supported |
| 60+ minutes | 0.083342 | 0.082830 | -0.000512 | 0.261876 | 0.259271 | 6/8 | Supported screen |

Start improved both aggregate proper scores, calibration remained within
tolerance and no position crossed the material-regression threshold. However,
its Brier score was non-worse in exactly half—not a majority—of folds. The
combined screen therefore rejects the factorization under the fixed gate.

This is useful rather than wasted evidence. It shows that appearance can be
preserved while coherent child marginals improve in aggregate, but the start
gain is not temporally consistent enough on the opened holdout. The raw,
five-state and factorized variants should now be frozen and compared
prospectively. No fourth coherence candidate should be selected on these same
outcomes.

The retained machine-readable report is
[`historical-conditional-participation-2025-26-v1.json`](results/historical-conditional-participation-2025-26-v1.json).
Its file SHA-256 is
`555e42b50d947ffa1ab96de2b9ec85b751e684f7e47f8fca09a7bd2be3ba0438`.

## Reproduce

Run against an application-native database backup and the retained raw report:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_conditional_participation_evaluation \
  --database /path/to/autofpl.db \
  --raw-report docs/research/results/historical-participation-coherence-2025-26-v1.json \
  --season 2025-26 \
  --output /path/to/historical-conditional-participation.json
```

The command is deterministic, reads SQLite in read-only mode and refuses to
overwrite an existing output. Missing, changed or unreproducible comparator
evidence fails closed.

## Decision boundary

The screen rejects this fixed factorization because start failed the
majority-fold gate. It cannot be promoted or replace the current raw artifact.
Preserve this exact contract and score it alongside the raw and five-state
classifiers on genuinely new 2026/27 folds. Do not use the opened holdout to
select another coherence transformation.

Current official availability fusion and evaluated history coverage for
promoted or new players remain independent product-import gates. Exact minutes
also remains on its retained player-last baseline until a separate challenger
passes its own temporal evaluation.
