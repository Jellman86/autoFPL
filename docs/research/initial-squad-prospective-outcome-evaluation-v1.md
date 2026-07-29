# Initial-squad prospective outcome evaluation v1

## Decision

The first 2026/27 result must be an untouched temporal fold, not another input
to preseason model selection. This evaluator freezes the already persisted
Baseline v0 squad, initial-squad candidate, full-cohort Baseline v0 player
table and joint scenario artifact, then waits for the later final official
Gameweek outcome.

It does not retrain, choose a model, change the squad or make a promotion
decision.

## Exact pairing

The evaluator opens the analytics SQLite snapshot read-only and selects the
latest immutable initial-squad artifact that existed before its recorded
deadline. It verifies the stored content hashes for:

- the initial-squad artifact;
- its exact joint scenario;
- the exact-capture full-cohort Baseline v0 player table; and
- the latest later official outcome payload for the same season and
  Gameweek.

An outcome is eligible only when its recorded availability is after the frozen
deadline. Every frozen scenario and Baseline player must have an official
outcome row. Missing evidence returns an explicit waiting state; incomplete or
changed evidence fails closed.

## Measures

The candidate and served-model selections are scored once using official
player points, `minutes > 0` appearance truth, and the same deterministic FPL
auto-substitution and captaincy reference used by the prospective simulation.
The report retains realised totals, captain bonus, activated substitutes,
unreplaced starters and candidate-minus-model points.

On their explicitly separate cohorts it also reports:

- Baseline v0 point-mean MAE, RMSE and bias;
- joint-scenario empirical-mean MAE, RMSE and bias;
- empirical-distribution CRPS from the frozen scenario rows; and
- appearance Brier score, log loss, predicted rate and observed rate.

The separation prevents eligibility differences from manufacturing a
favourable comparison.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.initial_squad_outcome_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/initial-squad-outcome.json
```

Before the first final result, the command exits successfully with
`waiting-for-official-outcome` and writes no artifact. Once available, output
is deterministic, content-identified and refuses overwrite.

## Promotion boundary

One Gameweek can falsify obvious assumptions and expose failure slices, but it
cannot establish generalisation. The fold is appended without tuning. Model or
policy promotion requires the registered sequence of later prospective folds,
calibration and proper-score evidence, and decision-utility improvement without
unacceptable position or availability regressions.
