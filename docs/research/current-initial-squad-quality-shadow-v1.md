# Current initial-squad quality shadow v1

## Decision

The served Baseline v0 squad is an engineering baseline, not the strongest
available prediction. Its capture aggregate can overvalue players with few
starts, its expected-minutes proxy is deliberately crude, and its squad search
is a budget-repair heuristic. Initial-squad prediction quality is now the
critical development path ahead of additional AI, authentication or release
surface work.

This first replacement candidate reuses the strongest coherent current inputs
that autoFPL has already frozen:

- the exact two-season player point shadow;
- the provisional participation forecast;
- the current official-availability ceiling; and
- the resulting 38-row joint player outcome artifact.

The candidate remains `prospective-shadow-unscored`. It cannot influence served
advice or mutate an owner selection.

## Global constrained surrogate

For every scenario player, the generator requires an exact identity, team,
position, price and availability match in the same official capture. It
recomputes each player's empirical mean from the retained scenario rows.
Unavailable players are excluded.

`scipy.optimize.milp` selects squad membership, the starting XI and captain
jointly under:

- exactly 15 players with 2 goalkeepers, 5 defenders, 5 midfielders and 3
  forwards;
- at most 3 players per club;
- a maximum £100.0m budget;
- exactly 11 starters with one goalkeeper and a legal FPL formation; and
- one starting captain.

The fixed linear objective gives starters full empirical mean value, bench
players 8% value and the captain one additional mean. Vice-captain and bench
order use deterministic descending empirical means after the solve. HiGHS must
return a zero MIP gap; time-limited or approximate results fail closed.

This is a global optimum only for that transparent single-Gameweek surrogate.
The exact CPU FPL scorer then evaluates the selected roles on all 38 joint rows,
including captain fallback and legal automatic substitutions, and reports a
paired comparison with the served Baseline v0 squad.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_initial_squad_candidate \
  --database /path/to/autofpl.db \
  --output /path/to/current-initial-squad-shadow.json
```

The command opens SQLite read-only, validates source hashes and lineage, is
deterministic, refuses output overwrite and retains model, solver and source
identities.

## Private product boundary

Migration 30 retains one immutable, content-hashed candidate for an exact joint
scenario, served forecast and optimiser version. The isolated analytics worker
checks that target after the point and scenario shadows are current, writes one
private `initial-squad-quality-capture-<id>.json` handoff and cannot modify
SQLite. The application imports at most one handoff per poll cycle and
independently checks:

- exact scenario and Baseline v0 document hashes and target lineage;
- canonical Baseline selection identity;
- official player identity, position, price and availability in the same
  capture;
- complete scenario-player candidate-pool coverage;
- legal squad composition, club limit and integer-tenths budget;
- recomputed empirical player means and complete FPL scenario results; and
- the candidate-versus-model paired comparison.

The operator equivalent is:

```text
dotnet AutoFpl.Api.dll \
  --import-initial-squad-quality-shadow <json-file>
```

`GET /api/v1/forecasts/initial-squad-quality-shadow/latest` returns only the
artifact matching the latest official capture, scenario and served forecast.
Missing or stale evidence returns `404`; there is no fallback and no mutation
route.

## Promotion boundary and next work

The first candidate deliberately does not claim that a better optimiser repairs
uncalibrated inputs. Before serving it:

1. score its frozen Gameweek choice against the first real 2026/27 outcome;
2. add 3-, 6- and 8-Gameweek objectives with transfer and flexibility value;
3. evaluate point, appearance, minutes and external-source components on
   identical temporal folds; and
4. promote only a complete distribution-and-decision policy that improves
   calibration and realised decision utility without unacceptable failure
   slices.

GPU work remains downstream of calibrated input distributions and a measured
CPU simulation bottleneck.
