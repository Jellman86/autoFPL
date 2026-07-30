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
    Observation,
    _load_observations,
)
from .official_creative_features import (
    FEATURES_WITH_OFFICIAL_CREATIVE,
    OFFICIAL_CREATIVE_FEATURES,
    add_official_creative_features,
    build_official_creative_feature_table,
    load_official_creative_histories,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)
from .temporal_tree import TREE_CONFIGURATION

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-official-creative-opening-evaluation"
ARTIFACT_VERSION = "historical-official-creative-opening-evaluation-v1"
SCENARIO_ARTIFACT_TYPE = (
    "historical-official-creative-opening-scenario-reconstruction"
)
SCENARIO_ARTIFACT_VERSION = (
    "historical-official-creative-opening-scenario-reconstruction-v1"
)
STATUS = "retrospective-enrichment-challenger-evaluated"
SCENARIO_STATUS = "retrospective-input-reconstruction-no-outcomes-opened"
CHALLENGER_POINT_MODEL = (
    "multi-season-appearance-hurdle-points-official-creative"
)
CHALLENGER_APPEARANCE_MODEL = (
    "multi-season-appearance-classifier-official-creative"
)
CHALLENGER_CONDITIONAL_MODEL = (
    "multi-season-appearance-conditional-points-official-creative"
)
INCUMBENT_DISTRIBUTION = (
    "appearance-hurdle-opening-joint-distribution"
)
CHALLENGER_DISTRIBUTION = (
    "appearance-hurdle-official-creative-opening-joint-distribution"
)
INCUMBENT_APPEARANCE = "appearance-hurdle-opening-appearance"
CHALLENGER_APPEARANCE = (
    "appearance-hurdle-official-creative-opening-appearance"
)
DECISION_RETAIN = (
    "retain-official-creative-opening-challenger-prospective-shadow"
)
DECISION_REJECT = "do-not-retain-official-creative-opening-challenger"


def build_historical_official_creative_opening_evaluation(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    incumbent = build_historical_appearance_hurdle_opening_scenarios(path)
    challenger = build_historical_official_creative_opening_scenarios(path)
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
        distribution_predictions.extend(
            target_distribution_predictions
        )
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
            "OpenFPL-inspired official creative and BPS summaries added "
            "to the retained hurdle model under the same opening folds"
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
            "appearanceModel": CHALLENGER_APPEARANCE_MODEL,
            "conditionalPointModel": CHALLENGER_CONDITIONAL_MODEL,
            "artifactVersion": challenger["artifactVersion"],
            "dataIdentitySha256": challenger["dataIdentitySha256"],
            "runIdentitySha256": challenger["runIdentitySha256"],
            "addedFeatures": list(OFFICIAL_CREATIVE_FEATURES),
        },
        "fixedScreen": {
            "distribution": {
                "primaryMetric": "aggregate-player-gameweek-mean-crps",
                "minimumCrpsImprovementFraction": (
                    MINIMUM_CRPS_IMPROVEMENT_FRACTION
                ),
                "minimumTargetSeasonWins": (
                    MINIMUM_DISTRIBUTION_TARGET_WINS
                ),
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
                "minimumMeanImprovementPoints": (
                    MINIMUM_MEAN_IMPROVEMENT_POINTS
                ),
                "minimumTargetWins": MINIMUM_POLICY_TARGET_WINS,
                "maximumWorstTargetRegressionPoints": (
                    MAXIMUM_WORST_TARGET_REGRESSION_POINTS
                ),
            },
            "jointDecisionRule": (
                "all-distribution-and-policy-gates-must-pass"
            ),
        },
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
                "The three opening targets were already opened. The result "
                "can retain a prospective challenger but cannot promote it."
            ),
            (
                "The candidate is inspired by OpenFPL's public feature "
                "portfolio, not a reproduction of its unpublished training "
                "pipeline or a claim about its Gameweek 1 performance."
            ),
            (
                "Historical opening health is unavailable and fixture "
                "structure remains the registered final-archive proxy."
            ),
            (
                "Official BPS, influence, creativity and threat share event "
                "information with FPL points and expected-event fields; the "
                "fixed out-of-season screen, not novelty, decides retention."
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


def build_historical_official_creative_opening_scenarios(
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
        "appearanceModel": CHALLENGER_APPEARANCE_MODEL,
        "conditionalPointModel": CHALLENGER_CONDITIONAL_MODEL,
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
        "featureContract": {
            "baseFeatureCount": (
                len(FEATURES_WITH_OFFICIAL_CREATIVE)
                - len(OFFICIAL_CREATIVE_FEATURES)
            ),
            "addedFeatureCount": len(OFFICIAL_CREATIVE_FEATURES),
            "addedFeatures": list(OFFICIAL_CREATIVE_FEATURES),
            "historyAvailability": (
                "strictly-prior-player-gameweeks-only"
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
    samples_by_origin = build_official_creative_feature_table(
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
    creative_histories = load_official_creative_histories(
        connection,
        fold.training_captures,
    )
    fixtures = _load_fixture_proxy(
        connection,
        fold.target_capture.capture_id,
    )

    def target_sample(gameweek: int, player: Any) -> Any:
        base = _target_sample(
            fold.target_capture.season_code,
            len(fold.training_captures),
            gameweek,
            player,
            histories.get(player.player_code, ()),
            fixtures,
        )
        return add_official_creative_features(
            base,
            creative_histories.get(player.player_code, ()),
        )

    return _reconstruct_hurdle_target_from_samples(
        connection,
        fold,
        samples_by_origin,
        observations_by_origin,
        target_sample,
        FEATURES_WITH_OFFICIAL_CREATIVE,
        CHALLENGER_APPEARANCE_MODEL,
        CHALLENGER_CONDITIONAL_MODEL,
    )


def _distribution_screen(
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
        set(distributions)
        == {INCUMBENT_DISTRIBUTION, CHALLENGER_DISTRIBUTION}
        and set(appearances)
        == {INCUMBENT_APPEARANCE, CHALLENGER_APPEARANCE},
        "official-creative.models",
        "The fixed model pair is incomplete.",
    )
    incumbent = distributions[INCUMBENT_DISTRIBUTION]
    challenger = distributions[CHALLENGER_DISTRIBUTION]
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
            by_name[INCUMBENT_DISTRIBUTION]["metrics"]["meanCrps"]
        )
        challenger_target = float(
            by_name[CHALLENGER_DISTRIBUTION]["metrics"]["meanCrps"]
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
    incumbent_positions = incumbent["slices"]["position"]
    challenger_positions = challenger["slices"]["position"]
    _require(
        set(incumbent_positions) == set(challenger_positions),
        "official-creative.positions",
        "The position slices differ.",
    )
    position_rows = []
    maximum_position_regression = float("-inf")
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
    incumbent_probability = appearances[INCUMBENT_APPEARANCE][
        "metrics"
    ]
    challenger_probability = appearances[CHALLENGER_APPEARANCE][
        "metrics"
    ]
    brier_delta = (
        float(challenger_probability["brierScore"])
        - float(incumbent_probability["brierScore"])
    )
    log_loss_delta = (
        float(challenger_probability["logLoss"])
        - float(incumbent_probability["logLoss"])
    )
    target_wins = sum(bool(row["challengerWins"]) for row in target_rows)
    gates = {
        "aggregateCrpsImprovement": (
            crps_improvement >= MINIMUM_CRPS_IMPROVEMENT_FRACTION
        ),
        "targetSeasonWins": (
            target_wins >= MINIMUM_DISTRIBUTION_TARGET_WINS
        ),
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
    }
    return {
        "aggregateCrpsImprovementFraction": _round(crps_improvement),
        "targetSeasonWins": target_wins,
        "targetSeasonCount": len(target_rows),
        "targetComparisons": target_rows,
        "positionComparisons": position_rows,
        "maximumPositionCrpsRegressionFraction": _round(
            maximum_position_regression
        ),
        "appearanceBrierDelta": _round(brier_delta),
        "appearanceLogLossDelta": _round(log_loss_delta),
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
        "official-creative.scenario-pair",
        "The fixed target-outcome-free scenario pair is unavailable.",
    )


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate official BPS and creative-history enrichment on fixed "
            "historical opening distributions and squad policy."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_historical_official_creative_opening_evaluation(
            options.database
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
