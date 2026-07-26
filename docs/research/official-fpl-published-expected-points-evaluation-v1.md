# Official FPL published expected-points evaluation v1

## Status and question

- **Evaluator:** `official-fpl-published-expected-points-evaluation-v1`
- **Research status:** exploratory external baseline, not promoted
- **Source value:** official FPL `ep_next`
- **Target:** later official FPL player total points for one Gameweek
- **Unit:** player-Gameweek

This evaluation asks whether the provider-published next-Gameweek expected
points are an accurate standalone forecast. It does not assume that the value
improves autoFPL, train a model, populate Baseline v0 or promote a feature.

## Point-in-time pairing

For each season and Gameweek with a retained final outcome, the read-only
operator command:

1. selects the latest official capture available by its recorded deadline for
   which that Gameweek was the provider's `next_gameweek_number`;
2. does not fall back to an older capture when the selected capture is missing
   any published value;
3. selects the latest immutable official outcome correction;
4. requires the outcome to have become available after the deadline; and
5. joins players by the provider's season identity code, requiring complete
   forecast and outcome coverage.

The report records forecast and outcome capture IDs, availability times and
bootstrap, fixture and live-response hashes. Missing captures, incomplete
values, incomplete player coverage and invalid chronology are explicit
Gameweek-level exclusion reasons. The command never exposes retained raw
provider JSON.

## Metrics and slices

The provider-published value is scored without modification using:

- sample count;
- mean absolute error;
- root mean squared error;
- signed bias, defined as prediction minus outcome;
- position slices; and
- a zero-minute slice when present.

Zero-minute players remain in the scored population. This is essential because
a useful unconditional expected-points forecast must carry non-appearance risk
rather than receiving a favourable evaluation only among players who appeared.
Metrics are descriptive until multiple out-of-time Gameweeks cover relevant
positions, availability states and fixture patterns.

## Reproducibility

Run:

```text
dotnet AutoFpl.Api.dll \
  --evaluate-official-fpl-expected-points [season-code]
```

The command opens SQLite read-only and emits one deterministic JSON document to
stdout. Exit code `0` means at least one complete forecast/outcome pair was
scored. Exit code `2` returns a machine-readable `insufficient-data` report.
`dataIdentitySha256` binds selected source/outcome content identities and
exclusions; `runIdentitySha256` additionally binds evaluator version,
interpretation and results.

Redirect stdout to a new operator-managed report file. Do not overwrite an
earlier report: later official corrections produce a distinct identity and
should remain comparable with previous evidence.

## Promotion gate

`ep_next` cannot replace Baseline v0 or become a model feature until sufficient
real out-of-time folds exist, its failure and missingness slices are reported,
and a predeclared same-cohort comparison demonstrates value against retained
incumbents. A later feature ablation must keep raw missingness and source
identity explicit rather than treating the provider forecast as ground truth.
