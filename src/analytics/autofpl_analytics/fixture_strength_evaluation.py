from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, List, Mapping, Optional, Sequence, Tuple

from .historical_preseason_evaluation import HistoricalCapture, _load_capture
from .multi_season_evaluation import (
    DEFAULT_EVALUATION_START_GAMEWEEK,
    DEFAULT_SEASONS,
    FEATURES,
    MINIMUM_TRAINING_ORIGINS,
    TREE_MODEL,
    Origin,
    _build_feature_table,
    _open_connection,
)
from .temporal_ridge import (
    Prediction,
    Sample,
    TemporalRidgeError,
    _round,
    _sha256,
    _summarise_model,
    _write_report,
)
from .temporal_tree import TREE_CONFIGURATION, _predict_tree

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "fixture-strength-expanding-origin-v1"
FIXTURE_MODEL = "multi-season-fixture-strength-histogram-tree"
MODELS = (TREE_MODEL, FIXTURE_MODEL)
FORM_WINDOW_GAMEWEEKS = 3
FIXTURE_FEATURES = (
    "teamPositionRolling3PointsMean",
    "opponentPositionRolling3PointsAllowedMean",
)
ALL_FEATURES = FEATURES + FIXTURE_FEATURES


def evaluate_fixture_strength(
    database_path: Path,
    season_codes: Sequence[str] = DEFAULT_SEASONS,
    evaluation_start_gameweek: int = DEFAULT_EVALUATION_START_GAMEWEEK,
    minimum_training_origins: int = MINIMUM_TRAINING_ORIGINS,
) -> Dict[str, Any]:
    path = Path(database_path)
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found", "The SQLite database does not exist."
        )
    seasons = tuple(season_codes)
    if len(seasons) < 2 or len(set(seasons)) != len(seasons):
        raise TemporalRidgeError(
            "configuration.seasons",
            "At least two distinct chronological seasons are required.",
        )
    if not 1 <= evaluation_start_gameweek <= 38:
        raise TemporalRidgeError(
            "configuration.evaluation-start",
            "The evaluation start Gameweek must be between 1 and 38.",
        )
    if minimum_training_origins < 1:
        raise TemporalRidgeError(
            "configuration.minimum-training-origins",
            "At least one training origin is required.",
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

        plain = _build_feature_table(connection, exact)
        enriched = _build_fixture_feature_table(connection, exact, plain)
        origins = sorted(plain)
        target_origins = [
            origin
            for origin in origins
            if origin.season_code == seasons[-1]
            and origin.gameweek >= evaluation_start_gameweek
        ]
        predictions: List[Prediction] = []
        folds: List[Dict[str, Any]] = []
        for target_origin in target_origins:
            training_origins = [
                origin for origin in origins if origin < target_origin
            ]
            if len(training_origins) < minimum_training_origins:
                continue
            plain_training = [
                sample
                for origin in training_origins
                for sample in plain[origin]
            ]
            enriched_training = [
                sample
                for origin in training_origins
                for sample in enriched[origin]
            ]
            incumbent, incumbent_diagnostics = _predict_tree(
                plain_training,
                plain[target_origin],
                continuous_features=FEATURES,
                model_name=TREE_MODEL,
            )
            challenger, challenger_diagnostics = _predict_tree(
                enriched_training,
                enriched[target_origin],
                continuous_features=ALL_FEATURES,
                model_name=FIXTURE_MODEL,
            )
            fold_predictions = [*incumbent, *challenger]
            predictions.extend(fold_predictions)
            folds.append(
                {
                    "seasonCode": target_origin.season_code,
                    "gameweek": target_origin.gameweek,
                    "trainingOriginCount": len(training_origins),
                    "trainingRows": len(plain_training),
                    "targetPlayerCount": len(plain[target_origin]),
                    "models": [
                        _summarise_model(model, fold_predictions)
                        for model in MODELS
                    ],
                    "diagnostics": {
                        TREE_MODEL: incumbent_diagnostics,
                        FIXTURE_MODEL: challenger_diagnostics,
                    },
                }
            )
        if not folds:
            return _finish(base, "insufficient-data", [], [], [], None)

        models = [
            _summarise_model(model, predictions) for model in MODELS
        ]
        models.sort(key=lambda item: (item["metrics"]["mae"], item["name"]))
        return _finish(
            base,
            "complete",
            [],
            folds,
            models,
            _comparison(models, folds),
        )
    finally:
        connection.close()


def _build_fixture_feature_table(
    connection: sqlite3.Connection,
    captures: Sequence[HistoricalCapture],
    plain: Mapping[Origin, Sequence[Sample]],
) -> Dict[Origin, List[Sample]]:
    metadata: Dict[Tuple[int, int, int], Tuple[str, Tuple[int, ...]]] = {}
    for season_index, capture in enumerate(captures):
        rows = connection.execute(
            """
            SELECT player_code, gameweek, team_name, opponent_team_id
            FROM historical_fpl_player_gameweeks
            WHERE capture_id = :capture_id
            ORDER BY player_code, gameweek, kickoff_utc, fixture_id;
            """,
            {"capture_id": capture.capture_id},
        ).fetchall()
        grouped: DefaultDict[
            Tuple[int, int], Dict[str, Any]
        ] = defaultdict(lambda: {"teams": set(), "opponents": set()})
        for row in rows:
            key = (int(row["player_code"]), int(row["gameweek"]))
            grouped[key]["teams"].add(str(row["team_name"]))
            grouped[key]["opponents"].add(int(row["opponent_team_id"]))
        for (player_code, gameweek), values in grouped.items():
            if len(values["teams"]) != 1:
                raise TemporalRidgeError(
                    "data.ambiguous-team-membership",
                    "A player has multiple team identities in one Gameweek.",
                )
            metadata[(season_index, gameweek, player_code)] = (
                next(iter(values["teams"])),
                tuple(sorted(values["opponents"])),
            )

    team_history: DefaultDict[
        Tuple[int, str, str], List[Tuple[int, float]]
    ] = defaultdict(list)
    allowed_history: DefaultDict[
        Tuple[int, int, str], List[Tuple[int, float]]
    ] = defaultdict(list)
    result: Dict[Origin, List[Sample]] = {}
    for origin in sorted(plain):
        enriched: List[Sample] = []
        for sample in plain[origin]:
            key = (origin.season_index, origin.gameweek, sample.player_id)
            if key not in metadata:
                raise TemporalRidgeError(
                    "data.fixture-context-not-found",
                    "A player-Gameweek has no exact team/opponent context.",
                )
            team_name, opponent_ids = metadata[key]
            team_points = _recent_values(
                team_history[
                    (origin.season_index, team_name, sample.position)
                ],
                origin.gameweek,
            )
            opponent_points = [
                value
                for opponent_id in opponent_ids
                for value in _recent_values(
                    allowed_history[
                        (
                            origin.season_index,
                            opponent_id,
                            sample.position,
                        )
                    ],
                    origin.gameweek,
                )
            ]
            features = {
                **sample.features,
                FIXTURE_FEATURES[0]: _mean(team_points),
                FIXTURE_FEATURES[1]: _mean(opponent_points),
            }
            enriched.append(
                Sample(
                    season_code=sample.season_code,
                    gameweek=sample.gameweek,
                    player_id=sample.player_id,
                    position=sample.position,
                    features=features,
                    actual=sample.actual,
                )
            )
        result[origin] = enriched
        for sample in plain[origin]:
            team_name, opponent_ids = metadata[
                (origin.season_index, origin.gameweek, sample.player_id)
            ]
            team_history[
                (origin.season_index, team_name, sample.position)
            ].append((origin.gameweek, float(sample.actual)))
            for opponent_id in opponent_ids:
                allowed_history[
                    (origin.season_index, opponent_id, sample.position)
                ].append((origin.gameweek, float(sample.actual)))
    return result


def _recent_values(
    history: Sequence[Tuple[int, float]], target_gameweek: int
) -> List[float]:
    first = target_gameweek - FORM_WINDOW_GAMEWEEKS
    return [
        value
        for gameweek, value in history
        if first <= gameweek < target_gameweek
    ]


def _mean(values: Sequence[float]) -> Optional[float]:
    return sum(values) / len(values) if values else None


def _comparison(
    models: Sequence[Mapping[str, Any]],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    indexed = {str(model["name"]): model for model in models}
    incumbent = indexed[TREE_MODEL]
    challenger = indexed[FIXTURE_MODEL]
    incumbent_mae = float(incumbent["metrics"]["mae"])
    challenger_mae = float(challenger["metrics"]["mae"])
    improvement = (
        (incumbent_mae - challenger_mae) / incumbent_mae
        if incumbent_mae > 0
        else 0.0
    )
    fold_wins = 0
    for fold in folds:
        fold_models = {
            str(model["name"]): model for model in fold["models"]
        }
        if (
            fold_models[FIXTURE_MODEL]["metrics"]["mae"]
            < fold_models[TREE_MODEL]["metrics"]["mae"]
        ):
            fold_wins += 1
    incumbent_positions = incumbent["slices"]["position"]
    challenger_positions = challenger["slices"]["position"]
    position_regressions = {
        position: _round(
            (
                challenger_positions[position]["mae"]
                - incumbent_positions[position]["mae"]
            )
            / incumbent_positions[position]["mae"]
            if incumbent_positions[position]["mae"] > 0
            else 0.0
        )
        for position in incumbent_positions
    }
    passes = (
        improvement >= 0.01
        and challenger["metrics"]["rmse"] <= incumbent["metrics"]["rmse"]
        and fold_wins > len(folds) / 2
        and all(value <= 0.05 for value in position_regressions.values())
    )
    return {
        "incumbent": TREE_MODEL,
        "challenger": FIXTURE_MODEL,
        "maeImprovementFraction": _round(improvement),
        "rmseDelta": _round(
            challenger["metrics"]["rmse"] - incumbent["metrics"]["rmse"]
        ),
        "foldWins": fold_wins,
        "foldCount": len(folds),
        "positionMaeRegressionFractions": position_regressions,
        "fixedScreenPassed": passes,
        "decision": (
            "retain-for-prospective-shadow"
            if passes
            else "do-not-retain"
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
        "researchStatus": "registered-retrospective-ablation-not-promotion",
        "isPromoted": False,
        "target": "historical-player-gameweek-total-points",
        "split": "cross-season-expanding-origin-identical-fold-ablation",
        "configuration": {
            "seasonCodes": list(seasons),
            "evaluationSeasonCode": seasons[-1],
            "evaluationStartGameweek": evaluation_start_gameweek,
            "minimumTrainingOrigins": minimum_training_origins,
            "formWindowGameweeks": FORM_WINDOW_GAMEWEEKS,
            "incumbentFeatures": list(FEATURES),
            "fixtureStrengthFeatures": list(FIXTURE_FEATURES),
            "treeConfiguration": dict(TREE_CONFIGURATION),
            "promotionGate": {
                "minimumMaeImprovementFraction": 0.01,
                "maximumRmseDelta": 0.0,
                "minimumFoldWins": "strict-majority",
                "maximumPositionMaeRegressionFraction": 0.05,
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
    reason = None
    if status != "complete":
        reason = (
            "historical-season-archive-not-found"
            if missing_seasons
            else "no-eligible-fixture-strength-folds"
        )
    report = {
        **base,
        "status": status,
        "reason": reason,
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
            "Evaluate cutoff-correct team and opponent fixture-strength "
            "features on fixed multi-season folds."
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
        report = evaluate_fixture_strength(
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
            else "data.fixture-context-schema"
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
