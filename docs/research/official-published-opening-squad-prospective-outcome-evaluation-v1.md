# Official-published opening-squad prospective outcome evaluation v1

## Question

Did the official FPL `ep_next` opening squad frozen before the deadline
outperform the best-supported autoFPL v2 incumbent frozen beside it?

The evaluator answers only that paired prospective question. It never rebuilds
the challenger from later provider values, retunes either squad or changes
served advice.

## Frozen comparison

The read-only command selects the latest eligible immutable
`current-official-published-opening-squad-shadow-v1` document. It verifies the
document hash and fails closed unless its official capture, bootstrap and
fixture hashes, availability and deadline, producer identities, v2 incumbent
run identity and registered Gameweek 1–8 scoring policy all agree with the
stored lineage.

For every available final official outcome in Gameweeks 1–8, it scores the
artifact's frozen `ep_next` challenger and same-artifact autoFPL incumbent. Both
use `cpu-joint-scenario-reference-v1`: exact captain fallback, ordered
goalkeeper/outfield substitutions and legal formation repair. Squad membership,
captaincy and bench order are never re-optimised after an outcome arrives.

## Incremental evidence status

With no eligible outcome, the command exits successfully with
`waiting-for-official-outcome` and writes no report. Gameweeks 1–7 produce a
`prospective-outcome-partial` artifact and cannot support review.

After all eight outcomes, the report includes paired weekly scores, cumulative
points, challenger delta and weekly win/tie/loss counts. A positive cumulative
delta plus at least as many weekly wins as losses supports review of whether
official published values add useful opening-squad information.

`isPromotionDecision` and `isSufficientForAutomaticPromotion` always remain
false. One opening period cannot precisely estimate cross-season source value;
any future blend or replacement requires an explicit, separately versioned
decision.

## Command

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.official_published_opening_squad_outcome_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/official-published-opening-squad-outcome.json
```

The output identities bind the immutable source artifact hash and every
official outcome content hash. Repeated runs are deterministic as the outcome
window grows, and the database remains read-only.
