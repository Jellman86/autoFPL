# Historical opening-policy registration v1

## Purpose

This registration freezes the historical 3/6/8-Gameweek opening-squad
comparison before target-season outcomes are opened by the policy evaluator.
The retained machine-readable registration is
[historical-opening-policy-registration-v1.json](results/historical-opening-policy-registration-v1.json).
Its data identity is
`b9d28cac497af35fc0762b7080db7e369759678872f78d47135e050f1920b675`.

## Outcome-free scenario reconstruction

The point means come from the strictly expanding-season reconstruction. A
missingness-safe version of the retained preseason appearance classifier is
fitted on the latest strictly earlier season. It preserves unavailable
defensive-contribution fields as missing; it does not invent zeroes. Historical
opening status/injury captures do not exist, so the raw preseason appearance
probability is used for all eight weeks and final archived status is excluded.

The joint residual bootstrap uses complete player rows from the latest prior
season. It preserves within-Gameweek dependence and applies the current
fixed-seed independent weekly permutations to form multi-week paths:

| Target | Opening players | Donor paths | Scenario identity |
| --- | ---: | ---: | --- |
| 2023/24 | 658 | 37 | `8656765a77454718554a731dac1b63948bbe5f65ef4d4b869b8c21061d9571aa` |
| 2024/25 | 616 | 38 | `18e696f7681ff4cb3b872fffa5a458bb7573f9a50f7d62567239e49021e30fed` |
| 2025/26 | 690 | 38 | `9cc23c3ede34e34ae0c0db924b5b31e67d56a11cdd066e02ae2b1007f5ed4037` |

No target points, minutes, starts, expected events, scoring events or final
availability fields are read in this phase.

## Frozen candidates

The six candidates are the Cartesian product of:

- 3, 6 and 8-Gameweek squad-construction horizons; and
- expected-points (`CVaR weight 0`) and downside-balanced (`CVaR weight 0.15`)
  objectives using the worst 20% scenario tail.

Every optimiser must reach a zero MIP gap under the existing legal squad,
budget, club, weekly formation and captain constraints.

## Common realized score

Every selected opening squad is held for Gameweeks 1–8 with no transfers. This
prevents a longer construction horizon from receiving more outcome points
merely because it optimised more weeks.

- Within its construction horizon, a policy keeps its optimised XI, captain,
  vice-captain and bench.
- After a shorter construction horizon, the same fixed squad receives the
  legal maximum preseason scenario-mean XI and captain with deterministic
  vice-captain and bench ordering.
- Realized points use the exact FPL scorer, including captain fallback,
  goalkeeper replacement, ordered outfield auto-substitution and formation
  legality.
- Each target season has equal weight.

The primary metric is mean realized eight-Gameweek FPL points across the three
target seasons.

## Frozen selection rule

The predeclared reference is `6-expected-points`, the central horizon and
simplest risk-neutral objective. Candidates rank by:

1. higher mean target score;
2. higher worst-target score;
3. expected-points before downside-balanced;
4. shorter horizon; and
5. policy key.

The ranked leader replaces the reference only when all checks pass:

- mean improvement of at least 2.0 points across eight Gameweeks;
- wins against the reference in at least two of three target seasons; and
- no worst-target regression greater than 2.0 points.

Otherwise the registered reference is retained. This deliberately resists
selecting among six policies from only three seasons using aggregate mean
alone.

The result is still retrospective and cannot influence advice. The selected
policy must be frozen and scored prospectively on new 2026/27 outcomes before
promotion.

## Commands

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_opening_scenario_reconstruction \
  --database /path/to/autofpl.db \
  --output /path/to/historical-opening-scenarios.json

PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_opening_policy_registration \
  --database /path/to/autofpl.db \
  --output /path/to/historical-opening-policy-registration.json
```
