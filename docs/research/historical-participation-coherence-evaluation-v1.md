# Historical participation coherence evaluation v1

## Question and status

Does one fixed, outcome-independent projection remove logically impossible
relationships between the provisional appearance, start and 60-minute
probabilities without worsening their historical predictive quality?

The relationship required by the target definitions is:

```text
P(appearance) >= P(start)
P(appearance) >= P(played at least 60 minutes)
```

There is deliberately no ordering constraint between start and 60 minutes. A
substitute can play 60 minutes, while a starter can leave before 60; a double
Gameweek also makes a total order inappropriate.

This result is a **secondary holdout diagnostic, not a new promotion test**.
The underlying classifiers' Gameweek 31–38 holdout had already been opened
before current-player incoherence motivated this experiment. The fixed
projection does not use outcomes, but the historical result cannot restore an
untouched holdout or authorize product import.

## Research basis

Probabilistic forecasts should respect relationships implied by their event
definitions. Predd and colleagues connect such coherence with proper scoring
rules, while work by Rangapuram and colleagues shows how coherent
probabilistic forecasting can accommodate general convex constraints.
Projection-based reconciliation is also an established way to map
independently generated forecasts into a coherent space.

These sources support the general method, not the empirical claim that this
specific correction improves FPL participation forecasts:

- [Probabilistic coherence and proper scoring rules](https://arxiv.org/abs/0710.3183)
- [End-to-End Learning of Coherent Probabilistic Forecasts for Hierarchical Time Series](https://proceedings.mlr.press/v139/rangapuram21a.html)
- [Forecast Reconciliation via Model Projection](https://proceedings.mlr.press/v235/tsiourvas24b.html)

## Fixed projection

For each player and fold, the evaluator finds the equal-weight Euclidean
projection of the three independently fitted probabilities onto the convex
set defined above. It is the unique vector minimizing the sum of squared
changes from the raw probabilities.

The exact deterministic algorithm sorts the two child probabilities from
largest to smallest, pools any child above the current appearance-pool mean,
and assigns the final pooled mean to appearance and every pooled child.
Already coherent vectors remain unchanged. Examples are:

| Raw appearance/start/60 | Projected appearance/start/60 |
| --- | --- |
| 0.80 / 0.60 / 0.40 | 0.80 / 0.60 / 0.40 |
| 0.40 / 0.80 / 0.20 | 0.60 / 0.60 / 0.20 |
| 0.40 / 0.80 / 0.70 | 0.6333 / 0.6333 / 0.6333 |

No weight, threshold, model or projection family is selected from the
historical result. Inputs must be finite probabilities in `[0, 1]`.

## Evaluation and diagnostic gate

The evaluator reconstructs the same historical player/feature cohorts for all
three targets, fails if the binary outcomes violate their logical
relationships and refits the unchanged classifiers on the same expanding
Gameweek origins. Raw and projected forecasts therefore contain identical
players, labels, training windows and fitted models.

For every target, the projection must:

1. have no aggregate Brier-score regression;
2. have no aggregate log-loss regression;
3. keep ten-bin calibration error within 0.01 of the raw forecast;
4. avoid Brier regression in a majority of holdout Gameweeks; and
5. avoid a position Brier regression greater than 0.02.

The combined historical diagnostic is supported only when the raw forecasts
contain at least one nesting violation, the projection removes every
violation and all three target gates pass. The report always sets
`isPromoted`, `canReplaceCurrentRawProbabilities` and `productImportReady` to
false.

## Reproduce

Run the deterministic read-only command against an application-native backup:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_participation_coherence_evaluation \
  --database /path/to/autofpl.db \
  --season 2025-26 \
  --output /path/to/historical-participation-coherence.json
```

The output path must not already exist. The report retains the archive
provenance, fixed configuration, every fold, raw and projected metrics,
violation counts and immutable data/run identities.

## Limitations and next evidence

The settled archive lacks historical decision-time injury status, so this
experiment cannot evaluate the separate current-official-availability fusion
rule. It also cannot establish calibration for promoted clubs, transfers or
players without prior-season identity.

If the secondary diagnostic supports the projection, it may justify carrying
that exact transform as a registered challenger into genuinely new 2026/27
temporal folds. Only those future folds can support replacing the raw current
probabilities. Availability fusion and evaluated sparse/new-player fallbacks
must pass their own gates before a participation artifact can influence
served advice.
