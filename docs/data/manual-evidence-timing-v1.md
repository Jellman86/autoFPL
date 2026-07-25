# Manual evidence timing contract v1

## Status and scope

This document defines how the current private, stateless POST routes interpret manually submitted fields for reproducible research. It changes no request or response shape and adds no persistence, connector, external retrieval or account action.

The exact machine-readable inventory is [`contracts/manual-evidence/v1/current-post-routes.json`](../../contracts/manual-evidence/v1/current-post-routes.json), validated by [`manual-evidence-catalog.schema.json`](../../contracts/manual-evidence/v1/manual-evidence-catalog.schema.json). Contract tests compare that inventory with the actual .NET request DTOs so a field cannot be added or reinterpreted silently.

## Current runtime boundary

The API validates one request in memory, returns a deterministic response and discards the request. It does not accept or persist timestamps, credentials, cookies, sessions, opaque uploads or account-action instructions. Application logging APIs are prohibited throughout `src/backend`; the all-route integration test captures framework messages, structured state, exceptions and scopes and verifies that unique request values are absent. Unknown fields continue to return `400`; well-formed but domain-invalid evidence continues to return stable `422` responses.

Because the service stores no request record, a successful response proves only that the submitted values were valid under that contract at processing time. It does not prove where the user obtained a value, when an external event occurred, when a provider published it or that it was historically available.

## Field inventory and interpretation

The catalog contains every top-level and nested field accepted by all eight current POST routes. The grouped inventory below is the reader-facing summary; the catalog is authoritative for exact paths.

| Route | Accepted field groups | Interpretation |
| --- | --- | --- |
| `/api/v1/decision-snapshot-metadata/validation` | `schemaVersion`, `sourceType` | Decision-state contract metadata. `sourceType=manual` describes submission mode; it is not proof of origin or historical availability. |
| `/api/v1/squads/validation` | budget; player IDs, clubs, positions and integer-tenths prices | Decision state submitted at receipt. |
| `/api/v1/lineups/validation` | squad fields; starting XI; captain and vice-captain | Decision state submitted at receipt. |
| `/api/v1/gameweek-selections/validation` | lineup fields; replacement goalkeeper; ordered outfield bench | Decision state submitted at receipt. |
| `/api/v1/gameweek-outcomes/captaincy-resolution` | selection fields; `playerIdsWithMinutes` | Selection is decision state; positive-minutes membership is observed-event evidence known at receipt. |
| `/api/v1/gameweek-outcomes/substitution-resolution` | selection fields; `playerIdsWhoPlayed` | Selection is decision state; play membership is observed-event evidence known at receipt. |
| `/api/v1/gameweek-outcomes/effective-resolution` | selection fields; `playerIdsWhoPlayed` | Same evidence classes as substitution resolution; the response composes deterministic derived values. |
| `/api/v1/gameweek-outcomes/effective-score` | selection fields; `playerIdsWhoPlayed`; complete `playerPoints` entries | Play membership is observed-event evidence; points are outcome evidence known at receipt. |

No current request accepts a derived value. Effective lineups, activated substitutes, captaincy resolution and point totals are deterministic response values calculated from accepted evidence.

## Point-in-time semantics

A replayable decision needs distinct clocks. They must not be inferred from one another:

- **`decisionTime`** — the cutoff at which the advice or decision is evaluated. Current routes do not accept it.
- **`observedAt`** — when an event or state occurred, if separately evidenced. It is unknown for current manual submissions.
- **`publishedAt`** — when a provider made information public, if separately evidenced. It is unknown for current manual submissions.
- **`retrievedAt`** — when autoFPL or its caller received the submission. Current routes do not retain it; a replay wrapper must record it externally.
- **`availableAt`** — earliest time the evidence may enter a replay. For evidence first received through a current manual route, it must be no earlier than `retrievedAt`. A typed historical date cannot backdate availability.

For leakage-free replay, include a field only when `availableAt <= decisionTime`. If an external collector has stronger point-in-time evidence, it belongs to that collector's separately versioned source record rather than being inferred from a later manual request.

## Migration and replay guidance

Existing API callers make **no payload changes**. Adding timestamp or provenance properties to current requests would remain an undeclared-field `400`.

A future ingestion or persistence layer that needs replay must wrap the canonical request outside these routes with:

1. the route and contract version;
2. an immutable record ID and canonical-content hash;
3. `retrievedAt` recorded by the receiving boundary;
4. `observedAt` and `publishedAt` only when separately evidenced, otherwise null;
5. `availableAt` no earlier than receipt for manual submissions;
6. the intended `decisionTime` or snapshot deadline;
7. correction lineage that creates a new record rather than rewriting history.

That future envelope requires its own reviewed implementation, retention/deletion design and deployment verification. The existing [`data-source/v1` source record](../../contracts/data-source/v1/source-record.schema.json) demonstrates the timestamp ordering and immutable-correction model but is not emitted by the current API.

## Change control

Any POST-route field addition, removal or semantic change must update the machine-readable catalog, schema/tests and this document in the same reviewed change. Changes that introduce persistence, authenticated material, opaque content, account instructions or request-body logging require a new threat and privacy review rather than weakening this contract.
