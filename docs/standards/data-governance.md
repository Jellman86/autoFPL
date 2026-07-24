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

## Data zones

- **Quarantine:** untrusted arrivals; no model or product use.
- **Raw immutable:** validated byte-for-byte source objects with hash and ingest metadata.
- **Curated:** typed, deduplicated, point-in-time entities with lineage.
- **Features:** decision-time materialisations with `available_at` and code version.
- **Serving:** approved forecasts and recommendation inputs.

Promotion between zones is explicit and audited. Raw corrections create a new version; history is not overwritten.

## Provenance

Every record or partition must be traceable to source, source revision, retrieval/receipt time, content hash, transformation code SHA and schema version. Derived statistics preserve their upstream snapshot IDs.

## Quality controls

Validate schema, uniqueness, referential integrity, ranges, missingness, timeliness, distribution shift and cross-source contradictions. Quarantine failures; do not silently coerce. Quality thresholds and exception owners are versioned.

## Privacy and minimisation

Collect the minimum necessary user data. Separate identity from analytical data, use pseudonymous internal IDs, document retention and deletion, and test deletion end-to-end. Do not train shared models on private user content without explicit informed consent and a reviewed purpose.

## Research artefacts

Large datasets and model binaries do not belong in Git. Store them in governed object storage or an approved data-version system with immutable IDs and checksums. A dataset card is required for every research or serving snapshot.
