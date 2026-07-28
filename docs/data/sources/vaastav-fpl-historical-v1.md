# Vaastav historical FPL archive v1

- **Source ID:** `vaastav-fpl-historical/v1`
- **Status:** Admitted for pinned private historical research
- **Purpose:** Backfill 2024/25 and 2025/26 player identity, match performance,
  minutes, starts, underlying events and final archived availability state
  before autoFPL's own point-in-time collection existed.
- **Canonical source:** Vaastav's public
  [`Fantasy-Premier-League`](https://github.com/vaastav/Fantasy-Premier-League)
  repository at commit
  `f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a`.
- **Resources:** fixed commit-addressed `players_raw.csv` and
  `gws/merged_gw.csv` files under registered `data/2024-25` and
  `data/2025-26` paths; callers can choose only one of those season codes and
  cannot supply a URL or revision.
- **Authentication:** none.
- **Transport:** bounded direct HTTPS GET from `raw.githubusercontent.com`;
  redirects and automatic decompression are disabled.
- **Publication and availability:** the pinned commit timestamp,
  `2026-06-17T12:19:44Z`, is retained as repository publication evidence.
  Runtime retrieval and availability are the completed import time. This does
  not claim that every archived field was published before each original
  season's Gameweek deadline.
- **Integrity:** both seasons require source revision
  `f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a`, players SHA-256
  `75686051b265cbe7755ac71213ecaad21b26ee1cc46a8bafbba19c39ce894b05`
  and Gameweeks SHA-256
  `5bbbcba6353b4c72ad273adcc8e3aa451946a826564679788f45b1cb3325b84e`
  for 2024/25, and players SHA-256
  `412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b`
  and Gameweeks SHA-256
  `0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d`
  for 2025/26. Normalization expects 784 players and 27,283 player-fixture
  rows for 2024/25 after excluding the 20 temporary Assistant Manager entries
  and their 322 rows, and 841 players plus 29,747 player-fixture rows for
  2025/26 after deterministically collapsing ten normalized-equivalent
  duplicate source rows. Conflicting duplicates fail.
- **Identity:** season-local `element` values join to `players_raw.id`; the
  archive's official `code` is retained as the stable cross-season player key.
  Unknown elements and duplicate player IDs/codes fail atomically.
- **Retained features:** fixture time and context, minutes, starts, FPL points
  and scoring events, BPS/ICT components, expected goals/assists/involvement/
  conceded and defensive actions. Player final status, chance, news timestamp
  and a news-content hash are retained; normalized news text is not served.
  The four defensive-action fields absent from 2024/25 remain null, never
  invented zeroes.
- **Deliberately excluded:** `xP` is never normalized or served because the
  dataset maintainers warn it can contain same-Gameweek look-ahead. Selection,
  transfer and price columns are not admitted by this slice.
- **Retention:** exact CSV bytes are Brotli-compressed in private SQLite;
  normalized rows and capture metadata are immutable and non-deletable.
- **Serving:** the read API exposes only provenance, hashes and coverage counts,
  never raw CSV or player rows. A separate audit revalidates both pinned
  archive identities and compares only exact official player codes; it reports
  534 shared identities, 250 departures and 307 introductions from the
  normalized 2024/25-to-2025/26 cohorts without a name fallback.
- **Model status:** data-ready only. No value affects Baseline v0 until a
  separately versioned cross-season state contract is fitted and improves
  registered rolling-origin evaluation.

Archived final health is a long-run durability prior, not current medical
evidence. Current official availability, current news and current-season
participation must dominate it when the cross-season feature is implemented.
