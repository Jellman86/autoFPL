from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence

from .historical_preseason_evaluation import HistoricalCapture, _load_capture
from .multi_season_evaluation import (
    DEFAULT_EVALUATION_START_GAMEWEEK,
    DEFAULT_SEASONS,
    MINIMUM_TRAINING_ORIGINS,
    Origin,
    _open_connection,
)
from .team_goal_strength_evaluation import (
    BASELINE_MODEL,
    CHALLENGER_MODEL,
    HALF_LIFE_DAYS,
    L2_PENALTY,
    RHO_BOUNDS,
    _fit_baseline,
    _fit_dixon_coles,
    _instant,
    _load_matches,
    _prediction,
    _rates,
    _summarise,
    _validate,
)
from .temporal_ridge import TemporalRidgeError, _round, _sha256, _write_report

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "expected-goals-team-strength-expanding-origin-v1"
XG_MODEL = "time-decayed-expected-goals-dixon-coles"
MODEL_NAMES = (BASELINE_MODEL, CHALLENGER_MODEL, XG_MODEL)


def evaluate_xg_team_strength(
    database_path: Path,
    season_codes: Sequence[str] = DEFAULT_SEASONS,
    evaluation_start_gameweek: int = DEFAULT_EVALUATION_START_GAMEWEEK,
    minimum_training_origins: int = MINIMUM_TRAINING_ORIGINS,
) -> Dict[str, Any]:
    path = Path(database_path)
    seasons = tuple(season_codes)
    _validate(
        path,
        seasons,
        evaluation_start_gameweek,
        minimum_training_origins,
    )
    connection = _open_connection(path)
    try:
        captures = [_load_capture(connection, season) for season in seasons]
        missing = [
            season
            for season, capture in zip(seasons, captures)
            if capture is None
        ]
        exact = [capture for capture in captures if capture is not None]
        base = _base(
            seasons,
            evaluation_start_gameweek,
            minimum_training_origins,
            exact,
        )
        if missing:
            return _finish(base, "insufficient-data", missing, [], [], None)
        matches = [
            match
            for season_index, capture in enumerate(exact)
            for match in _load_matches(connection, capture, season_index)
        ]
    finally:
        connection.close()

    origins = sorted({match.origin for match in matches})
    targets = [
        origin
        for origin in origins
        if origin.season_code == seasons[-1]
        and origin.gameweek >= evaluation_start_gameweek
    ]
    predictions: Dict[str, List[Dict[str, Any]]] = {
        name: [] for name in MODEL_NAMES
    }
    folds: List[Dict[str, Any]] = []
    for target_origin in targets:
        training_origins = [
            origin for origin in origins if origin < target_origin
        ]
        if len(training_origins) < minimum_training_origins:
            continue
        training = [
            match for match in matches if match.origin < target_origin
        ]
        target = [
            match for match in matches if match.origin == target_origin
        ]
        if not training or not target:
            continue
        cutoff = min(_instant(match.kickoff_utc) for match in target)
        baseline = _fit_baseline(training, cutoff)
        goals_model, goals_diagnostics = _fit_dixon_coles(
            training, cutoff, rate_target="goals"
        )
        xg_model, xg_diagnostics = _fit_dixon_coles(
            training, cutoff, rate_target="expected-goals"
        )
        fold_predictions = {
            BASELINE_MODEL: [
                _prediction(match, baseline) for match in target
            ],
            CHALLENGER_MODEL: [
                _prediction(match, _rates(goals_model, match))
                for match in target
            ],
            XG_MODEL: [
                _prediction(match, _rates(xg_model, match))
                for match in target
            ],
        }
        for name in MODEL_NAMES:
            predictions[name].extend(fold_predictions[name])
        folds.append(
            {
                "seasonCode": target_origin.season_code,
                "gameweek": target_origin.gameweek,
                "trainingOriginCount": len(training_origins),
                "trainingMatchCount": len(training),
                "targetMatchCount": len(target),
                "models": [
                    _summarise(name, fold_predictions[name])
                    for name in MODEL_NAMES
                ],
                "diagnostics": {
                    CHALLENGER_MODEL: goals_diagnostics,
                    XG_MODEL: xg_diagnostics,
                },
            }
        )
    if not folds:
        return _finish(base, "insufficient-data", [], [], [], None)
    models = [_summarise(name, predictions[name]) for name in MODEL_NAMES]
    models.sort(
        key=lambda model: (
            model["metrics"]["jointNegativeLogLikelihood"],
            model["name"],
        )
    )
    return _finish(
        base,
        "complete",
        [],
        folds,
        models,
        _comparison(models, folds),
    )


def _comparison(
    models: Sequence[Mapping[str, Any]],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    indexed = {str(model["name"]): model["metrics"] for model in models}
    xg = indexed[XG_MODEL]
    comparisons: Dict[str, Any] = {}
    passes = True
    for reference_name in (BASELINE_MODEL, CHALLENGER_MODEL):
        reference = indexed[reference_name]
        improvement = (
            reference["jointNegativeLogLikelihood"]
            - xg["jointNegativeLogLikelihood"]
        ) / reference["jointNegativeLogLikelihood"]
        wins = 0
        for fold in folds:
            fold_models = {
                str(model["name"]): model["metrics"]
                for model in fold["models"]
            }
            if (
                fold_models[XG_MODEL]["jointNegativeLogLikelihood"]
                < fold_models[reference_name][
                    "jointNegativeLogLikelihood"
                ]
            ):
                wins += 1
        comparison_passes = (
            improvement >= 0.01
            and xg["outcomeNegativeLogLikelihood"]
            <= reference["outcomeNegativeLogLikelihood"]
            and xg["goalRmse"] <= reference["goalRmse"]
            and wins > len(folds) / 2
        )
        passes = passes and comparison_passes
        comparisons[reference_name] = {
            "jointNllImprovementFraction": _round(improvement),
            "outcomeNllDelta": _round(
                xg["outcomeNegativeLogLikelihood"]
                - reference["outcomeNegativeLogLikelihood"]
            ),
            "goalRmseDelta": _round(
                xg["goalRmse"] - reference["goalRmse"]
            ),
            "foldWins": wins,
            "foldCount": len(folds),
            "passes": comparison_passes,
        }
    return {
        "challenger": XG_MODEL,
        "references": comparisons,
        "fixedScreenPassed": passes,
        "decision": (
            "retain-for-player-distribution-ablation"
            if passes
            else "do-not-use-as-player-feature"
        ),
        "isPromoted": False,
    }


def _base(
    seasons: Sequence[str],
    evaluation_start_gameweek: int,
    minimum_training_origins: int,
    captures: Sequence[HistoricalCapture],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": "exploratory-reused-holdout-screen-not-promotion",
        "isPromoted": False,
        "target": "joint-home-away-match-score-distribution",
        "split": "cross-season-expanding-origin-identical-fold-ablation",
        "configuration": {
            "seasonCodes": list(seasons),
            "evaluationSeasonCode": seasons[-1],
            "evaluationStartGameweek": evaluation_start_gameweek,
            "minimumTrainingOrigins": minimum_training_origins,
            "halfLifeDays": HALF_LIFE_DAYS,
            "l2Penalty": L2_PENALTY,
            "rhoBounds": list(RHO_BOUNDS),
            "expectedGoalsSource": (
                "sum-of-fpl-history-player-expected-goals-per-team-fixture"
            ),
            "models": list(MODEL_NAMES),
            "retentionGate": {
                "references": [BASELINE_MODEL, CHALLENGER_MODEL],
                "minimumJointNllImprovementFraction": 0.01,
                "maximumOutcomeNllDelta": 0.0,
                "maximumGoalRmseDelta": 0.0,
                "minimumFoldWins": "strict-majority-against-each-reference",
                "promotionAllowed": False,
            },
        },
        "captures": [
            {
                "captureId": capture.capture_id,
                "seasonCode": capture.season_code,
                "sourceRevision": capture.source_revision,
                "playersSha256": capture.players_sha256,
                "gameweeksSha256": capture.gameweeks_sha256,
            }
            for capture in captures
        ],
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    missing_seasons: Sequence[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
    comparison: Optional[Mapping[str, Any]],
) -> Dict[str, Any]:
    report = {
        **base,
        "status": status,
        "reason": (
            None
            if status == "complete"
            else "historical-season-archive-not-found"
            if missing_seasons
            else "no-eligible-xg-team-strength-folds"
        ),
        "missingSeasonCodes": list(missing_seasons),
        "eligibleFoldCount": len(folds),
        "folds": list(folds),
        "models": list(models),
        "comparison": comparison,
    }
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare expected-goals and realized-goals latent team-strength "
            "models on identical expanding origins."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument(
        "--evaluation-start-gameweek",
        type=int,
        default=DEFAULT_EVALUATION_START_GAMEWEEK,
    )
    parser.add_argument(
        "--minimum-training-origins",
        type=int,
        default=MINIMUM_TRAINING_ORIGINS,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_xg_team_strength(
            options.database,
            evaluation_start_gameweek=options.evaluation_start_gameweek,
            minimum_training_origins=options.minimum_training_origins,
        )
        _write_report(report, options.output)
        return 0 if report["status"] == "complete" else 2
    except (TemporalRidgeError, sqlite3.Error) as exception:
        code = (
            exception.code
            if isinstance(exception, TemporalRidgeError)
            else "data.xg-team-strength-schema"
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
