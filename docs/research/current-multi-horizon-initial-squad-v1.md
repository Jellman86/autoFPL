# Current multi-horizon initial squad v1

## Decision

This shadow solves the actual opening-squad decision rather than ranking
players. One 15-player squad is held for the complete horizon while the legal
starting XI, captain, vice-captain, reserve goalkeeper and ordered outfield
bench may change each Gameweek.

The registered horizons are 3, 6 and 8 Gameweeks. Two policies are frozen
before outcome comparison:

| Policy | Expected-score weight | Lower-tail CVaR weight |
| --- | ---: | ---: |
| `expected-points` | 1.00 | 0.00 |
| `downside-balanced` | 1.00 | 0.15 |

The lower tail is the worst 20% of paired cumulative scenario outcomes. The
historical opening-policy evaluation has now selected the six-Gameweek
expected-points policy for prospective scoring. The downside weight remains a
challenger, not a promoted preference.

## Global optimisation

The SciPy/HiGHS mixed-integer model jointly enforces:

- exactly 15 players;
- two goalkeepers, five defenders, five midfielders and three forwards;
- no more than three players per club;
- a budget no greater than 100.0 million;
- eleven starters in a legal formation each target Gameweek;
- one starting captain each target Gameweek; and
- every weekly starter and captain belonging to the one opening squad.

The transparent weekly surrogate gives all squad players 8% of their scenario
points, adds the remaining 92% for starters and adds one captain copy. For the
robust policy, empirical lower-tail CVaR is represented exactly with a VaR
variable and one shortfall variable per paired path. Every result must finish
with a zero MIP gap.

Vice-captain and bench order are deterministic post-solve choices from the
weekly scenario means. The complete selection is then rescored with the CPU
reference implementation of FPL captain fallback, goalkeeper substitution,
ordered outfield auto-substitution and formation legality. The word "global"
therefore applies to the declared linear mean/CVaR surrogate; exact FPL scenario
utility is an evaluation layer, not an overstated nonlinear optimality claim.

## First real solve

The first run used official capture `18`, 560 eligible candidates and 38 paired
paths. All six models reached zero MIP gap.

| Horizon | Policy | Exact mean | Worst-20% CVaR | Budget |
| ---: | --- | ---: | ---: | ---: |
| 3 | expected points | 161.55 | 135.24 | 99.0 |
| 3 | downside balanced | 161.82 | 142.24 | 99.5 |
| 6 | expected points | 327.89 | 287.16 | 98.0 |
| 6 | downside balanced | 324.74 | 291.00 | 99.5 |
| 8 | expected points | 437.24 | 390.11 | 98.0 |
| 8 | downside balanced | 434.11 | 395.18 | 97.5 |

These are in-sample scores on the same scenario support used by the
optimisation, so they compare mechanics and trade-offs but do not select a
policy. In particular, the 3-Gameweek robust policy improving both displayed
figures is not prospective evidence.

## Retrospective selection and remaining gate

The outcome-free registration was frozen before target points and minutes were
opened. The ensuing three-season comparison retained the six-Gameweek
expected-points reference because the higher-mean three-Gameweek downside
leader breached the registered worst-season regression gate. The current v2
artifact therefore returns `recommendedPolicyKey: 6-expected-points`, marks
exactly one policy for prospective scoring and binds the complete retained
evaluation identity.

This is not a serving recommendation. The selected policy now enters the
already registered prospective 2026/27 outcome evaluation and cannot replace
served Baseline v0 until that evidence passes the promotion gate.

Run the current shadow with:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_multi_horizon_initial_squad \
  --database /path/to/autofpl.db \
  --output /path/to/current-multi-horizon-initial-squad.json
```
