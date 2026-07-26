# Contracts

Versioned machine-readable contracts live here. Changes require review and compatibility consideration.

- [`decision-snapshot/v1/metadata.schema.json`](decision-snapshot/v1/metadata.schema.json) defines metadata accepted by the current validation API.
- [`data-source/v1/source-record.schema.json`](data-source/v1/source-record.schema.json) defines the provenance envelope for the two initial source profiles. Its historical `admissionRecord` field names the reviewed technical record; it is not a written-permission certificate. The examples are fictional fixtures; the current API does not yet emit or persist source records.
- [`manual-evidence/v1/manual-evidence-catalog.schema.json`](manual-evidence/v1/manual-evidence-catalog.schema.json) defines the field-classification and timing catalog for current manual POST routes. [`current-post-routes.json`](manual-evidence/v1/current-post-routes.json) inventories every accepted top-level and nested field without changing the stateless wire API.

The runtime OpenAPI contract also exposes the version `1.0` quarantined
evidence-claim read model. Its strict operator JSON importer and .NET contract
are documented in the
[container operations guide](../docs/operations/container.md#quarantined-evidence-claims);
claims remain non-authoritative and are not accepted through a web write route.

NJsonSchema tests enforce structure and exact versioned source profiles. Reflection-backed contract tests keep the manual route catalog aligned with the .NET request DTOs. The small Python policy check covers timestamp ordering that Draft-07 cannot express.
