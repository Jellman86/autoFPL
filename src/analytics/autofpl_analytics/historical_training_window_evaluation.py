from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

from .multi_season_evaluation import (
    DEFAULT_EVALUATION_START_GAMEWEEK,
    TREE_MODEL,
    evaluate_multi_season,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-training-window-evaluation-v1"
INCUMBENT_SEASONS = ("2024-25", "2025-26")
CHALLENGER_SEASONS = (
    "2022-23",
    "2023-24",
    "2024-25",
    "2025-26",
)
TARGET_SEASON = "2025-26"
EVALUATION_START_GAMEWEEK = DEFAULT_EVALUATION_START_GAMEWEEK
MINIMUM_MAE_IMPROVEMENT_FRACTION = 0.01
MAXIMUM_RMSE_DELTA = 0.0
MAXIMUM_POSITION_MAE_REGRESSION_FRACTION = 0.05


def evaluate_historical_training_window(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    incumbent = evaluate_multi_season(
        path,
        season_codes=INCUMBENT_SEASONS,
        evaluation_start_gameweek=EVALUATION_START_GAMEWEEK,
    )
    challenger = evaluate_multi_season(
        path,
        season_codes=CHALLENGER_SEASONS,
        evaluation_start_gameweek=EVALUATION_START_GAMEWEEK,
    )
    return _compare_reports(incumbent, challenger)


def _compare_reports(
    incumbent: Mapping[str, Any],
    challenger: Mapping[str, Any],
) -> Dict[str, Any]:
    _require_complete(incumbent, INCUMBENT_SEASONS)
    _require_complete(challenger, CHALLENGER_SEASONS)
    incumbent_capture = _target_capture(incumbent)
    challenger_capture = _target_capture(challenger)
    _require(
        incumbent_capture == challenger_capture,
        "training-window.target-archive",
        "The two evaluations do not use the same target archive.",
    )
    incumbent_folds = {
        (str(row["seasonCode"]), int(row["gameweek"])): row
        for row in incumbent["folds"]
    }
    challenger_folds = {
        (str(row["seasonCode"]), int(row["gameweek"])): row
        for row in challenger["folds"]
    }
    _require(
        incumbent_folds
        and set(incumbent_folds) == set(challenger_folds),
        "training-window.fold-identity",
        "The two evaluations do not have identical target folds.",
    )

    fold_rows = []
    fold_wins = 0
    for key in sorted(incumbent_folds):
        incumbent_fold = incumbent_folds[key]
        challenger_fold = challenger_folds[key]
        _require(
            int(incumbent_fold["targetPlayerCount"])
            == int(challenger_fold["targetPlayerCount"]),
            "training-window.target-cohort",
            "A target fold does not contain the same player cohort.",
        )
        incumbent_model = _model(incumbent_fold, TREE_MODEL)
        challenger_model = _model(challenger_fold, TREE_MODEL)
        incumbent_mae = float(incumbent_model["metrics"]["mae"])
        challenger_mae = float(challenger_model["metrics"]["mae"])
        won = challenger_mae < incumbent_mae
        fold_wins += int(won)
        fold_rows.append(
            {
                "seasonCode": key[0],
                "gameweek": key[1],
                "targetPlayerCount": int(
                    incumbent_fold["targetPlayerCount"]
                ),
                "incumbentMae": _round(incumbent_mae),
                "challengerMae": _round(challenger_mae),
                "maeDelta": _round(challenger_mae - incumbent_mae),
                "challengerWon": won,
                "incumbentTrainingRows": int(
                    incumbent_fold["trainingRows"]
                ),
                "challengerTrainingRows": int(
                    challenger_fold["trainingRows"]
                ),
            }
        )

    incumbent_model = _model(incumbent, TREE_MODEL)
    challenger_model = _model(challenger, TREE_MODEL)
    incumbent_metrics = incumbent_model["metrics"]
    challenger_metrics = challenger_model["metrics"]
    incumbent_mae = float(incumbent_metrics["mae"])
    challenger_mae = float(challenger_metrics["mae"])
    mae_improvement = (
        (incumbent_mae - challenger_mae) / incumbent_mae
        if incumbent_mae > 0.0
        else 0.0
    )
    rmse_delta = (
        float(challenger_metrics["rmse"])
        - float(incumbent_metrics["rmse"])
    )
    incumbent_positions = incumbent_model["slices"]["position"]
    challenger_positions = challenger_model["slices"]["position"]
    _require(
        set(incumbent_positions) == set(challenger_positions),
        "training-window.position-slices",
        "The two evaluations do not have identical position slices.",
    )
    position_rows = []
    position_regressions = []
    for position in sorted(incumbent_positions):
        incumbent_position_mae = float(
            incumbent_positions[position]["mae"]
        )
        challenger_position_mae = float(
            challenger_positions[position]["mae"]
        )
        regression = (
            (challenger_position_mae - incumbent_position_mae)
            / incumbent_position_mae
            if incumbent_position_mae > 0.0
            else 0.0
        )
        position_regressions.append(regression)
        position_rows.append(
            {
                "position": position,
                "incumbentMae": _round(incumbent_position_mae),
                "challengerMae": _round(challenger_position_mae),
                "maeRegressionFraction": _round(regression),
            }
        )

    fold_count = len(fold_rows)
    gates = {
        "aggregateMaeImprovement": (
            mae_improvement >= MINIMUM_MAE_IMPROVEMENT_FRACTION
        ),
        "aggregateRmseNonRegression": rmse_delta <= MAXIMUM_RMSE_DELTA,
        "strictMajorityFoldWins": fold_wins > fold_count / 2,
        "positionMaeStability": (
            max(position_regressions)
            <= MAXIMUM_POSITION_MAE_REGRESSION_FRACTION
        ),
    }
    retained = all(gates.values())
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "status": "complete",
        "researchStatus": "reused-holdout-screen-not-promoted",
        "isPromoted": False,
        "influencesAdvice": False,
        "target": "historical-player-gameweek-total-points",
        "targetSeasonCode": TARGET_SEASON,
        "evaluationStartGameweek": EVALUATION_START_GAMEWEEK,
        "model": TREE_MODEL,
        "incumbent": {
            "seasonCodes": list(INCUMBENT_SEASONS),
            "sourceRunIdentitySha256": incumbent[
                "runIdentitySha256"
            ],
            "metrics": dict(incumbent_metrics),
        },
        "challenger": {
            "seasonCodes": list(CHALLENGER_SEASONS),
            "sourceRunIdentitySha256": challenger[
                "runIdentitySha256"
            ],
            "metrics": dict(challenger_metrics),
        },
        "targetArchive": dict(incumbent_capture),
        "comparison": {
            "maeImprovementFraction": _round(mae_improvement),
            "rmseDelta": _round(rmse_delta),
            "foldWins": fold_wins,
            "foldCount": fold_count,
            "positionSlices": position_rows,
            "folds": fold_rows,
        },
        "retentionRule": {
            "minimumMaeImprovementFraction": (
                MINIMUM_MAE_IMPROVEMENT_FRACTION
            ),
            "maximumRmseDelta": MAXIMUM_RMSE_DELTA,
            "strictMajorityFoldWins": True,
            "maximumPositionMaeRegressionFraction": (
                MAXIMUM_POSITION_MAE_REGRESSION_FRACTION
            ),
        },
        "gates": gates,
        "decision": (
            "retain-four-season-prospective-shadow"
            if retained
            else "do-not-retain-four-season-window"
        ),
        "limitations": [
            (
                "The 2025/26 target folds were opened by earlier experiments. "
                "This reused-holdout screen can reject or retain a prospective "
                "shadow but cannot promote it."
            ),
            (
                "The challenger changes both training-row depth and each "
                "exact-code player's available cross-season history."
            ),
            (
                "Older seasons may add sample size while increasing concept "
                "drift from rule, team, role and playing-style changes."
            ),
            (
                "Passing this point-mean screen would still require current "
                "distribution construction and prospective decision scoring."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "incumbentRunIdentitySha256": incumbent[
                "runIdentitySha256"
            ],
            "challengerRunIdentitySha256": challenger[
                "runIdentitySha256"
            ],
            "retentionRule": artifact["retentionRule"],
            "targetArchive": artifact["targetArchive"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _require_complete(
    report: Mapping[str, Any],
    seasons: Sequence[str],
) -> None:
    _require(
        report.get("status") == "complete"
        and report.get("configuration", {}).get("seasonCodes")
        == list(seasons)
        and report.get("configuration", {}).get(
            "evaluationSeasonCode"
        )
        == TARGET_SEASON
        and int(
            report.get("configuration", {}).get(
                "evaluationStartGameweek",
                -1,
            )
        )
        == EVALUATION_START_GAMEWEEK
        and isinstance(report.get("runIdentitySha256"), str),
        "training-window.source-report",
        "A source evaluation is incomplete or has the wrong configuration.",
    )


def _target_capture(report: Mapping[str, Any]) -> Mapping[str, Any]:
    matches = [
        row
        for row in report["captures"]
        if str(row["seasonCode"]) == TARGET_SEASON
    ]
    _require(
        len(matches) == 1,
        "training-window.target-capture",
        "A source evaluation does not bind exactly one target archive.",
    )
    return matches[0]


def _model(
    report: Mapping[str, Any],
    model_name: str,
) -> Mapping[str, Any]:
    matches = [
        row
        for row in report["models"]
        if str(row["name"]) == model_name
    ]
    _require(
        len(matches) == 1,
        "training-window.model",
        "A source evaluation does not contain the fixed model exactly once.",
    )
    return matches[0]


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare the retained two-season point model with an otherwise "
            "unchanged four-season training-window challenger."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = evaluate_historical_training_window(options.database)
        _write_report(artifact, options.output)
        return 0
    except TemporalRidgeError as exception:
        sys.stderr.write(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "error",
                    "errorCode": exception.code,
                    "message": str(exception),
                },
                sort_keys=True,
            )
            + "\n"
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
