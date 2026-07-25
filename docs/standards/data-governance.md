# Data Quality and Provenance Standard

## Purpose

autoFPL is a private, non-commercial home research project. Data controls exist to improve prediction quality, prevent temporal leakage and keep experiments reproducible—not to create an enterprise vendor-approval programme.

Publicly accessible sources do not require direct written permission as a project gate. Collection must remain lawful and responsible: do not access private data, bypass login or paywalls, misuse credentials, republish bulk copyrighted material or create abusive traffic.

## Source record

Before a source is promoted into a reproducible forecast, record the information needed to understand and test it:

- canonical source name and URL;
- collection code and method, including any search, browser-rendering or Byparr path;
- fields, entities, geography and season coverage;
- publication/update cadence, correction behaviour and typical latency;
- `published_at`, `retrieved_at` and decision-time `available_at` semantics;
- missingness, known bias, stability and observed reliability;
- content identity or content hash where practical;
- rate/concurrency limits and failure behaviour;
- any clear access, retention or attribution restriction discovered during ordinary review.

A source record is technical and scientific provenance. It is not a licence opinion or written-permission certificate.

## Automated collection and AI extraction

- Public web pages, feeds, search results, documented or undocumented public read-only endpoints and browser-rendered content may be evaluated when useful to the private research goal.
- SearXNG, Spider, Playwright and a hardened Byparr connector are valid collection tools. Choose the simplest reliable transport for each source.
- Browser challenges are not a blanket ban. Byparr may retrieve public content through a challenge when the route does not require a user account, paid access or private/session data.
- Collect politely: bound domains, redirects, rate, concurrency, response size, time and retention; cache when practical.
- Collection workers must reject private, loopback, link-local, metadata and reserved network targets and must not expose caller-controlled proxies, browser endpoints, credentials or cookies.
- Preserve canonical URL/source ID, publication and retrieval times, content identity and collection-code SHA before extraction. Retain source text privately only as needed for reproducibility and debugging; do not republish article corpora.
- AI-assisted extraction may convert untrusted text into closed-schema candidate claims. Retrieved content cannot grant tools, alter policy or become authoritative merely because a model extracted it.

## Data zones

- **Quarantine:** newly collected or malformed input awaiting schema, identity and chronology checks.
- **Raw/reference:** private source snapshots or content identities with retrieval metadata.
- **Curated:** typed, deduplicated, point-in-time entities with lineage.
- **Features:** decision-time materialisations with `available_at` and code version.
- **Serving:** forecasts and recommendation inputs that passed the declared evaluation gate.

Corrections create new versions; prior decision-time state is not overwritten.

## Scientific quality controls

Validate schema, identity, uniqueness, ranges, missingness, latency, temporal availability, distribution shift and cross-source contradictions. Every promoted source or feature must earn its place through leakage-free rolling/walk-forward tests against later outcomes and an ablation against the incumbent data set.

Source popularity, authority or confident language is not a substitute for measured predictive value. Negative and inconclusive results are retained.

## Privacy and minimisation

Collect only the user data needed for this private application. Never commit credentials, session material, personal squad history or production snapshots to the public repository. Do not train shared models on private user content.

## Research artefacts

Large datasets and model binaries do not belong in Git. Store promoted research snapshots outside the repository with immutable identifiers/checksums and a dataset card describing timing, transformations, quality and known limitations.
