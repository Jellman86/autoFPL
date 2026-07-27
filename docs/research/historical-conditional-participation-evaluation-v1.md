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

The screen can reject this fixed factorization before the season. It cannot
promote it because the model family was selected after the holdout was
inspected. If the result is promising, preserve this exact contract and score
it alongside the raw classifiers on genuinely new 2026/27 folds.

Current official availability fusion and evaluated history coverage for
promoted or new players remain independent product-import gates. Exact minutes
also remains on its retained player-last baseline until a separate challenger
passes its own temporal evaluation.
