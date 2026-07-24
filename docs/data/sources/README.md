# Data-source admission register

No source may enter autoFPL merely because it is technically accessible. Admission records apply the [data-governance standard](../../standards/data-governance.md) and [FPL terms boundary](../../compliance/fpl-terms-boundary.md) before a connector, persistent dataset or derived feature is implemented.

## Status meanings

- **Proposed:** under review; not available for implementation or product use.
- **Admitted:** approved only for the exact purpose, fields and collection method in its record.
- **Rejected:** reviewed and forbidden for the stated purpose or method.
- **Deprecated:** no new snapshots; retained history follows the record's retention rules.

Admission is not connector approval. A live connector requires its own issue, threat/privacy review, tests and an already admitted source. A new field, purpose, collection method, retention rule or redistribution mode requires a new source-record version and review.

## Initial admissions

| Source ID | Status | Scope |
|---|---|---|
| [`manual-user-input/v1`](manual-user-input-v1.md) | Admitted | Direct, deliberate entry by the user into their own decision-support workflow |
| [`repository-synthetic-fixtures/v1`](repository-synthetic-fixtures-v1.md) | Admitted | Fictional deterministic fixtures authored in this repository for tests and examples |

## Universal prohibitions

No admitted source record may be used to introduce:

- FPL passwords, cookies, session material, authentication tokens or account-write instructions;
- automated access to or extraction from the FPL game;
- undocumented FPL endpoints, browser automation or request replay;
- anti-bot, authentication, paywall, rate-limit or access-control bypass;
- unnecessary personal data, secrets, hidden executable content or opaque bulk payloads;
- third-party data whose collection, purpose, retention or redistribution rights are not recorded;
- a claim that user entry, community use or robots policy cures an otherwise prohibited collection method.

Source arrivals start in quarantine. Promotion, persistence, derivation and serving require separate contracts and tests. The [source-record v1 contract](../../../contracts/data-source/v1/source-record.schema.json) defines the future provenance envelope and fictional examples; the current API does not emit, persist or ingest it.

## Review record

These admissions are tracked by [issue #16](https://github.com/Jellman86/autoFPL/issues/16). The exact reviewed commit, initial blockers, corrections and independent PASS results are recorded in the [PR #18 review evidence](https://github.com/Jellman86/autoFPL/pull/18#issuecomment-5074192727). They became effective on protected `dev` in merge commit [`8d91954626b8e6a0bcf237887adb66009a3ce6bb`](https://github.com/Jellman86/autoFPL/commit/8d91954626b8e6a0bcf237887adb66009a3ce6bb).
