# Historical team-fixture-strength opening evaluation v1

## Question

Do cutoff-safe team attack and opponent defence rates improve the retained
appearance-hurdle opening forecast enough to change autoFPL's initial squad?

This is a fixed retrospective feature ablation. It uses the same three
expanding-season opening targets, exact six-Gameweek selection policy and
eight-Gameweek FPL scorer as the retained v2 model. Its thresholds were fixed
before the challenger result was opened.

## Candidate

The challenger reconstructs each historical match by summing official player
expected goals for each team. At every training origin it uses only strictly
prior matches, applies a 180-day half-life and estimates separate home and away
attack and defence rates. Each team rate is shrunk toward the corresponding
venue league rate with a five-match-equivalent prior.

For each target fixture, two conditional-point features are calculated:

- `fixtureTeamExpectedGoals`, the geometric mean of the player's team attack
  rate and its opponent's defence rate; and
- `fixtureOpponentExpectedGoals`, the geometric mean of the opponent attack
  rate and the player's team defence rate.

Double-Gameweek values are averaged and blanks are zero. An unseen or promoted
team receives the venue league prior. The target-season query reads only
fixture identity, Gameweek, kickoff, team and venue; it does not select target
points, minutes, goals or expected goals.

The appearance classifier deliberately retains the incumbent features. The
candidate changes only conditional points, while the hurdle factorisation,
tree configuration, scenario donors, opening cohorts and optimiser remain
fixed.

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

| Measure | Retained hurdle | Fixture-strength hurdle | Difference |
|---|---:|---:|---:|
| Mean CRPS | 0.905546 | 0.915733 | 1.125% regression |
| Appearance Brier | 0.203814 | 0.203814 | 0 |
| Appearance log loss | 0.609646 | 0.609646 | 0 |
| Mean realised squad points | 410.33 | 378.67 | -31.67 |

The challenger lost all three season-level CRPS comparisons. Every position
regressed, although the worst position regression, 1.64% for midfielders, was
inside the stability tolerance. The unchanged appearance scores confirm that
the intended component isolation held.

The complete selections won one of three target seasons, lost 31.67 realised
points on average and regressed by 66 points in the worst target. All three
policy gates failed.

The decision is
`do-not-retain-team-fixture-strength-opening-challenger`. The served v2 squad
and its forecast lineage remain unchanged.

Data identity:
`3700f06af970cd3791e88b2867848b747bc5ca034b846ee42a62b6f0a5bd6f07`.

Run identity:
`db12a646e74f5c3fb4f719421a62fb86bde4acb4d3ca362c01d2bc20066956ba`.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_team_fixture_strength_opening_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-team-fixture-strength-opening-evaluation.json
```

The evaluator opens SQLite read-only, refuses output overwrite and rebuilds
both target-outcome-free scenario families. Tests cover the fixed feature
contract, target query boundary, future-match exclusion, unseen-team shrinkage
and blank-versus-missing semantics. Reconstructing the incumbent after the
component-feature refactor reproduces its exact retained source identities.

## Next boundary

Do not tune the half-life, shrinkage prior or matchup formula on these opened
targets. This negative result rejects this empirical player-summed-xG
representation; it does not establish that opponent context has no predictive
value.

A future team-strength challenger should use a genuinely different,
point-in-time source or model, such as pre-deadline market goal expectations or
a registered hierarchical match/player component model. It must first prove
standalone temporal accuracy and then pass the same player-distribution and
complete-squad gates. Current availability evidence can advance independently
because it affects a different component and is available before the opening
deadline.
