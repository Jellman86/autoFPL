from __future__ import annotations

import argparse
import json
import math
import sqlite3
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

from .historical_opening_policy_data import REGISTERED_SEASONS
from .team_goal_strength_evaluation import (
    BASELINE_MODEL,
    CHALLENGER_MODEL,
    EVALUATOR_VERSION as SCORELINE_EVALUATOR_VERSION,
    evaluate_team_goal_strength,
)
from .temporal_ridge import TemporalRidgeError, _round, _sha256, _write_report

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-scoreline-replication-evaluation-v1"
TARGET_SEASONS = REGISTERED_SEASONS[1:]
MINIMUM_JOINT_NLL_IMPROVEMENT_FRACTION = 0.01
MINIMUM_CLEAN_SHEET_BRIER_IMPROVEMENT_FRACTION = 0.01


def evaluate_historical_scoreline_replication(
    database_path: Path,
) -> Dict[str, Any]:
    reports = [
        evaluate_team_goal_strength(
            database_path,
            season_codes=REGISTERED_SEASONS[: target_index + 1],
        )
        for target_index in range(1, len(REGISTERED_SEASONS))
    ]
    for expected_target, report in zip(TARGET_SEASONS, reports):
        _require(
            report["status"] == "complete",
            "scoreline-replication.incomplete-target",
            f"The {expected_target} scoreline replication is incomplete.",
        )
        _require(
            report["evaluatorVersion"] == SCORELINE_EVALUATOR_VERSION,
            "scoreline-replication.evaluator-version",
            "A scoreline evaluator version differs from the fixed model.",
        )
        _require(
            report["configuration"]["evaluationSeasonCode"]
            == expected_target,
            "scoreline-replication.target-season",
            "A scoreline replication has the wrong target season.",
        )

    comparison = _aggregate(reports)
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": "historical-scoreline-replication-evaluation",
        "evaluatorVersion": EVALUATOR_VERSION,
        "status": "complete",
        "researchStatus": "retrospective-replication-not-promoted",
        "fixedScorelineEvaluatorVersion": SCORELINE_EVALUATOR_VERSION,
        "registeredSeasonCodes": list(REGISTERED_SEASONS),
        "evaluationTargetSeasonCodes": list(TARGET_SEASONS),
        "split": (
            "expanding-season-prefix-with-gameweek-31-to-38-targets"
        ),
        "models": [BASELINE_MODEL, CHALLENGER_MODEL],
        "acceptanceGate": {
            "minimumJointNllImprovementFraction": (
                MINIMUM_JOINT_NLL_IMPROVEMENT_FRACTION
            ),
            "minimumCleanSheetBrierImprovementFraction": (
                MINIMUM_CLEAN_SHEET_BRIER_IMPROVEMENT_FRACTION
            ),
            "maximumOutcomeNllDelta": 0.0,
            "maximumGoalRmseDelta": 0.0,
            "minimumJointNllFoldWins": "strict-majority",
            "minimumCleanSheetBrierFoldWins": "strict-majority",
            "cleanSheetBrierMustNotRegressInAnyTargetSeason": True,
            "promotionAllowed": False,
        },
        "targetEvaluations": [
            _target_document(report) for report in reports
        ],
        "comparison": comparison,
        "decision": (
            "retain-scoreline-model-for-player-component-ablation"
            if comparison["fixedScreenPassed"]
            else "do-not-use-scoreline-model-as-player-component"
        ),
        "isPromoted": False,
        "influencesAdvice": False,
        "limitations": [
            (
                "This replication retains a fixed match model only as an "
                "input to a player-distribution ablation. It does not prove "
                "that replacing any player forecast improves FPL points."
            ),
            (
                "The latest-season folds were opened by the original "
                "scoreline study. The two earlier targets are replications "
                "from archives added later; no parameters or thresholds are "
                "tuned on any target result."
            ),
            (
                "Previously unseen promoted clubs receive league-average "
                "latent effects. Prior-competition team strength remains a "
                "separate, required new-club challenger."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "registeredSeasonCodes": artifact["registeredSeasonCodes"],
            "targetEvaluations": [
                {
                    "targetSeasonCode": target["targetSeasonCode"],
                    "captures": target["captures"],
                    "sourceRunIdentitySha256": (
                        target["sourceRunIdentitySha256"]
                    ),
                }
                for target in artifact["targetEvaluations"]
            ],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _target_document(report: Mapping[str, Any]) -> Dict[str, Any]:
    models = {
        str(model["name"]): model["metrics"]
        for model in report["models"]
    }
    return {
        "targetSeasonCode": report["configuration"][
            "evaluationSeasonCode"
        ],
        "captureCount": len(report["captures"]),
        "captures": report["captures"],
        "foldCount": len(report["folds"]),
        "matchCount": models[BASELINE_MODEL]["matchCount"],
        "baselineMetrics": models[BASELINE_MODEL],
        "challengerMetrics": models[CHALLENGER_MODEL],
        "jointNllFoldWins": _fold_wins(
            report["folds"],
            "jointNegativeLogLikelihood",
        ),
        "cleanSheetBrierFoldWins": _fold_wins(
            report["folds"],
            "cleanSheetBrier",
        ),
        "sourceRunIdentitySha256": report["runIdentitySha256"],
    }


def _aggregate(reports: Sequence[Mapping[str, Any]]) -> Dict[str, Any]:
    _require(
        len(reports) == len(TARGET_SEASONS),
        "scoreline-replication.target-count",
        "Every registered replication target is required.",
    )
    target_documents = [_target_document(report) for report in reports]
    total_matches = sum(target["matchCount"] for target in target_documents)
    total_folds = sum(target["foldCount"] for target in target_documents)
    baseline = _aggregate_metrics(target_documents, "baselineMetrics")
    challenger = _aggregate_metrics(target_documents, "challengerMetrics")
    joint_improvement = _fractional_improvement(
        baseline["jointNegativeLogLikelihood"],
        challenger["jointNegativeLogLikelihood"],
    )
    clean_sheet_improvement = _fractional_improvement(
        baseline["cleanSheetBrier"],
        challenger["cleanSheetBrier"],
    )
    joint_wins = sum(
        target["jointNllFoldWins"] for target in target_documents
    )
    clean_sheet_wins = sum(
        target["cleanSheetBrierFoldWins"] for target in target_documents
    )
    target_clean_sheet_non_regression = all(
        target["challengerMetrics"]["cleanSheetBrier"]
        <= target["baselineMetrics"]["cleanSheetBrier"]
        for target in target_documents
    )
    passed = (
        joint_improvement
        >= MINIMUM_JOINT_NLL_IMPROVEMENT_FRACTION
        and clean_sheet_improvement
        >= MINIMUM_CLEAN_SHEET_BRIER_IMPROVEMENT_FRACTION
        and challenger["outcomeNegativeLogLikelihood"]
        <= baseline["outcomeNegativeLogLikelihood"]
        and challenger["goalRmse"] <= baseline["goalRmse"]
        and joint_wins > total_folds / 2
        and clean_sheet_wins > total_folds / 2
        and target_clean_sheet_non_regression
    )
    return {
        "matchCount": total_matches,
        "foldCount": total_folds,
        "baseline": {
            "name": BASELINE_MODEL,
            "metrics": baseline,
        },
        "challenger": {
            "name": CHALLENGER_MODEL,
            "metrics": challenger,
        },
        "jointNllImprovementFraction": _round(joint_improvement),
        "cleanSheetBrierImprovementFraction": _round(
            clean_sheet_improvement
        ),
        "outcomeNllDelta": _round(
            challenger["outcomeNegativeLogLikelihood"]
            - baseline["outcomeNegativeLogLikelihood"]
        ),
        "goalRmseDelta": _round(
            challenger["goalRmse"] - baseline["goalRmse"]
        ),
        "jointNllFoldWins": joint_wins,
        "cleanSheetBrierFoldWins": clean_sheet_wins,
        "targetCleanSheetNonRegression": (
            target_clean_sheet_non_regression
        ),
        "fixedScreenPassed": passed,
    }


def _aggregate_metrics(
    targets: Sequence[Mapping[str, Any]],
    key: str,
) -> Dict[str, Any]:
    total = sum(int(target["matchCount"]) for target in targets)
    _require(
        total > 0,
        "scoreline-replication.match-count",
        "The replication has no target matches.",
    )

    def mean(metric: str) -> float:
        return sum(
            int(target["matchCount"]) * float(target[key][metric])
            for target in targets
        ) / total

    return {
        "matchCount": total,
        "jointNegativeLogLikelihood": _round(
            mean("jointNegativeLogLikelihood")
        ),
        "outcomeNegativeLogLikelihood": _round(
            mean("outcomeNegativeLogLikelihood")
        ),
        "outcomeBrier": _round(mean("outcomeBrier")),
        "goalRmse": _round(
            math.sqrt(
                sum(
                    int(target["matchCount"])
                    * float(target[key]["goalRmse"]) ** 2
                    for target in targets
                )
                / total
            )
        ),
        "cleanSheetBrier": _round(mean("cleanSheetBrier")),
    }


def _fold_wins(
    folds: Sequence[Mapping[str, Any]],
    metric: str,
) -> int:
    wins = 0
    for fold in folds:
        models = {
            str(model["name"]): model["metrics"]
            for model in fold["models"]
        }
        if (
            models[CHALLENGER_MODEL][metric]
            < models[BASELINE_MODEL][metric]
        ):
            wins += 1
    return wins


def _fractional_improvement(baseline: float, challenger: float) -> float:
    _require(
        baseline > 0,
        "scoreline-replication.baseline-metric",
        "A baseline proper score is not positive.",
    )
    return (baseline - challenger) / baseline


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Replicate the fixed scoreline model across three expanding "
            "historical season targets."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = evaluate_historical_scoreline_replication(
            options.database
        )
        _write_report(artifact, options.output)
        return 0
    except (TemporalRidgeError, sqlite3.Error) as exception:
        code = (
            exception.code
            if isinstance(exception, TemporalRidgeError)
            else "scoreline-replication.database"
        )
        sys.stderr.write(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "error",
                    "errorCode": code,
                    "message": str(exception),
                },
                sort_keys=True,
            )
            + "\n"
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
