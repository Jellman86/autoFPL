# Contracts

Versioned machine-readable contracts live here. Changes require review and compatibility consideration.

- [`decision-snapshot/v1/metadata.schema.json`](decision-snapshot/v1/metadata.schema.json) defines metadata accepted by the current validation API.
- [`data-source/v1/source-record.schema.json`](data-source/v1/source-record.schema.json) defines the provenance envelope for the two initial source profiles. Its historical `admissionRecord` field names the reviewed technical record; it is not a written-permission certificate. The examples are fictional fixtures; the current API does not yet emit or persist source records.

NJsonSchema tests enforce structure and exact versioned source profiles. The small Python policy check covers timestamp ordering that Draft-07 cannot express.
