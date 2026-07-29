# Current best-supported opening squad v2

## Decision

autoFPL's displayed opening prediction now uses the retained
appearance-hurdle point model and the registered six-Gameweek expected-points
policy. The exact current solve selects:

- goalkeepers: Leno and Roefs;
- defenders: Truffert, Van Hecke, Tarkowski, Virgil and Mukiele;
- midfielders: Rayan, Enzo, Szoboszlai, Bruno Fernandes and Le Fée; and
- forwards: Watkins, Thiago and Calvert-Lewin.

The squad costs £98.5m. Relative to v1 it retains 12 players, removes Pickford,
Rodon and Anderson, and adds Leno, Tarkowski and Rayan. Those removed slots
were independently the three least stable v1 selections in the deterministic
bootstrap audit.

## Why this is the current product prediction

The two-part point target estimates
`P(appearance) × E(points | appearance)`. On the fixed historical screen it
improved unconditional MAE by 1.86%, improved RMSE, won all eight folds and
improved every position group. The opening optimizer then reached a zero-gap
global solution under the retained six-Gameweek policy. On the same 38
hurdle-derived paths the v2 squad beat v1 by 1.97 mean points and won 63.16% of
paired paths.

This supports a versioned product handoff, but not a claim that v2 is
empirically optimal. The model screen reused the development holdout and no
2026/27 outcome exists yet.

## Frozen lineage

The v2 selected artifact binds two separate decisions:

- policy evaluation:
  `historical-opening-policy-evaluation-v1`, data identity
  `e99bb4fce3615c91ecaf642037c6cebb3d36efffc2df54857ffbc4c27714e048`;
- point-model evaluation:
  `historical-appearance-hurdle-points-evaluation-v1`, data identity
  `1ef395d722844b1833fb059d606606e0bf1f1d42732377474d671f1a3f17b16e`.

It freezes the 15 players and legal XI, captain, vice-captain and ordered bench
for every Gameweek from 1 through 8. Selected player records also carry their
model GW1 appearance probability, GW1 point mean and six-Gameweek point mean.

The status is
`best-supported-current-prospective-unscored`. `influencesAdvice` is true
because the decision room displays v2 and “Use prediction as draft” copies its
Gameweek 1 roles. `isPromoted` remains false because prospective outcomes have
not passed the registered gate.

## Product and persistence boundary

The analytics worker generates
`selected-opening-squad-capture-<capture-id>-hurdle-v2.json` only after the v1
registration and all upstream current artifacts exist. The product imports v1
and v2 immutably under distinct evaluation identities. The existing typed
route:

`GET /api/v1/forecasts/selected-opening-squad-shadow/current`

prefers exact-capture v2 and falls back to v1. A forward-only SQLite migration
widens the immutable table constraint without changing existing v1 content.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_best_supported_opening_squad \
  --database /path/to/autofpl.db \
  --output /path/to/current-best-supported-opening-squad.json
```

The artifact remains subject to the registered prospective GW1–8 outcome
evaluation. Both the retained v1 comparator and v2 decision must remain
recoverable; future outcomes may justify promotion, retention or replacement.
