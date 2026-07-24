from __future__ import annotations

import copy
import json
import tempfile
import unittest
from pathlib import Path

from tools.governance.check_data_source_policy import (
    check_source_record_document,
    check_source_record_examples,
)


class DataSourcePolicyTests(unittest.TestCase):
    RepositoryRoot = Path(__file__).resolve().parents[2]

    def test_checked_in_examples_pass_timestamp_policy(self) -> None:
        self.assertEqual([], check_source_record_examples(self.RepositoryRoot))

    def test_non_utc_receipt_is_rejected(self) -> None:
        record = self._example("manual.json")
        record["retrievedAt"] = "2026-07-24T21:00:00+01:00"

        self.assertContains(record, "retrievedAt must be an RFC 3339 UTC timestamp ending in Z")

    def test_availability_before_receipt_is_rejected(self) -> None:
        record = self._example("manual.json")
        record["availableAt"] = "2026-07-24T19:59:59Z"

        self.assertContains(record, "availableAt must not precede retrievedAt")

    def test_observation_after_publication_is_rejected(self) -> None:
        record = self._example("synthetic.json")
        record["observedAt"] = "2026-07-24T19:45:00Z"

        self.assertContains(record, "observedAt must not follow publishedAt")

    def test_record_cannot_supersede_itself(self) -> None:
        record = self._example("manual.json")
        record["supersedesRecordId"] = record["recordId"]

        self.assertContains(record, "supersedesRecordId must differ from recordId")

    def test_missing_example_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            directory = root / "contracts/data-source/v1/examples"
            directory.mkdir(parents=True)
            (directory / "manual.json").write_text(
                json.dumps(self._example("manual.json")),
                encoding="utf-8",
            )

            violations = check_source_record_examples(root)

        self.assertIn("missing source-record example: synthetic.json", violations)

    def assertContains(self, record: dict[str, object], expected: str) -> None:
        violations = check_source_record_document(record)
        self.assertTrue(any(expected in item for item in violations), violations)

    def _example(self, name: str) -> dict[str, object]:
        path = self.RepositoryRoot / "contracts/data-source/v1/examples" / name
        return copy.deepcopy(json.loads(path.read_text(encoding="utf-8")))


if __name__ == "__main__":
    unittest.main()
