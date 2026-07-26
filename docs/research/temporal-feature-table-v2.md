# Temporal feature table v2

## Status

`official-temporal-v2` is an executable exploratory feature artefact. It is not
a fitted model, promoted forecast or performance claim. Its purpose is to make
the information leading into a prediction explicit, reproducible and safe to
use in later rolling-origin comparisons.

## Command

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.feature_table \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 2 \
  --output /path/to/features-2026-27-gw2.json
```

The command opens SQLite in read-only/query-only mode and refuses to overwrite
an existing artefact. It returns exit code `2` and a structured error on stderr
when configuration, schema, replay or retained outcome coverage is invalid.

## Point-in-time boundary

The target is one official pre-deadline replay. The artefact records its
deadline separately from its decision cutoff:

- `deadlineUtc` is the Gameweek deadline recorded in the selected capture;
- `decisionCutoffUtc` is when that exact capture became available;
- the selected replay must satisfy
  `replay.availableAtUtc <= target.deadlineUtc`;
- player history contains only earlier Gameweeks;
- an earlier outcome revision enters only when
  `outcome.availableAtUtc <= replay.availableAtUtc`; and
- for each earlier Gameweek, the newest correction satisfying that cutoff wins.

Using capture availability rather than a future deadline matters for a live
run: an outcome or correction received after the selected capture cannot
silently enter its feature table. Player IDs are matched only within a season.
Cross-season identity remains disabled until provider-code matching is explicit
and tested.

## Player features

Every player in the target replay receives:

- official identity, team, position, price, availability status, chance of
  playing, selected percentage and cumulative official points/minutes/starts;
- every target-Gameweek fixture with opponent, venue and kickoff;
- days from the most recent prior team kickoff to the first target kickoff;
- the minimum gap between target fixtures for a double Gameweek;
- the latest eligible prior Gameweek outcome;
- rolling 1/3/5-Gameweek means for points, minutes, starts, goals, assists,
  clean sheets, goals conceded, saves, bonus and cards;
- rolling 1/3/5-Gameweek means for the retained official expected-goal,
  expected-assist, expected-goal-involvement/conceded, ICT/BPS, influence,
  creativity, threat, defensive-action and penalty outcomes;
- a separate observed sample count for every nullable underlying metric, so a
  legacy missing value is never treated as a zero;
- rolling appearance and 60-minute rates; and
- an exponentially weighted version of the same history using the declared
  alpha.

Official event-live outcomes are Gameweek totals. A double Gameweek is
therefore one aggregate player-history observation and can exceed 90 minutes.
The artefact does not pretend these aggregates are per-match player rows.

New, promoted and previously unused players keep `sampleCount: 0`, `latest:
null`, null rolling values and a null exponentially weighted block. The
generator does not substitute a league, position or zero value for missing
personal history; model-specific fallback and missingness treatment belong
inside each training fold.

Outcome rows created before database migration 10 retain null underlying
values. A window can therefore contain more historical Gameweeks than observed
underlying-stat samples. Each `{metric}SampleCount` records that distinction,
and an underlying mean stays null until at least one eligible observation is
available.

## Team and opponent context

The table also includes one team feature row for every target-capture team.
Only finished, scored fixtures before the target Gameweek that are present in
the selected capture enter team history. It reports:

- the latest match and total match count;
- rolling 1/3/5-match goals for, goals against, points per match, clean-sheet
  rate and scoring rate;
- the same rolling values split into home and away samples; and
- exponentially weighted attacking, defensive and result form.

Player target fixtures reference opponent team IDs, so a later model can join
the same cutoff-correct opponent form without duplicating it across every
player row.

## Reproducibility

The artefact records the target bootstrap/fixture hashes and every admitted
history outcome ID, availability time and live-payload hash. A canonical
`dataIdentitySha256` identifies those inputs and a canonical
`runIdentitySha256` identifies the complete output and feature configuration.
The output contains no wall-clock generation time, so identical inputs produce
byte-equivalent values after canonical JSON serialization.

## Current limitations and next use

- No cross-season player history is admitted.
- Player outcomes are Gameweek aggregates; team form is fixture-specific.
- Underlying official values are provider observations, not an independent
  event-data feed, and are not yet model inputs. Their incremental value must
  be measured against the unchanged official feature baseline on identical
  expanding-origin folds.
- Rest-day features use scheduled kickoff gaps and do not infer travel or
  recovery burden.
- Status and chance fields are current to the selected capture but no richer
  timestamped news sequence is present.
- Rolling windows and the EWMA alpha are fixed candidate features, not
  retrospectively tuned evidence.

As complete 2026/27 outcomes accumulate, this table should feed regularised and
tree-based challengers inside the existing expanding-origin evaluator. Every
transform, encoder, imputer and hyperparameter choice must be fitted inside its
training fold. These features may populate product forecasts only after the
registered out-of-time promotion gate is satisfied.
