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

import numpy as np
from sklearn.ensemble import HistGradientBoostingRegressor

from .historical_appearance_hurdle_points_evaluation import (
    APPEARANCE_MODEL,
)
from .historical_opening_policy_data import REGISTERED_SEASONS
from .historical_participation_evaluation import _predict_classifier
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
    _rates,
)
from .temporal_ridge import (
    Prediction,
    Sample,
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)
from .temporal_tree import (
    RANDOM_SEED,
    TREE_CONFIGURATION,
    _can_split,
    _matrix,
)

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-player-attacking-component-evaluation-v1"
TARGET_SEASONS = REGISTERED_SEASONS[1:]
EVALUATION_START_GAMEWEEK = 31
EVENTS = ("goal", "assist")
DIRECT_MODEL = "appearance-times-conditional-player-poisson"
LEAGUE_ALLOCATOR_MODEL = (
    "league-scoreline-times-player-attacking-share"
)
DIXON_COLES_ALLOCATOR_MODEL = (
    "dixon-coles-scoreline-times-player-attacking-share"
)
POSITION_ALLOCATOR_MODEL = (
    "dixon-coles-scoreline-times-position-attacking-share"
)
MODEL_NAMES = (
    DIRECT_MODEL,
    LEAGUE_ALLOCATOR_MODEL,
    DIXON_COLES_ALLOCATOR_MODEL,
    POSITION_ALLOCATOR_MODEL,
)
GOAL_MODEL = "conditional-player-goal-poisson"
ASSIST_MODEL = "conditional-player-assist-poisson"
MINIMUM_INTENSITY = 1e-6
MINIMUM_COMBINED_NLL_IMPROVEMENT_FRACTION = 0.01
MAXIMUM_EVENT_NLL_REGRESSION_FRACTION = 0.0
MAXIMUM_EVENT_BRIER_REGRESSION_FRACTION = 0.0
MAXIMUM_POSITION_NLL_REGRESSION_FRACTION = 0.05
MINIMUM_COMBINED_FOLD_WINS = 13
SCORELINE_DATA_IDENTITY = (
    "f6aecac9568d720248fe5e1078be1f0396f970dad3de82a7cc269b79dc90b25e"
)
SCORELINE_RUN_IDENTITY = (
    "2c10b68b1b0732a268002109c5bf7e4e5b266bc4df46ea16cf42b617bcdf6599"
)


@dataclass(frozen=True)
class AttackingObservation:
    player_code: int
    position: str
    team_name: str
    fixture_id: int
    minutes: int
    goals: int
    assists: int


@dataclass(frozen=True)
class EventPrediction:
    model: str
    event: str
    season_code: str
    gameweek: int
    player_id: int
    position: str
    intensity: float
    actual: int


def evaluate_historical_player_attacking_component(
    database_path: Path,
    scoreline_evaluation_path: Path,
) -> Dict[str, Any]:
    scoreline_identity = _load_scoreline_identity(
        Path(scoreline_evaluation_path)
    )
    connection = _open_connection(Path(database_path))
    try:
        captures = [
            _load_capture(connection, season)
            for season in REGISTERED_SEASONS
        ]
        _require(
            all(capture is not None for capture in captures),
            "attacking-component.archive",
            "Every registered historical archive is required.",
        )
        exact = [capture for capture in captures if capture is not None]
        targets = [
            _evaluate_target(
                connection,
                exact[: target_index + 1],
            )
            for target_index in range(1, len(exact))
        ]
    finally:
        connection.close()
    return _finish(exact, scoreline_identity, targets)


def _evaluate_target(
    connection: sqlite3.Connection,
    captures: Sequence[Any],
) -> Dict[str, Any]:
    samples_by_origin = _build_feature_table(connection, captures)
    observations_by_origin = _load_attacking_observations(
        connection,
        captures,
    )
    event_totals_by_origin = _load_attacking_event_totals(
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
    all_predictions: List[EventPrediction] = []
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
                observations_by_origin.get(origin, {}),
            )
        ]
        target = [
            sample
            for sample in samples_by_origin[target_origin]
            if _single_fixture(
                sample,
                observations_by_origin.get(target_origin, {}),
            )
        ]
        _require(
            training and target,
            "attacking-component.player-cohort",
            "An attacking component fold has an empty fixed cohort.",
        )
        appearance_training = [
            _appearance_sample(
                sample,
                observations_by_origin[
                    _sample_origin(captures, sample)
                ],
            )
            for sample in training
        ]
        appearance_target = [
            _appearance_sample(
                sample,
                observations_by_origin[target_origin],
            )
            for sample in target
        ]
        conditional_training = [
            sample
            for sample in training
            if _observation(
                sample,
                observations_by_origin[
                    _sample_origin(captures, sample)
                ],
            ).minutes
            > 0
        ]
        goal_training = [
            _event_sample(
                sample,
                observations_by_origin[
                    _sample_origin(captures, sample)
                ],
                "goal",
            )
            for sample in conditional_training
        ]
        assist_training = [
            _event_sample(
                sample,
                observations_by_origin[
                    _sample_origin(captures, sample)
                ],
                "assist",
            )
            for sample in conditional_training
        ]
        goal_target = [
            _event_sample(
                sample,
                observations_by_origin[target_origin],
                "goal",
            )
            for sample in target
        ]
        assist_target = [
            _event_sample(
                sample,
                observations_by_origin[target_origin],
                "assist",
            )
            for sample in target
        ]
        appearance, appearance_diagnostics = _predict_classifier(
            appearance_training,
            appearance_target,
            APPEARANCE_MODEL,
            continuous_features=FEATURES,
        )
        conditional_goals, goal_diagnostics = _predict_poisson(
            goal_training,
            goal_target,
            GOAL_MODEL,
        )
        conditional_assists, assist_diagnostics = _predict_poisson(
            assist_training,
            assist_target,
            ASSIST_MODEL,
        )
        scoreline_rates = _scoreline_rates(matches, target_origin)
        attribution = _attribution_fractions(
            matches,
            event_totals_by_origin,
            training_origins,
        )
        position_rates = _position_event_rates(
            conditional_training,
            observations_by_origin,
            captures,
        )
        fold_predictions = _allocate_events(
            target,
            observations_by_origin[target_origin],
            appearance,
            {
                "goal": conditional_goals,
                "assist": conditional_assists,
            },
            scoreline_rates,
            attribution,
            position_rates,
        )
        all_predictions.extend(fold_predictions)
        folds.append(
            {
                "seasonCode": target_season,
                "gameweek": target_origin.gameweek,
                "trainingOriginCount": len(training_origins),
                "trainingPlayerCount": len(training),
                "conditionalTrainingPlayerCount": len(
                    conditional_training
                ),
                "targetPlayerCount": len(target),
                "attributionFractions": {
                    event: _round(attribution[event])
                    for event in EVENTS
                },
                "models": [
                    _summarise_model(model, fold_predictions)
                    for model in MODEL_NAMES
                ],
                "diagnostics": {
                    APPEARANCE_MODEL: appearance_diagnostics,
                    GOAL_MODEL: goal_diagnostics,
                    ASSIST_MODEL: assist_diagnostics,
                },
            }
        )
    _require(
        folds,
        "attacking-component.folds",
        "No attacking component fold is eligible.",
    )
    return {
        "targetSeasonCode": target_season,
        "targetCaptureId": captures[-1].capture_id,
        "foldCount": len(folds),
        "playerEventRowCount": len(all_predictions),
        "models": [
            _summarise_model(model, all_predictions)
            for model in MODEL_NAMES
        ],
        "folds": folds,
        "_predictions": all_predictions,
    }


def _load_attacking_observations(
    connection: sqlite3.Connection,
    captures: Sequence[Any],
) -> Dict[Origin, Dict[int, AttackingObservation]]:
    result: Dict[Origin, Dict[int, AttackingObservation]] = {}
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
                gameweek.goals_scored,
                gameweek.assists
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
        grouped: Dict[tuple[int, int], List[Any]] = {}
        for row in rows:
            grouped.setdefault(
                (int(row["gameweek"]), int(row["player_code"])),
                [],
            ).append(row)
        for (gameweek, player_code), player_rows in grouped.items():
            if len(player_rows) != 1:
                continue
            row = player_rows[0]
            origin = Origin(
                season_index,
                gameweek,
                capture.season_code,
            )
            result.setdefault(origin, {})[player_code] = (
                AttackingObservation(
                    player_code=player_code,
                    position=str(row["position"]),
                    team_name=str(row["team_name"]),
                    fixture_id=int(row["fixture_id"]),
                    minutes=int(row["minutes"]),
                    goals=int(row["goals_scored"]),
                    assists=int(row["assists"]),
                )
            )
    return result


def _load_attacking_event_totals(
    connection: sqlite3.Connection,
    captures: Sequence[Any],
) -> Dict[Origin, Dict[str, int]]:
    result: Dict[Origin, Dict[str, int]] = {}
    for season_index, capture in enumerate(captures):
        rows = connection.execute(
            """
            SELECT
                gameweek,
                SUM(goals_scored) AS goals,
                SUM(assists) AS assists
            FROM historical_fpl_player_gameweeks
            WHERE capture_id = :capture_id
            GROUP BY gameweek
            ORDER BY gameweek;
            """,
            {"capture_id": capture.capture_id},
        ).fetchall()
        for row in rows:
            origin = Origin(
                season_index,
                int(row["gameweek"]),
                capture.season_code,
            )
            result[origin] = {
                "goal": int(row["goals"]),
                "assist": int(row["assists"]),
            }
    return result


def _predict_poisson(
    training: Sequence[Sample],
    target: Sequence[Sample],
    model_name: str,
) -> tuple[List[Prediction], Dict[str, Any]]:
    _require(
        training and target,
        "attacking-component.poisson-cohort",
        "An attacking-event Poisson fold is empty.",
    )
    raw_training, feature_names = _matrix(training, FEATURES)
    raw_target, _ = _matrix(target, FEATURES)
    selected = [
        index
        for index in range(raw_training.shape[1])
        if _can_split(raw_training[:, index])
    ]
    actuals = np.asarray(
        [sample.actual for sample in training],
        dtype=float,
    )
    _require(
        bool(np.all(actuals >= 0.0)),
        "attacking-component.negative-event-count",
        "An attacking-event training count is negative.",
    )
    if not selected or float(actuals.sum()) == 0.0:
        intensities = np.full(
            len(target),
            (float(actuals.sum()) + 1.0)
            / (len(actuals) + 1.0),
        )
        iterations = 0
    else:
        estimator = HistGradientBoostingRegressor(
            loss="poisson",
            learning_rate=0.05,
            max_iter=100,
            max_leaf_nodes=7,
            min_samples_leaf=20,
            l2_regularization=10.0,
            max_bins=63,
            early_stopping=False,
            random_state=RANDOM_SEED,
        )
        estimator.fit(raw_training[:, selected], actuals)
        intensities = estimator.predict(raw_target[:, selected])
        iterations = int(estimator.n_iter_)
    _require(
        bool(np.isfinite(intensities).all()),
        "attacking-component.non-finite-intensity",
        "The attacking-event model produced a non-finite intensity.",
    )
    intensities = np.maximum(intensities, MINIMUM_INTENSITY)
    return (
        [
            Prediction(
                model=model_name,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=float(intensity),
                actual=sample.actual,
            )
            for sample, intensity in zip(
                target,
                intensities,
                strict=True,
            )
        ],
        {
            "implementation": (
                "sklearn.ensemble.HistGradientBoostingRegressor"
            ),
            "loss": "poisson",
            "trainingRows": len(training),
            "eventCount": int(actuals.sum()),
            "candidateFeatureCount": len(feature_names),
            "modelFeatureCount": len(selected),
            "completedIterations": iterations,
            "configuration": {
                **dict(TREE_CONFIGURATION),
                "loss": "poisson",
            },
        },
    )


def _scoreline_rates(
    matches: Sequence[Any],
    target_origin: Origin,
) -> Dict[str, Dict[tuple[int, str], float]]:
    training = [
        match for match in matches if match.origin < target_origin
    ]
    target = [
        match for match in matches if match.origin == target_origin
    ]
    _require(
        training and target,
        "attacking-component.scoreline-cohort",
        "An attacking-event fold has no scoreline cohort.",
    )
    cutoff: datetime = min(_instant(match.kickoff_utc) for match in target)
    baseline = _fit_baseline(training, cutoff)
    challenger, _ = _fit_dixon_coles(training, cutoff)
    result = {
        LEAGUE_SCORELINE_MODEL: {},
        DIXON_COLES_SCORELINE_MODEL: {},
    }
    for match in target:
        dixon_coles = _rates(challenger, match)
        for model, home_rate, away_rate in (
            (
                LEAGUE_SCORELINE_MODEL,
                baseline.home,
                baseline.away,
            ),
            (
                DIXON_COLES_SCORELINE_MODEL,
                dixon_coles.home,
                dixon_coles.away,
            ),
        ):
            result[model][
                (match.fixture_id, match.home_team)
            ] = float(home_rate)
            result[model][
                (match.fixture_id, match.away_team)
            ] = float(away_rate)
    return result


def _attribution_fractions(
    matches: Sequence[Any],
    event_totals: Mapping[Origin, Mapping[str, int]],
    training_origins: Sequence[Origin],
) -> Dict[str, float]:
    origin_set = set(training_origins)
    team_goals = sum(
        match.home_goals + match.away_goals
        for match in matches
        if match.origin in origin_set
    )
    _require(
        team_goals > 0,
        "attacking-component.no-training-goals",
        "The attacking-event fold has no training team goals.",
    )
    player_goals = sum(
        int(event_totals.get(origin, {}).get("goal", 0))
        for origin in training_origins
    )
    player_assists = sum(
        int(event_totals.get(origin, {}).get("assist", 0))
        for origin in training_origins
    )
    return {
        "goal": min(
            1.0,
            max(0.0, (player_goals + 1.0) / (team_goals + 2.0)),
        ),
        "assist": min(
            1.0,
            max(0.0, (player_assists + 1.0) / (team_goals + 2.0)),
        ),
    }


def _position_event_rates(
    training: Sequence[Sample],
    observations: Mapping[Origin, Mapping[int, AttackingObservation]],
    captures: Sequence[Any],
) -> Dict[str, Dict[str, float]]:
    counts: Dict[str, Dict[str, int]] = {
        event: {} for event in EVENTS
    }
    exposures: Dict[str, int] = {}
    for sample in training:
        observation = _observation(
            sample,
            observations[_sample_origin(captures, sample)],
        )
        exposures[sample.position] = (
            exposures.get(sample.position, 0) + 1
        )
        for event in EVENTS:
            counts[event][sample.position] = (
                counts[event].get(sample.position, 0)
                + _event_value(observation, event)
            )
    global_rates = {
        event: (
            sum(counts[event].values()) + 1.0
        )
        / (sum(exposures.values()) + 1.0)
        for event in EVENTS
    }
    return {
        event: {
            position: (
                counts[event].get(position, 0)
                + 20.0 * global_rates[event]
            )
            / (exposure + 20.0)
            for position, exposure in exposures.items()
        }
        for event in EVENTS
    }


def _allocate_events(
    target: Sequence[Sample],
    observations: Mapping[int, AttackingObservation],
    appearance: Sequence[Prediction],
    conditional: Mapping[str, Sequence[Prediction]],
    scoreline_rates: Mapping[
        str,
        Mapping[tuple[int, str], float],
    ],
    attribution: Mapping[str, float],
    position_rates: Mapping[str, Mapping[str, float]],
) -> List[EventPrediction]:
    appearance_by_player = {
        prediction.player_id: float(prediction.predicted)
        for prediction in appearance
    }
    conditional_by_event = {
        event: {
            prediction.player_id: float(prediction.predicted)
            for prediction in conditional[event]
        }
        for event in EVENTS
    }
    cohorts: Dict[tuple[int, str], List[Sample]] = {}
    for sample in target:
        observation = _observation(sample, observations)
        cohorts.setdefault(
            (observation.fixture_id, observation.team_name),
            [],
        ).append(sample)
    predictions: List[EventPrediction] = []
    for cohort_key, players in sorted(cohorts.items()):
        _require(
            cohort_key
            in scoreline_rates[DIXON_COLES_SCORELINE_MODEL]
            and cohort_key
            in scoreline_rates[LEAGUE_SCORELINE_MODEL],
            "attacking-component.fixture-alignment",
            "A player cohort has no matching team scoring rate.",
        )
        for event in EVENTS:
            direct = {
                sample.player_id: (
                    appearance_by_player[sample.player_id]
                    * conditional_by_event[event][sample.player_id]
                )
                for sample in players
            }
            player_shares = _normalise_weights(
                direct,
                {
                    sample.player_id: (
                        appearance_by_player[sample.player_id]
                        * position_rates[event][sample.position]
                    )
                    for sample in players
                },
            )
            position_shares = _normalise_weights(
                {
                    sample.player_id: (
                        appearance_by_player[sample.player_id]
                        * position_rates[event][sample.position]
                    )
                    for sample in players
                },
                {
                    sample.player_id: 1.0 for sample in players
                },
            )
            allocated_team_rates = {
                LEAGUE_ALLOCATOR_MODEL: (
                    scoreline_rates[LEAGUE_SCORELINE_MODEL][cohort_key]
                    * attribution[event]
                ),
                DIXON_COLES_ALLOCATOR_MODEL: (
                    scoreline_rates[DIXON_COLES_SCORELINE_MODEL][
                        cohort_key
                    ]
                    * attribution[event]
                ),
                POSITION_ALLOCATOR_MODEL: (
                    scoreline_rates[DIXON_COLES_SCORELINE_MODEL][
                        cohort_key
                    ]
                    * attribution[event]
                ),
            }
            for sample in players:
                observation = _observation(sample, observations)
                values = {
                    DIRECT_MODEL: direct[sample.player_id],
                    LEAGUE_ALLOCATOR_MODEL: (
                        allocated_team_rates[LEAGUE_ALLOCATOR_MODEL]
                        * player_shares[sample.player_id]
                    ),
                    DIXON_COLES_ALLOCATOR_MODEL: (
                        allocated_team_rates[
                            DIXON_COLES_ALLOCATOR_MODEL
                        ]
                        * player_shares[sample.player_id]
                    ),
                    POSITION_ALLOCATOR_MODEL: (
                        allocated_team_rates[POSITION_ALLOCATOR_MODEL]
                        * position_shares[sample.player_id]
                    ),
                }
                for model, intensity in values.items():
                    predictions.append(
                        EventPrediction(
                            model=model,
                            event=event,
                            season_code=sample.season_code,
                            gameweek=sample.gameweek,
                            player_id=sample.player_id,
                            position=sample.position,
                            intensity=max(
                                MINIMUM_INTENSITY,
                                float(intensity),
                            ),
                            actual=_event_value(
                                observation,
                                event,
                            ),
                        )
                    )
    expected_count = len(target) * len(EVENTS) * len(MODEL_NAMES)
    _require(
        len(predictions) == expected_count,
        "attacking-component.allocation-count",
        "The attacking-event allocation is incomplete.",
    )
    return predictions


def _normalise_weights(
    weights: Mapping[int, float],
    fallback: Mapping[int, float],
) -> Dict[int, float]:
    positive = {
        key: max(0.0, float(value))
        for key, value in weights.items()
    }
    total = sum(positive.values())
    if total <= 0.0:
        positive = {
            key: max(0.0, float(value))
            for key, value in fallback.items()
        }
        total = sum(positive.values())
    _require(
        total > 0.0,
        "attacking-component.zero-allocation-weight",
        "An attacking-event team has no positive allocation weight.",
    )
    return {key: value / total for key, value in positive.items()}


def _summarise_model(
    model: str,
    predictions: Sequence[EventPrediction],
) -> Dict[str, Any]:
    rows = [row for row in predictions if row.model == model]
    _require(
        rows,
        "attacking-component.model-empty",
        "An attacking-event model has no scored rows.",
    )
    return {
        "name": model,
        "metrics": _metrics(rows),
        "slices": {
            "event": {
                event: _metrics(
                    [row for row in rows if row.event == event]
                )
                for event in EVENTS
            },
            "position": {
                position: _metrics(
                    [
                        row
                        for row in rows
                        if row.position == position
                    ]
                )
                for position in sorted(
                    {row.position for row in rows}
                )
            },
        },
    }


def _metrics(rows: Sequence[EventPrediction]) -> Dict[str, Any]:
    _require(
        rows,
        "attacking-component.metrics-empty",
        "An attacking-event metric slice is empty.",
    )
    nll = [
        row.intensity
        - row.actual * math.log(max(MINIMUM_INTENSITY, row.intensity))
        + math.lgamma(row.actual + 1.0)
        for row in rows
    ]
    brier = [
        (
            (1.0 - math.exp(-row.intensity))
            - float(row.actual > 0)
        )
        ** 2
        for row in rows
    ]
    predicted_mean = sum(row.intensity for row in rows) / len(rows)
    actual_mean = sum(row.actual for row in rows) / len(rows)
    return {
        "count": len(rows),
        "poissonNegativeLogLikelihood": _round(
            sum(nll) / len(nll)
        ),
        "eventBrierScore": _round(sum(brier) / len(brier)),
        "meanAbsoluteError": _round(
            sum(
                abs(row.intensity - row.actual)
                for row in rows
            )
            / len(rows)
        ),
        "predictedMean": _round(predicted_mean),
        "actualMean": _round(actual_mean),
        "absoluteCalibrationError": _round(
            abs(predicted_mean - actual_mean)
        ),
    }


def _finish(
    captures: Sequence[Any],
    scoreline_identity: Mapping[str, str],
    target_reports: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    predictions = [
        row
        for target in target_reports
        for row in target["_predictions"]
    ]
    folds = [
        fold
        for target in target_reports
        for fold in target["folds"]
    ]
    models = [
        _summarise_model(model, predictions)
        for model in MODEL_NAMES
    ]
    screen = _screen(models, folds)
    targets = [
        {
            key: value
            for key, value in target.items()
            if key != "_predictions"
        }
        for target in target_reports
    ]
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": (
            "historical-player-attacking-component-evaluation"
        ),
        "evaluatorVersion": EVALUATOR_VERSION,
        "status": "complete",
        "researchStatus": "retrospective-component-screen",
        "registeredSeasonCodes": list(REGISTERED_SEASONS),
        "evaluationTargetSeasonCodes": list(TARGET_SEASONS),
        "evaluationStartGameweek": EVALUATION_START_GAMEWEEK,
        "captures": [
            {
                "seasonCode": capture.season_code,
                "captureId": capture.capture_id,
                "sourceRevision": capture.source_revision,
                "gameweeksSha256": capture.gameweeks_sha256,
            }
            for capture in captures
        ],
        "scorelineReplicationIdentity": dict(scoreline_identity),
        "factorization": (
            "team-goal-intensity-times-attributed-event-fraction-times-"
            "normalised-player-appearance-conditional-poisson-share"
        ),
        "models": models,
        "targets": targets,
        "fixedScreen": {
            "minimumCombinedNllImprovementFraction": (
                MINIMUM_COMBINED_NLL_IMPROVEMENT_FRACTION
            ),
            "maximumEventNllRegressionFraction": (
                MAXIMUM_EVENT_NLL_REGRESSION_FRACTION
            ),
            "maximumEventBrierRegressionFraction": (
                MAXIMUM_EVENT_BRIER_REGRESSION_FRACTION
            ),
            "maximumPositionNllRegressionFraction": (
                MAXIMUM_POSITION_NLL_REGRESSION_FRACTION
            ),
            "minimumCombinedFoldWins": (
                MINIMUM_COMBINED_FOLD_WINS
            ),
            "mustBeatLeagueScorelineAllocator": True,
            "mustBeatPositionShareAllocator": True,
        },
        "screen": screen,
        "decision": (
            "retain-attacking-event-component-for-opening-distribution"
            if screen["passes"]
            else "do-not-retain-attacking-event-component"
        ),
        "isPromoted": False,
        "influencesAdvice": False,
        "limitations": [
            (
                "The component scores Gameweeks 31 through 38 rather than "
                "historical opening distributions. Passing only permits the "
                "separate opening reconstruction."
            ),
            (
                "The marked-Poisson allocation represents goal and assist "
                "counts but not scorer-assister exclusion, rebound state, "
                "penalty takers, own goals or multiple-assist rule details."
            ),
            (
                "Team event attribution fractions are expanding-fold league "
                "rates. They do not use target outcomes."
            ),
            (
                "The target cohort excludes double-fixture player-Gameweeks "
                "because one aggregated player label cannot identify the "
                "fixture receiving each event."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "captures": artifact["captures"],
            "scorelineReplicationIdentity": artifact[
                "scorelineReplicationIdentity"
            ],
            "evaluationTargetSeasonCodes": artifact[
                "evaluationTargetSeasonCodes"
            ],
            "evaluationStartGameweek": artifact[
                "evaluationStartGameweek"
            ],
            "factorization": artifact["factorization"],
            "fixedScreen": artifact["fixedScreen"],
            "targets": [
                {
                    "targetSeasonCode": target[
                        "targetSeasonCode"
                    ],
                    "targetCaptureId": target["targetCaptureId"],
                    "foldCount": target["foldCount"],
                    "playerEventRowCount": target[
                        "playerEventRowCount"
                    ],
                }
                for target in targets
            ],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _screen(
    models: Sequence[Mapping[str, Any]],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    by_name = {str(model["name"]): model for model in models}
    _require(
        set(by_name) == set(MODEL_NAMES),
        "attacking-component.screen-models",
        "The fixed attacking-event model set is incomplete.",
    )
    incumbent = by_name[DIRECT_MODEL]
    challenger = by_name[DIXON_COLES_ALLOCATOR_MODEL]
    league = by_name[LEAGUE_ALLOCATOR_MODEL]
    position = by_name[POSITION_ALLOCATOR_MODEL]
    incumbent_nll = float(
        incumbent["metrics"]["poissonNegativeLogLikelihood"]
    )
    challenger_nll = float(
        challenger["metrics"]["poissonNegativeLogLikelihood"]
    )
    combined_improvement = (
        (incumbent_nll - challenger_nll) / incumbent_nll
        if incumbent_nll > 0.0
        else 0.0
    )
    event_comparisons = []
    event_nll_regressions = []
    event_brier_regressions = []
    for event in EVENTS:
        incumbent_event = incumbent["slices"]["event"][event]
        challenger_event = challenger["slices"]["event"][event]
        incumbent_event_nll = float(
            incumbent_event["poissonNegativeLogLikelihood"]
        )
        challenger_event_nll = float(
            challenger_event["poissonNegativeLogLikelihood"]
        )
        incumbent_brier = float(
            incumbent_event["eventBrierScore"]
        )
        challenger_brier = float(
            challenger_event["eventBrierScore"]
        )
        nll_regression = (
            (challenger_event_nll - incumbent_event_nll)
            / incumbent_event_nll
            if incumbent_event_nll > 0.0
            else 0.0
        )
        brier_regression = (
            (challenger_brier - incumbent_brier)
            / incumbent_brier
            if incumbent_brier > 0.0
            else 0.0
        )
        event_nll_regressions.append(nll_regression)
        event_brier_regressions.append(brier_regression)
        event_comparisons.append(
            {
                "event": event,
                "incumbentPoissonNll": _round(
                    incumbent_event_nll
                ),
                "challengerPoissonNll": _round(
                    challenger_event_nll
                ),
                "nllRegressionFraction": _round(
                    nll_regression
                ),
                "incumbentEventBrier": _round(incumbent_brier),
                "challengerEventBrier": _round(
                    challenger_brier
                ),
                "brierRegressionFraction": _round(
                    brier_regression
                ),
            }
        )
    position_comparisons = []
    position_regressions = []
    for player_position in sorted(
        incumbent["slices"]["position"]
    ):
        incumbent_position = float(
            incumbent["slices"]["position"][player_position][
                "poissonNegativeLogLikelihood"
            ]
        )
        challenger_position = float(
            challenger["slices"]["position"][player_position][
                "poissonNegativeLogLikelihood"
            ]
        )
        regression = (
            (challenger_position - incumbent_position)
            / incumbent_position
            if incumbent_position > 0.0
            else 0.0
        )
        position_regressions.append(regression)
        position_comparisons.append(
            {
                "position": player_position,
                "incumbentPoissonNll": _round(
                    incumbent_position
                ),
                "challengerPoissonNll": _round(
                    challenger_position
                ),
                "nllRegressionFraction": _round(regression),
            }
        )
    fold_rows = []
    fold_wins = 0
    for fold in folds:
        fold_models = {
            str(model["name"]): model
            for model in fold["models"]
        }
        incumbent_fold = float(
            fold_models[DIRECT_MODEL]["metrics"][
                "poissonNegativeLogLikelihood"
            ]
        )
        challenger_fold = float(
            fold_models[DIXON_COLES_ALLOCATOR_MODEL]["metrics"][
                "poissonNegativeLogLikelihood"
            ]
        )
        wins = challenger_fold < incumbent_fold
        fold_wins += int(wins)
        fold_rows.append(
            {
                "seasonCode": fold["seasonCode"],
                "gameweek": fold["gameweek"],
                "incumbentPoissonNll": _round(incumbent_fold),
                "challengerPoissonNll": _round(challenger_fold),
                "challengerWins": wins,
            }
        )
    league_nll = float(
        league["metrics"]["poissonNegativeLogLikelihood"]
    )
    position_nll = float(
        position["metrics"]["poissonNegativeLogLikelihood"]
    )
    gates = {
        "combinedNllImprovement": (
            combined_improvement
            >= MINIMUM_COMBINED_NLL_IMPROVEMENT_FRACTION
        ),
        "eventNllNonRegression": (
            max(event_nll_regressions)
            <= MAXIMUM_EVENT_NLL_REGRESSION_FRACTION
        ),
        "eventBrierNonRegression": (
            max(event_brier_regressions)
            <= MAXIMUM_EVENT_BRIER_REGRESSION_FRACTION
        ),
        "positionNllStability": (
            max(position_regressions)
            <= MAXIMUM_POSITION_NLL_REGRESSION_FRACTION
        ),
        "combinedFoldWins": (
            fold_wins >= MINIMUM_COMBINED_FOLD_WINS
        ),
        "beatsLeagueScorelineAllocator": challenger_nll < league_nll,
        "beatsPositionShareAllocator": (
            challenger_nll < position_nll
        ),
    }
    return {
        "combinedNllImprovementFraction": _round(
            combined_improvement
        ),
        "eventComparisons": event_comparisons,
        "positionComparisons": position_comparisons,
        "combinedFoldWins": fold_wins,
        "foldCount": len(fold_rows),
        "foldComparisons": fold_rows,
        "challengerPoissonNll": _round(challenger_nll),
        "leagueAllocatorPoissonNll": _round(league_nll),
        "positionAllocatorPoissonNll": _round(position_nll),
        "gates": gates,
        "passes": all(gates.values()),
    }


def _appearance_sample(
    sample: Sample,
    observations: Mapping[int, AttackingObservation],
) -> Sample:
    observation = _observation(sample, observations)
    return Sample(
        season_code=sample.season_code,
        gameweek=sample.gameweek,
        player_id=sample.player_id,
        position=sample.position,
        features=sample.features,
        actual=int(observation.minutes > 0),
    )


def _event_sample(
    sample: Sample,
    observations: Mapping[int, AttackingObservation],
    event: str,
) -> Sample:
    observation = _observation(sample, observations)
    return Sample(
        season_code=sample.season_code,
        gameweek=sample.gameweek,
        player_id=sample.player_id,
        position=sample.position,
        features=sample.features,
        actual=_event_value(observation, event),
    )


def _event_value(
    observation: AttackingObservation,
    event: str,
) -> int:
    _require(
        event in EVENTS,
        "attacking-component.event",
        "An unsupported attacking event was requested.",
    )
    return observation.goals if event == "goal" else observation.assists


def _single_fixture(
    sample: Sample,
    observations: Mapping[int, AttackingObservation],
) -> bool:
    return sample.player_id in observations


def _observation(
    sample: Sample,
    observations: Mapping[int, AttackingObservation],
) -> AttackingObservation:
    observation = observations.get(sample.player_id)
    _require(
        observation is not None
        and observation.position == sample.position,
        "attacking-component.player-alignment",
        "A player sample does not match its attacking observation.",
    )
    return observation


def _sample_origin(
    captures: Sequence[Any],
    sample: Sample,
) -> Origin:
    matches = [
        index
        for index, capture in enumerate(captures)
        if capture.season_code == sample.season_code
    ]
    _require(
        len(matches) == 1,
        "attacking-component.season-index",
        "An attacking-event sample has an unknown season.",
    )
    return Origin(matches[0], sample.gameweek, sample.season_code)


def _load_scoreline_identity(path: Path) -> Dict[str, str]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise TemporalRidgeError(
            "attacking-component.scoreline-artifact",
            "The retained scoreline replication artifact is unavailable.",
        ) from exception
    _require(
        document.get("dataIdentitySha256") == SCORELINE_DATA_IDENTITY
        and document.get("runIdentitySha256") == SCORELINE_RUN_IDENTITY
        and document.get("decision")
        == "retain-scoreline-model-for-player-component-ablation",
        "attacking-component.scoreline-identity",
        "The scoreline replication identity or decision differs.",
    )
    return {
        "dataIdentitySha256": SCORELINE_DATA_IDENTITY,
        "runIdentitySha256": SCORELINE_RUN_IDENTITY,
    }


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate leakage-safe team-conditioned player goal and assist "
            "intensities on expanding historical folds."
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
        artifact = evaluate_historical_player_attacking_component(
            options.database,
            options.scoreline_evaluation,
        )
        _write_report(artifact, options.output)
        return 0
    except TemporalRidgeError as exception:
        sys.stderr.write(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "error",
                    "error": str(exception),
                },
                sort_keys=True,
            )
            + "\n"
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
