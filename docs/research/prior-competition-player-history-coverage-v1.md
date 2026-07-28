# Prior-competition player-history coverage v1

## Purpose

This audit turns missing cross-season identity into a concrete collection
target. It reads one immutable provisional participation artifact, separates
players at explicitly configured prior-competition clubs from other
new/transferred players, and lists every gap using the current official stable
player code.

The audit is an exploratory coverage artifact. It does not invent player
history, alter a probability or authorize a source to influence advice.

## Identity and collection boundary

Names are review evidence, not automatic identity. Each admitted source player
must be explicitly mapped from a source-specific player ID to exactly one
current official `player.code`. Ambiguous, duplicate and missing mappings remain
unresolved; fuzzy name matching cannot silently close a gap.

The required capture boundary reuses Quark's existing Spider, Playwright and
research egress services. A typed adapter must retain:

- source, competition and season identity;
- canonical URL, source revision, content hash and collector-code revision;
- publication, retrieval and availability times;
- source player ID/name/team and the reviewed official-code mapping; and
- match identity, kickoff, opponent, minutes and starting status.

Goals, assists, shots, expected goals/assists, saves and cards are optional
source fields with explicit coverage. Missing values remain null rather than
being converted to observed zero.

## Chronology and scientific gate

Only content available by the forecast decision cutoff may enter its artifact,
and each match observation must precede the target. A collected source remains
quarantined until schema, chronology and identity coverage pass.

Closing an identity gap is not evidence of predictive value. Prior-competition
history must be compared with the incumbent on identical temporal folds, with
current official health retaining precedence, before any field can influence a
forecast.

## Command

```text
python -m autofpl_analytics.prior_competition_coverage \
  --forecast /path/to/preseason-participation-v1.2.json \
  --prior-competition-club "Coventry City" \
  --prior-competition-club "Hull City" \
  --prior-competition-club "Ipswich Town" \
  --output /path/to/prior-competition-coverage.json
```

The command is deterministic, refuses to overwrite an existing output, and
cryptographically verifies the complete source forecast before reporting
coverage.
