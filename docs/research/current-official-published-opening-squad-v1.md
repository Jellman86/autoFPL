# Current official published opening squad v1

## Question

Does official FPL's own published next-Gameweek expected-points value identify
a better 2026/27 opening squad than the best-supported autoFPL v2 forecast?

`ep_next` is a useful full-pool baseline, not ground truth. Its methodology and
predictive distribution are not published, and no 2026/27 outcome yet exists.

## Cutoff and identity boundary

For the exact current official capture, the command requires:

- the capture season, next Gameweek, availability time and deadline to equal
  the retained appearance-hurdle scenario target;
- a finite provider-published `ep_next` value for every eligible candidate;
- the immutable bootstrap and fixture hashes; and
- the best-supported incumbent to share the same official capture and selected
  six-Gameweek evaluation policy.

Missing values fail the whole screen. The command never falls back to another
capture or silently substitutes an autoFPL mean for an eligible candidate.

## Frozen method

The baseline replaces every candidate's Gameweek 1 optimizer mean with the
published value. Gameweeks 2–8, the player pool, prices, availability rules,
budget, club quotas, formation rules and six-Gameweek policy remain unchanged.
The same zero-gap SciPy/HiGHS MILP selects the complete squad and weekly roles.

The published values affect only the linear expected-value surrogate; they do
not manufacture an official uncertainty distribution. Incumbent and challenger
are therefore rescored on the unchanged retained autoFPL paths to expose what
the alternative costs if `ep_next` contains no additional information.

## First live screen

Official capture 40 contains a published value for all 560 eligible candidates.
The exact baseline agrees with autoFPL on 12 of 15 squad places:

| Removed from autoFPL | Added by `ep_next` |
| --- | --- |
| Thiago | Gabriel |
| Van Hecke | Pickford |
| Roefs | Richarlison |

The published-value surrogate improves from `297.780211` to `301.483158`, a
`+3.702947` change. On unchanged autoFPL scenarios, the baseline mean improves
from `430.421053` to `431.526316` and lower-tail CVaR from `379.184211` to
`379.842105`, while p10 falls from `390.1` to `383.7`.

That is a mixed prospective diagnostic. It does not establish predictive gain,
change the selected squad or justify blending official and autoFPL values.

## Command

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_official_published_opening_squad \
  --database /path/to/autofpl.db \
  --output /path/to/current-official-published-opening-squad.json
```

The artifact freezes both legal squads, all eight weekly role decisions, exact
retained-scenario scores, source hashes, solver identity and prospective outcome
window. It is `prospective-official-baseline-unscored`, `isPromoted: false` and
`influencesAdvice: false`.

## Immutable product handoff

After the best-supported v2 squad is stored for an exact Gameweek 1 capture,
the private analytics worker generates
`official-published-opening-squad-capture-<capture-id>.json`. The API accepts
the document only while that capture is still current and verifies:

- the official bootstrap, fixture, deadline and availability identities;
- complete non-unavailable-player `ep_next` coverage;
- the exact persisted v2 incumbent run identity;
- both legal 15-player squads and all eight legal frozen role sets; and
- the registered source, method, solver and outcome-scoring identities.

The application inserts one immutable artifact per official capture. An
identical retry is idempotent; a different document for the same capture fails
closed. The current exact artifact is read-only at
`GET /api/v1/forecasts/official-published-opening-squad-shadow/current`; a
`404` never substitutes an older capture. Persistence does not promote the
baseline or make it influence advice. The
[prospective outcome evaluator](official-published-opening-squad-prospective-outcome-evaluation-v1.md)
now consumes this exact document incrementally, so it never reconstructs the
challenger after results exist.
