from __future__ import annotations

import argparse
import json
import math
import sqlite3
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

from .historical_appearance_hurdle_points_evaluation import (
    APPEARANCE_MODEL,
    CONDITIONAL_MODEL,
    HURDLE_MODEL,
)
from .historical_opening_policy_data import REGISTERED_SEASONS
from .historical_participation_evaluation import (
    _predict_classifier,
    _probability_metrics,
)
from .historical_preseason_evaluation import _load_capture
from .multi_season_evaluation import (
    FEATURES,
    MINIMUM_TRAINING_ORIGINS,
    Origin,
    _build_feature_table,
    _open_connection,
)
from .team_goal_strength_evaluation import (
    BASELINE_MODEL as LEAGUE_SCORELINE_MODEL,
    CHALLENGER_MODEL as DIXON_COLES_SCORELINE_MODEL,
    _fit_baseline,
    _fit_dixon_coles,
    _instant,
    _load_matches,
    _prediction as _scoreline_prediction,
    _rates,
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
from .temporal_tree import _predict_tree

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-player-clean-sheet-component-evaluation-v1"
TARGET_SEASONS = REGISTERED_SEASONS[1:]
EVALUATION_START_GAMEWEEK = 31
PLAYED_60_MODEL = "multi-season-played-60-histogram-classifier"
RESIDUAL_MODEL = "appearance-conditional-points-excluding-clean-sheet"
LEAGUE_COMPONENT_MODEL = (
    "appearance-residual-plus-league-clean-sheet-component"
)
DIXON_COLES_COMPONENT_MODEL = (
    "appearance-residual-plus-dixon-coles-clean-sheet-component"
)
LEAGUE_CLEAN_SHEET_PROBABILITY_MODEL = (
    "league-clean-sheet-times-coherent-played-60"
)
DIXON_COLES_CLEAN_SHEET_PROBABILITY_MODEL = (
    "dixon-coles-clean-sheet-times-coherent-played-60"
)
SCORELINE_DATA_IDENTITY = (
    "f6aecac9568d720248fe5e1078be1f0396f970dad3de82a7cc269b79dc90b25e"
)
SCORELINE_RUN_IDENTITY = (
    "2c10b68b1b0732a268002109c5bf7e4e5b266bc4df46ea16cf42b617bcdf6599"
)
MINIMUM_MAE_IMPROVEMENT_FRACTION = 0.01
MAXIMUM_RMSE_DELTA = 0.0
MAXIMUM_POSITION_MAE_REGRESSION_FRACTION = 0.05
MINIMUM_CLEAN_SHEET_BRIER_IMPROVEMENT_FRACTION = 0.01


@dataclass(frozen=True)
class ComponentObservation:
    player_code: int
    position: str
    team_name: str
    fixture_id: int
    minutes: int
    total_points: int
    clean_sheet_points: int


def evaluate_historical_player_clean_sheet_component(
    database_path: Path,
    scoreline_evaluation_path: Path,
) -> Dict[str, Any]:
    scoreline_identity = _load_scoreline_identity(
        scoreline_evaluation_path
    )
    connection = _open_connection(database_path)
    try:
        captures = [
            _load_capture(connection, season)
            for season in REGISTERED_SEASONS
        ]
        _require(
            all(capture is not None for capture in captures),
            "clean-sheet-component.archive",
            "Every registered historical archive is required.",
        )
        exact_captures = [
            capture for capture in captures if capture is not None
        ]
        target_reports = [
            _evaluate_target(
                connection,
                exact_captures[: target_index + 1],
            )
            for target_index in range(1, len(exact_captures))
        ]
    finally:
        connection.close()

    result = _finish(
        exact_captures,
        scoreline_identity,
        target_reports,
    )
    return result


def _evaluate_target(
    connection: sqlite3.Connection,
    captures: Sequence[Any],
) -> Dict[str, Any]:
    samples_by_origin = _build_feature_table(connection, captures)
    observations_by_origin = _load_component_observations(
        connection,
        captures,
    )
    matches = [
        match
        for season_index, capture in enumerate(captures)
        for match in _load_matches(connection, capture, season_index)
    ]
    target_season = captures[-1].season_code
    ordered_origins = sorted(samples_by_origin)
    target_origins = [
        origin
        for origin in ordered_origins
        if origin.season_code == target_season
        and origin.gameweek >= EVALUATION_START_GAMEWEEK
    ]
    all_points: List[Prediction] = []
    all_league_clean_sheets: List[Prediction] = []
    all_dixon_coles_clean_sheets: List[Prediction] = []
    folds = []
    for target_origin in target_origins:
        training_origins = [
            origin for origin in ordered_origins if origin < target_origin
        ]
        if len(training_origins) < MINIMUM_TRAINING_ORIGINS:
            continue
        training = [
            sample
            for origin in training_origins
            for sample in samples_by_origin[origin]
            if _single_fixture(
                sample,
                observations_by_origin[origin],
            )
        ]
        target = [
            sample
            for sample in samples_by_origin[target_origin]
            if _single_fixture(
                sample,
                observations_by_origin[target_origin],
            )
        ]
        _require(
            training and target,
            "clean-sheet-component.player-cohort",
            "A player component fold has an empty fixed cohort.",
        )
        appearance_training = [
            _binary_sample(
                sample,
                observations_by_origin[origin],
                "appearance",
            )
            for origin in training_origins
            for sample in samples_by_origin[origin]
            if _single_fixture(
                sample,
                observations_by_origin[origin],
            )
        ]
        appearance_target = [
            _binary_sample(
                sample,
                observations_by_origin[target_origin],
                "appearance",
            )
            for sample in target
        ]
        played_60_training = [
            _binary_sample(
                sample,
                observations_by_origin[origin],
                "played-60",
            )
            for origin in training_origins
            for sample in samples_by_origin[origin]
            if _single_fixture(
                sample,
                observations_by_origin[origin],
            )
        ]
        played_60_target = [
            _binary_sample(
                sample,
                observations_by_origin[target_origin],
                "played-60",
            )
            for sample in target
        ]
        conditional_training = [
            sample
            for sample in training
            if _observation(
                sample,
                observations_by_origin[
                    Origin(
                        _season_index(captures, sample.season_code),
                        sample.gameweek,
                        sample.season_code,
                    )
                ],
            ).minutes
            > 0
        ]
        residual_training = [
            _residual_sample(
                sample,
                observations_by_origin[
                    Origin(
                        _season_index(captures, sample.season_code),
                        sample.gameweek,
                        sample.season_code,
                    )
                ],
            )
            for sample in conditional_training
        ]
        residual_target = [
            _residual_sample(
                sample,
                observations_by_origin[target_origin],
            )
            for sample in target
        ]
        _require(
            conditional_training and residual_training,
            "clean-sheet-component.conditional-training",
            "A fold has no appearance-positive training rows.",
        )

        appearance, appearance_diagnostics = _predict_classifier(
            appearance_training,
            appearance_target,
            APPEARANCE_MODEL,
            continuous_features=FEATURES,
        )
        played_60, played_60_diagnostics = _predict_classifier(
            played_60_training,
            played_60_target,
            PLAYED_60_MODEL,
            continuous_features=FEATURES,
        )
        conditional, conditional_diagnostics = _predict_tree(
            conditional_training,
            target,
            continuous_features=FEATURES,
            model_name=CONDITIONAL_MODEL,
        )
        residual, residual_diagnostics = _predict_tree(
            residual_training,
            residual_target,
            continuous_features=FEATURES,
            model_name=RESIDUAL_MODEL,
        )
        scoreline_probabilities = _scoreline_probabilities(
            matches,
            target_origin,
        )
        incumbent = _combine_incumbent(
            target,
            appearance,
            conditional,
        )
        league, league_clean_sheets = _combine_component(
            target,
            observations_by_origin[target_origin],
            appearance,
            played_60,
            residual,
            scoreline_probabilities[LEAGUE_SCORELINE_MODEL],
            LEAGUE_COMPONENT_MODEL,
            LEAGUE_CLEAN_SHEET_PROBABILITY_MODEL,
        )
        dixon_coles, dixon_coles_clean_sheets = _combine_component(
            target,
            observations_by_origin[target_origin],
            appearance,
            played_60,
            residual,
            scoreline_probabilities[DIXON_COLES_SCORELINE_MODEL],
            DIXON_COLES_COMPONENT_MODEL,
            DIXON_COLES_CLEAN_SHEET_PROBABILITY_MODEL,
        )
        point_predictions = [*incumbent, *league, *dixon_coles]
        all_points.extend(point_predictions)
        all_league_clean_sheets.extend(league_clean_sheets)
        all_dixon_coles_clean_sheets.extend(dixon_coles_clean_sheets)
        models = [
            _summarise_model(name, point_predictions)
            for name in (
                HURDLE_MODEL,
                LEAGUE_COMPONENT_MODEL,
                DIXON_COLES_COMPONENT_MODEL,
            )
        ]
        folds.append(
            {
                "seasonCode": target_season,
                "gameweek": target_origin.gameweek,
                "trainingOriginCount": len(training_origins),
                "trainingPlayerCount": len(training),
                "targetPlayerCount": len(target),
                "models": models,
                "cleanSheetProbabilityMetrics": {
                    LEAGUE_CLEAN_SHEET_PROBABILITY_MODEL: (
                        _probability_metrics(league_clean_sheets)
                    ),
                    DIXON_COLES_CLEAN_SHEET_PROBABILITY_MODEL: (
                        _probability_metrics(dixon_coles_clean_sheets)
                    ),
                },
                "diagnostics": {
                    APPEARANCE_MODEL: appearance_diagnostics,
                    PLAYED_60_MODEL: played_60_diagnostics,
                    CONDITIONAL_MODEL: conditional_diagnostics,
                    RESIDUAL_MODEL: residual_diagnostics,
                },
            }
        )
    _require(
        folds,
        "clean-sheet-component.folds",
        f"No {target_season} player component folds are available.",
    )
    return {
        "targetSeasonCode": target_season,
        "folds": folds,
        "pointPredictions": all_points,
        "leagueCleanSheetPredictions": all_league_clean_sheets,
        "dixonColesCleanSheetPredictions": (
            all_dixon_coles_clean_sheets
        ),
    }


def _finish(
    captures: Sequence[Any],
    scoreline_identity: Mapping[str, str],
    target_reports: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    point_predictions = [
        prediction
        for target in target_reports
        for prediction in target["pointPredictions"]
    ]
    league_clean_sheets = [
        prediction
        for target in target_reports
        for prediction in target["leagueCleanSheetPredictions"]
    ]
    dixon_coles_clean_sheets = [
        prediction
        for target in target_reports
        for prediction in target["dixonColesCleanSheetPredictions"]
    ]
    models = [
        _summarise_model(name, point_predictions)
        for name in (
            HURDLE_MODEL,
            LEAGUE_COMPONENT_MODEL,
            DIXON_COLES_COMPONENT_MODEL,
        )
    ]
    by_name = {str(model["name"]): model for model in models}
    incumbent = by_name[HURDLE_MODEL]
    league = by_name[LEAGUE_COMPONENT_MODEL]
    challenger = by_name[DIXON_COLES_COMPONENT_MODEL]
    incumbent_mae = float(incumbent["metrics"]["mae"])
    challenger_mae = float(challenger["metrics"]["mae"])
    improvement = (
        (incumbent_mae - challenger_mae) / incumbent_mae
        if incumbent_mae > 0
        else 0.0
    )
    rmse_delta = (
        float(challenger["metrics"]["rmse"])
        - float(incumbent["metrics"]["rmse"])
    )
    folds = [
        fold for target in target_reports for fold in target["folds"]
    ]
    point_fold_wins = _fold_wins(
        folds,
        DIXON_COLES_COMPONENT_MODEL,
        HURDLE_MODEL,
        "mae",
    )
    component_fold_wins = sum(
        float(
            fold["cleanSheetProbabilityMetrics"][
                DIXON_COLES_CLEAN_SHEET_PROBABILITY_MODEL
            ]["brierScore"]
        )
        < float(
            fold["cleanSheetProbabilityMetrics"][
                LEAGUE_CLEAN_SHEET_PROBABILITY_MODEL
            ]["brierScore"]
        )
        for fold in folds
    )
    league_component_metrics = _probability_metrics(league_clean_sheets)
    dixon_coles_component_metrics = _probability_metrics(
        dixon_coles_clean_sheets
    )
    component_brier_improvement = _fractional_improvement(
        float(league_component_metrics["brierScore"]),
        float(dixon_coles_component_metrics["brierScore"]),
    )
    position_comparison, position_regressions = _position_comparison(
        incumbent,
        challenger,
    )
    mean_gates = {
        "aggregateMaeImprovement": (
            improvement >= MINIMUM_MAE_IMPROVEMENT_FRACTION
        ),
        "aggregateRmseNonRegression": rmse_delta <= MAXIMUM_RMSE_DELTA,
        "strictMajorityPointFoldWins": point_fold_wins > len(folds) / 2,
        "positionMaeStability": (
            max(position_regressions)
            <= MAXIMUM_POSITION_MAE_REGRESSION_FRACTION
        ),
        "dixonColesNonWorseThanLeagueComponent": (
            challenger_mae <= float(league["metrics"]["mae"])
        ),
    }
    distribution_gates = _distribution_gates(
        league_component_metrics,
        dixon_coles_component_metrics,
        component_fold_wins,
        len(folds),
    )
    mean_retained = all(mean_gates.values())
    distribution_retained = all(distribution_gates.values())
    target_documents = [
        {
            "targetSeasonCode": target["targetSeasonCode"],
            "foldCount": len(target["folds"]),
            "targetPlayerCount": sum(
                fold["targetPlayerCount"] for fold in target["folds"]
            ),
            "models": [
                _summarise_model(
                    name,
                    target["pointPredictions"],
                )
                for name in (
                    HURDLE_MODEL,
                    LEAGUE_COMPONENT_MODEL,
                    DIXON_COLES_COMPONENT_MODEL,
                )
            ],
            "leagueCleanSheetProbabilityMetrics": _probability_metrics(
                target["leagueCleanSheetPredictions"]
            ),
            "dixonColesCleanSheetProbabilityMetrics": (
                _probability_metrics(
                    target["dixonColesCleanSheetPredictions"]
                )
            ),
        }
        for target in target_reports
    ]
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": "historical-player-clean-sheet-component-evaluation",
        "evaluatorVersion": EVALUATOR_VERSION,
        "status": "complete",
        "researchStatus": "retrospective-component-screen-not-promoted",
        "registeredSeasonCodes": list(REGISTERED_SEASONS),
        "evaluationTargetSeasonCodes": list(TARGET_SEASONS),
        "evaluationStartGameweek": EVALUATION_START_GAMEWEEK,
        "cohort": (
            "single-fixture-player-gameweeks-with-zero-minute-players-retained"
        ),
        "factorization": (
            "appearance-times-non-clean-sheet-conditional-points-plus-"
            "coherent-played-60-times-team-clean-sheet-probability-times-"
            "position-clean-sheet-points"
        ),
        "scorelineReplicationIdentity": dict(scoreline_identity),
        "models": models,
        "cleanSheetProbabilityComponent": {
            "leagueMetrics": league_component_metrics,
            "dixonColesMetrics": dixon_coles_component_metrics,
            "brierImprovementFraction": _round(
                component_brier_improvement
            ),
            "foldWins": component_fold_wins,
            "foldCount": len(folds),
        },
        "comparison": {
            "maeImprovementFraction": _round(improvement),
            "rmseDelta": _round(rmse_delta),
            "pointFoldWins": point_fold_wins,
            "foldCount": len(folds),
            "positionSlices": position_comparison,
        },
        "retentionRule": {
            "pointMean": {
                "minimumMaeImprovementFraction": (
                    MINIMUM_MAE_IMPROVEMENT_FRACTION
                ),
                "maximumRmseDelta": MAXIMUM_RMSE_DELTA,
                "strictMajorityPointFoldWins": True,
                "maximumPositionMaeRegressionFraction": (
                    MAXIMUM_POSITION_MAE_REGRESSION_FRACTION
                ),
                "dixonColesNonWorseThanLeagueComponent": True,
            },
            "cleanSheetDistribution": {
                "minimumBrierImprovementFraction": (
                    MINIMUM_CLEAN_SHEET_BRIER_IMPROVEMENT_FRACTION
                ),
                "maximumLogLossDelta": 0.0,
                "maximumCalibrationErrorDelta": 0.0,
                "strictMajorityFoldWins": True,
            },
        },
        "pointMeanGates": mean_gates,
        "cleanSheetDistributionGates": distribution_gates,
        "targetEvaluations": target_documents,
        "pointMeanDecision": (
            "retain-clean-sheet-point-mean"
            if mean_retained
            else "do-not-retain-clean-sheet-point-mean"
        ),
        "cleanSheetDistributionDecision": (
            "retain-clean-sheet-probability-for-distribution-screen"
            if distribution_retained
            else "do-not-retain-clean-sheet-probability"
        ),
        "decision": (
            "retain-clean-sheet-probability-for-distribution-only"
            if distribution_retained and not mean_retained
            else "retain-clean-sheet-mean-and-distribution"
            if distribution_retained and mean_retained
            else "do-not-retain-clean-sheet-component"
        ),
        "isPromoted": False,
        "influencesAdvice": False,
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
        "limitations": [
            (
                "The screen isolates clean-sheet points only. Goals-conceded "
                "deductions, attacking events, saves, cards, bonus and "
                "defensive contributions remain inside the residual model."
            ),
            (
                "Double-fixture player-Gameweeks are excluded because one "
                "Gameweek-level 60-minute probability cannot identify the "
                "fixture in which the threshold was reached."
            ),
            (
                "A retained probability component still requires a complete "
                "point-distribution and opening-squad policy evaluation. It "
                "must not replace the incumbent point mean when that gate "
                "fails."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "captures": artifact["captures"],
            "scorelineReplicationIdentity": (
                artifact["scorelineReplicationIdentity"]
            ),
            "evaluationTargetSeasonCodes": (
                artifact["evaluationTargetSeasonCodes"]
            ),
            "evaluationStartGameweek": artifact[
                "evaluationStartGameweek"
            ],
            "cohort": artifact["cohort"],
            "retentionRule": artifact["retentionRule"],
            "targetEvaluations": [
                {
                    "targetSeasonCode": target["targetSeasonCode"],
                    "foldCount": target["foldCount"],
                    "targetPlayerCount": target["targetPlayerCount"],
                }
                for target in target_documents
            ],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _load_scoreline_identity(path: Path) -> Dict[str, str]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise TemporalRidgeError(
            "clean-sheet-component.scoreline-artifact",
            "The retained scoreline replication artifact is unavailable.",
        ) from exception
    _require(
        document.get("dataIdentitySha256") == SCORELINE_DATA_IDENTITY
        and document.get("runIdentitySha256") == SCORELINE_RUN_IDENTITY
        and document.get("decision")
        == "retain-scoreline-model-for-player-component-ablation",
        "clean-sheet-component.scoreline-identity",
        "The scoreline replication identity or decision differs.",
    )
    return {
        "dataIdentitySha256": SCORELINE_DATA_IDENTITY,
        "runIdentitySha256": SCORELINE_RUN_IDENTITY,
    }


def _load_component_observations(
    connection: sqlite3.Connection,
    captures: Sequence[Any],
) -> Dict[Origin, Dict[int, ComponentObservation]]:
    result: Dict[Origin, Dict[int, ComponentObservation]] = {}
    for season_index, capture in enumerate(captures):
        rows = connection.execute(
            """
            SELECT
                gameweek.player_code,
                player.position,
                gameweek.gameweek,
                gameweek.fixture_id,
                gameweek.team_name,
                gameweek.minutes,
                gameweek.total_points,
                gameweek.clean_sheets
            FROM historical_fpl_player_gameweeks AS gameweek
            INNER JOIN historical_fpl_players AS player
                ON player.capture_id = gameweek.capture_id
                AND player.season_element_id = gameweek.season_element_id
            WHERE gameweek.capture_id = :capture_id
            ORDER BY
                gameweek.gameweek,
                gameweek.player_code,
                gameweek.fixture_id;
            """,
            {"capture_id": capture.capture_id},
        ).fetchall()
        grouped: Dict[Tuple[int, int], List[Any]] = {}
        for row in rows:
            grouped.setdefault(
                (int(row["gameweek"]), int(row["player_code"])),
                [],
            ).append(row)
        for (gameweek, player_code), player_rows in grouped.items():
            if len(player_rows) != 1:
                continue
            row = player_rows[0]
            position = str(row["position"])
            clean_sheet_value = (
                4
                if position in {"goalkeeper", "defender"}
                else 1
                if position == "midfielder"
                else 0
            )
            observation = ComponentObservation(
                player_code=player_code,
                position=position,
                team_name=str(row["team_name"]),
                fixture_id=int(row["fixture_id"]),
                minutes=int(row["minutes"]),
                total_points=int(row["total_points"]),
                clean_sheet_points=(
                    int(row["clean_sheets"]) * clean_sheet_value
                ),
            )
            origin = Origin(
                season_index,
                gameweek,
                capture.season_code,
            )
            result.setdefault(origin, {})[player_code] = observation
    return result


def _scoreline_probabilities(
    matches: Sequence[Any],
    target_origin: Origin,
) -> Dict[str, Dict[Tuple[int, str], float]]:
    training = [
        match for match in matches if match.origin < target_origin
    ]
    target = [
        match for match in matches if match.origin == target_origin
    ]
    _require(
        training and target,
        "clean-sheet-component.scoreline-cohort",
        "A scoreline fold has no training or target fixtures.",
    )
    cutoff: datetime = min(_instant(match.kickoff_utc) for match in target)
    baseline = _fit_baseline(training, cutoff)
    challenger, _ = _fit_dixon_coles(training, cutoff)
    result = {
        LEAGUE_SCORELINE_MODEL: {},
        DIXON_COLES_SCORELINE_MODEL: {},
    }
    for match in target:
        for name, rates in (
            (LEAGUE_SCORELINE_MODEL, baseline),
            (
                DIXON_COLES_SCORELINE_MODEL,
                _rates(challenger, match),
            ),
        ):
            prediction = _scoreline_prediction(match, rates)
            result[name][
                (match.fixture_id, match.home_team)
            ] = float(prediction["homeCleanSheetProbability"])
            result[name][
                (match.fixture_id, match.away_team)
            ] = float(prediction["awayCleanSheetProbability"])
    return result


def _combine_incumbent(
    target: Sequence[Sample],
    appearance: Sequence[Prediction],
    conditional: Sequence[Prediction],
) -> List[Prediction]:
    _require(
        len(target) == len(appearance) == len(conditional),
        "clean-sheet-component.incumbent-count",
        "The incumbent components have different player counts.",
    )
    result = []
    for sample, appearance_probability, conditional_mean in zip(
        target,
        appearance,
        conditional,
    ):
        _require_prediction_identity(
            sample,
            appearance_probability,
            conditional_mean,
        )
        result.append(
            Prediction(
                model=HURDLE_MODEL,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=(
                    float(appearance_probability.predicted)
                    * float(conditional_mean.predicted)
                ),
                actual=sample.actual,
            )
        )
    return result


def _combine_component(
    target: Sequence[Sample],
    observations: Mapping[int, ComponentObservation],
    appearance: Sequence[Prediction],
    played_60: Sequence[Prediction],
    residual: Sequence[Prediction],
    clean_sheet_probabilities: Mapping[Tuple[int, str], float],
    model_name: str,
    probability_model_name: str,
) -> Tuple[List[Prediction], List[Prediction]]:
    _require(
        len(target) == len(appearance) == len(played_60) == len(residual),
        "clean-sheet-component.prediction-count",
        "The player component predictions have different counts.",
    )
    points = []
    clean_sheets = []
    for sample, appearance_row, played_60_row, residual_row in zip(
        target,
        appearance,
        played_60,
        residual,
    ):
        _require_prediction_identity(
            sample,
            appearance_row,
            played_60_row,
            residual_row,
        )
        observation = _observation(sample, observations)
        clean_sheet_probability = clean_sheet_probabilities.get(
            (observation.fixture_id, observation.team_name)
        )
        _require(
            clean_sheet_probability is not None,
            "clean-sheet-component.fixture-alignment",
            "A player has no matching team clean-sheet probability.",
        )
        coherent_played_60 = min(
            float(appearance_row.predicted),
            float(played_60_row.predicted),
        )
        position_points = (
            4
            if sample.position in {"goalkeeper", "defender"}
            else 1
            if sample.position == "midfielder"
            else 0
        )
        player_clean_sheet_probability = (
            coherent_played_60 * clean_sheet_probability
        )
        predicted = (
            float(appearance_row.predicted)
            * float(residual_row.predicted)
            + player_clean_sheet_probability * position_points
        )
        _require(
            math.isfinite(predicted),
            "clean-sheet-component.non-finite",
            "A reconstructed player point mean is not finite.",
        )
        points.append(
            Prediction(
                model=model_name,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=predicted,
                actual=sample.actual,
            )
        )
        if position_points > 0:
            clean_sheets.append(
                Prediction(
                    model=probability_model_name,
                    season_code=sample.season_code,
                    gameweek=sample.gameweek,
                    player_id=sample.player_id,
                    position=sample.position,
                    predicted=player_clean_sheet_probability,
                    actual=int(observation.clean_sheet_points > 0),
                )
            )
    return points, clean_sheets


def _binary_sample(
    sample: Sample,
    observations: Mapping[int, ComponentObservation],
    target: str,
) -> Sample:
    observation = _observation(sample, observations)
    actual = (
        int(observation.minutes > 0)
        if target == "appearance"
        else int(observation.minutes >= 60)
    )
    return Sample(
        season_code=sample.season_code,
        gameweek=sample.gameweek,
        player_id=sample.player_id,
        position=sample.position,
        features=sample.features,
        actual=actual,
    )


def _residual_sample(
    sample: Sample,
    observations: Mapping[int, ComponentObservation],
) -> Sample:
    observation = _observation(sample, observations)
    return Sample(
        season_code=sample.season_code,
        gameweek=sample.gameweek,
        player_id=sample.player_id,
        position=sample.position,
        features=sample.features,
        actual=sample.actual - observation.clean_sheet_points,
    )


def _single_fixture(
    sample: Sample,
    observations: Mapping[int, ComponentObservation],
) -> bool:
    return sample.player_id in observations


def _observation(
    sample: Sample,
    observations: Mapping[int, ComponentObservation],
) -> ComponentObservation:
    observation = observations.get(sample.player_id)
    _require(
        observation is not None
        and observation.position == sample.position
        and observation.total_points == sample.actual,
        "clean-sheet-component.player-alignment",
        "A point sample does not match its component observation.",
    )
    return observation


def _season_index(captures: Sequence[Any], season_code: str) -> int:
    matches = [
        index
        for index, capture in enumerate(captures)
        if capture.season_code == season_code
    ]
    _require(
        len(matches) == 1,
        "clean-sheet-component.season-index",
        "A player sample has an unknown season.",
    )
    return matches[0]


def _fold_wins(
    folds: Sequence[Mapping[str, Any]],
    challenger: str,
    baseline: str,
    metric: str,
) -> int:
    return sum(
        float(_fold_model(fold, challenger)["metrics"][metric])
        < float(_fold_model(fold, baseline)["metrics"][metric])
        for fold in folds
    )


def _fold_model(
    fold: Mapping[str, Any],
    model_name: str,
) -> Mapping[str, Any]:
    matches = [
        model for model in fold["models"] if model["name"] == model_name
    ]
    _require(
        len(matches) == 1,
        "clean-sheet-component.fold-model",
        "A fold does not contain one copy of a fixed point model.",
    )
    return matches[0]


def _position_comparison(
    incumbent: Mapping[str, Any],
    challenger: Mapping[str, Any],
) -> Tuple[List[Dict[str, Any]], List[float]]:
    incumbent_positions = incumbent["slices"]["position"]
    challenger_positions = challenger["slices"]["position"]
    _require(
        set(incumbent_positions) == set(challenger_positions),
        "clean-sheet-component.position-slices",
        "The fixed point models have different position slices.",
    )
    rows = []
    regressions = []
    for position in sorted(incumbent_positions):
        incumbent_mae = float(incumbent_positions[position]["mae"])
        challenger_mae = float(challenger_positions[position]["mae"])
        regression = (
            (challenger_mae - incumbent_mae) / incumbent_mae
            if incumbent_mae > 0
            else 0.0 if challenger_mae <= incumbent_mae else math.inf
        )
        regressions.append(regression)
        rows.append(
            {
                "position": position,
                "incumbentMae": _round(incumbent_mae),
                "challengerMae": _round(challenger_mae),
                "maeRegressionFraction": _round(regression),
            }
        )
    return rows, regressions


def _fractional_improvement(baseline: float, challenger: float) -> float:
    return (
        (baseline - challenger) / baseline
        if baseline > 0
        else 0.0
    )


def _distribution_gates(
    league_metrics: Mapping[str, Any],
    challenger_metrics: Mapping[str, Any],
    fold_wins: int,
    fold_count: int,
) -> Dict[str, bool]:
    brier_improvement = _fractional_improvement(
        float(league_metrics["brierScore"]),
        float(challenger_metrics["brierScore"]),
    )
    return {
        "minimumBrierImprovement": (
            brier_improvement
            >= MINIMUM_CLEAN_SHEET_BRIER_IMPROVEMENT_FRACTION
        ),
        "logLossNonRegression": (
            float(challenger_metrics["logLoss"])
            <= float(league_metrics["logLoss"])
        ),
        "calibrationNonRegression": (
            float(challenger_metrics["calibrationError10"])
            <= float(league_metrics["calibrationError10"])
        ),
        "strictMajorityFoldWins": fold_wins > fold_count / 2,
    }


def _require_prediction_identity(
    sample: Sample,
    *predictions: Prediction,
) -> None:
    identity = (
        sample.season_code,
        sample.gameweek,
        sample.player_id,
        sample.position,
    )
    _require(
        all(
            identity
            == (
                prediction.season_code,
                prediction.gameweek,
                prediction.player_id,
                prediction.position,
            )
            for prediction in predictions
        ),
        "clean-sheet-component.prediction-alignment",
        "A component prediction is not aligned to its player sample.",
    )


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate a clean-sheet point component using the retained "
            "scoreline model and coherent player participation."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument(
        "--scoreline-evaluation",
        required=True,
        type=Path,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = evaluate_historical_player_clean_sheet_component(
            options.database,
            options.scoreline_evaluation,
        )
        _write_report(artifact, options.output)
        return 0
    except (TemporalRidgeError, sqlite3.Error) as exception:
        code = (
            exception.code
            if isinstance(exception, TemporalRidgeError)
            else "clean-sheet-component.database"
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
