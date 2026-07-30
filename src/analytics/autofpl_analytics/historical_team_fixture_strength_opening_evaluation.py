from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, List, Mapping, Optional, Sequence

from .baseline import (
    DistributionPrediction,
    ProbabilityPrediction,
    _summarise_distribution_model,
    _summarise_probability_model,
)
from .historical_appearance_hurdle_opening_distribution_evaluation import (
    MAXIMUM_APPEARANCE_BRIER_DELTA,
    MAXIMUM_APPEARANCE_LOG_LOSS_DELTA,
    MAXIMUM_POSITION_CRPS_REGRESSION_FRACTION,
    MINIMUM_CRPS_IMPROVEMENT_FRACTION,
    MINIMUM_TARGET_WINS as MINIMUM_DISTRIBUTION_TARGET_WINS,
    _predictions_for_target,
)
from .historical_appearance_hurdle_opening_evaluation import (
    APPEARANCE_MODEL,
    CONDITIONAL_MODEL,
    HURDLE_MODEL,
    SCENARIO_ARTIFACT_VERSION as INCUMBENT_SCENARIO_ARTIFACT_VERSION,
    _evaluate_pair,
    _reconstruct_hurdle_target_from_samples,
    _screen as _policy_screen,
    build_historical_appearance_hurdle_opening_scenarios,
)
from .historical_appearance_hurdle_points_evaluation import (
    _observations_by_origin,
)
from .historical_joint_scenario_evaluation import (
    MODEL_NAME as SCENARIO_MODEL,
)
from .historical_official_creative_opening_evaluation import (
    _distribution_screen,
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
from .historical_opening_policy_registration import (
    MAXIMUM_WORST_TARGET_REGRESSION_POINTS,
    MINIMUM_MEAN_IMPROVEMENT_POINTS,
    MINIMUM_TARGET_WINS as MINIMUM_POLICY_TARGET_WINS,
    REFERENCE_POLICY_KEY,
)
from .multi_season_evaluation import (
    FEATURES,
    Observation,
    _load_observations,
)
from .team_fixture_strength_features import (
    FEATURES_WITH_TEAM_FIXTURE_STRENGTH,
    TEAM_FIXTURE_STRENGTH_FEATURES,
    add_team_fixture_strength_features,
    build_team_fixture_strength_feature_table,
    build_team_rate_state,
    load_fixture_contexts,
    team_fixture_strength,
)
from .team_goal_strength_evaluation import _instant, _load_matches
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _sha256,
    _write_report,
)
from .temporal_tree import TREE_CONFIGURATION

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-team-fixture-strength-opening-evaluation"
ARTIFACT_VERSION = "historical-team-fixture-strength-opening-evaluation-v1"
SCENARIO_ARTIFACT_TYPE = (
    "historical-team-fixture-strength-opening-scenario-reconstruction"
)
SCENARIO_ARTIFACT_VERSION = (
    "historical-team-fixture-strength-opening-scenario-reconstruction-v1"
)
STATUS = "retrospective-fixture-strength-challenger-evaluated"
SCENARIO_STATUS = "retrospective-input-reconstruction-no-outcomes-opened"
CHALLENGER_POINT_MODEL = (
    "multi-season-appearance-hurdle-points-team-fixture-strength"
)
CHALLENGER_CONDITIONAL_MODEL = (
    "multi-season-appearance-conditional-points-team-fixture-strength"
)
INCUMBENT_DISTRIBUTION = (
    "appearance-hurdle-opening-joint-distribution"
)
CHALLENGER_DISTRIBUTION = (
    "appearance-hurdle-team-fixture-strength-opening-joint-distribution"
)
INCUMBENT_APPEARANCE = "appearance-hurdle-opening-appearance"
CHALLENGER_APPEARANCE = (
    "appearance-hurdle-team-fixture-strength-opening-appearance"
)
DECISION_RETAIN = (
    "retain-team-fixture-strength-opening-challenger-prospective-shadow"
)
DECISION_REJECT = "do-not-retain-team-fixture-strength-opening-challenger"


def build_historical_team_fixture_strength_opening_evaluation(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    incumbent = build_historical_appearance_hurdle_opening_scenarios(path)
    challenger = build_historical_team_fixture_strength_opening_scenarios(
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

    incumbent_targets = {
        str(target["targetSeasonCode"]): target
        for target in incumbent["targets"]
    }
    challenger_targets = {
        str(target["targetSeasonCode"]): target
        for target in challenger["targets"]
    }
    distribution_predictions: List[DistributionPrediction] = []
    appearance_predictions: List[ProbabilityPrediction] = []
    distribution_targets = []
    policy_targets = []
    for season in sorted(challenger_targets):
        incumbent_distribution, incumbent_appearance = (
            _predictions_for_target(
                incumbent_targets[season],
                folds[season],
                INCUMBENT_DISTRIBUTION,
                INCUMBENT_APPEARANCE,
            )
        )
        challenger_distribution, challenger_appearance = (
            _predictions_for_target(
                challenger_targets[season],
                folds[season],
                CHALLENGER_DISTRIBUTION,
                CHALLENGER_APPEARANCE,
            )
        )
        target_distribution_predictions = [
            *incumbent_distribution,
            *challenger_distribution,
        ]
        target_appearance_predictions = [
            *incumbent_appearance,
            *challenger_appearance,
        ]
        distribution_predictions.extend(target_distribution_predictions)
        appearance_predictions.extend(target_appearance_predictions)
        distribution_targets.append(
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
                        target_distribution_predictions,
                    )
                    for model in (
                        INCUMBENT_DISTRIBUTION,
                        CHALLENGER_DISTRIBUTION,
                    )
                ],
                "appearanceModels": [
                    _summarise_probability_model(
                        model,
                        target_appearance_predictions,
                    )
                    for model in (
                        INCUMBENT_APPEARANCE,
                        CHALLENGER_APPEARANCE,
                    )
                ],
            }
        )
        policy_targets.append(
            _evaluate_pair(
                season,
                folds[season],
                incumbent_targets[season],
                challenger_targets[season],
            )
        )

    distribution_models = [
        _summarise_distribution_model(model, distribution_predictions)
        for model in (
            INCUMBENT_DISTRIBUTION,
            CHALLENGER_DISTRIBUTION,
        )
    ]
    appearance_models = [
        _summarise_probability_model(model, appearance_predictions)
        for model in (
            INCUMBENT_APPEARANCE,
            CHALLENGER_APPEARANCE,
        )
    ]
    distribution_screen = _distribution_screen(
        distribution_models,
        appearance_models,
        distribution_targets,
        incumbent_distribution=INCUMBENT_DISTRIBUTION,
        challenger_distribution=CHALLENGER_DISTRIBUTION,
        incumbent_appearance=INCUMBENT_APPEARANCE,
        challenger_appearance=CHALLENGER_APPEARANCE,
        error_prefix="team-fixture-strength",
    )
    policy_screen = _policy_screen(policy_targets)
    passes = bool(
        distribution_screen["passes"] and policy_screen["passes"]
    )
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "researchStatus": "reused-opened-targets-no-promotion",
        "targetOutcomesOpened": True,
        "candidateRationale": (
            "Cutoff-safe, time-decayed and prior-shrunk team attack and "
            "opponent defence expected-goal rates added to the conditional "
            "point model under the retained opening folds"
        ),
        "incumbentSource": {
            "pointModel": HURDLE_MODEL,
            "appearanceModel": APPEARANCE_MODEL,
            "conditionalPointModel": CONDITIONAL_MODEL,
            "artifactVersion": incumbent["artifactVersion"],
            "dataIdentitySha256": incumbent["dataIdentitySha256"],
            "runIdentitySha256": incumbent["runIdentitySha256"],
        },
        "challengerSource": {
            "pointModel": CHALLENGER_POINT_MODEL,
            "appearanceModel": APPEARANCE_MODEL,
            "conditionalPointModel": CHALLENGER_CONDITIONAL_MODEL,
            "artifactVersion": challenger["artifactVersion"],
            "dataIdentitySha256": challenger["dataIdentitySha256"],
            "runIdentitySha256": challenger["runIdentitySha256"],
            "addedFeatures": list(TEAM_FIXTURE_STRENGTH_FEATURES),
        },
        "fixedScreen": _fixed_screen(),
        "distributionModels": distribution_models,
        "appearanceModels": appearance_models,
        "distributionTargets": distribution_targets,
        "policyTargets": policy_targets,
        "distributionScreen": distribution_screen,
        "policyScreen": policy_screen,
        "passes": passes,
        "decision": DECISION_RETAIN if passes else DECISION_REJECT,
        "isPromoted": False,
        "influencesAdvice": False,
        "limitations": [
            (
                "The three opening targets were previously opened. This "
                "result can retain a prospective challenger but cannot "
                "promote it."
            ),
            (
                "Historical opening health is unavailable and fixture "
                "structure remains the registered final-archive proxy."
            ),
            (
                "Team expected goals are reconstructed by summing official "
                "player expected goals; they are not an independent market "
                "or event-provider estimate."
            ),
            (
                "Promoted and otherwise unseen teams receive the venue "
                "league prior until their own prior evidence accumulates."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "incumbentSource": artifact["incumbentSource"],
            "challengerSource": artifact["challengerSource"],
            "fixedScreen": artifact["fixedScreen"],
            "distributionModels": artifact["distributionModels"],
            "appearanceModels": artifact["appearanceModels"],
            "distributionTargets": artifact["distributionTargets"],
            "policyTargets": artifact["policyTargets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def build_historical_team_fixture_strength_opening_scenarios(
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
            _reconstruct_target(
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
        "pointModel": CHALLENGER_POINT_MODEL,
        "appearanceModel": APPEARANCE_MODEL,
        "conditionalPointModel": CHALLENGER_CONDITIONAL_MODEL,
        "scenarioModel": SCENARIO_MODEL,
        "targetGameweeks": list(TARGET_GAMEWEEKS),
        "temporalDesign": {
            "componentTrainingRule": (
                "strictly-earlier-season-archives-only"
            ),
            "teamRateTrainingRule": (
                "strictly-prior-matches-at-each-origin"
            ),
            "teamRateDecayHalfLifeDays": 180,
            "teamRatePriorMatchEquivalent": 5,
            "conditionalPointTrainingRows": (
                "strictly-earlier-appearance-positive-player-gameweeks"
            ),
            "appearanceFeatureContract": "retained-base-features-unchanged",
            "scenarioDonorRule": "latest-strictly-earlier-season-only",
            "targetPerformanceFieldsRead": False,
            "targetOutcomeFieldsRead": [],
            "targetFixtureInput": FIXTURE_PROXY,
            "targetFixtureFieldsRead": [
                "gameweek",
                "fixture-id",
                "kickoff",
                "team",
                "venue",
            ],
            "weeklyPathPairing": (
                "fixed-current-engine-independent-weekly-permutation"
            ),
        },
        "modelConfiguration": dict(TREE_CONFIGURATION),
        "featureContract": {
            "baseFeatureCount": len(FEATURES),
            "addedFeatureCount": len(TEAM_FIXTURE_STRENGTH_FEATURES),
            "addedFeatures": list(TEAM_FIXTURE_STRENGTH_FEATURES),
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
            "scenarioModel": artifact["scenarioModel"],
            "targetGameweeks": artifact["targetGameweeks"],
            "temporalDesign": artifact["temporalDesign"],
            "modelConfiguration": artifact["modelConfiguration"],
            "featureContract": artifact["featureContract"],
            "targets": artifact["targets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _reconstruct_target(
    connection: Any,
    fold: OpeningFold,
) -> Dict[str, Any]:
    samples_by_origin = build_team_fixture_strength_feature_table(
        connection,
        fold.training_captures,
    )
    observations_by_origin = _observations_by_origin(
        connection,
        fold.training_captures,
    )
    histories: DefaultDict[int, list[Observation]] = defaultdict(list)
    matches = []
    for season_index, capture in enumerate(fold.training_captures):
        histories_rows = _load_observations(
            connection,
            capture,
            season_index,
        )
        for observation in histories_rows:
            histories[observation.player_code].append(observation)
        matches.extend(_load_matches(connection, capture, season_index))
    fixture_proxy = _load_fixture_proxy(
        connection,
        fold.target_capture.capture_id,
    )
    all_target_fixtures = load_fixture_contexts(
        connection,
        fold.target_capture,
        len(fold.training_captures),
    )
    fixtures_by_gameweek = {
        gameweek: [
            fixture
            for fixture in all_target_fixtures
            if fixture.gameweek == gameweek
        ]
        for gameweek in TARGET_GAMEWEEKS
    }
    rate_states = {}
    for gameweek, fixtures in fixtures_by_gameweek.items():
        _require(
            bool(fixtures),
            "team-fixture-strength.target-fixtures",
            "A target Gameweek has no fixture identity.",
        )
        cutoff = min(_instant(fixture.kickoff_utc) for fixture in fixtures)
        rate_states[gameweek] = build_team_rate_state(matches, cutoff)

    def target_sample(gameweek: int, player: Any) -> Any:
        base = _target_sample(
            fold.target_capture.season_code,
            len(fold.training_captures),
            gameweek,
            player,
            histories.get(player.player_code, ()),
            fixture_proxy,
        )
        return add_team_fixture_strength_features(
            base,
            team_fixture_strength(
                rate_states[gameweek],
                fixtures_by_gameweek[gameweek],
                player.team_name,
            ),
        )

    return _reconstruct_hurdle_target_from_samples(
        connection,
        fold,
        samples_by_origin,
        observations_by_origin,
        target_sample,
        FEATURES,
        FEATURES_WITH_TEAM_FIXTURE_STRENGTH,
        APPEARANCE_MODEL,
        CHALLENGER_CONDITIONAL_MODEL,
    )


def _fixed_screen() -> Dict[str, Any]:
    return {
        "distribution": {
            "primaryMetric": "aggregate-player-gameweek-mean-crps",
            "minimumCrpsImprovementFraction": (
                MINIMUM_CRPS_IMPROVEMENT_FRACTION
            ),
            "minimumTargetSeasonWins": MINIMUM_DISTRIBUTION_TARGET_WINS,
            "maximumPositionCrpsRegressionFraction": (
                MAXIMUM_POSITION_CRPS_REGRESSION_FRACTION
            ),
            "maximumAppearanceBrierDelta": (
                MAXIMUM_APPEARANCE_BRIER_DELTA
            ),
            "maximumAppearanceLogLossDelta": (
                MAXIMUM_APPEARANCE_LOG_LOSS_DELTA
            ),
        },
        "policy": {
            "evaluationPolicyKey": REFERENCE_POLICY_KEY,
            "minimumMeanImprovementPoints": MINIMUM_MEAN_IMPROVEMENT_POINTS,
            "minimumTargetWins": MINIMUM_POLICY_TARGET_WINS,
            "maximumWorstTargetRegressionPoints": (
                MAXIMUM_WORST_TARGET_REGRESSION_POINTS
            ),
        },
        "jointDecisionRule": "all-distribution-and-policy-gates-must-pass",
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
        == INCUMBENT_SCENARIO_ARTIFACT_VERSION
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
        "team-fixture-strength.scenario-pair",
        "The fixed target-outcome-free scenario pair is unavailable.",
    )


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate cutoff-safe team and opponent fixture strength in "
            "historical opening distributions and squad policy."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = (
            build_historical_team_fixture_strength_opening_evaluation(
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
