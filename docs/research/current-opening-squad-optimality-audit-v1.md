# Current opening-squad optimality audit v1

## Question

How much confidence should the product place in each player selected by the
retained six-Gameweek expected-points opening policy?

The zero-gap MILP proves that the selected squad is a global optimum for its
declared linear surrogate. That statement alone does not say whether the
optimum wins by a meaningful margin, whether many squads are effectively tied,
or whether a small change in the finite scenario sample replaces several
players. This read-only diagnostic measures those distinctions.

## Fixed audit

The audit rebuilds the exact current eight-week paired scenario artifact and
uses only the retrospectively selected `6-expected-points` policy. It then:

1. reproduces the incumbent zero-gap solution;
2. requires a different squad and solves for the best remaining feasible
   solution;
3. excludes each of the 15 incumbent players in turn and globally reoptimises
   every other squad and weekly role decision; and
4. performs 200 deterministic nonparametric bootstrap refits by resampling the
   38 paired scenario paths with replacement.

The same path indices are used across all six target Gameweeks in each
bootstrap replicate. This preserves the scenario artifact's cross-Gameweek
pairing instead of independently scrambling weekly uncertainty.

For every exclusion, the artifact reports the linear-surrogate opportunity
cost, all players removed and added by the conditional optimum, and a paired
exact-FPL comparison on the original scenario paths. The bootstrap reports
player selection frequencies, the distribution of incumbent overlap and the
number of distinct optimal squads.

## First current result

The current capture's best distinct squad replaces Rodon with Senesi. Its
linear-surrogate regret is only 0.012631 points over six Gameweeks, although it
scores 2.078948 fewer mean points under the post-solve exact FPL scorer. The
incumbent therefore has a valid zero-gap optimum but almost no linear-objective
margin over another legal squad.

All 200 bootstrap refits produced a distinct squad and none reproduced the
complete incumbent. The median overlap was 10 of 15 players, the mean was
10.205 and the observed range was 7–14. Six incumbent players met the
descriptive 80% core threshold:

| Player | Bootstrap selection frequency |
| --- | ---: |
| Watkins | 100.0% |
| Bruno Fernandes | 97.0% |
| Truffert | 95.0% |
| Calvert-Lewin | 94.5% |
| Virgil | 88.5% |
| Van Hecke | 81.0% |

Four incumbent players fell below the descriptive 50% fragile threshold:

| Player | Bootstrap selection frequency |
| --- | ---: |
| Thiago | 47.5% |
| Pickford | 42.5% |
| Anderson | 40.5% |
| Rodon | 24.0% |

This is evidence against presenting the current 15 as a uniquely reliable
answer. It supports displaying player-level confidence and prioritising
better forecast discrimination over more elaborate optimisation. It does not
justify manually replacing a fragile player, because the bootstrap varies only
the existing scenario paths and has no new outcome evidence.

The result data identity is
`11f272cc47481cc380a3cf49f7e1f0a43521bd84a165e1292395502fdfeddd25`
and run identity is
`298e2d8ad30544bbbadf9c8682de7978d2374f4be2cab87c981080aaaffab364`.
An independent second run was byte-identical.

## Interpretation boundary

The audit separates three claims:

- **solver optimality:** the incumbent is a zero-gap global optimum for the
  declared forecast, objective and legal constraints;
- **conditional margin:** exclusion regret measures how much that model's
  objective values an incumbent player after the complete squad is
  reoptimised; and
- **finite-path stability:** bootstrap frequency measures sensitivity to the
  retained scenario sample.

None is a claim that the forecast is empirically optimal. Exclusion regret is
not causal player value. The bootstrap does not include model-specification,
injury, lineup, price or evidence-source uncertainty. Exact FPL rescoring is a
post-optimisation diagnostic, so a conditional squad can occasionally score
better on nonlinear auto-substitution rules while having a worse linear
surrogate.

For a compact UI summary, an incumbent selected in at least 80% of bootstrap
refits is labelled core and one selected in fewer than 50% is labelled
fragile. These are descriptive thresholds, not promotion gates.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_opening_squad_optimality_audit \
  --database /path/to/autofpl.db \
  --output /path/to/current-opening-squad-optimality-audit.json
```

The command reads SQLite without writing it. The artifact is deterministic,
non-promoted and unable to influence served advice. Tests cover conditional
constraint validation, global reoptimisation, deterministic bootstrap
resampling, legal squad size and read-only execution.
