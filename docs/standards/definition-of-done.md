# Definition of Done

The applicable gate is determined by what the change actually delivers. Development work does not need release, deployment or research-promotion paperwork before those stages exist.

## Every product slice

- The requested behaviour works as a complete vertical slice.
- Explicit non-goals remain outside the diff.
- Focused tests cover the important behaviour and failure paths.
- `make verify` and the relevant formatter/linter pass.
- User or operator documentation changes only where the old text would be misleading.
- No secret, private runtime data or unrelated refactor is committed.

## Predictive experiments

Exploration is allowed without prior approval. Results remain labelled exploratory and cannot drive recommendations until promotion.

## Predictive promotion

Before a model, feature or source influences recommendations:

- data is point-in-time correct and leakage checks pass;
- the target, temporal split, baselines, metrics and promotion rule were fixed before the final holdout;
- rolling/walk-forward results beat or materially complement declared baselines;
- calibration, decision utility, uncertainty and important failure slices are reported;
- negative and inconclusive results are retained; and
- the result is reproducible from versioned code, data, configuration and seeds.

Optimisers additionally require rules-feasible outputs and tiny cases checked against brute force or another trusted result.

## Database and deployment changes

- SQLite migrations preserve existing data and are tested against the latest schema.
- Destructive migrations require a verified backup or recovery path.
- If a change is deployed, health and the changed behaviour are verified on the deployed revision.
- Release rollback, signed tags, runbooks and broader operational evidence apply to releases, not ordinary development commits.

## Higher-risk changes

Security/trust boundaries, credentials, external actions, deployment, supply chain, authoritative FPL rules and promoted data/models receive independent review. Ordinary low-risk slices require focused author review and green CI.
