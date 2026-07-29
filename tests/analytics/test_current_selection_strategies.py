from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
ANALYTICS_ROOT = REPOSITORY_ROOT / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.current_selection_strategies import (  # noqa: E402
    ARTIFACT_TYPE,
    SEARCH_VERSION,
    build_current_selection_strategies,
    main,
)
from tests.analytics.test_current_scenario_selection_score import (  # noqa: E402
    CurrentScenarioSelectionScoreTests,
)

create_score_database = CurrentScenarioSelectionScoreTests._create_database
del CurrentScenarioSelectionScoreTests


class CurrentSelectionStrategiesTests(unittest.TestCase):
    def test_bounded_search_is_deterministic_legal_and_non_serving(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / "autofpl.db"
            create_score_database(database)
            before = database.read_bytes()

            first = build_current_selection_strategies(database)
            second = build_current_selection_strategies(database)

            self.assertEqual(first, second)
            self.assertEqual(before, database.read_bytes())
            self.assertEqual(ARTIFACT_TYPE, first["artifactType"])
            self.assertFalse(first["isPromoted"])
            self.assertFalse(first["influencesAdvice"])
            self.assertEqual(
                SEARCH_VERSION,
                first["search"]["searchVersion"],
            )
            self.assertGreater(
                first["search"]["uniqueCandidatesEvaluated"],
                100,
            )
            self.assertEqual(
                {"balanced", "safer", "higherCeiling"},
                set(first["strategies"]),
            )
            for strategy in first["strategies"].values():
                selection = strategy["result"]["selection"]
                self.assertEqual(15, len(selection["playerIds"]))
                self.assertEqual(
                    15,
                    len(set(selection["playerIds"])),
                )
                self.assertEqual(
                    11,
                    len(selection["startingPlayerIds"]),
                )
                self.assertEqual(
                    2,
                    strategy["vsModel"]["scenarioCount"],
                )

    def test_cli_refuses_an_existing_output(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            database = root / "autofpl.db"
            output = root / "strategies.json"
            create_score_database(database)

            self.assertEqual(
                0,
                main(
                    [
                        "--database",
                        str(database),
                        "--output",
                        str(output),
                    ]
                ),
            )
            document = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(
                "bounded-heuristic-not-global-optimum",
                document["search"]["searchStatus"],
            )
            self.assertEqual(
                1,
                main(
                    [
                        "--database",
                        str(database),
                        "--output",
                        str(output),
                    ]
                ),
            )


if __name__ == "__main__":
    unittest.main()
