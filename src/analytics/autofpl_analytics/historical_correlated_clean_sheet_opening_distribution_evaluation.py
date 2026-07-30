from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path
from typing import Any, DefaultDict, Dict, List, Mapping, Optional, Sequence

import numpy as np

from .baseline import (
    DistributionPrediction,
    ProbabilityPrediction,
    _summarise_distribution_model,
    _summarise_probability_model,
)
from .historical_appearance_hurdle_opening_distribution_evaluation import (
    _predictions_for_target,
)
from .historical_appearance_hurdle_opening_evaluation import (
    APPEARANCE_MODEL,
    CONDITIONAL_MODEL,
    HURDLE_MODEL,
    SCENARIO_ARTIFACT_VERSION as INCUMBENT_ARTIFACT_VERSION,
    _reconstruct_hurdle_target_from_samples,
    build_historical_appearance_hurdle_opening_scenarios,
)
from .historical_appearance_hurdle_points_evaluation import (
    _observations_by_origin,
)
from .historical_joint_scenario_evaluation import (
    MAXIMUM_POINTS,
    MINIMUM_POINTS,
    JointFold,
    generate_joint_fold,
)
from .historical_opening_forecast_reconstruction import (
    FIXTURE_PROXY,
    TARGET_GAMEWEEKS,
    _load_fixture_proxy,
    _target_sample,
)
from .historical_opening_policy_data import (
    REGISTERED_SEASONS,
    OpeningFold,
    _load_opening_fold,
    _required_capture,
)
from .historical_player_clean_sheet_component_evaluation import (
    _binary_sample,
    _load_component_observations,
)
from .historical_preseason_evaluation import _build_samples
from .multi_season_evaluation import (
    FEATURES,
    Observation,
    _build_feature_table,
    _load_observations,
)
from .team_goal_strength_evaluation import (
    CHALLENGER_MODEL as SCORELINE_MODEL,
    EVALUATOR_VERSION as SCORELINE_EVALUATOR_VERSION,
    Match,
    _fit_dixon_coles,
    _instant,
    _rates,
    _score_matrix,
)
from .temporal_ridge import (
    Prediction,
    Sample,
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)
from .temporal_tree import TREE_CONFIGURATION

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = (
    "historical-correlated-clean-sheet-opening-distribution-evaluation"
)
ARTIFACT_VERSION = (
    "historical-correlated-clean-sheet-opening-distribution-evaluation-v1"
)
SCENARIO_ARTIFACT_TYPE = (
    "historical-correlated-clean-sheet-opening-scenario-reconstruction"
)
SCENARIO_ARTIFACT_VERSION = (
    "historical-correlated-clean-sheet-opening-scenario-reconstruction-v1"
)
STATUS = "retrospective-distribution-challenger-evaluated"
SCENARIO_STATUS = "retrospective-input-reconstruction-no-outcomes-opened"
INCUMBENT_MODEL = "appearance-hurdle-opening-joint-distribution"
CHALLENGER_MODEL = (
    "mean-preserving-correlated-clean-sheet-opening-joint-distribution"
)
INCUMBENT_APPEARANCE = "appearance-hurdle-opening-appearance"
CHALLENGER_APPEARANCE = (
    "correlated-clean-sheet-opening-appearance-unchanged"
)
PLAYED_60_MODEL = "historical-played-60-histogram-classifier"
SCENARIO_MODEL = (
    "residual-joint-bootstrap-plus-correlated-dixon-coles-clean-sheet-v1"
)
SCORELINE_REPLICATION_DATA_IDENTITY = (
    "f6aecac9568d720248fe5e1078be1f0396f970dad3de82a7cc269b79dc90b25e"
)
SCORELINE_REPLICATION_RUN_IDENTITY = (
    "2c10b68b1b0732a268002109c5bf7e4e5b266bc4df46ea16cf42b617bcdf6599"
)
MINIMUM_CRPS_IMPROVEMENT_FRACTION = 0.01
MINIMUM_TARGET_WINS = 2
MAXIMUM_POSITION_CRPS_REGRESSION_FRACTION = 0.05
MAXIMUM_APPEARANCE_BRIER_DELTA = 0.0
MAXIMUM_APPEARANCE_LOG_LOSS_DELTA = 0.0
MAXIMUM_POINT_MEAN_ABSOLUTE_DELTA = 0.0
DECISION_RETAIN = (
    "retain-correlated-clean-sheet-opening-distribution-for-policy-screen"
)
DECISION_REJECT = (
    "do-not-retain-correlated-clean-sheet-opening-distribution"
)
POSITION_CLEAN_SHEET_POINTS = {
    "goalkeeper": 4,
    "defender": 4,
    "midfielder": 1,
    "forward": 0,
}


def build_historical_correlated_clean_sheet_opening_distribution_evaluation(
    database_path: Path,
    scoreline_evaluation_path: Path,
    *,
    challenger_scenarios: Optional[Mapping[str, Any]] = None,
) -> Dict[str, Any]:
    path = Path(database_path)
    scoreline_identity = _load_scoreline_identity(
        Path(scoreline_evaluation_path)
    )
    incumbent = build_historical_appearance_hurdle_opening_scenarios(path)
    challenger = (
        dict(challenger_scenarios)
        if challenger_scenarios is not None
        else build_historical_correlated_clean_sheet_opening_scenarios(
            path
        )
    )
    _require_scenario_pair(incumbent, challenger)

    connection = _open_connection(path)
    try:
        captures = tuple(
            _required_capture(connection, season)
            for season in REGISTERED_SEASONS
        )
        folds = {
            fold.target_capture.season_code: fold
            for target_index in range(1, len(captures))
            for fold in (
                _load_opening_fold(
                    connection,
                    captures,
                    target_index,
                    include_outcomes=True,
                ),
            )
        }
    finally:
        connection.close()

    incumbent_targets = {
        str(target["targetSeasonCode"]): target
        for target in incumbent["targets"]
    }
    challenger_targets = {
        str(target["targetSeasonCode"]): target
        for target in challenger["targets"]
    }
    distributions: List[DistributionPrediction] = []
    appearances: List[ProbabilityPrediction] = []
    targets = []
    for season in sorted(challenger_targets):
        incumbent_distribution, incumbent_appearance = (
            _predictions_for_target(
                incumbent_targets[season],
                folds[season],
                INCUMBENT_MODEL,
                INCUMBENT_APPEARANCE,
            )
        )
        challenger_distribution, challenger_appearance = (
            _predictions_for_target(
                challenger_targets[season],
                folds[season],
                CHALLENGER_MODEL,
                CHALLENGER_APPEARANCE,
            )
        )
        distributions.extend(incumbent_distribution)
        distributions.extend(challenger_distribution)
        appearances.extend(incumbent_appearance)
        appearances.extend(challenger_appearance)
        target_distributions = [
            *incumbent_distribution,
            *challenger_distribution,
        ]
        target_appearances = [
            *incumbent_appearance,
            *challenger_appearance,
        ]
        targets.append(
            {
                "targetSeasonCode": season,
                "targetCaptureId": folds[
                    season
                ].target_capture.capture_id,
                "playerGameweekCount": len(incumbent_distribution),
                "scenarioCount": int(
                    challenger_targets[season]["scenarioCount"]
                ),
                "distributionModels": [
                    _summarise_distribution_model(
                        model,
                        target_distributions,
                    )
                    for model in (INCUMBENT_MODEL, CHALLENGER_MODEL)
                ],
                "appearanceModels": [
                    _summarise_probability_model(
                        model,
                        target_appearances,
                    )
                    for model in (
                        INCUMBENT_APPEARANCE,
                        CHALLENGER_APPEARANCE,
                    )
                ],
                "meanPreservation": challenger_targets[season][
                    "meanPreservation"
                ],
                "cleanSheetDiagnostics": challenger_targets[season][
                    "cleanSheetDiagnostics"
                ],
            }
        )

    distribution_models = [
        _summarise_distribution_model(model, distributions)
        for model in (INCUMBENT_MODEL, CHALLENGER_MODEL)
    ]
    appearance_models = [
        _summarise_probability_model(model, appearances)
        for model in (INCUMBENT_APPEARANCE, CHALLENGER_APPEARANCE)
    ]
    screen = _screen(distribution_models, appearance_models, targets)
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "researchStatus": "reused-opened-targets-no-promotion",
        "targetOutcomesOpened": True,
        "incumbentSource": {
            "artifactVersion": incumbent["artifactVersion"],
            "dataIdentitySha256": incumbent["dataIdentitySha256"],
            "runIdentitySha256": incumbent["runIdentitySha256"],
        },
        "challengerSource": {
            "artifactVersion": challenger["artifactVersion"],
            "dataIdentitySha256": challenger["dataIdentitySha256"],
            "runIdentitySha256": challenger["runIdentitySha256"],
        },
        "scorelineValidationSource": {
            "evaluatorVersion": SCORELINE_EVALUATOR_VERSION,
            "replicationDataIdentitySha256": (
                scoreline_identity["dataIdentitySha256"]
            ),
            "replicationRunIdentitySha256": (
                scoreline_identity["runIdentitySha256"]
            ),
            "replicationDecision": (
                "retain-scoreline-model-for-player-component-ablation"
            ),
        },
        "fixedScreen": {
            "primaryMetric": "aggregate-player-gameweek-mean-crps",
            "minimumCrpsImprovementFraction": (
                MINIMUM_CRPS_IMPROVEMENT_FRACTION
            ),
            "minimumTargetSeasonWins": MINIMUM_TARGET_WINS,
            "maximumPositionCrpsRegressionFraction": (
                MAXIMUM_POSITION_CRPS_REGRESSION_FRACTION
            ),
            "maximumAppearanceBrierDelta": (
                MAXIMUM_APPEARANCE_BRIER_DELTA
            ),
            "maximumAppearanceLogLossDelta": (
                MAXIMUM_APPEARANCE_LOG_LOSS_DELTA
            ),
            "maximumPointMeanAbsoluteDelta": (
                MAXIMUM_POINT_MEAN_ABSOLUTE_DELTA
            ),
        },
        "distributionModels": distribution_models,
        "appearanceModels": appearance_models,
        "targets": targets,
        "screen": screen,
        "decision": (
            DECISION_RETAIN if screen["passes"] else DECISION_REJECT
        ),
        "isPromoted": False,
        "influencesAdvice": False,
        "limitations": [
            (
                "The three historical opening targets were already opened. "
                "The result can retain a prospective policy challenger but "
                "cannot promote one."
            ),
            (
                "Target fixture identity comes from the registered final-"
                "archive proxy; target goals, minutes and points are not "
                "read until both scenario artifacts are frozen."
            ),
            (
                "Fixture-level 60-minute states are predicted independently "
                "inside double Gameweeks, conditional on the shared "
                "Gameweek appearance state; substitution and rotation "
                "dependence between the two fixtures is not modelled."
            ),
            (
                "Only clean-sheet scoring is isolated. Goals conceded, "
                "saves, bonus and attacking-return event allocation remain "
                "inside the residual empirical paths."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "incumbentSource": artifact["incumbentSource"],
            "challengerSource": artifact["challengerSource"],
            "scorelineValidationSource": artifact[
                "scorelineValidationSource"
            ],
            "fixedScreen": artifact["fixedScreen"],
            "distributionModels": artifact["distributionModels"],
            "appearanceModels": artifact["appearanceModels"],
            "targets": artifact["targets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def build_historical_correlated_clean_sheet_opening_scenarios(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    connection = _open_connection(path)
    try:
        captures = tuple(
            _required_capture(connection, season)
            for season in REGISTERED_SEASONS
        )
        targets = [
            _reconstruct_correlated_target(
                connection,
                _load_opening_fold(
                    connection,
                    captures,
                    target_index,
                    include_outcomes=False,
                ),
            )
            for target_index in range(1, len(captures))
        ]
    finally:
        connection.close()

    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": SCENARIO_ARTIFACT_TYPE,
        "artifactVersion": SCENARIO_ARTIFACT_VERSION,
        "status": SCENARIO_STATUS,
        "pointModel": HURDLE_MODEL,
        "appearanceModel": APPEARANCE_MODEL,
        "conditionalPointModel": CONDITIONAL_MODEL,
        "played60Model": PLAYED_60_MODEL,
        "scorelineModel": SCORELINE_MODEL,
        "scenarioModel": SCENARIO_MODEL,
        "targetGameweeks": list(TARGET_GAMEWEEKS),
        "temporalDesign": {
            "componentTrainingRule": (
                "strictly-earlier-season-archives-only"
            ),
            "scenarioDonorRule": "latest-strictly-earlier-season-only",
            "scorelineTrainingRule": (
                "strictly-earlier-season-fixtures-only"
            ),
            "targetPerformanceFieldsRead": False,
            "targetOutcomeFieldsRead": [],
            "targetFixtureInput": FIXTURE_PROXY,
            "fixtureEventDependence": (
                "one-shared-dixon-coles-scoreline-per-fixture-path"
            ),
            "pointMeanPolicy": (
                "exact-column-sum-equality-with-incumbent-hurdle-scenarios"
            ),
        },
        "modelConfiguration": {
            "tree": dict(TREE_CONFIGURATION),
            "scorelineEvaluatorVersion": SCORELINE_EVALUATOR_VERSION,
            "scorelineReplicationDataIdentitySha256": (
                SCORELINE_REPLICATION_DATA_IDENTITY
            ),
            "scorelineReplicationRunIdentitySha256": (
                SCORELINE_REPLICATION_RUN_IDENTITY
            ),
        },
        "targets": targets,
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "pointModel": artifact["pointModel"],
            "appearanceModel": artifact["appearanceModel"],
            "conditionalPointModel": artifact[
                "conditionalPointModel"
            ],
            "played60Model": artifact["played60Model"],
            "scorelineModel": artifact["scorelineModel"],
            "scenarioModel": artifact["scenarioModel"],
            "targetGameweeks": artifact["targetGameweeks"],
            "temporalDesign": artifact["temporalDesign"],
            "modelConfiguration": artifact["modelConfiguration"],
            "targets": artifact["targets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _reconstruct_correlated_target(
    connection: Any,
    fold: OpeningFold,
) -> Dict[str, Any]:
    samples_by_origin = _build_feature_table(
        connection,
        fold.training_captures,
    )
    observations_by_origin = _observations_by_origin(
        connection,
        fold.training_captures,
    )
    histories: DefaultDict[int, List[Observation]] = defaultdict(list)
    for season_index, capture in enumerate(fold.training_captures):
        for observation in _load_observations(
            connection,
            capture,
            season_index,
        ):
            histories[observation.player_code].append(observation)
    fixture_proxy = _load_fixture_proxy(
        connection,
        fold.target_capture.capture_id,
    )

    def target_sample(gameweek: int, player: Any) -> Sample:
        return _target_sample(
            fold.target_capture.season_code,
            len(fold.training_captures),
            gameweek,
            player,
            histories.get(player.player_code, ()),
            fixture_proxy,
        )

    component_observations = _load_component_observations(
        connection,
        fold.training_captures,
    )
    played_60_training = [
        _binary_sample(
            sample,
            component_observations[origin],
            "played-60",
        )
        for origin in sorted(samples_by_origin)
        for sample in samples_by_origin[origin]
        if sample.player_id in component_observations.get(origin, {})
    ]
    _require(
        played_60_training,
        "correlated-clean-sheet.played-60-training",
        "The strictly earlier played-60 training cohort is empty.",
    )
    ordered_players = sorted(
        fold.players,
        key=lambda player: player.player_code,
    )
    played_60_by_week: Dict[
        int,
        Dict[tuple[int, int], float],
    ] = {}
    played_60_diagnostics = []
    for gameweek in TARGET_GAMEWEEKS:
        target = []
        identities = []
        for player in ordered_players:
            player_fixtures = list(
                fixture_proxy[gameweek]["teams"].get(
                    player.team_name,
                    (),
                )
            )
            for fixture in player_fixtures:
                single_fixture_proxy = {
                    gameweek: {
                        "teams": {
                            player.team_name: [fixture],
                        },
                        "allKickoffs": [fixture["kickoffUtc"]],
                    }
                }
                target.append(
                    _target_sample(
                        fold.target_capture.season_code,
                        len(fold.training_captures),
                        gameweek,
                        player,
                        histories.get(player.player_code, ()),
                        single_fixture_proxy,
                    )
                )
                identities.append(
                    (
                        player.player_code,
                        int(fixture["fixtureId"]),
                    )
                )
        predictions, diagnostics = _predict_played_60(
            played_60_training,
            target,
        )
        played_60_by_week[gameweek] = {
            identity: float(prediction.predicted)
            for identity, prediction in zip(
                identities,
                predictions,
                strict=True,
            )
        }
        played_60_diagnostics.append(
            {
                "gameweek": gameweek,
                "playerFixtureCount": len(identities),
                "diagnostics": diagnostics,
            }
        )

    latest_prior = fold.training_captures[-1]
    residual_donors = _residual_donors(
        connection,
        latest_prior,
    )
    scoreline = _opening_scoreline_state(
        connection,
        fold,
    )
    clean_sheet_diagnostics: Dict[int, Dict[str, Any]] = {}

    def joint_generator(
        donor_points: Mapping[int, Sequence[Sample]],
        donor_appearance: Mapping[int, Sequence[Sample]],
        target: Sequence[Sample],
        point_predictions: Sequence[Prediction],
        appearance_predictions: Sequence[Prediction],
    ) -> JointFold:
        gameweek = int(target[0].gameweek)
        result, diagnostics = _correlated_clean_sheet_joint(
            donor_points,
            residual_donors,
            donor_appearance,
            target,
            point_predictions,
            appearance_predictions,
            played_60_by_week[gameweek],
            scoreline[gameweek],
            {
                player.player_code: player.team_name
                for player in ordered_players
            },
        )
        clean_sheet_diagnostics[gameweek] = diagnostics
        return result

    target = _reconstruct_hurdle_target_from_samples(
        connection,
        fold,
        samples_by_origin,
        observations_by_origin,
        target_sample,
        FEATURES,
        FEATURES,
        APPEARANCE_MODEL,
        CONDITIONAL_MODEL,
        joint_generator=joint_generator,
    )
    maximum_mean_delta = max(
        float(week["pointMeanMaximumAbsoluteDelta"])
        for week in target["weeks"]
    )
    total_reconciled_units = sum(
        int(clean_sheet_diagnostics[gameweek]["reconciledPointUnits"])
        for gameweek in TARGET_GAMEWEEKS
    )
    target["played60TrainingRowCount"] = len(played_60_training)
    target["played60Diagnostics"] = played_60_diagnostics
    target["scorelineDiagnostics"] = scoreline["diagnostics"]
    target["cleanSheetDiagnostics"] = [
        clean_sheet_diagnostics[gameweek]
        for gameweek in TARGET_GAMEWEEKS
    ]
    target["meanPreservation"] = {
        "reference": "incumbent-hurdle-scenario-column-sums",
        "maximumAbsolutePointMeanDelta": _round(maximum_mean_delta),
        "reconciledPointUnits": total_reconciled_units,
        "passes": (
            maximum_mean_delta
            <= MAXIMUM_POINT_MEAN_ABSOLUTE_DELTA
        ),
    }
    target["scenarioContentSha256"] = _sha256(
        {
            "players": target["players"],
            "weeks": target["weeks"],
            "scorelineDiagnostics": target["scorelineDiagnostics"],
            "cleanSheetDiagnostics": target[
                "cleanSheetDiagnostics"
            ],
        }
    )
    return target


def _predict_played_60(
    training: Sequence[Sample],
    target: Sequence[Sample],
) -> tuple[List[Prediction], Dict[str, Any]]:
    from .historical_participation_evaluation import (
        _predict_classifier,
    )

    binary_target = [
        Sample(
            season_code=sample.season_code,
            gameweek=sample.gameweek,
            player_id=sample.player_id,
            position=sample.position,
            features=sample.features,
            actual=0,
        )
        for sample in target
    ]
    return _predict_classifier(
        training,
        binary_target,
        PLAYED_60_MODEL,
        continuous_features=FEATURES,
    )


def _residual_donors(
    connection: Any,
    capture: Any,
) -> Dict[int, List[Sample]]:
    points = _build_samples(
        connection,
        capture,
        target_name="total-points",
    )
    rows = connection.execute(
        """
        SELECT
            gameweek.gameweek,
            gameweek.player_code,
            player.position,
            SUM(gameweek.clean_sheets) AS clean_sheets
        FROM historical_fpl_player_gameweeks AS gameweek
        INNER JOIN historical_fpl_players AS player
            ON player.capture_id = gameweek.capture_id
            AND player.season_element_id = gameweek.season_element_id
        WHERE gameweek.capture_id = :capture_id
        GROUP BY
            gameweek.gameweek,
            gameweek.player_code,
            player.position;
        """,
        {"capture_id": capture.capture_id},
    ).fetchall()
    clean_sheet_points = {
        (int(row["gameweek"]), int(row["player_code"])): (
            int(row["clean_sheets"])
            * POSITION_CLEAN_SHEET_POINTS[str(row["position"])]
        )
        for row in rows
    }
    residual: Dict[int, List[Sample]] = {}
    for gameweek, samples in points.items():
        residual[gameweek] = [
            Sample(
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=sample.player_id,
                position=sample.position,
                features=sample.features,
                actual=(
                    sample.actual
                    - clean_sheet_points.get(
                        (gameweek, sample.player_id),
                        0,
                    )
                ),
            )
            for sample in samples
        ]
    _require(
        set(residual) == set(points),
        "correlated-clean-sheet.residual-donors",
        "Residual donor Gameweeks do not match point donors.",
    )
    return residual


def _opening_scoreline_state(
    connection: Any,
    fold: OpeningFold,
) -> Dict[Any, Any]:
    from .team_goal_strength_evaluation import _load_matches

    training = [
        match
        for season_index, capture in enumerate(fold.training_captures)
        for match in _load_matches(connection, capture, season_index)
    ]
    target = _target_fixture_matches(
        connection,
        fold.target_capture.capture_id,
        fold.target_capture.season_code,
        len(fold.training_captures),
    )
    _require(
        training and target,
        "correlated-clean-sheet.scoreline-cohort",
        "The opening scoreline cohort is incomplete.",
    )
    cutoff: datetime = min(_instant(match.kickoff_utc) for match in target)
    model, diagnostics = _fit_dixon_coles(training, cutoff)
    by_gameweek: Dict[int, List[Dict[str, Any]]] = {
        gameweek: [] for gameweek in TARGET_GAMEWEEKS
    }
    for match in target:
        rates = _rates(model, match)
        matrix = _score_matrix(rates)
        by_gameweek[match.gameweek].append(
            {
                "fixtureId": match.fixture_id,
                "homeTeam": match.home_team,
                "awayTeam": match.away_team,
                "homeRate": float(rates.home),
                "awayRate": float(rates.away),
                "rho": float(rates.rho),
                "scoreMatrix": matrix,
                "homeCleanSheetProbability": float(
                    matrix[:, 0].sum()
                ),
                "awayCleanSheetProbability": float(
                    matrix[0, :].sum()
                ),
            }
        )
    result: Dict[Any, Any] = {
        gameweek: by_gameweek[gameweek]
        for gameweek in TARGET_GAMEWEEKS
    }
    result["diagnostics"] = {
        "model": SCORELINE_MODEL,
        "trainingSeasonCodes": [
            capture.season_code
            for capture in fold.training_captures
        ],
        "trainingMatchCount": len(training),
        "targetFixtureCount": len(target),
        "targetFixtureFieldsRead": [
            "fixture_id",
            "gameweek",
            "kickoff_utc",
            "team_name",
            "was_home",
        ],
        "targetOutcomeFieldsRead": [],
        "cutoffUtc": cutoff.isoformat(),
        "fit": diagnostics,
    }
    return result


def _target_fixture_matches(
    connection: Any,
    capture_id: int,
    season_code: str,
    season_index: int,
) -> List[Match]:
    rows = connection.execute(
        """
        SELECT DISTINCT
            fixture_id,
            gameweek,
            kickoff_utc,
            team_name,
            was_home
        FROM historical_fpl_player_gameweeks
        WHERE capture_id = :capture_id
          AND gameweek BETWEEN :first_gameweek AND :last_gameweek
        ORDER BY gameweek, kickoff_utc, fixture_id, was_home DESC;
        """,
        {
            "capture_id": capture_id,
            "first_gameweek": TARGET_GAMEWEEKS[0],
            "last_gameweek": TARGET_GAMEWEEKS[-1],
        },
    ).fetchall()
    grouped: Dict[int, Dict[str, Any]] = {}
    for row in rows:
        fixture = grouped.setdefault(
            int(row["fixture_id"]),
            {
                "gameweeks": set(),
                "kickoffs": set(),
                "home": set(),
                "away": set(),
            },
        )
        fixture["gameweeks"].add(int(row["gameweek"]))
        fixture["kickoffs"].add(str(row["kickoff_utc"]))
        fixture["home" if bool(row["was_home"]) else "away"].add(
            str(row["team_name"])
        )
    matches = []
    for fixture_id, fixture in sorted(grouped.items()):
        _require(
            len(fixture["gameweeks"]) == 1
            and len(fixture["kickoffs"]) == 1
            and len(fixture["home"]) == 1
            and len(fixture["away"]) == 1,
            "correlated-clean-sheet.fixture-identity",
            "A target fixture cannot be reconstructed from identity fields.",
        )
        matches.append(
            Match(
                season_code=season_code,
                season_index=season_index,
                gameweek=next(iter(fixture["gameweeks"])),
                fixture_id=fixture_id,
                kickoff_utc=next(iter(fixture["kickoffs"])),
                home_team=next(iter(fixture["home"])),
                away_team=next(iter(fixture["away"])),
                home_goals=0,
                away_goals=0,
                home_expected_goals=0.0,
                away_expected_goals=0.0,
            )
        )
    return matches


def _correlated_clean_sheet_joint(
    donor_points: Mapping[int, Sequence[Sample]],
    residual_donors: Mapping[int, Sequence[Sample]],
    donor_appearance: Mapping[int, Sequence[Sample]],
    target: Sequence[Sample],
    point_predictions: Sequence[Prediction],
    appearance_predictions: Sequence[Prediction],
    played_60_probabilities: Mapping[tuple[int, int], float],
    fixtures: Sequence[Mapping[str, Any]],
    team_by_player: Mapping[int, str],
) -> tuple[JointFold, Dict[str, Any]]:
    incumbent = generate_joint_fold(
        donor_points,
        donor_appearance,
        target,
        point_predictions,
        appearance_predictions,
    )
    scenario_count = len(incumbent.source_gameweeks)
    _require(
        scenario_count > 0,
        "correlated-clean-sheet.empty-scenarios",
        "The clean-sheet challenger has no scenario paths.",
    )
    point_by_player = {
        prediction.player_id: prediction
        for prediction in point_predictions
    }
    target_by_player = {
        sample.player_id: sample for sample in target
    }
    clean_sheet_rows, fixture_diagnostics = (
        _scoreline_clean_sheet_rows(
            fixtures,
            scenario_count,
            int(target[0].gameweek),
        )
    )
    residual_means = []
    for player_id in incumbent.player_ids:
        sample = target_by_player[player_id]
        team = team_by_player[player_id]
        appearance_probability = float(
            next(
                prediction.predicted
                for prediction in appearance_predictions
                if prediction.player_id == player_id
            )
        )
        clean_sheet_mean = sum(
            min(
                appearance_probability,
                float(
                    played_60_probabilities[
                        (player_id, int(fixture["fixtureId"]))
                    ]
                ),
            )
            * float(
                np.mean(
                    clean_sheet_rows[
                        (
                            int(fixture["fixtureId"]),
                            team,
                        )
                    ]
                )
            )
            * POSITION_CLEAN_SHEET_POINTS[sample.position]
            for fixture in fixtures
            if team
            in {
                str(fixture["homeTeam"]),
                str(fixture["awayTeam"]),
            }
        )
        original = point_by_player[player_id]
        residual_means.append(
            Prediction(
                model=SCENARIO_MODEL,
                season_code=original.season_code,
                gameweek=original.gameweek,
                player_id=original.player_id,
                position=original.position,
                predicted=float(original.predicted) - clean_sheet_mean,
                actual=original.actual,
            )
        )
    residual = generate_joint_fold(
        residual_donors,
        donor_appearance,
        target,
        residual_means,
        appearance_predictions,
    )
    _require(
        residual.player_ids == incumbent.player_ids
        and residual.source_gameweeks == incumbent.source_gameweeks
        and np.array_equal(residual.played, incumbent.played),
        "correlated-clean-sheet.residual-alignment",
        "Residual and incumbent joint scenarios are not aligned.",
    )
    points = residual.points.copy()
    played_60 = np.zeros_like(residual.played, dtype=np.bool_)
    clean_sheet_awarded = np.zeros_like(
        residual.played,
        dtype=np.bool_,
    )
    gameweek = int(target[0].gameweek)
    for column, player_id in enumerate(residual.player_ids):
        played_indices = np.flatnonzero(residual.played[:, column])
        clean_sheet_value = POSITION_CLEAN_SHEET_POINTS[
            target_by_player[player_id].position
        ]
        team = team_by_player[player_id]
        for fixture in fixtures:
            if team not in {
                str(fixture["homeTeam"]),
                str(fixture["awayTeam"]),
            }:
                continue
            fixture_id = int(fixture["fixtureId"])
            probability = float(
                played_60_probabilities[(player_id, fixture_id)]
            )
            requested = int(
                math.floor(
                    min(1.0, max(0.0, probability))
                    * scenario_count
                    + 0.5
                )
            )
            played_60_count = min(len(played_indices), requested)
            ranked = sorted(
                played_indices.tolist(),
                key=lambda row: (
                    _stable_rank(
                        "played-60",
                        gameweek,
                        fixture_id,
                        player_id,
                        row,
                    ),
                    row,
                ),
            )
            selected = ranked[:played_60_count]
            fixture_played_60 = np.zeros(
                scenario_count,
                dtype=np.bool_,
            )
            fixture_played_60[selected] = True
            played_60[:, column] |= fixture_played_60
            if clean_sheet_value <= 0:
                continue
            team_rows = clean_sheet_rows[(fixture_id, team)]
            awarded = fixture_played_60 & team_rows
            clean_sheet_awarded[:, column] |= awarded
            points[awarded, column] += clean_sheet_value

    incumbent_sums = np.sum(incumbent.points, axis=0)
    before_sums = np.sum(points, axis=0)
    reconciled_units = 0
    reconciled_columns = 0
    for column in range(points.shape[1]):
        delta = int(incumbent_sums[column] - before_sums[column])
        if delta == 0:
            continue
        reconciled_columns += 1
        reconciled_units += abs(delta)
        _reconcile_column(
            points[:, column],
            residual.played[:, column],
            delta,
            gameweek,
            residual.player_ids[column],
        )
    after_sums = np.sum(points, axis=0)
    _require(
        np.array_equal(after_sums, incumbent_sums),
        "correlated-clean-sheet.mean-preservation",
        "Integer reconciliation did not preserve incumbent point means.",
    )
    _require(
        not np.any((points != 0) & ~residual.played),
        "correlated-clean-sheet.nonplayer-points",
        "A non-playing player has non-zero challenger points.",
    )
    mean_predictions = tuple(
        float(value) / scenario_count for value in incumbent_sums
    )
    result = JointFold(
        player_ids=residual.player_ids,
        positions=residual.positions,
        source_gameweeks=residual.source_gameweeks,
        points=points,
        played=residual.played,
        mean_predictions=mean_predictions,
        appearance_predictions=incumbent.appearance_predictions,
        self_donor_assignments=residual.self_donor_assignments,
        fallback_donor_assignments=residual.fallback_donor_assignments,
    )
    eligible_columns = [
        column
        for column, position in enumerate(result.positions)
        if POSITION_CLEAN_SHEET_POINTS[position] > 0
    ]
    award_count = int(
        clean_sheet_awarded[:, eligible_columns].sum()
    )
    diagnostics = {
        "gameweek": gameweek,
        "scenarioCount": scenario_count,
        "fixtureCount": len(fixtures),
        "fixtureScorelineDraws": fixture_diagnostics,
        "played60PlayerScenarioCount": int(played_60.sum()),
        "cleanSheetAwardUniquePlayerScenarioCount": award_count,
        "reconciledColumnCount": reconciled_columns,
        "reconciledPointUnits": reconciled_units,
        "maximumAbsoluteMeanDeltaBeforeReconciliation": _round(
            float(
                np.max(
                    np.abs(
                        before_sums.astype(float)
                        - incumbent_sums.astype(float)
                    )
                )
                / scenario_count
            )
        ),
        "maximumAbsoluteMeanDeltaAfterReconciliation": _round(
            float(
                np.max(
                    np.abs(
                        after_sums.astype(float)
                        - incumbent_sums.astype(float)
                    )
                )
                / scenario_count
            )
        ),
    }
    return result, diagnostics


def _scoreline_clean_sheet_rows(
    fixtures: Sequence[Mapping[str, Any]],
    scenario_count: int,
    gameweek: int,
) -> tuple[
    Dict[tuple[int, str], np.ndarray],
    List[Dict[str, Any]],
]:
    result: Dict[tuple[int, str], np.ndarray] = {}
    diagnostics = []
    for fixture in fixtures:
        matrix = np.asarray(fixture["scoreMatrix"], dtype=float)
        _require(
            matrix.ndim == 2
            and matrix.shape[0] == matrix.shape[1]
            and np.isclose(float(matrix.sum()), 1.0),
            "correlated-clean-sheet.score-matrix",
            "A fixture score matrix is invalid.",
        )
        quantiles = (
            np.arange(scenario_count, dtype=float) + 0.5
        ) / scenario_count
        permutation = _stable_permutation(
            scenario_count,
            "scoreline",
            gameweek,
            int(fixture["fixtureId"]),
        )
        flat_indices = np.searchsorted(
            np.cumsum(matrix.ravel()),
            quantiles,
            side="right",
        )
        home_goals, away_goals = np.unravel_index(
            np.minimum(flat_indices, matrix.size - 1),
            matrix.shape,
        )
        home_clean = np.zeros(scenario_count, dtype=np.bool_)
        away_clean = np.zeros(scenario_count, dtype=np.bool_)
        home_clean[permutation] = away_goals == 0
        away_clean[permutation] = home_goals == 0
        home_team = str(fixture["homeTeam"])
        away_team = str(fixture["awayTeam"])
        result[(int(fixture["fixtureId"]), home_team)] = home_clean
        result[(int(fixture["fixtureId"]), away_team)] = away_clean
        diagnostics.append(
            {
                "fixtureId": int(fixture["fixtureId"]),
                "homeTeam": home_team,
                "awayTeam": away_team,
                "homeRate": _round(float(fixture["homeRate"])),
                "awayRate": _round(float(fixture["awayRate"])),
                "rho": _round(float(fixture["rho"])),
                "homeCleanSheetProbability": _round(
                    float(fixture["homeCleanSheetProbability"])
                ),
                "homeCleanSheetScenarioFrequency": _round(
                    float(np.mean(home_clean))
                ),
                "awayCleanSheetProbability": _round(
                    float(fixture["awayCleanSheetProbability"])
                ),
                "awayCleanSheetScenarioFrequency": _round(
                    float(np.mean(away_clean))
                ),
                "zeroZeroScenarioFrequency": _round(
                    float(np.mean(home_clean & away_clean))
                ),
            }
        )
    return result, diagnostics


def _reconcile_column(
    points: np.ndarray,
    played: np.ndarray,
    delta: int,
    gameweek: int,
    player_id: int,
) -> None:
    eligible = np.flatnonzero(played).tolist()
    _require(
        eligible,
        "correlated-clean-sheet.reconcile-no-played-path",
        "A non-zero mean delta has no played path to reconcile.",
    )
    ranked = sorted(
        eligible,
        key=lambda row: (
            _stable_rank(
                "mean-reconciliation",
                gameweek,
                player_id,
                row,
            ),
            row,
        ),
    )
    step = 1 if delta > 0 else -1
    remaining = abs(delta)
    while remaining:
        changed = False
        for row in ranked:
            candidate = int(points[row]) + step
            if candidate < MINIMUM_POINTS or candidate > MAXIMUM_POINTS:
                continue
            points[row] = candidate
            remaining -= 1
            changed = True
            if remaining == 0:
                break
        _require(
            changed,
            "correlated-clean-sheet.reconcile-bounds",
            "Point bounds prevent exact mean reconciliation.",
        )


def _stable_permutation(
    count: int,
    *parts: Any,
) -> np.ndarray:
    seed = int.from_bytes(
        hashlib.sha256(
            "|".join(str(part) for part in parts).encode("utf-8")
        ).digest()[:8],
        "big",
    )
    return np.random.Generator(np.random.PCG64(seed)).permutation(
        count
    )


def _stable_rank(*parts: Any) -> str:
    return hashlib.sha256(
        "|".join(str(part) for part in parts).encode("utf-8")
    ).hexdigest()


def _screen(
    distribution_models: Sequence[Mapping[str, Any]],
    appearance_models: Sequence[Mapping[str, Any]],
    targets: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    distributions = {
        str(model["name"]): model for model in distribution_models
    }
    appearances = {
        str(model["name"]): model for model in appearance_models
    }
    _require(
        set(distributions) == {INCUMBENT_MODEL, CHALLENGER_MODEL},
        "correlated-clean-sheet.distribution-models",
        "The fixed distribution pair is incomplete.",
    )
    _require(
        set(appearances)
        == {INCUMBENT_APPEARANCE, CHALLENGER_APPEARANCE},
        "correlated-clean-sheet.appearance-models",
        "The fixed appearance pair is incomplete.",
    )
    incumbent = distributions[INCUMBENT_MODEL]
    challenger = distributions[CHALLENGER_MODEL]
    incumbent_crps = float(incumbent["metrics"]["meanCrps"])
    challenger_crps = float(challenger["metrics"]["meanCrps"])
    crps_improvement = (
        (incumbent_crps - challenger_crps) / incumbent_crps
        if incumbent_crps > 0.0
        else 0.0
    )
    target_rows = []
    for target in targets:
        by_name = {
            str(model["name"]): model
            for model in target["distributionModels"]
        }
        incumbent_target = float(
            by_name[INCUMBENT_MODEL]["metrics"]["meanCrps"]
        )
        challenger_target = float(
            by_name[CHALLENGER_MODEL]["metrics"]["meanCrps"]
        )
        target_rows.append(
            {
                "targetSeasonCode": target["targetSeasonCode"],
                "incumbentMeanCrps": _round(incumbent_target),
                "challengerMeanCrps": _round(challenger_target),
                "challengerWins": (
                    challenger_target < incumbent_target
                ),
            }
        )
    position_rows = []
    maximum_position_regression = float("-inf")
    incumbent_positions = incumbent["slices"]["position"]
    challenger_positions = challenger["slices"]["position"]
    _require(
        set(incumbent_positions) == set(challenger_positions),
        "correlated-clean-sheet.position-slices",
        "The fixed distribution pair has different position slices.",
    )
    for position in sorted(incumbent_positions):
        incumbent_position = float(
            incumbent_positions[position]["meanCrps"]
        )
        challenger_position = float(
            challenger_positions[position]["meanCrps"]
        )
        regression = (
            (challenger_position - incumbent_position)
            / incumbent_position
            if incumbent_position > 0.0
            else 0.0
        )
        maximum_position_regression = max(
            maximum_position_regression,
            regression,
        )
        position_rows.append(
            {
                "position": position,
                "incumbentMeanCrps": _round(incumbent_position),
                "challengerMeanCrps": _round(challenger_position),
                "crpsRegressionFraction": _round(regression),
            }
        )
    incumbent_appearance = appearances[INCUMBENT_APPEARANCE][
        "metrics"
    ]
    challenger_appearance = appearances[CHALLENGER_APPEARANCE][
        "metrics"
    ]
    brier_delta = (
        float(challenger_appearance["brierScore"])
        - float(incumbent_appearance["brierScore"])
    )
    log_loss_delta = (
        float(challenger_appearance["logLoss"])
        - float(incumbent_appearance["logLoss"])
    )
    target_wins = sum(bool(row["challengerWins"]) for row in target_rows)
    maximum_mean_delta = max(
        float(
            target["meanPreservation"][
                "maximumAbsolutePointMeanDelta"
            ]
        )
        for target in targets
    )
    gates = {
        "aggregateCrpsImprovement": (
            crps_improvement >= MINIMUM_CRPS_IMPROVEMENT_FRACTION
        ),
        "targetSeasonWins": target_wins >= MINIMUM_TARGET_WINS,
        "positionCrpsStability": (
            maximum_position_regression
            <= MAXIMUM_POSITION_CRPS_REGRESSION_FRACTION
        ),
        "appearanceBrierNonRegression": (
            brier_delta <= MAXIMUM_APPEARANCE_BRIER_DELTA
        ),
        "appearanceLogLossNonRegression": (
            log_loss_delta <= MAXIMUM_APPEARANCE_LOG_LOSS_DELTA
        ),
        "pointMeanExactlyPreserved": (
            maximum_mean_delta
            <= MAXIMUM_POINT_MEAN_ABSOLUTE_DELTA
        ),
        "scorelineReplicationRetained": True,
    }
    return {
        "aggregateCrpsImprovementFraction": _round(
            crps_improvement
        ),
        "targetSeasonWins": target_wins,
        "targetSeasonCount": len(target_rows),
        "targetComparisons": target_rows,
        "positionComparisons": position_rows,
        "maximumPositionCrpsRegressionFraction": _round(
            maximum_position_regression
        ),
        "appearanceBrierDelta": _round(brier_delta),
        "appearanceLogLossDelta": _round(log_loss_delta),
        "maximumPointMeanAbsoluteDelta": _round(
            maximum_mean_delta
        ),
        "gates": gates,
        "passes": all(gates.values()),
    }


def _require_scenario_pair(
    incumbent: Mapping[str, Any],
    challenger: Mapping[str, Any],
) -> None:
    incumbent_seasons = [
        str(target["targetSeasonCode"])
        for target in incumbent.get("targets", ())
    ]
    challenger_seasons = [
        str(target["targetSeasonCode"])
        for target in challenger.get("targets", ())
    ]
    _require(
        incumbent.get("artifactVersion")
        == INCUMBENT_ARTIFACT_VERSION
        and challenger.get("artifactVersion")
        == SCENARIO_ARTIFACT_VERSION
        and incumbent_seasons == challenger_seasons
        and incumbent.get("temporalDesign", {}).get(
            "targetPerformanceFieldsRead"
        )
        is False
        and challenger.get("temporalDesign", {}).get(
            "targetPerformanceFieldsRead"
        )
        is False,
        "correlated-clean-sheet.scenario-pair",
        "The fixed target-outcome-free scenario pair is unavailable.",
    )
    incumbent_targets = {
        str(target["targetSeasonCode"]): target
        for target in incumbent["targets"]
    }
    for target in challenger["targets"]:
        season = str(target["targetSeasonCode"])
        reference = incumbent_targets[season]
        _require(
            int(target["scenarioCount"])
            == int(reference["scenarioCount"])
            and [
                int(player["playerCode"])
                for player in target["players"]
            ]
            == [
                int(player["playerCode"])
                for player in reference["players"]
            ],
            "correlated-clean-sheet.scenario-alignment",
            "The challenger and incumbent scenario cohorts differ.",
        )
        for challenger_week, incumbent_week in zip(
            target["weeks"],
            reference["weeks"],
            strict=True,
        ):
            challenger_points = np.asarray(
                challenger_week["pointRows"],
                dtype=np.int64,
            )
            incumbent_points = np.asarray(
                incumbent_week["pointRows"],
                dtype=np.int64,
            )
            _require(
                np.array_equal(
                    np.asarray(
                        challenger_week["playedRows"],
                        dtype=np.bool_,
                    ),
                    np.asarray(
                        incumbent_week["playedRows"],
                        dtype=np.bool_,
                    ),
                )
                and np.array_equal(
                    np.sum(challenger_points, axis=0),
                    np.sum(incumbent_points, axis=0),
                ),
                "correlated-clean-sheet.mean-or-appearance-change",
                "The challenger changed appearance paths or point means.",
            )


def _load_scoreline_identity(path: Path) -> Dict[str, str]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise TemporalRidgeError(
            "correlated-clean-sheet.scoreline-artifact",
            "The retained scoreline replication artifact is unavailable.",
        ) from exception
    _require(
        document.get("dataIdentitySha256")
        == SCORELINE_REPLICATION_DATA_IDENTITY
        and document.get("runIdentitySha256")
        == SCORELINE_REPLICATION_RUN_IDENTITY
        and document.get("decision")
        == "retain-scoreline-model-for-player-component-ablation",
        "correlated-clean-sheet.scoreline-identity",
        "The scoreline replication identity or decision differs.",
    )
    return {
        "dataIdentitySha256": SCORELINE_REPLICATION_DATA_IDENTITY,
        "runIdentitySha256": SCORELINE_REPLICATION_RUN_IDENTITY,
    }


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate mean-preserving, fixture-correlated clean-sheet "
            "events in historical opening player distributions."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument(
        "--scoreline-evaluation",
        required=True,
        type=Path,
    )
    parser.add_argument("--output", type=Path)
    parser.add_argument(
        "--scenario-output",
        type=Path,
        help=(
            "Optionally write the target-outcome-free challenger scenario "
            "artifact separately."
        ),
    )
    options = parser.parse_args(arguments)
    try:
        challenger = None
        if options.scenario_output is not None:
            challenger = (
                build_historical_correlated_clean_sheet_opening_scenarios(
                    options.database
                )
            )
            _write_report(
                challenger,
                options.scenario_output,
            )
        artifact = (
            build_historical_correlated_clean_sheet_opening_distribution_evaluation(
                options.database,
                options.scoreline_evaluation,
                challenger_scenarios=challenger,
            )
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
