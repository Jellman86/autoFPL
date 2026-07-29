# Selected opening-squad prospective outcome evaluation v1

## Purpose

This evaluation is the untouched 2026/27 test of the registered selected
opening-squad policy. It scores the final immutable predeadline squad and all
eight frozen weekly role decisions only after official outcomes become
available. It never rebuilds the squad, changes a captain or opens a later
outcome while making a preseason decision.

The complete machine-readable registration was frozen before the first target
outcome in
[selected-opening-squad-prospective-registration-v1.json](results/selected-opening-squad-prospective-registration-v1.json).
Its data identity is
`7b875e15eef7672d2b177f35c74c85bc59151cbce930abd8ed2e5ce01c09c887`.

## Frozen decision and benchmarks

The evaluated decision is the latest immutable
`current-selected-opening-squad-shadow-v1` artifact whose cutoff is strictly
before the recorded Gameweek 1 deadline. It must be the retained
`6-expected-points` policy and supplies:

- one fixed legal 15-player squad;
- Gameweek 1–8 XI, bench, captain and vice-captain roles; and
- its frozen preseason cumulative score distribution.

Both benchmarks come from the exact-capture
`current-initial-squad-quality-shadow-v1` artifact:

1. **Primary — served Baseline v0 GW1 roles held.** The current served squad,
   XI, bench and captaincy are frozen once and held through Gameweek 8.
2. **Diagnostic — single-Gameweek optimiser GW1 roles held.** The previous
   linear one-Gameweek shadow squad and roles are frozen in the same way.

The primary comparison therefore asks whether explicitly optimising the
opening decision over multiple Gameweeks improves on the product behaviour it
is intended to replace. The diagnostic comparison is reported but does not
control the gate. Neither benchmark is allowed to select new roles after
outcomes begin.

## Outcome boundary and scoring

For each Gameweek from 1 through 8, the evaluator selects the latest immutable
official outcome correction whose recorded availability is strictly after
that Gameweek's recorded deadline. It verifies the raw live-payload hash and
complete normalised player coverage.

Every decision uses the common CPU reference scorer:

- doubled captain points with vice-captain fallback;
- goalkeeper replacement;
- ordered, formation-preserving outfield auto-substitution; and
- zero points for a selected player who records no appearance.

Partial reports are allowed as Gameweeks arrive, but they cannot pass the gate.
The report is deterministic and read-only; a new official correction creates a
new data identity instead of rewriting an earlier result.

## Predeclared evidence gate

The gate is evaluated only after all eight official outcomes exist. Both checks
must pass:

1. the selected policy must beat the primary benchmark by at least two realised
   FPL points over Gameweeks 1–8; and
2. its realised cumulative score must be at or above the frozen preseason
   predictive distribution's 10th percentile.

Passing means **eligible for a separate promotion review**. The evaluator sets
`isPromotionDecision: false` and cannot change served advice. One prospective
season checks direction and downside safety but is not enough to estimate the
policy effect precisely, so promotion must also consider the separately
registered point, participation and minutes component evidence.

## Command

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.selected_opening_squad_outcome_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/selected-opening-squad-outcome.json
```

Before any eligible result exists, the command returns
`waiting-for-official-outcome` with exit code `0` and writes no artifact. Once
one or more outcomes exist, it writes a partial or complete result containing
the exact selected, benchmark, outcome, registration, data and run identities.

## Limitations

The primary benchmark is deliberately the current single-Gameweek served
advice held unchanged; it is not a strong separately optimised eight-Gameweek
challenger. Transfers, chips, price changes and manager intervention are
excluded from every policy. Promotion evidence for 2027/28 will therefore
combine this prospective result with the larger leakage-safe historical
comparison rather than treating eight weekly scores as independent trials.
