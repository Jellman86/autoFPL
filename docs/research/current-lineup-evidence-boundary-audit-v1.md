# Current lineup-evidence boundary audit v1.1

## Question

What cutoff-safe lineup or availability evidence currently touches the
best-supported opening squad or its globally solved replacement boundary, and
can it be applied without confusing a predicted start with a Gameweek
appearance?

## Result

The v1.1 audit binds the selected opening-squad v2 artifact and the exact
forecast-sensitivity solve at official capture `#20` to claims available by
`2026-07-30T05:35:00Z`. It collapses the immutable claim tape to the latest row
for each player, source and target before counting coverage.

Fantasy Football Scout covers all 15 selected players in revision 17:

- 13 are included in the predicted XI;
- Enzo and Van Hecke are omitted from complete predicted XIs; and
- no selected player has a second independent source in this capture.

The globally solved exit thresholds produce five distinct direct alternatives:
Kelleher, Darlow, Milenković, Anderson and João Pedro. FFScout also covers all
five. Darlow and Anderson are omitted, while Kelleher, Milenković and João
Pedro are included. None of the selected 15 or these five alternatives appears
in the captured official Premier League injury list.

The latest selected-player claims became available at
`2026-07-30T04:46:00.567681Z`, `438.128997` seconds after the official capture
used by the forecast. All claims remain quarantined.

The result is useful and decision-relevant, but it does not justify replacing
Enzo's or Van Hecke's `0.921053` appearance probability with zero. A predicted
XI is a categorical start forecast. A player omitted from it can still appear
as a substitute, while the incumbent hurdle models any appearance rather than
a start.

## Decision

Leave the served v2 squad unchanged for this slice and keep both selected
players on the pre-deadline risk list. The boundary evidence strengthens two
near-tie decisions: Kelleher is the clean direct alternative to Roefs, while
the Anderson evidence does not justify displacing Rayan. Darlow is not a clean
fallback. The next registered model challenger must separate:

1. probability of starting;
2. probability of appearing as a substitute;
3. zero minutes;
4. points and minutes conditional on starting; and
5. points and minutes conditional on a substitute appearance.

External start evidence can receive a learned weight only after it is scored
against exact start outcomes by source and lead-time bucket. The complete
mixture must then pass the existing proper-score evaluation and unchanged
3/6/8-Gameweek globally constrained policy screen. A categorical source is not
given an arbitrary probability merely because it is relevant to a selected
player.

That first fixed mixture has now been
[evaluated](historical-start-state-hurdle-points-evaluation-v1.md). Its
coherent start probability improves start Brier and log loss, but the resulting
point mean improves MAE by only 0.0811%, regresses RMSE and wins 4/8 folds. It
is rejected. Lineup state remains a valid participation input; the next point
challenger must reconstruct scoring components rather than further retune this
aggregate conditional tree.

strAIghtred remains explicitly classified as dependent consensus. Where it is
available, its agreement or disagreement is visible, but it is never counted
as an additional independent vote. Premier League injury listings are
classified as official availability aggregation and remain `doubtful` flags,
not invented absence probabilities or return dates.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_lineup_evidence_boundary_audit \
  --database /path/to/autofpl.db \
  --evidence-cutoff-utc 2026-07-30T05:35:00Z \
  --output /path/to/current-lineup-evidence-boundary-audit.json
```

The retained result is
[current-lineup-evidence-boundary-audit-v1.1.json](results/current-lineup-evidence-boundary-audit-v1.1.json).
Its data identity is
`2faa8020bcba0a9828626352a694d57aa7487d366bf3610bec8e1484211cc5bc`
and its run identity is
`54a9431fab2057bda8518f0e5a244014423cd4c483a155ed132d2c52bde5c2af`.
The retained file has SHA-256
`216e93165ae9a512d68aef086ae463b466164acfb1beca003d141f114d568e6e`.
