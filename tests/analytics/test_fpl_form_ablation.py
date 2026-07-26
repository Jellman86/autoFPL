from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.fpl_form_ablation import (  # noqa: E402
    ADJUSTED_RIDGE,
    ADJUSTED_TREE,
    CONDITIONAL_RIDGE,
    CONDITIONAL_TREE,
    evaluate_fpl_form_ablation,
    main,
)
from tests.analytics import test_fpl_form_feature_table as source_helpers  # noqa: E402


class FplFormAblationTests(unittest.TestCase):
    def test_source_complete_fold_compares_all_variants_deterministically(
        self,
    ) -> None:
        with self._database() as database:
            before = hashlib.sha256(database.read_bytes()).hexdigest()
            first = evaluate_fpl_form_ablation(
                database,
                season_code="2026-27",
            )
            second = evaluate_fpl_form_ablation(
                database,
                season_code="2026-27",
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(first, second)
        self.assertEqual(before, after)
        self.assertEqual("complete", first["status"])
        self.assertEqual(
            "fpl-form-feature-ablation-v1",
            first["evaluatorVersion"],
        )
        self.assertEqual(1, first["candidateOfficialFoldCount"])
        self.assertEqual(1, first["eligibleFoldCount"])
        self.assertEqual(0, first["excludedFoldCount"])
        self.assertEqual(64, len(first["dataIdentitySha256"]))
        self.assertEqual(64, len(first["runIdentitySha256"]))

        fold = first["folds"][0]
        self.assertEqual(
            [47, 48, 49],
            [
                item["fplFormForecast"]["captureId"]
                for item in fold["training"]
            ],
        )
        self.assertEqual(
            50,
            fold["target"]["fplFormForecast"]["captureId"],
        )
        expected_models = {
            "temporal-ridge",
            "hist-gradient-boosting",
            CONDITIONAL_RIDGE,
            CONDITIONAL_TREE,
            ADJUSTED_RIDGE,
            ADJUSTED_TREE,
            "zero-points",
            "position-expanding-mean",
            "player-last-points",
            "official-running-mean",
        }
        self.assertEqual(
            expected_models,
            {model["name"] for model in first["models"]},
        )
        for model in first["models"]:
            self.assertEqual(2, model["metrics"]["count"])
        self.assertEqual(
            45,
            fold["diagnostics"][CONDITIONAL_TREE][
                "candidateFeatureCount"
            ],
        )
        self.assertEqual(
            47,
            fold["diagnostics"][ADJUSTED_TREE]["candidateFeatureCount"],
        )

    def test_incomplete_source_history_excludes_the_entire_fold(self) -> None:
        with self._database() as database:
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    DELETE FROM fpl_form_fixture_predictions
                    WHERE capture_id = 48;
                    """
                )
                connection.execute(
                    """
                    DELETE FROM fpl_form_forecast_captures
                    WHERE capture_id = 48;
                    """
                )

            report = evaluate_fpl_form_ablation(
                database,
                season_code="2026-27",
            )

        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual(
            "no-source-complete-expanding-origin-folds",
            report["reason"],
        )
        self.assertEqual(0, report["eligibleFoldCount"])
        self.assertEqual(1, report["excludedFoldCount"])
        exclusion = report["excludedFolds"][0]
        self.assertEqual("training-source-unavailable", exclusion["reason"])
        self.assertEqual(2, exclusion["unavailableGameweek"])
        self.assertEqual([], report["models"])

    def test_cli_reports_insufficient_data_and_refuses_overwrite(self) -> None:
        with self._database() as database:
            with sqlite3.connect(database) as connection:
                connection.execute(
                    """
                    DELETE FROM fpl_form_fixture_predictions
                    WHERE capture_id IN (47, 48, 49);
                    """
                )
                connection.execute(
                    """
                    DELETE FROM fpl_form_forecast_captures
                    WHERE capture_id IN (47, 48, 49);
                    """
                )
            output = database.parent / "ablation.json"
            first_exit = main(
                [
                    "--database",
                    str(database),
                    "--season",
                    "2026-27",
                    "--output",
                    str(output),
                ]
            )
            report = json.loads(output.read_text(encoding="utf-8"))
            errors = StringIO()
            with redirect_stderr(errors):
                second_exit = main(
                    [
                        "--database",
                        str(database),
                        "--season",
                        "2026-27",
                        "--output",
                        str(output),
                    ]
                )

        self.assertEqual(2, first_exit)
        self.assertEqual("insufficient-data", report["status"])
        self.assertEqual(1, second_exit)
        self.assertEqual(
            "output.already-exists",
            json.loads(errors.getvalue())["errorCode"],
        )

    class _database:
        def __init__(self) -> None:
            self._inner = source_helpers.FplFormFeatureTableTests._database()

        def __enter__(self) -> Path:
            path = self._inner.__enter__()
            FplFormAblationTests._add_historical_forecasts(path)
            return path

        def __exit__(self, *args: object) -> None:
            self._inner.__exit__(*args)

    @staticmethod
    def _add_historical_forecasts(path: Path) -> None:
        captures = [
            (
                47,
                1,
                "2026-08-21T11:00:00+00:00",
                101,
                "2026-08-22 16:00:00",
                "2.5",
                "0.95",
            ),
            (
                48,
                2,
                "2026-08-28T11:00:00+00:00",
                202,
                "2026-08-29 16:00:00",
                "4.0",
                "0.90",
            ),
            (
                49,
                3,
                "2026-09-04T11:00:00+00:00",
                303,
                "2026-09-07 16:00:00",
                "6.0",
                "0.80",
            ),
        ]
        with sqlite3.connect(path) as connection:
            for (
                capture_id,
                gameweek,
                available_at,
                fixture_id,
                kickoff,
                points,
                probability,
            ) in captures:
                connection.execute(
                    """
                    INSERT INTO fpl_form_forecast_captures VALUES (
                        ?, '2026-27', ?, ?, ?, 'playwright-mcp/v1',
                        'fpl-form-page-data/v2', ?, 1, 1, 1
                    );
                    """,
                    (
                        capture_id,
                        gameweek,
                        available_at,
                        FplFormAblationTests._digest(
                            "content",
                            capture_id,
                        ),
                        FplFormAblationTests._digest(
                            "payload",
                            capture_id,
                        ),
                    ),
                )
                connection.execute(
                    """
                    INSERT INTO fpl_form_fixture_predictions VALUES (
                        ?, 1, ?, 'Ada Example', 'Beta', 'midfielder',
                        ?, ?, ?
                    );
                    """,
                    (
                        capture_id,
                        fixture_id,
                        kickoff,
                        points,
                        probability,
                    ),
                )

    @staticmethod
    def _digest(prefix: str, value: int) -> str:
        return hashlib.sha256(f"{prefix}-{value}".encode()).hexdigest()


if __name__ == "__main__":
    unittest.main()
