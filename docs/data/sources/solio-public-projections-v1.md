# Solio public projections v1

- **Source ID:** `solio-public-projections/v1`
- **Status:** Admitted for prospective shadow capture only
- **Purpose:** Compare an independent public Gameweek 1 points model with the
  best-supported autoFPL opening squad without silently changing advice.
- **Canonical endpoint:**
  `https://fpl.solioanalytics.com/api/data/latest.json`.
- **Declared data source:**
  `https://fpl.solioanalytics.com/api/data/latest` in the retained JSON.
- **Transport:** Quark's pinned hardened Spider MCP. No caller-controlled URL,
  authentication, browser session or proxy is accepted.
- **Timing:** the source `generatedAt`, target `deadlineIso`, completed capture
  time and official FPL deadline are retained. Generation must precede
  capture, and capture must precede the exact target deadline.
- **Identity:** source key, source class, dependence group, canonical and final
  URL, transport, official capture, revision, decompressed byte count and
  SHA-256 are all checked before use.
- **Coverage:** only `topProjected` is published. At least 20 rows, an 80%
  exact identity match and coverage of all four FPL positions are required.
  The live preimplementation check matched all 30 published rows exactly.
- **Method boundary:** matched Gameweek 1 means may form a visible squad
  challenger. Later Gameweeks and unpublished players retain autoFPL values.
  External means do not fabricate scenario variance or alter the incumbent's
  exact retained paths.
- **Serving:** never direct. Each revision produces an immutable,
  `influencesAdvice: false` artifact and a decision-room comparison. The
  best-supported v2 prediction remains the recommendation.
- **Quality status:** prospectively unscored. The provider publishes no
  retained historical folds through this endpoint, so retrospective promotion
  evidence cannot be reconstructed honestly. Frozen current artifacts can be
  scored after official outcomes arrive.
- **Failure behavior:** malformed JSON, wrong target, late generation or
  capture, wrong lineage, hash/size mismatch, insufficient coverage,
  ambiguous identity or illegal optimized squad fails closed.

This record admits a bounded external challenger, not a promoted feature.
See the [challenger specification](../../research/current-public-projection-opening-squad-v1.md).
