# Public-projection opening-squad prospective outcome evaluation v1

## Question

Did the final predeadline Solio-assisted opening squad outperform the exact
autoFPL v2 incumbent frozen beside it?

The evaluator answers only that paired prospective question. It does not
backfill historical Solio values, retune the challenger or alter served advice.

## Frozen comparison

The read-only command selects the latest retained Solio opening-squad artifact
whose source snapshot was available before the registered deadline. It
fail-closes unless the artifact hash, source snapshot, exact official capture,
source content hash, Gameweek 1 target and eight-week score registration agree.

For every later final official outcome available in Gameweeks 1–8, it scores:

- the artifact's frozen autoFPL incumbent squad and weekly roles; and
- the artifact's frozen Solio-assisted challenger squad and weekly roles.

Both use the shared `cpu-joint-scenario-reference-v1` realised scorer: exact
captain fallback, ordered goalkeeper/outfield substitutions and legal formation
repair. Squad membership, captaincy, bench order and weekly roles are never
re-optimised after an outcome arrives.

## Incremental evidence status

With no eligible outcome, the command exits successfully with
`waiting-for-official-outcome` and writes no report. Gameweeks 1–7 produce a
`prospective-outcome-partial` artifact and cannot support review.

After all eight outcomes, the comparison is complete. It reports paired weekly
scores, cumulative points, challenger delta and weekly win/tie/loss counts. A
positive cumulative delta plus at least as many weekly wins as losses supports
a source-promotion review. That directional check was fixed before any 2026/27
outcome existed.

`isPromotionDecision` and `isSufficientForAutomaticPromotion` remain false.
One opening period can reveal direction and material failures, but it cannot
precisely estimate cross-season source value. Any future blend or replacement
therefore requires an explicit review and separately versioned decision.

## Command

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.public_projection_opening_squad_outcome_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/public-projection-opening-squad-outcome.json
```

The output identities bind the final public-projection artifact hash and every
official outcome content hash, making repeated runs deterministic as the
outcome window grows.
