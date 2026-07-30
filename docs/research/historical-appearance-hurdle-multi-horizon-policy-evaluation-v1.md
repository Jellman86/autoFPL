# Historical appearance-hurdle multi-horizon policy evaluation v1

## Question

Does the selected opening-squad horizon or downside objective change when the
originally registered six-policy comparison is applied to the retained
appearance-hurdle distributions?

This is a direct decision test. Every policy globally solves a legal
15-player opening squad; it is not a player-ranking proxy.

## Frozen method

The evaluator requires:

- the exact original policy registration created before target outcomes were
  opened;
- the exact retained appearance-hurdle scenario identity that passed the
  separate CRPS and calibration screen;
- the original 3-, 6- and 8-Gameweek expected-points and 15%
  downside-balanced objectives;
- the original six-Gameweek expected-points reference; and
- the unchanged +2 mean, two-target-win and maximum two-point worst-target
  regression gates.

Every selected squad is held for all eight target Gameweeks. Roles inside the
optimisation horizon come from the optimiser; later roles use legal
maximum-scenario-mean choices from the fixed squad. All policies receive the
same eight actual Gameweeks and use exact captain fallback and ordered legal
auto-substitution.

These outcomes had already been opened by earlier policy and hurdle-model
experiments. The result therefore confirms or freezes a prospective
challenger only; it cannot promote a policy into serving.

## Results

| Policy | 2023/24 | 2024/25 | 2025/26 | Mean | Mean vs ref | Wins | Worst vs ref |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 3 expected-points | 461 | 408 | 378 | **415.67** | **+5.33** | 1 | 0 |
| 3 downside-balanced | 461 | 393 | 384 | 412.67 | +2.33 | 2 | 0 |
| 6 expected-points | 461 | 392 | 378 | 410.33 | 0.00 | 0 | 0 |
| 6 downside-balanced | 464 | 340 | 410 | 404.67 | -5.67 | 2 | -52 |
| 8 expected-points | 461 | 398 | 378 | 412.33 | +2.00 | 1 | 0 |
| 8 downside-balanced | 461 | 390 | 383 | 411.33 | +1.00 | 1 | -2 |

Three-Gameweek expected points is the ranked leader, but it beats the reference
in only one of three targets. It fails the original target-win gate, so the
registered rule retains six-Gameweek expected points.

Three-Gameweek downside-balanced is an informative runner-up: it clears the
numerical stability thresholds with +2.33 mean points, two wins and no target
regression. The preregistered rule ranks a leader first, tests that leader,
then falls back to the reference if any gate fails. It does not search for the
highest-ranked runner-up that passes. Selecting the downside policy now would
change the decision rule after seeing outcomes, so autoFPL does not do that.

## Decision

Keep the six-Gameweek expected-points policy as the best-supported opening
policy. There is no new horizon candidate to promote, and the served v2 squad
does not change.

The downside result remains worth prospective measurement, but it must stay a
named shadow. The next current-squad slice should confirm that the six-week
global optimum remains exact and inspect robustness around near-boundary
players, availability evidence and plausible input perturbations.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_appearance_hurdle_multi_horizon_policy_evaluation \
  --database /path/to/autofpl.db \
  --registration \
    docs/research/results/historical-opening-policy-registration-v1.json \
  --output /path/to/historical-hurdle-multi-horizon-policy.json
```

The retained result is
[historical-appearance-hurdle-multi-horizon-policy-evaluation-v1.json](results/historical-appearance-hurdle-multi-horizon-policy-evaluation-v1.json).
Its data identity is
`68d240ac32219092b8171ce5e28d0b8315ca2c19542a213bdec803c3ed354d14`
and its run identity is
`1f4d8abcdeaf7b886b1f734eaa177798de0e80f4efed969adebc5793be5549b6`.
The retained file SHA-256 is
`ac6cc5564e5829bb964c0967e6568fb1790bf7ef9891d1c555d0c6bd152f7af2`.
