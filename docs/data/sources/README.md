# Data-source provenance register

This register records where inputs came from, when they were available and how they behave. It supports reproducibility, temporal-leakage prevention and source/feature evaluation; it is not an enterprise permission register.

Public sources may be evaluated without direct written permission. An entry becomes usable when its collection path is technically bounded and its timing, identity and quality semantics are sufficient for the intended experiment.

## Status meanings

- **Proposed:** candidate awaiting technical or scientific evaluation.
- **Admitted:** available for the recorded private research purpose and collection method.
- **Rejected:** unusable for the stated purpose because of quality, leakage, reliability, access, safety or concrete legal problems.
- **Deprecated:** no new snapshots; historical experiment references remain immutable.

A live connector requires tests and a source record, but not a separate vendor-style approval ceremony. Material changes to fields, timing semantics or collection method update the record so experiments remain reproducible.

## Initial records

| Source ID | Status | Scope |
|---|---|---|
| [`manual-user-input/v1`](manual-user-input-v1.md) | Admitted | Deliberate user input into the private decision-support workflow |
| [`repository-synthetic-fixtures/v1`](repository-synthetic-fixtures-v1.md) | Admitted | Fictional deterministic fixtures authored for tests and examples |
| [`official-fpl-api/v1`](official-fpl-api-v1.md) | Admitted | Fixed-origin public player, Gameweek, team, fixture and outcome captures for private point-in-time research |
| [`fpl-form-public-forecast/v1`](fpl-form-public-forecast-v1.md) | Admitted | Fixed-origin public conditional predicted-points captures for private external-baseline evaluation |
| [`public-research-shadow-sources/v1`](public-research-shadow-sources-v1.md) | Admitted for shadow capture | Fixed-registry official availability, specialist lineup and dependent-consensus page snapshots for private source evaluation |
| [`vaastav-fpl-historical/v1`](vaastav-fpl-historical-v1.md) | Admitted | Commit-pinned 2025/26 player and fixture outcomes for private cross-season performance and durability research |

## Baseline boundaries

Source records must not introduce:

- passwords, authentication tokens, account cookies or account-write instructions;
- login/paywall bypass, private user data or impersonation;
- arbitrary private-network fetching, caller-controlled proxies or shared browser sessions;
- unnecessary personal data, secrets, executable payloads or public redistribution of bulk third-party content;
- future-known evidence presented as though it existed at the decision deadline.

Public web retrieval, scraping, browser rendering, search, public read-only endpoints and Byparr-assisted challenge handling are not universally prohibited. They are evaluated as engineering transports under the [data-quality and provenance standard](../../standards/data-governance.md).

Source arrivals remain untrusted. Promotion into curated features or serving forecasts requires schema, identity, chronology and out-of-time predictive evaluation. The [source-record v1 contract](../../../contracts/data-source/v1/source-record.schema.json) remains the envelope for the two initial manual/synthetic sources. Official FPL and FPL Form runtime captures use source-specific SQLite schemas and the timing/content rules in their records; no endpoint exports raw provider data.

## Review record

The initial records were created by [issue #16](https://github.com/Jellman86/autoFPL/issues/16) and [PR #18](https://github.com/Jellman86/autoFPL/pull/18). Their original legal-admission framing is superseded by ADR-0009; their provenance and point-in-time evidence remain valid.
