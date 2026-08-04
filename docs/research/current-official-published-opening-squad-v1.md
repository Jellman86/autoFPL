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
