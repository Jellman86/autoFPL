# Current multi-horizon joint scenarios v1

## Purpose

This read-only artifact converts the Gameweek 1–8 player means into aligned
scenario paths for legal opening-squad optimisation. Each target Gameweek keeps
one complete historical donor Gameweek together, preserving shared
player-level shocks within that week rather than sampling players
independently.

The weekly residual candidate is the retained model from
`historical-joint-scenario-evaluation-v1`: mean CRPS `0.639806`, 7.1611% better
than the player-empirical reference, with eight wins from eight locked folds
and no regressing position slice.

## Path construction

The support has one row for every source Gameweek in the exact 2025/26 archive.
For each target Gameweek:

1. fit the already frozen player point and appearance inputs;
2. shift complete historical point/appearance donor rows to those current
   marginals;
3. keep every source row exactly once; and
4. pair weekly rows into paths with a fixed PCG64 permutation.

Gameweek 1 keeps the existing row order and exactly reproduces the current
single-Gameweek scenario shadow. Gameweeks 2–8 use independently seeded
permutations. This preserves each weekly empirical marginal and its
cross-player dependence without inventing a claim that the same historical
shock repeats for eight weeks.

The cross-Gameweek pairing is an explicit unvalidated reference assumption.
Estimating temporal residual dependence and increasing the path count with
batched Monte Carlo are later challengers.

## Availability boundary

Gameweek 1 uses the current official appearance ceiling and matching point-mean
multiplier. Gameweeks 2–8 use the raw preseason appearance probability and raw
point mean. This avoids treating a next-round injury flag as either permanent
or magically resolved on a guessed date.

The later availability model should introduce explicit recovery-time scenarios
when suitable evidence exists.

## Optimiser contract

The artifact exposes:

- one immutable player column index;
- eight weekly point and played/not-played matrices;
- a source Gameweek for every path/week pair;
- per-player weekly point and appearance inputs;
- the registered 3/6/8 horizons; and
- exact point, participation, archive, screen and cutoff identities.

It remains prospectively unscored and cannot influence served advice. The next
slice selects one legal 15-player opening squad while allowing a separate legal
XI, captain and bench order in each target Gameweek, then scores the complete
choice with exact FPL auto-substitution and captaincy on these paired paths.

Run it with:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_multi_horizon_joint_scenarios \
  --database /path/to/autofpl.db \
  --output /path/to/current-multi-horizon-joint-scenarios.json
```
