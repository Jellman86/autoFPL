# Current lineup-evidence boundary audit v1

## Question

What cutoff-safe lineup or availability evidence currently touches the
best-supported opening squad, and can it be applied without confusing a
predicted start with a Gameweek appearance?

## Result

The audit binds the selected opening-squad v2 artifact at official capture
`#19` to the latest exact-capture claims available by
`2026-07-30T01:15:25.301306Z`. It collapses the immutable claim tape to the
latest row for each player, source and target before counting coverage.

Fantasy Football Scout covers all 15 selected players in revision 16:

- 13 are included in the predicted XI;
- Enzo and Van Hecke are omitted from complete predicted XIs; and
- no selected player has a second independent source in this capture.

The latest selected-player claims became available at
`2026-07-29T22:40:09.899252Z`, only `87.568372` seconds after the official
capture used by the forecast. All claims remain quarantined.

The result is useful and decision-relevant, but it does not justify replacing
Enzo's or Van Hecke's `0.921053` appearance probability with zero. A predicted
XI is a categorical start forecast. A player omitted from it can still appear
as a substitute, while the incumbent hurdle models any appearance rather than
a start.

## Decision

Leave the served v2 squad unchanged for this slice and keep both players on the
pre-deadline risk list. The next registered model challenger must separate:

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

strAIghtred remains explicitly classified as dependent consensus. Where it is
available, its agreement or disagreement is visible, but it is never counted
as an additional independent vote.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_lineup_evidence_boundary_audit \
  --database /path/to/autofpl.db \
  --evidence-cutoff-utc 2026-07-30T01:15:25.3013069Z \
  --output /path/to/current-lineup-evidence-boundary-audit.json
```

The retained result is
[current-lineup-evidence-boundary-audit-v1.json](results/current-lineup-evidence-boundary-audit-v1.json).
Its data identity is
`194607cd911db5418cea590adca4ec10c83aa8ee72d0ead08ee0cceb9038a498`
and its run identity is
`daa8770f5adc05a2ce63139ef5e5c41a2dc824358913f3c2098a4cb4355a4d1a`.
An independent rerun was byte-identical with file SHA-256
`3362ccfc7ee4970d0128595f3ef8fde73086b01ac937677bcc879149808cbf7a`.
