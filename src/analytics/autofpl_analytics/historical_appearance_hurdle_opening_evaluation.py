from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import (
    Any,
    Callable,
    DefaultDict,
    Dict,
    Mapping,
    Optional,
    Sequence,
)

import numpy as np

from .current_multi_horizon_joint_scenarios import _path_permutation
from .historical_appearance_hurdle_points_evaluation import (
    APPEARANCE_MODEL,
    CONDITIONAL_MODEL,
    HURDLE_MODEL,
    _appearance_sample,
    _appeared,
    _combine_hurdle,
    _observations_by_origin,
)
from .historical_joint_scenario_evaluation import (
    MODEL_NAME as SCENARIO_MODEL,
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
from .historical_opening_policy_evaluation import _evaluate_target
from .historical_opening_policy_registration import (
    MAXIMUM_WORST_TARGET_REGRESSION_POINTS,
    MINIMUM_MEAN_IMPROVEMENT_POINTS,
    MINIMUM_TARGET_WINS,
    REFERENCE_POLICY_KEY,
)
from .historical_opening_scenario_reconstruction import (
    ARTIFACT_VERSION as INCUMBENT_SCENARIO_ARTIFACT_VERSION,
    build_historical_opening_scenario_reconstruction,
)
from .historical_participation_evaluation import _predict_classifier
from .historical_preseason_evaluation import _build_samples
from .multi_season_evaluation import (
    FEATURES,
    Observation,
    _build_feature_table,
    _load_observations,
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
from .temporal_tree import TREE_CONFIGURATION, _predict_tree

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-appearance-hurdle-opening-policy-evaluation"
ARTIFACT_VERSION = (
    "historical-appearance-hurdle-opening-policy-evaluation-v1"
)
SCENARIO_ARTIFACT_TYPE = (
    "historical-appearance-hurdle-opening-scenario-reconstruction"
)
SCENARIO_ARTIFACT_VERSION = (
    "historical-appearance-hurdle-opening-scenario-reconstruction-v1"
)
STATUS = "retrospective-challenger-evaluated-prospective-gate-required"
SCENARIO_STATUS = "retrospective-input-reconstruction-no-outcomes-opened"
DECISION_RETAIN = "retain-hurdle-opening-policy-prospective-challenger"
DECISION_REJECT = "do-not-retain-hurdle-opening-policy"
RETAINED_DATA_IDENTITY = (
    "75a4094bec13ae6226f80547344787ad4241405d5b4234720b9f2ba226acd210"
)
RETAINED_RUN_IDENTITY = (
    "ad66dca436de3520b9c73f65440a652d42248ef6eb050e52c4968d86c8a62f04"
)


def build_historical_appearance_hurdle_opening_evaluation(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    incumbent = build_historical_opening_scenario_reconstruction(path)
    challenger = build_historical_appearance_hurdle_opening_scenarios(
        path
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

    incumbent_by_season = {
        str(target["targetSeasonCode"]): target
        for target in incumbent["targets"]
    }
    challenger_by_season = {
        str(target["targetSeasonCode"]): target
        for target in challenger["targets"]
    }
    comparisons = [
        _evaluate_pair(
            season,
            folds[season],
            incumbent_by_season[season],
            challenger_by_season[season],
        )
        for season in sorted(challenger_by_season)
    ]
    screen = _screen(comparisons)
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "researchStatus": "reused-opened-targets-no-promotion",
        "targetOutcomesOpened": True,
        "incumbent": {
            "modelKey": "multi-season-histogram-tree",
            "scenarioArtifactVersion": incumbent["artifactVersion"],
            "scenarioDataIdentitySha256": incumbent[
                "dataIdentitySha256"
            ],
            "scenarioRunIdentitySha256": incumbent[
                "runIdentitySha256"
            ],
            "evaluationPolicyKey": REFERENCE_POLICY_KEY,
        },
        "challenger": {
            "modelKey": HURDLE_MODEL,
            "appearanceModelKey": APPEARANCE_MODEL,
            "conditionalPointModelKey": CONDITIONAL_MODEL,
            "scenarioArtifactVersion": challenger["artifactVersion"],
            "scenarioDataIdentitySha256": challenger[
                "dataIdentitySha256"
            ],
            "scenarioRunIdentitySha256": challenger[
                "runIdentitySha256"
            ],
            "evaluationPolicyKey": REFERENCE_POLICY_KEY,
        },
        "fixedScreen": {
            "primaryMetric": (
                "mean-realised-eight-gameweek-fpl-points-across-targets"
            ),
            "minimumMeanImprovementPoints": (
                MINIMUM_MEAN_IMPROVEMENT_POINTS
            ),
            "minimumTargetWins": MINIMUM_TARGET_WINS,
            "maximumWorstTargetRegressionPoints": (
                MAXIMUM_WORST_TARGET_REGRESSION_POINTS
            ),
            "samePolicyIsolation": (
                "six-gameweek-expected-points-for-both-models"
            ),
            "sameExactFplOutcomeScorer": True,
        },
        "targets": comparisons,
        "screen": screen,
        "decision": (
            DECISION_RETAIN if screen["passes"] else DECISION_REJECT
        ),
        "isPromoted": False,
        "influencesAdvice": False,
        "limitations": [
            (
                "These three opening-season outcomes were already opened by "
                "the registered incumbent policy experiment. This comparison "
                "can reject or retain a prospective challenger, but it cannot "
                "promote one."
            ),
            (
                "Historical opening-day injury and status captures remain "
                "unavailable. Neither model receives final archived target "
                "health state."
            ),
            (
                "Target fixture structure remains the labelled final-archive "
                "proxy. Target point, minute and event fields are not read "
                "until both scenario reconstructions are complete."
            ),
            (
                "The experiment isolates the point/distribution model while "
                "holding the already selected six-Gameweek expected-points "
                "policy and exact eight-Gameweek outcome scorer fixed."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "incumbent": artifact["incumbent"],
            "challenger": artifact["challenger"],
            "fixedScreen": artifact["fixedScreen"],
            "targets": artifact["targets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def build_historical_appearance_hurdle_opening_scenarios(
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
            _reconstruct_hurdle_target(
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
        "scenarioModel": SCENARIO_MODEL,
        "targetGameweeks": list(TARGET_GAMEWEEKS),
        "temporalDesign": {
            "componentTrainingRule": (
                "strictly-earlier-season-archives-only"
            ),
            "conditionalPointTrainingRows": (
                "strictly-earlier-appearance-positive-player-gameweeks"
            ),
            "scenarioDonorRule": "latest-strictly-earlier-season-only",
            "targetPerformanceFieldsRead": False,
            "targetOutcomeFieldsRead": [],
            "targetFixtureInput": FIXTURE_PROXY,
            "weeklyPathPairing": (
                "fixed-current-engine-independent-weekly-permutation"
            ),
        },
        "modelConfiguration": dict(TREE_CONFIGURATION),
        "targets": targets,
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "pointModel": artifact["pointModel"],
            "appearanceModel": artifact["appearanceModel"],
            "conditionalPointModel": artifact[
                "conditionalPointModel"
            ],
            "scenarioModel": artifact["scenarioModel"],
            "targetGameweeks": artifact["targetGameweeks"],
            "temporalDesign": artifact["temporalDesign"],
            "modelConfiguration": artifact["modelConfiguration"],
            "targets": artifact["targets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _reconstruct_hurdle_target(
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
    histories: DefaultDict[int, list[Observation]] = defaultdict(list)
    for season_index, capture in enumerate(fold.training_captures):
        for observation in _load_observations(
            connection,
            capture,
            season_index,
        ):
            histories[observation.player_code].append(observation)
    fixtures = _load_fixture_proxy(
        connection,
        fold.target_capture.capture_id,
    )
    return _reconstruct_hurdle_target_from_samples(
        connection,
        fold,
        samples_by_origin,
        observations_by_origin,
        lambda gameweek, player: _target_sample(
            fold.target_capture.season_code,
            len(fold.training_captures),
            gameweek,
            player,
            histories.get(player.player_code, ()),
            fixtures,
        ),
        FEATURES,
        FEATURES,
        APPEARANCE_MODEL,
        CONDITIONAL_MODEL,
    )


def _reconstruct_hurdle_target_from_samples(
    connection: Any,
    fold: OpeningFold,
    samples_by_origin: Mapping[Any, Sequence[Sample]],
    observations_by_origin: Mapping[Any, Mapping[int, Any]],
    target_sample: Callable[[int, Any], Sample],
    appearance_features: Sequence[str],
    conditional_features: Sequence[str],
    appearance_model: str,
    conditional_model: str,
    appearance_transform: Optional[
        Callable[
            [int, Sequence[Any], Sequence[Prediction]],
            Sequence[Prediction],
        ]
    ] = None,
) -> Dict[str, Any]:
    ordered_origins = sorted(samples_by_origin)
    _require(
        ordered_origins
        and set(ordered_origins) == set(observations_by_origin),
        "hurdle-opening.training-origins",
        "Point samples and appearance observations are incomplete.",
    )
    training = [
        sample
        for origin in ordered_origins
        for sample in samples_by_origin[origin]
    ]
    appearance_training = [
        _appearance_sample(
            sample,
            observations_by_origin[origin],
        )
        for origin in ordered_origins
        for sample in samples_by_origin[origin]
    ]
    conditional_training = [
        sample
        for origin in ordered_origins
        for sample in samples_by_origin[origin]
        if _appeared(sample, observations_by_origin[origin])
    ]
    _require(
        training
        and len(training) == len(appearance_training)
        and conditional_training,
        "hurdle-opening.training",
        "The fixed hurdle training cohorts are incomplete.",
    )

    latest_prior = fold.training_captures[-1]
    donor_points = _build_samples(
        connection,
        latest_prior,
        target_name="total-points",
    )
    donor_appearance = _build_samples(
        connection,
        latest_prior,
        target_name="appearance",
    )
    _require(
        set(donor_points) == set(donor_appearance),
        "hurdle-opening.donor-origins",
        "Point and appearance donor origins differ.",
    )

    ordered_players = sorted(
        fold.players,
        key=lambda player: player.player_code,
    )
    player_codes = tuple(player.player_code for player in ordered_players)
    weeks = []
    appearance_by_week: Dict[int, Dict[int, float]] = {}
    conditional_by_week: Dict[int, Dict[int, float]] = {}
    point_by_week: Dict[int, Dict[int, float]] = {}
    component_diagnostics = []
    scenario_count: Optional[int] = None
    for gameweek in TARGET_GAMEWEEKS:
        target = [
            target_sample(gameweek, player)
            for player in ordered_players
        ]
        appearance, appearance_diagnostics = _predict_classifier(
            appearance_training,
            [
                Sample(
                    season_code=sample.season_code,
                    gameweek=sample.gameweek,
                    player_id=sample.player_id,
                    position=sample.position,
                    features=sample.features,
                    actual=0,
                )
                for sample in target
            ],
            appearance_model,
            continuous_features=appearance_features,
        )
        if appearance_transform is not None:
            transformed = list(
                appearance_transform(
                    gameweek,
                    ordered_players,
                    appearance,
                )
            )
            _require(
                len(transformed) == len(appearance)
                and all(
                    (
                        updated.season_code,
                        updated.gameweek,
                        updated.player_id,
                        updated.position,
                        updated.actual,
                    )
                    == (
                        original.season_code,
                        original.gameweek,
                        original.player_id,
                        original.position,
                        original.actual,
                    )
                    and 0.0 <= updated.predicted <= 1.0
                    for original, updated in zip(
                        appearance,
                        transformed,
                        strict=True,
                    )
                ),
                "hurdle-opening.appearance-transform",
                "The appearance transform changed row identity or bounds.",
            )
            appearance = transformed
        conditional, conditional_diagnostics = _predict_tree(
            conditional_training,
            target,
            continuous_features=conditional_features,
            model_name=conditional_model,
        )
        hurdle = _combine_hurdle(target, appearance, conditional)
        joint = generate_joint_fold(
            donor_points,
            donor_appearance,
            target,
            hurdle,
            appearance,
        )
        _require(
            joint.player_ids == player_codes,
            "hurdle-opening.player-column-alignment",
            "A reconstructed week uses a different player column order.",
        )
        _require(
            not np.any((joint.points != 0) & ~joint.played),
            "hurdle-opening.nonplayer-points",
            "A non-playing scenario player has non-zero points.",
        )
        if scenario_count is None:
            scenario_count = len(joint.source_gameweeks)
        _require(
            scenario_count == len(joint.source_gameweeks),
            "hurdle-opening.scenario-count",
            "Reconstructed weeks have different path counts.",
        )
        permutation = _path_permutation(scenario_count, gameweek)
        points = joint.points[permutation]
        played = joint.played[permutation]
        weeks.append(
            {
                "gameweek": gameweek,
                "sourceGameweeksByPath": [
                    int(joint.source_gameweeks[index])
                    for index in permutation
                ],
                "pointRows": points.tolist(),
                "playedRows": played.tolist(),
                "pointMeanMaximumAbsoluteDelta": _round(
                    float(
                        np.max(
                            np.abs(
                                np.mean(joint.points, axis=0)
                                - np.asarray(
                                    joint.mean_predictions,
                                    dtype=float,
                                )
                            )
                        )
                    )
                ),
                "appearanceMaximumAbsoluteDelta": _round(
                    float(
                        np.max(
                            np.abs(
                                np.mean(joint.played, axis=0)
                                - np.asarray(
                                    joint.appearance_predictions,
                                    dtype=float,
                                )
                            )
                        )
                    )
                ),
            }
        )
        appearance_by_week[gameweek] = {
            prediction.player_id: _round(prediction.predicted)
            for prediction in appearance
        }
        conditional_by_week[gameweek] = {
            prediction.player_id: _round(prediction.predicted)
            for prediction in conditional
        }
        point_by_week[gameweek] = {
            prediction.player_id: _round(prediction.predicted)
            for prediction in hurdle
        }
        component_diagnostics.append(
            {
                "gameweek": gameweek,
                "appearance": appearance_diagnostics,
                "conditionalPoints": conditional_diagnostics,
            }
        )

    assert scenario_count is not None
    players = [
        {
            "columnIndex": index,
            "playerCode": player.player_code,
            "webName": player.web_name,
            "position": player.position,
            "teamName": player.team_name,
            "teamId": player.team_id,
            "priceTenths": player.price_tenths,
            "gameweeks": [
                {
                    "gameweek": gameweek,
                    "appearanceProbability": appearance_by_week[
                        gameweek
                    ][player.player_code],
                    "conditionalExpectedPoints": conditional_by_week[
                        gameweek
                    ][player.player_code],
                    "expectedPoints": point_by_week[gameweek][
                        player.player_code
                    ],
                }
                for gameweek in TARGET_GAMEWEEKS
            ],
        }
        for index, player in enumerate(ordered_players)
    ]
    forecast_identity = _sha256(
        {
            "players": players,
            "componentTrainingOriginCount": len(ordered_origins),
            "componentTrainingRowCount": len(training),
            "conditionalTrainingRowCount": len(conditional_training),
        }
    )
    return {
        "targetSeasonCode": fold.target_capture.season_code,
        "targetCaptureId": fold.target_capture.capture_id,
        "latestPriorSeasonCode": latest_prior.season_code,
        "latestPriorCaptureId": latest_prior.capture_id,
        "forecastIdentitySha256": forecast_identity,
        "playerCount": len(players),
        "scenarioCount": scenario_count,
        "componentTrainingOriginCount": len(ordered_origins),
        "componentTrainingRowCount": len(training),
        "conditionalTrainingRowCount": len(conditional_training),
        "componentDiagnostics": component_diagnostics,
        "players": players,
        "weeks": weeks,
        "scenarioContentSha256": _sha256(
            {
                "players": players,
                "weeks": weeks,
            }
        ),
    }


def _evaluate_pair(
    season: str,
    fold: OpeningFold,
    incumbent_target: Mapping[str, Any],
    challenger_target: Mapping[str, Any],
) -> Dict[str, Any]:
    incumbent = _policy(
        _evaluate_target(incumbent_target, fold),
        REFERENCE_POLICY_KEY,
    )
    challenger = _policy(
        _evaluate_target(challenger_target, fold),
        REFERENCE_POLICY_KEY,
    )
    incumbent_points = int(
        incumbent["realisedOutcome"]["totalPoints"]
    )
    challenger_points = int(
        challenger["realisedOutcome"]["totalPoints"]
    )
    incumbent_ids = {
        int(value) for value in incumbent["selection"]["playerIds"]
    }
    challenger_ids = {
        int(value) for value in challenger["selection"]["playerIds"]
    }
    return {
        "targetSeasonCode": season,
        "targetCaptureId": fold.target_capture.capture_id,
        "incumbent": incumbent,
        "challenger": challenger,
        "comparison": {
            "incumbentTotalPoints": incumbent_points,
            "challengerTotalPoints": challenger_points,
            "differencePoints": challenger_points - incumbent_points,
            "sharedPlayerCount": len(incumbent_ids & challenger_ids),
            "incumbentOnlyPlayerIds": sorted(
                incumbent_ids - challenger_ids
            ),
            "challengerOnlyPlayerIds": sorted(
                challenger_ids - incumbent_ids
            ),
        },
    }


def _policy(
    target: Mapping[str, Any],
    key: str,
) -> Mapping[str, Any]:
    matches = [
        policy
        for policy in target["policies"]
        if str(policy["evaluationPolicyKey"]) == key
    ]
    _require(
        len(matches) == 1,
        "hurdle-opening.policy",
        "A target does not contain exactly one fixed policy.",
    )
    return matches[0]


def _screen(comparisons: Sequence[Mapping[str, Any]]) -> Dict[str, Any]:
    _require(
        comparisons,
        "hurdle-opening.comparisons",
        "The opening-policy comparison is empty.",
    )
    differences = [
        int(target["comparison"]["differencePoints"])
        for target in comparisons
    ]
    incumbent_scores = [
        int(target["comparison"]["incumbentTotalPoints"])
        for target in comparisons
    ]
    challenger_scores = [
        int(target["comparison"]["challengerTotalPoints"])
        for target in comparisons
    ]
    mean_difference = float(np.mean(differences))
    gates = {
        "meanImprovement": (
            mean_difference >= MINIMUM_MEAN_IMPROVEMENT_POINTS
        ),
        "targetWins": (
            sum(difference > 0 for difference in differences)
            >= MINIMUM_TARGET_WINS
        ),
        "worstTargetRegression": (
            min(differences)
            >= -MAXIMUM_WORST_TARGET_REGRESSION_POINTS
        ),
    }
    return {
        "targetCount": len(comparisons),
        "incumbentMeanPoints": _round(
            float(np.mean(incumbent_scores))
        ),
        "challengerMeanPoints": _round(
            float(np.mean(challenger_scores))
        ),
        "meanDifferencePoints": _round(mean_difference),
        "targetWins": sum(
            difference > 0 for difference in differences
        ),
        "targetTies": sum(
            difference == 0 for difference in differences
        ),
        "targetLosses": sum(
            difference < 0 for difference in differences
        ),
        "worstTargetDifferencePoints": min(differences),
        "bestTargetDifferencePoints": max(differences),
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
    if (
        incumbent.get("artifactVersion")
        != INCUMBENT_SCENARIO_ARTIFACT_VERSION
        or challenger.get("artifactVersion")
        != SCENARIO_ARTIFACT_VERSION
        or incumbent_seasons != challenger_seasons
        or incumbent.get("temporalDesign", {}).get(
            "targetPerformanceFieldsRead"
        )
        is not False
        or challenger.get("temporalDesign", {}).get(
            "targetPerformanceFieldsRead"
        )
        is not False
    ):
        raise TemporalRidgeError(
            "hurdle-opening.scenario-pair",
            "The incumbent and hurdle scenario reconstructions are not "
            "the fixed target-outcome-free pair.",
        )


def _require(
    condition: bool,
    code: str,
    message: str,
) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare the appearance-hurdle and incumbent opening-squad "
            "policies on identical historical targets."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = (
            build_historical_appearance_hurdle_opening_evaluation(
                options.database
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
