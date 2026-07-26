# Public research shadow sources v1

- **Source ID:** `public-research-shadow-sources/v1`
- **Status:** Admitted for private shadow capture only
- **Purpose:** Build a point-in-time corpus for evaluating availability and
  likely-start evidence without allowing it to influence forecasts.
- **Canonical sources:** the fixed runtime registry currently contains the
  Premier League injury page, Fantasy Football Scout predicted-lineup page and
  strAIghtred lineup-consensus page.
- **Transport:** Quark's pinned hardened Spider MCP
  `de35b3a9dd740542070fa2ee0e70bc804dde07ee`; static extraction by default and
  dedicated Spider rendering only where the registry requires it.
- **Authentication:** none; public pages only.
- **Timing:** retrieval and availability are the completed capture time.
  Provider publication/update time is not inferred at capture. Later extraction
  may retain a page-displayed timestamp as a separate field after validation.
- **Identity:** fixed source key and canonical URL, final URL, transport
  version, target official capture, season/Gameweek/deadline, per-source
  revision and SHA-256 of exact retained text.
- **Retention:** bounded Markdown is Brotli-compressed in private SQLite.
  Duplicate content for the same source and official target is idempotent.
- **Serving:** only inventory, timing, size, revision and hash metadata. Raw
  third-party text is not returned by the API or committed to Git.
- **Quality status:** unknown. Source classes and dependence groups are
  explicit. FFScout start/availability and strAIghtred consensus extraction
  produce only identity-checked `quarantined` claims; snapshots remain
  `shadow-only` and no derived value can influence a forecast until outcome
  scoring and same-fold ablation are complete.
- **Failure behavior:** unknown source keys, wrong MCP identity, redirects to a
  different resource, non-200 results, missing trust markers, oversized or
  malformed responses and missing official target identity fail closed without
  a partial snapshot.

This record admits a collection boundary, not a predictive feature. See the
[source portfolio](../../research/research-source-portfolio-v1.md) for the
scientific comparison plan.
