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
    SCENARIO_ARTIFACT_VERSION as INCUMBENT_SCENARIO_ARTIFACT_VERSION,
    _evaluate_pair,
    _observations_by_origin,
    _reconstruct_hurdle_target_from_samples,
    _screen as _policy_screen,
    build_historical_appearance_hurdle_opening_scenarios,
)
from .historical_joint_scenario_evaluation import (
    MODEL_NAME as SCENARIO_MODEL,
)
from .historical_official_creative_opening_evaluation import (
    _distribution_screen,
)
from .historical_opening_forecast_reconstruction import (
    FIXTURE_PROXY,
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
from .historical_preseason_evaluation import _build_samples
from .historical_promoted_appearance_evaluation import (
    BLEND_WEIGHT,
    CHALLENGER_MODEL as RETAINED_APPEARANCE_CHALLENGER,
    SOURCE_FEATURES,
    SOURCE_MODEL,
    SOURCE_SEASONS,
    PromotionClass,
    _fit_source_model,
    _load_promotion_class,
    _parse_extractions,
    _source_feature_vector,
)
from .multi_season_evaluation import (
    FEATURES,
    Observation,
    _build_feature_table,
    _load_observations,
)
from .temporal_ridge import (
    Prediction,
    TemporalRidgeError,
    _open_connection,
    _sha256,
    _write_report,
)
from .temporal_tree import TREE_CONFIGURATION

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-promoted-opening-evaluation"
ARTIFACT_VERSION = "historical-promoted-opening-evaluation-v1"
SCENARIO_ARTIFACT_TYPE = (
    "historical-promoted-opening-scenario-reconstruction"
)
SCENARIO_ARTIFACT_VERSION = (
    "historical-promoted-opening-scenario-reconstruction-v1"
)
STATUS = "retrospective-promoted-opening-challenger-evaluated"
SCENARIO_STATUS = "retrospective-input-reconstruction-no-outcomes-opened"
CHALLENGER_APPEARANCE = (
    "appearance-hurdle-promoted-equal-weight-opening-appearance"
)
CHALLENGER_POINT_MODEL = (
    "multi-season-appearance-hurdle-points-promoted-appearance-pool"
)
INCUMBENT_DISTRIBUTION = (
    "appearance-hurdle-opening-joint-distribution"
)
CHALLENGER_DISTRIBUTION = (
    "appearance-hurdle-promoted-opening-joint-distribution"
)
INCUMBENT_APPEARANCE = "appearance-hurdle-opening-appearance"
RETAINED_APPEARANCE_DATA_IDENTITY = (
    "ae4650b3357310975379888539958cd4157f737155428461968a17ad8cabbdc7"
)
RETAINED_APPEARANCE_RUN_IDENTITY = (
    "55d13005b3cd6d107dddb2e5955d48623b590b8b6c1706dedcb6c9ca3699cc39"
)
DECISION_RETAIN = (
    "retain-promoted-appearance-for-current-opening-challenger"
)
DECISION_REJECT = "do-not-retain-promoted-appearance-opening-challenger"


def build_historical_promoted_opening_evaluation(
    database_path: Path,
    extraction_paths: Mapping[str, Path],
) -> Dict[str, Any]:
    path = Path(database_path)
    incumbent = build_historical_appearance_hurdle_opening_scenarios(path)
    challenger = build_historical_promoted_opening_scenarios(
        path,
        extraction_paths,
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
    affected_distribution_predictions: List[
        DistributionPrediction
    ] = []
    affected_appearance_predictions: List[
        ProbabilityPrediction
    ] = []
    all_distribution_predictions: List[DistributionPrediction] = []
    all_appearance_predictions: List[ProbabilityPrediction] = []
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
        affected_codes = {
            int(value)
            for value in challenger_targets[season][
                "promotedAppearancePlayerCodes"
            ]
        }
        incumbent_affected_distribution = [
            value
            for value in incumbent_distribution
            if value.player_id in affected_codes
        ]
        challenger_affected_distribution = [
            value
            for value in challenger_distribution
            if value.player_id in affected_codes
        ]
        incumbent_affected_appearance = [
            value
            for value in incumbent_appearance
            if value.player_id in affected_codes
        ]
        challenger_affected_appearance = [
            value
            for value in challenger_appearance
            if value.player_id in affected_codes
        ]
        target_distribution_predictions = [
            *incumbent_affected_distribution,
            *challenger_affected_distribution,
        ]
        target_appearance_predictions = [
            *incumbent_affected_appearance,
            *challenger_affected_appearance,
        ]
        affected_distribution_predictions.extend(
            target_distribution_predictions
        )
        affected_appearance_predictions.extend(
            target_appearance_predictions
        )
        all_distribution_predictions.extend(
            [*incumbent_distribution, *challenger_distribution]
        )
        all_appearance_predictions.extend(
            [*incumbent_appearance, *challenger_appearance]
        )
        distribution_targets.append(
            {
                "targetSeasonCode": season,
                "targetCaptureId": folds[
                    season
                ].target_capture.capture_id,
                "affectedPlayerCount": len(affected_codes),
                "affectedPlayerGameweekCount": len(
                    incumbent_affected_distribution
                ),
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
        _summarise_distribution_model(
            model,
            affected_distribution_predictions,
        )
        for model in (
            INCUMBENT_DISTRIBUTION,
            CHALLENGER_DISTRIBUTION,
        )
    ]
    appearance_models = [
        _summarise_probability_model(
            model,
            affected_appearance_predictions,
        )
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
        error_prefix="promoted-opening",
    )
    all_player_non_regression = _all_player_non_regression(
        all_distribution_predictions,
        all_appearance_predictions,
    )
    policy_screen = _policy_screen(policy_targets)
    passes = bool(
        distribution_screen["passes"]
        and all_player_non_regression["passes"]
        and policy_screen["passes"]
    )
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "researchStatus": "reused-opened-targets-no-promotion",
        "targetOutcomesOpened": True,
        "retainedAppearanceScreen": {
            "artifactVersion": (
                "historical-promoted-appearance-evaluation-v1"
            ),
            "dataIdentitySha256": RETAINED_APPEARANCE_DATA_IDENTITY,
            "runIdentitySha256": RETAINED_APPEARANCE_RUN_IDENTITY,
        },
        "incumbentSource": _scenario_identity(incumbent),
        "challengerSource": _scenario_identity(challenger),
        "fixedScreen": _fixed_screen_document(),
        "affectedDistributionModels": distribution_models,
        "affectedAppearanceModels": appearance_models,
        "distributionTargets": distribution_targets,
        "distributionScreen": distribution_screen,
        "allPlayerNonRegression": all_player_non_regression,
        "policyTargets": policy_targets,
        "policyScreen": policy_screen,
        "jointScreenPasses": passes,
        "decision": DECISION_RETAIN if passes else DECISION_REJECT,
        "isPromoted": False,
        "influencesAdvice": False,
        "limitations": [
            (
                "The historical target outcomes were already opened. This "
                "experiment can reject or retain a current challenger but "
                "cannot promote one."
            ),
            (
                "Only the fixed bridged promoted-player appearance "
                "probability changes. Conditional points, donor paths, "
                "fixtures, optimiser constraints and policy are unchanged."
            ),
            (
                "The primary proper-score screen is restricted to affected "
                "players so unchanged rows cannot dilute the measurement. "
                "A separate all-player non-regression gate and the complete "
                "squad policy screen protect global decision quality."
            ),
            (
                "Historical opening availability captures remain absent and "
                "the FBref aggregates were captured retrospectively."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "retainedAppearanceScreen": artifact[
                "retainedAppearanceScreen"
            ],
            "incumbentSource": artifact["incumbentSource"],
            "challengerSource": artifact["challengerSource"],
            "fixedScreen": artifact["fixedScreen"],
            "affectedDistributionModels": artifact[
                "affectedDistributionModels"
            ],
            "affectedAppearanceModels": artifact[
                "affectedAppearanceModels"
            ],
            "distributionTargets": artifact["distributionTargets"],
            "policyTargets": artifact["policyTargets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def build_historical_promoted_opening_scenarios(
    database_path: Path,
    extraction_paths: Mapping[str, Path],
) -> Dict[str, Any]:
    path = Path(database_path)
    _require(
        path.is_file() and set(extraction_paths) == set(SOURCE_SEASONS),
        "promoted-opening.inputs",
        "The database and four registered FBref extractions are required.",
    )
    connection = _open_connection(path)
    try:
        captures = tuple(
            _required_capture(connection, season)
            for season in REGISTERED_SEASONS
        )
        targets = []
        for target_index in range(1, len(captures)):
            training_classes = tuple(
                _load_promotion_class(
                    connection,
                    _load_opening_fold(
                        connection,
                        captures,
                        source_index,
                        include_outcomes=True,
                    ),
                    SOURCE_SEASONS[source_index],
                    Path(extraction_paths[SOURCE_SEASONS[source_index]]),
                )
                for source_index in range(target_index)
            )
            target_fold = _load_opening_fold(
                connection,
                captures,
                target_index,
                include_outcomes=False,
            )
            target_class = _load_promotion_class(
                connection,
                target_fold,
                SOURCE_SEASONS[target_index],
                Path(extraction_paths[SOURCE_SEASONS[target_index]]),
            )
            targets.append(
                _reconstruct_promoted_target(
                    connection,
                    target_fold,
                    training_classes,
                    target_class,
                )
            )
    finally:
        connection.close()

    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": SCENARIO_ARTIFACT_TYPE,
        "artifactVersion": SCENARIO_ARTIFACT_VERSION,
        "status": SCENARIO_STATUS,
        "pointModel": CHALLENGER_POINT_MODEL,
        "appearanceModel": CHALLENGER_APPEARANCE,
        "conditionalPointModel": CONDITIONAL_MODEL,
        "scenarioModel": SCENARIO_MODEL,
        "sourceModel": SOURCE_MODEL,
        "sourceFeatures": list(SOURCE_FEATURES),
        "fixedBlendWeight": BLEND_WEIGHT,
        "retainedAppearanceDataIdentitySha256": (
            RETAINED_APPEARANCE_DATA_IDENTITY
        ),
        "retainedAppearanceRunIdentitySha256": (
            RETAINED_APPEARANCE_RUN_IDENTITY
        ),
        "temporalDesign": {
            "componentTrainingRule": (
                "strictly-earlier-fpl-season-archives-only"
            ),
            "sourceTrainingRule": (
                "strictly-earlier-promotion-classes-only"
            ),
            "targetPerformanceFieldsRead": False,
            "targetOutcomeFieldsRead": [],
            "targetFixtureInput": FIXTURE_PROXY,
            "conditionalPointModelChanged": False,
            "unbridgedPlayerAppearanceChanged": False,
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
            "sourceModel": artifact["sourceModel"],
            "sourceFeatures": artifact["sourceFeatures"],
            "fixedBlendWeight": artifact["fixedBlendWeight"],
            "retainedAppearanceDataIdentitySha256": artifact[
                "retainedAppearanceDataIdentitySha256"
            ],
            "temporalDesign": artifact["temporalDesign"],
            "modelConfiguration": artifact["modelConfiguration"],
            "targets": artifact["targets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _reconstruct_promoted_target(
    connection: Any,
    fold: OpeningFold,
    training_classes: Sequence[PromotionClass],
    target_class: PromotionClass,
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
    source_model, source_diagnostics = _fit_source_model(
        training_classes
    )
    source_by_code = {
        value.target.player_code: value.source
        for value in target_class.bridged_players
    }

    def transform(
        gameweek: int,
        players: Sequence[Any],
        predictions: Sequence[Prediction],
    ) -> Sequence[Prediction]:
        return _pool_appearance_predictions(
            source_model,
            source_by_code,
            gameweek,
            players,
            predictions,
        )

    target = _reconstruct_hurdle_target_from_samples(
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
        appearance_transform=transform,
    )
    target["promotedAppearancePlayerCodes"] = sorted(source_by_code)
    target["promotedAppearancePlayerCount"] = len(source_by_code)
    target["sourceSeasonCode"] = target_class.source_season_code
    target["sourceSnapshotId"] = target_class.source_snapshot_id
    target["sourceContentSha256"] = target_class.source_content_sha256
    target["sourceModelDiagnostics"] = source_diagnostics
    target["forecastIdentitySha256"] = _sha256(
        {
            "baseForecastIdentitySha256": target[
                "forecastIdentitySha256"
            ],
            "promotedAppearancePlayerCodes": target[
                "promotedAppearancePlayerCodes"
            ],
            "sourceSeasonCode": target["sourceSeasonCode"],
            "sourceSnapshotId": target["sourceSnapshotId"],
            "sourceContentSha256": target["sourceContentSha256"],
            "sourceModelDiagnostics": target["sourceModelDiagnostics"],
        }
    )
    target["scenarioContentSha256"] = _sha256(
        {
            "players": target["players"],
            "weeks": target["weeks"],
            "promotedAppearancePlayerCodes": target[
                "promotedAppearancePlayerCodes"
            ],
        }
    )
    return target


def _pool_appearance_predictions(
    source_model: Any,
    source_by_code: Mapping[int, Any],
    gameweek: int,
    players: Sequence[Any],
    predictions: Sequence[Prediction],
) -> list[Prediction]:
    result = []
    for player, prediction in zip(
        players,
        predictions,
        strict=True,
    ):
        source = source_by_code.get(player.player_code)
        probability = prediction.predicted
        if source is not None:
            source_probability = float(
                source_model.predict_proba(
                    [
                        _source_feature_vector(
                            source,
                            player.position,
                            gameweek,
                        )
                    ]
                )[0, 1]
            )
            probability = (
                BLEND_WEIGHT * probability
                + (1.0 - BLEND_WEIGHT) * source_probability
            )
        result.append(
            Prediction(
                model=CHALLENGER_APPEARANCE,
                season_code=prediction.season_code,
                gameweek=prediction.gameweek,
                player_id=prediction.player_id,
                position=prediction.position,
                predicted=probability,
                actual=prediction.actual,
            )
        )
    return result


def _all_player_non_regression(
    distribution_predictions: Sequence[DistributionPrediction],
    appearance_predictions: Sequence[ProbabilityPrediction],
) -> Dict[str, Any]:
    distributions = [
        _summarise_distribution_model(model, distribution_predictions)
        for model in (
            INCUMBENT_DISTRIBUTION,
            CHALLENGER_DISTRIBUTION,
        )
    ]
    appearances = [
        _summarise_probability_model(model, appearance_predictions)
        for model in (
            INCUMBENT_APPEARANCE,
            CHALLENGER_APPEARANCE,
        )
    ]
    distribution_by_name = {
        str(value["name"]): value for value in distributions
    }
    appearance_by_name = {
        str(value["name"]): value for value in appearances
    }
    crps_delta = (
        float(
            distribution_by_name[CHALLENGER_DISTRIBUTION]["metrics"][
                "meanCrps"
            ]
        )
        - float(
            distribution_by_name[INCUMBENT_DISTRIBUTION]["metrics"][
                "meanCrps"
            ]
        )
    )
    brier_delta = (
        float(
            appearance_by_name[CHALLENGER_APPEARANCE]["metrics"][
                "brierScore"
            ]
        )
        - float(
            appearance_by_name[INCUMBENT_APPEARANCE]["metrics"][
                "brierScore"
            ]
        )
    )
    log_loss_delta = (
        float(
            appearance_by_name[CHALLENGER_APPEARANCE]["metrics"][
                "logLoss"
            ]
        )
        - float(
            appearance_by_name[INCUMBENT_APPEARANCE]["metrics"][
                "logLoss"
            ]
        )
    )
    gates = {
        "crpsNonRegression": crps_delta <= 0.0,
        "appearanceBrierNonRegression": brier_delta <= 0.0,
        "appearanceLogLossNonRegression": log_loss_delta <= 0.0,
    }
    return {
        "passes": all(gates.values()),
        "gates": gates,
        "crpsDelta": crps_delta,
        "appearanceBrierDelta": brier_delta,
        "appearanceLogLossDelta": log_loss_delta,
        "distributionModels": distributions,
        "appearanceModels": appearances,
    }


def _scenario_identity(value: Mapping[str, Any]) -> Dict[str, Any]:
    return {
        "artifactVersion": value["artifactVersion"],
        "dataIdentitySha256": value["dataIdentitySha256"],
        "runIdentitySha256": value["runIdentitySha256"],
    }


def _fixed_screen_document() -> Dict[str, Any]:
    return {
        "affectedDistribution": {
            "primaryMetric": (
                "affected-promoted-player-gameweek-mean-crps"
            ),
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
        "allPlayers": {
            "maximumCrpsDelta": 0.0,
            "maximumAppearanceBrierDelta": 0.0,
            "maximumAppearanceLogLossDelta": 0.0,
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
            "affected-distribution-all-player-and-policy-gates-must-pass"
        ),
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
        "promoted-opening.scenario-pair",
        "The fixed target-outcome-free scenario pair is unavailable.",
    )


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Propagate the retained promoted-player appearance pool through "
            "historical point distributions and opening-squad policy."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument(
        "--fbref-extraction",
        action="append",
        default=[],
        metavar="SEASON=PATH",
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_historical_promoted_opening_evaluation(
            options.database,
            _parse_extractions(options.fbref_extraction),
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
