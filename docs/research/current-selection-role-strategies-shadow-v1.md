# Current selection role strategies shadow v1

## Purpose

This bounded shadow asks a deliberately smaller question than transfer
optimisation: given the same 15 players, can XI, bench order and captaincy be
arranged differently for balanced, downside-protecting or upside-seeking
preferences?

Every candidate is scored on the exact same retained joint scenario rows using
`cpu-joint-scenario-reference-v1`. Goalkeeper and ordered outfield
substitutions, formation legality, captain fallback and unreplaced starters
therefore remain identical to the current model and user comparison.

The result is exploratory. It cannot alter advice or a user selection.

Migration 28 persists one immutable set for the exact current selection-score
artifact and search version. The network-isolated worker writes the
capture/revision-named handoff only after its score prerequisite is present.
The application revalidates score lineage, formation and role legality,
distribution summaries, objective values and paired comparisons before
storage. `GET /api/v1/selections/current/role-strategies-shadow` returns the
artifact only while its official capture, scenario, baseline forecast and
latest user revision are all current.

## Registered strategies

The v1 search fixes three objectives before examining a candidate:

- **balanced** maximises arithmetic mean scenario points;
- **safer** maximises the mean of the worst 20% of scenario totals, a bounded
  lower-tail expected-points objective; and
- **higher ceiling** maximises the mean of the best 20% of scenario totals.

Mean points break objective ties, followed by the canonical selection key.
The fixed 20% tail contains eight of the current 38 rows. This is less brittle
than selecting on one extreme row, but it remains coarse.

## Search boundary

`deterministic-role-beam-v1` begins at the persisted model selection. One
neighbour may:

- choose any ordered captain and vice-captain pair from the XI;
- permute the three outfield bench positions;
- swap the starting and replacement goalkeepers; or
- swap one starting and bench outfielder while retaining legal formation and
  inheriting captaincy when the outgoing starter held it.

The search keeps the best 12 legal candidates for three iterations. It is
deterministic, uses the exact CPU reference for every complete candidate and
records the unique workload. It does not claim a global optimum. The three
iterations permit combined role changes while keeping the workload
representative of the current product need.

## Current GW1 shadow

Against the deployed capture 16 snapshot, the search evaluated 4,008 unique
legal candidates in about two seconds on the local development machine:

| Strategy | Mean | P10 | P90 | Mean delta vs model |
| --- | ---: | ---: | ---: | ---: |
| Current model | 29.2105 | 15.7 | 44.3 | 0 |
| Balanced | 34.6579 | 20.0 | 57.6 | +5.4474 |
| Safer | 33.7105 | 20.0 | 53.3 | +4.5000 |
| Higher ceiling | 33.6842 | 16.0 | 58.3 | +4.4737 |

All three selected strategies differ from the current model roles. The large
in-sample differences are a product-development signal, not an accuracy claim:
the same 38 empirical rows generated the candidates and measured them. The
frozen strategies must be scored on genuinely new outcomes before they can
support promotion.

The retained identities are:

- data identity
  `ee80ae121834d9700db9df58a69bc3a7a85eba688f449ae9409f3ccd4e40e7eb`;
  and
- run identity
  `104110f62ef6901fef0909fd1aac3383eab41f6a79c48fbfcc2cca0ce5ab1fc5`.

This measured role-only workload does not justify GPU acceleration. GPU parity
and benchmarking should wait for transfer-aware or multi-Gameweek search whose
candidate volume demonstrates a real CPU bottleneck.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 -m \
  autofpl_analytics.current_selection_strategies \
  --database /path/to/stable-autofpl.db \
  --output /path/to/current-selection-strategies.json
```
