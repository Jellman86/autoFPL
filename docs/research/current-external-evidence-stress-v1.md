# Current external-evidence stress v1

## Decision

External lineup and injury evidence is now folded into the opening-squad
decision as an explicit stress test, not as an unearned probability adjustment.
The central appearance-hurdle v2 prediction remains unchanged. Each stress
world sets the affected players to zero minutes and zero points in Gameweek 1,
leaves Gameweeks 2–6 unchanged, and globally re-solves the same registered
six-Gameweek expected-points problem.

This answers a narrower and honest question:

> If this source's adverse Gameweek 1 assertions are correct, would another
> legal opening squad be better, and what would that alternative cost if the
> source is wrong?

The artifact is deterministic, cutoff-bound, read-only at generation time and
non-serving to the central forecast. It does not assign a source probability.
Straight Red is retained as dependent consensus evidence and is excluded from
the all-independent-sources world.

## Retained current result

The retained run uses official capture `20`, the forecast cutoff represented by
that exact capture, and an evidence cutoff of `2026-07-30T05:35:00Z`. It
collapses the evidence tape to 536 latest claims, of which 337 are adverse.
Two selected players carry adverse FFScout assertions: Van Hecke and Enzo.

Six exact stress worlds were solved:

| Stress world | Squad response | If stress is true | If stress is false | Decision |
|---|---|---:|---:|---|
| Van Hecke only | Van Hecke → Milenković | -0.08 | -0.26 | retain |
| Enzo only | Enzo → Anderson | +1.08 | +0.37 | reject as source-inconsistent |
| selected joint | Van Hecke + Enzo → Milenković + Anderson | +2.05 | +0.42 | reject as source-inconsistent |
| all FFScout claims | Van Hecke → Milenković | +0.71 | -0.53 | review if source trusted |
| all official injury claims | unchanged | 0.00 | 0.00 | retain |
| all independent sources | Van Hecke → Milenković | +0.71 | -0.53 | review if sources trusted |

The Enzo alternative is not admissible as a source-consistent recommendation:
the same FFScout revision also carries adverse evidence for Anderson. The
source-wide and all-independent cases instead produce a coherent single swap,
Van Hecke to Milenković. That alternative gains `0.710527` exact scenario
points if the source-adverse world is imposed and loses `0.526316` points under
the unchanged forecast world.

This is actionable sensitivity, not evidence that the swap should already be
made. The UI therefore shows the conditional alternative and both signed
consequences while preserving the central squad.

## Automation and product boundary

The analytics worker waits for the exact best-supported opening squad, detects
the newest supported pre-deadline evidence claim, generates an artifact keyed
to that claim revision and official capture, and hands it to the private
application inbox. The backend validates official player and claim identities,
cutoffs, solver status and the no-source-probability boundary before immutable
persistence. A typed current endpoint feeds the decision-room stress panel.

When a later supported claim becomes available before the recorded deadline,
the worker produces a new artifact. It does not rewrite the prior artifact.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_external_evidence_stress \
  --database /path/to/autofpl.db \
  --evidence-cutoff-utc 2026-07-30T05:35:00Z \
  --output /path/to/current-external-evidence-stress.json
```

The full artifact contains the private claim-level provenance required by the
product importer and is retained in SQLite rather than published in the
repository. Its data identity is
`6511dd8f472d31f227bb050e2327a6e6a6fe2ea372d8efedf93b0b9adcf2abc9`
and its run identity is
`458b91850e007814b01cb091f5f8df6b36a5cb6c73bb41f3a813719eaccc2339`.

## Next evidence gate

Stress testing is the correct bridge while source reliability is unlearned.
After completed Gameweeks provide balanced source outcomes, a registered
no-source versus source-feature challenger may estimate incremental value on
strictly later folds. Only then may a calibrated source contribution compete
for promotion into the appearance model. Full generative Monte Carlo remains a
separate next-stage distribution engine; it must reproduce this deterministic
reference before GPU acceleration is considered.
