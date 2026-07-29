# Current appearance-hurdle opening squad v1

## Decision

The retained appearance-hurdle point specification is now fitted to every
eligible current player for Gameweeks 1–8 under a separate model and artifact
identity. It emits:

- raw Gameweek appearance probability;
- expected points conditional on appearance;
- their unconditional product;
- 3, 6 and 8-Gameweek cumulative means; and
- the corresponding incumbent direct-tree mean and delta.

The hurdle means are combined with the retained whole-Gameweek donor
distribution. Gameweek 1 applies the official availability ceiling to the
hurdle appearance probability and scales the unconditional mean coherently.
Gameweeks 2–8 keep the raw hurdle probability rather than propagating a
next-round health state through the season.

The registered six-Gameweek expected-points MILP then solves the complete
15-player squad, weekly legal XIs and captains to a reported zero gap.

## First current result

The exact hurdle solve retains 12 of the 15 incumbent players:

| Removed | Added | Position |
| --- | --- | --- |
| Pickford | Leno | Goalkeeper |
| Rodon | Tarkowski | Defender |
| Anderson | Rayan | Midfielder |

The challenger costs £98.5m, compared with the incumbent's £98.0m. The
complete proposed squad is:

- goalkeepers: Leno and Roefs;
- defenders: Truffert, Van Hecke, Tarkowski, Virgil and Mukiele;
- midfielders: Rayan, Enzo, Szoboszlai, Bruno Fernandes and Le Fée; and
- forwards: Watkins, Thiago and Calvert-Lewin.

On the 38 hurdle-derived paired paths, the new squad and its own weekly roles
score 322.84 mean points through Gameweek 6 versus 320.87 for the incumbent
squad and roles. The challenger mean advantage is 1.97 points; it wins 63.16%
of paths, loses 36.84% and has no ties.

This change aligns with the independent stability audit: Pickford, Rodon and
Anderson were the three least frequently selected incumbent players, at
42.5%, 24.0% and 40.5% of 200 scenario-path bootstrap refits. A historically
better point model therefore replaced the same three slots without using the
stability labels as optimisation inputs.

The current hurdle point run identity is
`4240a54d957efddf2230eb2526539cb22089a4408dd5fe09e3c11e1eeb70f45a`.
The complete opening comparison run identity is
`2fcd5fbf381e908ab17b3ee7d244e1f187b90722d97daf2a682ce11db4393ff3`.

## Interpretation

This is now the strongest supported current initial-squad challenger:

- its point model passed every fixed historical gate;
- its current forecast preserves exact cutoff and archive lineage;
- its scenario construction keeps the retained dependency model;
- its legal squad reaches a zero-gap global optimum for the fixed six-week
  surrogate; and
- its changes target independently diagnosed fragile positions.

It is still prospectively unscored. The 1.97-point path gain is in-sample on
the hurdle scenarios and is not an outcome result. The artifact therefore
remains a retained challenger rather than silently replacing the incumbent
served shadow.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_appearance_hurdle_player_forecast \
  --database /path/to/autofpl.db \
  --output /path/to/current-hurdle-points.json

PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_appearance_hurdle_opening_squad \
  --database /path/to/autofpl.db \
  --output /path/to/current-hurdle-opening-squad.json
```

Both commands are deterministic, read-only, non-promoted and refuse output
overwrite.
