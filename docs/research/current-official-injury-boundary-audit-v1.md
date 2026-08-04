# Current official injury boundary audit v1

## Question

Does the latest cutoff-safe Premier League injury-page capture resolve every
raw row to the exact official player population, and do any resolved or
unresolved rows touch the selected opening squad or its globally solved direct
alternatives?

## Method

The audit binds three immutable identities before reading any row:

1. the current lineup-evidence boundary artifact and its selected 15-player
   squad;
2. the five distinct direct alternatives from the exact global forecast
   sensitivity solve; and
3. the latest Premier League injury snapshot for the same official capture at
   or before an explicit evidence cutoff.

It rechecks the retained Brotli length and SHA-256, fixed 20-club DOM schema,
bounded row shape, source lineage and cutoff. A raw row is considered resolved
only when the production extractor emitted one immutable claim with the same
source revision, content hash and exact source span. The audit does not run a
second fuzzy matcher. Unresolved rows remain named source gaps; a shared club
can narrow manual review but never creates a player association.

## Live result

At official capture `#41` and evidence cutoff `2026-08-04T10:40:00Z`, source
snapshot `#229` revision `35` contains 53 rows across all 20 clubs. Forty-nine
rows bind exact official identities and four remain unresolved, for an identity
resolution rate of `0.924528`.

The unresolved rows are Adam Webster at Brighton and Hove Albion, Emil Krafth
and Sandro Tonali at Newcastle United, and Willy Boly at Nottingham Forest.
The exact official capture has no current identity for Webster, Krafth or Boly;
it places Tonali at Spurs rather than the stale Newcastle source team. Tonali's
source injury field is also the literal placeholder `-`. Three resolved rows
for Alysson, Anton Stach and Joelinton carry the same placeholder, so four of
the 53 source rows lack a meaningful injury detail. These rows are retained as
source-quality evidence and are not guessed onto another player.

No resolved injury row belongs to the selected 15 players or the five direct
alternatives. Absence from an injury list is not evidence of fitness, so this
result does not clear Van Hecke, Enzo or any other player. It only establishes
that the captured official injury source contributes no exact selected-or-
boundary doubtful flag at this cutoff.

## Decision

Keep the selected v2 squad unchanged. Preserve all four unresolved rows and all
four placeholder injury details as visible source-quality gaps. Continue
cutoff-safe collection through the deadline, but do not apply an injury
probability or forecast mutation until exact resolved claims have prospective
outcome support.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_official_injury_boundary_audit \
  --database /path/to/autofpl.db \
  --evidence-cutoff-utc 2026-08-04T10:40:00Z \
  --output /path/to/current-official-injury-boundary-audit.json
```

The retained result is
[current-official-injury-boundary-audit-v1.json](results/current-official-injury-boundary-audit-v1.json).
Its data identity is
`b5594756050f5b13cd1d6a604e268d216db4020b9f31e4a87e0593426dd8938a`,
its run identity is
`831fd818a66d61a7cd0ddb7a62327c494723ea04d963750ae92a33292fa5cd49`,
and the retained file SHA-256 is
`307670b6dbe346873093816885bbff8a0b8e633c5a582c987003a5a2a4ef7333`.
