# Data Governance Standard

## Admission gate

No data source enters the platform until a source record identifies:

- owner/provider and authoritative URL;
- licence/contract and permitted purposes;
- collection method and explicit authorisation;
- fields, subjects, geography and season coverage;
- update cadence, correction policy and publication latency;
- personal/sensitive data classification;
- retention, deletion and redistribution rules;
- reliability, known bias and quality checks;
- accountable owner and review date.

Technical accessibility is not permission. Undocumented FPL endpoints and robots allowance do not establish contractual authorisation.

## Automated collection and AI extraction

- Automated collection uses only admitted, permissioned connectors such as licensed APIs/feeds, approved exports, RSS or web pages whose terms and access policy permit retrieval. Rate limits, robots controls, authentication boundaries and redistribution rights remain enforceable regardless of the technology used.
- “Scraping” is not a blanket product capability. In particular, the current compliance boundary forbids automated extraction from the FPL game and any login/session automation without written permission.
- Collection is a deterministic transport step: preserve the immutable source object, canonical URL/source ID, publication time, retrieval time, headers/metadata allowed by the source and content hash before any AI processing.
- AI-assisted extraction may convert permitted unstructured news or reports into candidate structured claims such as injury status, expected absence, role or probable minutes. Each claim records source snapshot/span, `published_at`, `retrieved_at`, `available_at`, model and prompt/schema versions, confidence, expiry and validation state.
- Candidate AI claims remain in quarantine until schema checks pass. Material claims require rule-based consistency checks and corroboration or human review according to a versioned risk policy before feature promotion.
- A model cannot override source rights, invent missing provenance or turn a blocked/failed retrieval into evidence. Collection failures remain explicit and no anti-bot, paywall, authentication or session control may be bypassed.

## Data zones

- **Quarantine:** untrusted arrivals; no model or product use.
- **Raw immutable:** validated byte-for-byte source objects with hash and ingest metadata.
- **Curated:** typed, deduplicated, point-in-time entities with lineage.
- **Features:** decision-time materialisations with `available_at` and code version.
- **Serving:** approved forecasts and recommendation inputs.

Promotion between zones is explicit and audited. Raw corrections create a new version; history is not overwritten.

## Provenance

Every record or partition must be traceable to source, source revision, retrieval/receipt time, content hash, transformation code SHA and schema version. Derived statistics preserve their upstream snapshot IDs. The [source-record v1 contract](../../contracts/data-source/v1/source-record.schema.json) is the executable envelope for the initial admitted manual/synthetic boundary; it is not a claim that the current API persists records.

## Quality controls

Validate schema, uniqueness, referential integrity, ranges, missingness, timeliness, distribution shift and cross-source contradictions. Quarantine failures; do not silently coerce. Quality thresholds and exception owners are versioned.

## Privacy and minimisation

Collect the minimum necessary user data. Separate identity from analytical data, use pseudonymous internal IDs, document retention and deletion, and test deletion end-to-end. Do not train shared models on private user content without explicit informed consent and a reviewed purpose.

## Research artefacts

Large datasets and model binaries do not belong in Git. Store them in governed object storage or an approved data-version system with immutable IDs and checksums. A dataset card is required for every research or serving snapshot.
