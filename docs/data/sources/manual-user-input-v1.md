# Source record: manual user input v1

- **Source ID:** `manual-user-input/v1`
- **Status:** Admitted on protected `dev` by PR #18 (`8d91954626b8e6a0bcf237887adb66009a3ce6bb`)
- **Decision owner:** `Jellman86`
- **Review date:** 24 July 2026
- **Review evidence:** [issue #16](https://github.com/Jellman86/autoFPL/issues/16) and the exact reviewed commit/blockers/corrections/PASS results in the [PR #18 review record](https://github.com/Jellman86/autoFPL/pull/18#issuecomment-5074192727)
- **Provider/subject:** the individual user operating their own autoFPL decision-support workflow
- **Authoritative URL:** not applicable; no remote collection occurs

## Purpose and provenance

This source represents deliberate values typed or submitted by the user for their own human-in-the-loop decision support. Values may originate from the user's knowledge or public research. The collection method is direct user submission through a versioned autoFPL contract; provenance and timing metadata describe the evidence rather than acting as a legal-origin gate.

The original source-record example covers decision-snapshot metadata (`schemaVersion` and `sourceType`). Later stateless deterministic endpoints accept typed squad, selection, play and points evidence; issue #54 aligns their timing/provenance semantics without withdrawing useful routes merely because a value was manually sourced.

## Allowed content

- the exact fields accepted by the current versioned API contracts;
- the user's current squad choices, public-source observations, private constraints, assumptions and annotations needed for the stated decision;
- explicit source/timing annotations when the user knows them;
- local pseudonymous identifiers created by autoFPL when an identifier is necessary.

## Prohibited content and methods

- passwords, cookies, sessions, authentication tokens, recovery material or account-action instructions;
- opaque archives, executable content, hidden URLs or bulk copied third-party datasets;
- unnecessary names, email addresses, contact details, financial details or other personal/sensitive data;
- a user-entered assertion of an earlier time being treated as proof that information was available then.

Validation must reject undeclared fields and over-size inputs. Application logs must not record request bodies.

## Coverage and quality

Coverage is limited to the submitting user's explicitly contracted fields and decision context. No geography, season, completeness, correctness or representativeness is implied. Manual values are unverified candidate evidence: they may be mistaken, stale or biased and cannot become authoritative external facts merely because the user entered them.

Schema, range, cross-field, deadline and deterministic-rule checks are required where applicable. Material conflicts remain visible or quarantined rather than being silently coerced.

## Time and revisions

The current service accepts no timestamp or snapshot-identity fields and creates no retained snapshot. A future separately reviewed contract must apply these semantics:

- `observed_at`: when the described event/state occurred, if known; otherwise absent.
- `published_at`: source publication time when known; otherwise absent.
- `retrieved_at`: when autoFPL receives the submission.
- `available_at`: no earlier than `retrieved_at` for decision use; a user-supplied historical time cannot backdate availability.

A future persistence contract must generate an immutable snapshot ID, canonical-content hash, receipt timestamp and schema version. A correction creates a new snapshot with a `supersedes` link; it never rewrites prior evidence.

## Privacy, retention and redistribution

The present service is stateless and discards the request after returning validation output. It has no request-body logging, user database or dataset redistribution. Future persistence requires a versioned schema, minimisation and deletion design, threat/privacy review, retention period and end-to-end deletion tests.

Manual user content is private to that user's workflow by default. It must not be committed to the repository, included in shared test fixtures, used to train shared models or publicly redistributed.

## Maintenance

Review before material contract or timing-semantics changes and at least once per season while enabled. Deprecate when the contract, collection path or privacy assumptions no longer support reproducible use. Connector engineering is tracked separately from this manual-input record.
