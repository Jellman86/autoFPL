# Manual evidence timing contract v1

## Status and scope

This document defines how the private POST routes interpret manually submitted fields for reproducible research. Validation and outcome routes remain stateless. `/api/v1/decision-snapshots` persists replay evidence, while the selection draft and edit routes resolve an existing forecast artifact into immutable user-owned decision revisions. No route adds a connector, external retrieval or account action.

The exact machine-readable inventory is [`contracts/manual-evidence/v1/current-post-routes.json`](../../contracts/manual-evidence/v1/current-post-routes.json), validated by [`manual-evidence-catalog.schema.json`](../../contracts/manual-evidence/v1/manual-evidence-catalog.schema.json). Contract tests compare that inventory with the actual .NET request DTOs so a field cannot be added or reinterpreted silently.

## Current runtime boundary

The validation and outcome routes validate one request in memory, return a deterministic response and discard it. The decision-snapshot route accepts explicit UTC timestamps and persists the validated squad, selection, observations, cutoff and immutable snapshot lineage in SQLite. The selection-draft route accepts only a persisted forecast-artifact ID, verifies the artifact and records an application-timestamped immutable selection revision. The edit route accepts a complete XI, bench and captaincy for the same forecast squad and creates a superseding revision; locking remains a separate bodyless action. No route accepts credentials, cookies, sessions, opaque uploads or account-action instructions. The all-route integration test captures framework messages, structured state, exceptions and scopes and verifies that unique request values are absent. Unknown fields return `400`; well-formed but invalid evidence returns stable `422` or `409` responses.

A persisted observation records what the caller asserted and when the caller says it was observed, retrieved and available. That does not prove provider origin or publication time. Until a real importer supplies stronger provenance, these records are acceptance/development evidence and must not be presented as independently verified historical facts.

## Field inventory and interpretation

The catalog contains every top-level and nested field accepted by all eleven current POST routes. The grouped inventory below is the reader-facing summary; the catalog is authoritative for exact paths.

| Route | Accepted field groups | Interpretation |
| --- | --- | --- |
| `/api/v1/decision-snapshot-metadata/validation` | `schemaVersion`, `sourceType` | Decision-state contract metadata. `sourceType=manual` describes submission mode; it is not proof of origin or historical availability. |
| `/api/v1/decision-snapshots` | season/Gameweek/deadline/cutoff; complete squad and selection; timestamped decimal observations; correction lineage | Persists authoritative local state and materialises only the latest eligible observation revision at the cutoff. |
| `/api/v1/selections/drafts` | persisted `forecastArtifactId` | Resolves the exact persisted forecast at receipt and creates or reuses an immutable local decision revision; it does not lock or submit the selection. |
| `/api/v1/selections/{selectionRevisionId}/revisions` | starting XI; captain and vice-captain; replacement goalkeeper; ordered outfield bench | Validates a user edit against the same forecast squad and creates a new unlocked revision that supersedes the latest revision; it does not submit the selection. |
| `/api/v1/squads/validation` | budget; player IDs, clubs, positions and integer-tenths prices | Decision state submitted at receipt. |
| `/api/v1/lineups/validation` | squad fields; starting XI; captain and vice-captain | Decision state submitted at receipt. |
| `/api/v1/gameweek-selections/validation` | lineup fields; replacement goalkeeper; ordered outfield bench | Decision state submitted at receipt. |
| `/api/v1/gameweek-outcomes/captaincy-resolution` | selection fields; `playerIdsWithMinutes` | Selection is decision state; positive-minutes membership is observed-event evidence known at receipt. |
| `/api/v1/gameweek-outcomes/substitution-resolution` | selection fields; `playerIdsWhoPlayed` | Selection is decision state; play membership is observed-event evidence known at receipt. |
| `/api/v1/gameweek-outcomes/effective-resolution` | selection fields; `playerIdsWhoPlayed` | Same evidence classes as substitution resolution; the response composes deterministic derived values. |
| `/api/v1/gameweek-outcomes/effective-score` | selection fields; `playerIdsWhoPlayed`; complete `playerPoints` entries | Play membership is observed-event evidence; points are outcome evidence known at receipt. |

The snapshot route accepts numeric source observations; it does not derive or endorse them. Effective lineups, activated substitutes, captaincy resolution, point totals and snapshot membership are deterministic response values calculated from accepted evidence.

## Point-in-time semantics

A replayable decision needs distinct clocks. They must not be inferred from one another:

- **`decisionTime`** — the cutoff at which the advice or decision is evaluated. The persistence route calls it `decisionCutoffUtc`.
- **`observedAt`** — when an event or state occurred, if separately evidenced.
- **`publishedAt`** — when a provider made information public, if separately evidenced. It is unknown for current manual submissions.
- **`retrievedAt`** — when autoFPL or its caller retrieved the source evidence.
- **`availableAt`** — earliest time the evidence may enter a replay. The persistence route requires `observedAt <= retrievedAt <= availableAt`.

For leakage-free replay, the database includes an observation only when `availableAt <= decisionCutoffUtc`. A later correction creates a new observation and snapshot revision linked to the records it supersedes. If an external collector has stronger point-in-time evidence, it belongs to that collector's separately versioned source record rather than being inferred from a later manual request.

## Migration and replay guidance

Existing validation/outcome callers make **no payload changes**. Adding timestamp or provenance properties to those requests remains an undeclared-field `400`.

The snapshot route is the first replay envelope. It generates immutable IDs, a canonical content hash and application creation time; stores deadline/cutoff and the submitted observation clocks; and requires explicit correction lineage. Before real personal history is retained, deployment still needs a reviewed persistent volume, backup/restore evidence and retention/deletion design. The existing [`data-source/v1` source record](../../contracts/data-source/v1/source-record.schema.json) remains the richer provenance contract for future collectors.

## Change control

Any POST-route field addition, removal or semantic change must update the machine-readable catalog, schema/tests and this document in the same reviewed change. Changes that introduce persistence, authenticated material, opaque content, account instructions or request-body logging require a new threat and privacy review rather than weakening this contract.
