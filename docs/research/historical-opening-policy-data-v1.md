# Historical opening-policy data v1

## Purpose

This audit establishes the exact retrospective inputs needed to compare the
six frozen opening-squad policies. It does not fit a forecast, optimise a squad
or select a policy.

Four immutable Vaastav archives are registered:

| Archive | Retrospective role |
| --- | --- |
| 2022/23 | first training seed |
| 2023/24 | target; train on 2022/23 |
| 2024/25 | target; train on 2022/23–2023/24 |
| 2025/26 | target; train on 2022/23–2024/25 |

There are therefore three target folds, not four. Treating the seed season as
an evaluated opening origin would leave it with no earlier registered
training data and would break the temporal design.

## Decision-time constraint reconstruction

For each target, the evaluator decompresses the immutable
`gameweeks_csv_brotli` payload, recomputes its SHA-256 and requires the frozen
capture identity. It reads only Gameweek 1:

- `element` to join the season-local row to the normalized stable player code;
- `position` and `team` to reconstruct squad constraints; and
- `value` as integer tenths of a million for the 100.0 million budget.

The raw `xP` column is explicitly excluded. Price is a decision constraint,
not a predictive feature. Inconsistent duplicate Gameweek 1 rows, unknown
identities, invalid positions or prices, incomplete normalized identity
coverage and an infeasible positional/budget pool all fail closed.

## Outcomes

Normalized official player-fixture rows are summed to player/Gameweek points
and minutes for Gameweeks 1–8. An opening player with no later archived row is
scored as zero for that Gameweek, which is the correct no-appearance outcome
for a squad that still contains the player. The audit records observed-player
counts and hashes both the opening cohort and complete outcome matrix.

Outcomes are scoring-only. They may not participate in model fitting,
transforms, player filtering, horizon choice or scenario generation for their
own target fold.

## Read-only command

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_opening_policy_data \
  --database /path/to/autofpl.db \
  --output /path/to/historical-opening-policy-data.json
```

The evaluator opens SQLite read-only, emits a deterministic artifact and
refuses to overwrite an existing output.

## Next registered step

The next evaluator must reconstruct preseason player means and scenario paths
using strictly earlier seasons, solve all registered 3/6/8 Gameweek
expected-points and downside-balanced policies, and score every selected
opening squad on a common realized Gameweek 1–8 outcome. Fixture information
used by a forecast must either be proven available at the opening cutoff or be
labelled as a fixed retrospective proxy.

Only after the aggregate metric and failure-slice rule are frozen may those
three target outcomes be opened to select a policy. The selected policy then
enters prospective 2026/27 scoring; this audit by itself cannot promote or
serve anything.
