# FPL Form external evaluation v1

## Status and question

- **Evaluator:** `fpl-form-external-evaluation-v1`
- **Research status:** exploratory external baseline, not promoted
- **Target:** official FPL player total points for one Gameweek
- **Unit:** player-Gameweek

This evaluation asks how FPL Form's public fixture predictions compare with
later official outcomes. It does not assume that the source improves autoFPL,
train a model, populate the decision room or promote any feature.

## Point-in-time pairing

For each season and Gameweek, the read-only operator command:

1. ignores captures retrieved after the official deadline and selects the
   latest remaining immutable FPL Form capture;
2. requires the implemented identity report to be complete against an official
   catalogue available by both forecast retrieval and deadline;
3. refuses to fall back to an earlier capture when the latest eligible capture
   has incomplete identity coverage;
4. selects the latest immutable official outcome revision; and
5. requires that outcome to be available after the deadline and to contain
   every resolved forecast player.

The report records the forecast, identity-catalogue and outcome capture IDs,
availability times and content hashes. Missing outcomes, incomplete identity
and invalid chronology are explicit exclusion reasons. Source predictions and
official outcomes remain private in SQLite.

## Compared values

`fpl-form-published-conditional-points` sums the provider's fixture values for
each player. The provider describes these values as conditional on appearing,
but evaluation deliberately retains zero-minute outcomes. The report includes
a zero-minute slice so this interpretation mismatch stays visible.

`autofpl-appearance-probability-adjusted-points` is a separately named
challenger:

```text
sum over fixtures(predicted points * appearance probability)
```

It is computed for a player only when every predicted fixture has an appearance
probability. Missing players are counted; the challenger remains present with
`metrics: null` when no complete probabilities exist. It is not represented as
the provider's published forecast.

Double-Gameweek fixture predictions are summed to the player-Gameweek target.
The fold reports fixture and player counts so those cases can be separated once
real samples exist.

## Metrics and slices

Both candidates report:

- sample count;
- mean absolute error;
- root mean squared error;
- signed bias, defined as prediction minus outcome;
- position slices; and
- a zero-minute slice when present.

Metrics are descriptive until enough Gameweeks exist for stable comparisons
with the retained autoFPL incumbents. No p-value, promotion decision or
predictive-quality claim is produced by this command.

## Reproducibility

Run:

```text
dotnet AutoFpl.Api.dll --evaluate-fpl-form-forecast [season-code]
```

The command reads SQLite without writing evaluation state and emits one
deterministic JSON document to stdout. Exit code `0` means at least one complete
forecast/outcome pair was scored. Exit code `2` returns a machine-readable
`insufficient-data` report. `dataIdentitySha256` binds selected source and
official content identities; `runIdentitySha256` additionally binds evaluator
version, interpretation, exclusions and results.

Redirect stdout to a new operator-managed report file. Do not overwrite prior
reports: corrected source or outcome captures should produce a distinct
identity that remains comparable with earlier evidence.

## Promotion gate

Neither candidate may become a product forecast or model feature until real
out-of-time samples cover relevant positions, appearance outcomes and
single/double Gameweeks, failure and missingness are reported, and an ablation
beats retained incumbents on predeclared accuracy and calibration criteria.
