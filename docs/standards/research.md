# Research and Model Validation Standard

## Governing principle

A model is useful only if it improves an explicitly defined decision using information genuinely available at that decision time. Leaderboard fit or one successful historical season is insufficient.

## Research lifecycle

1. **Question:** state the decision, population, horizon and utility.
2. **Registration:** record hypothesis, baselines, metrics, split and promotion rule before final evaluation.
3. **Snapshot:** freeze immutable, source-attributed point-in-time data.
4. **Develop:** use training and validation periods only.
5. **Evaluate:** unlock the final test period once; retain all results.
6. **Review:** independent leakage, statistical and domain review.
7. **Promote:** create model/dataset cards and an operational shadow evaluation.
8. **Monitor:** calibration, drift, missingness, latency and realised decision impact.
9. **Retire:** preserve reproducibility and route consumers to the successor.

## Temporal validity and leakage

- Use expanding-window or rolling-origin walk-forward validation.
- Embargo data whose publication latency may overlap the forecast deadline.
- `available_at <= decision_deadline` is required for every feature row.
- Fit preprocessing inside each training fold only.
- Injuries, line-ups, prices, ownership and corrected match statistics use the revision available at the deadline, not the final database value.
- Never use future-season entity mappings or post-match identifiers to simplify a historical backtest without documenting and neutralising the leak.
- A random split cannot support a temporal performance claim.

## Baselines

At minimum compare with:

- no-change/current squad;
- simple minutes × rate or rolling average;
- market/crowd baseline when lawfully available;
- incumbent production model;
- deterministic expected-points optimiser without advanced uncertainty.

Complexity is accepted only when it delivers a repeated, material and well-calibrated improvement over these baselines.

## Metrics

### Forecast quality

Use proper scoring rules and calibration appropriate to the target:

- log loss and Brier score for binary events;
- log score/CRPS for distributions;
- MAE/RMSE only as supporting point metrics;
- calibration slope/intercept, reliability plots and interval coverage;
- ranking metrics only when the downstream decision truly ranks alternatives.

Report uncertainty intervals from resampling by time block/season where valid. Do not treat correlated player-game rows as independent observations.

### Decision quality

Evaluate realised points, regret against an information-matched oracle, transfer-hit utility, constraint violations, rank/mini-league objective where lawful data exists, and robustness across seasons, positions, clubs, price bands, injury states and fixture congestion.

A decision-focused metric must not silently use future ownership, final line-ups or other unavailable information.

## Model selection and multiplicity

- Define the search space before final evaluation.
- Track every run, including failed and negative runs.
- Correct interpretation for repeated comparisons; do not present the best of many seeds as typical.
- Use nested temporal validation when tuning materially affects claims.
- Prefer the simplest model within uncertainty of the best validated result.

## Probabilistic simulation

Simulations preserve material dependencies: team scoring, shared clean sheets, mutually exclusive event attribution, minutes/appearance states and postponements. Record seed, simulation count and convergence evidence. Scenario assumptions must be inspectable and adjustable.

## Optimisation

Each recommendation records objective, risk measure, horizon, constraints, solver/version, termination status and optimality gap. Validate the formulation with tiny brute-force instances, hand-worked rule examples and a trusted independent implementation where possible.

“Optimal” means optimal for the stated forecasts, objective and constraints—not clairvoyant or guaranteed to win.

## Promotion gate

A candidate cannot influence a recommendation until:

- all leakage checks pass;
- it beats declared baselines across more than one temporal regime or has a documented safety reason;
- calibration and material slices are acceptable;
- sensitivity and failure analysis are complete;
- model and dataset cards are approved;
- shadow-mode monitoring succeeds;
- rollback to the incumbent is tested.
