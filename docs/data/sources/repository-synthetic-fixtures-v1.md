# Source record: repository synthetic fixtures v1

- **Source ID:** `repository-synthetic-fixtures/v1`
- **Status:** Admitted on protected `dev` by PR #18 (`8d91954626b8e6a0bcf237887adb66009a3ce6bb`)
- **Decision owner:** `Jellman86`
- **Review date:** 24 July 2026
- **Review evidence:** [issue #16](https://github.com/Jellman86/autoFPL/issues/16) and the exact reviewed commit/blockers/corrections/PASS results in the [PR #18 review record](https://github.com/Jellman86/autoFPL/pull/18#issuecomment-5074192727)
- **Provider:** autoFPL repository maintainers
- **Authoritative location:** version-controlled fixture path and Git commit in this repository

## Purpose and authorisation

This source permits deterministic fictional fixtures authored for contract, domain, integration, documentation and leakage tests. Fixture content is maintainer-authored contribution material accepted and distributed under the repository licence. It is not football evidence and must never support a real-world FPL claim or recommendation.

## Allowed content

- clearly fictional player, club, squad and event identifiers;
- boundary and invalid values designed to exercise fail-closed behavior;
- fixed UTC timestamps covering deadline, publication, receipt, availability and correction scenarios;
- explicit invalid fixtures proving impossible availability ordering fails closed;
- deterministic generated values when the generator, seed/configuration and exact command are versioned;
- small examples that contain no copied production payload or personal information.

## Prohibited content and methods

- production or user payloads relabelled as synthetic;
- real credentials, cookies, sessions, identifiers or personal data;
- copied FPL responses, undocumented endpoint output or scraped pages;
- real player/club facts presented as current evidence;
- randomness without a fixed, recorded generator configuration;
- fixtures whose licence or origin cannot be established.

## Coverage and quality

Fixtures cover only the behavior named by their tests and examples. They do not claim statistical representativeness, realism, completeness or current-season validity. Every fixture must identify the contract/schema version it targets and have an assertion that would fail if its intended behavior changed.

Synthetic source quality means deterministic reproduction, schema validity where intended, explicit invalidity where intended, no hidden external dependency and no accidental resemblance to secrets or live personal records.

## Time and revisions

All timestamps are fictional scenario values in UTC. They exercise point-in-time rules but do not establish that a real event occurred:

- `observed_at`: when the fictional event/state occurs, if the scenario models one;
- `published_at`: when a fictional provider publishes the value, if publication is modelled;
- `retrieved_at`: when the fictional autoFPL ingestion/receipt occurs;
- `available_at`: the earliest fictional instant the value may be used by a decision.

Every decision-use fixture must provide `retrieved_at` and `available_at`, with `available_at >= retrieved_at`. When `published_at` is present, it must also satisfy `available_at >= published_at`. The test suite must contain failing leakage fixtures for each violated ordering; an impossible timeline must never become a valid example. Corrections create a new fixture/snapshot linked to the superseded version rather than mutating historical expected evidence without review.

The immutable source identity is the Git commit plus repository-relative path. Generated fixture snapshots additionally record generator code revision and configuration hash.

## Privacy, retention and redistribution

Fixtures contain no personal or sensitive data. They are retained in Git with normal repository history and distributed under the repository's AGPL-3.0-only licence. Any third-party-derived synthetic material requires its own provenance, compatible licence and source review; this record does not admit it automatically.

## Maintenance

Review when fixture scope, generation, licence, schemas or intended use changes. Remove accidental live/user data immediately through the security process; ordinary Git deletion is not sufficient remediation for committed sensitive content. No live connector, dataset or external source is authorised by this record.
