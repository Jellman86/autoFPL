from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence

import numpy as np

from .baseline import (
    DistributionPrediction,
    ProbabilityPrediction,
    _empirical_distribution,
    _summarise_distribution_model,
    _summarise_probability_model,
)
from .historical_appearance_hurdle_opening_evaluation import (
    SCENARIO_ARTIFACT_VERSION as HURDLE_SCENARIO_ARTIFACT_VERSION,
    build_historical_appearance_hurdle_opening_scenarios,
)
from .historical_opening_policy_data import (
    REGISTERED_SEASONS,
    OpeningFold,
    _load_opening_fold,
    _required_capture,
)
from .historical_opening_scenario_reconstruction import (
    ARTIFACT_VERSION as INCUMBENT_SCENARIO_ARTIFACT_VERSION,
    build_historical_opening_scenario_reconstruction,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = (
    "historical-appearance-hurdle-opening-distribution-evaluation"
)
ARTIFACT_VERSION = (
    "historical-appearance-hurdle-opening-distribution-evaluation-v1"
)
STATUS = "retrospective-distribution-challenger-evaluated"
INCUMBENT_MODEL = "direct-tree-opening-joint-distribution"
CHALLENGER_MODEL = "appearance-hurdle-opening-joint-distribution"
INCUMBENT_APPEARANCE = "prior-season-opening-appearance"
CHALLENGER_APPEARANCE = "appearance-hurdle-opening-appearance"
MINIMUM_CRPS_IMPROVEMENT_FRACTION = 0.01
MINIMUM_TARGET_WINS = 2
MAXIMUM_POSITION_CRPS_REGRESSION_FRACTION = 0.05
MAXIMUM_APPEARANCE_BRIER_DELTA = 0.0
MAXIMUM_APPEARANCE_LOG_LOSS_DELTA = 0.0
DECISION_RETAIN = (
    "retain-hurdle-opening-distributions-prospective-challenger"
)
DECISION_REJECT = "do-not-retain-hurdle-opening-distributions"


def build_historical_appearance_hurdle_opening_distribution_evaluation(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    incumbent = build_historical_opening_scenario_reconstruction(path)
    challenger = build_historical_appearance_hurdle_opening_scenarios(
        path
    )
    _require_scenarios(incumbent, challenger)

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
        distribution_predictions.extend(incumbent_distribution)
        distribution_predictions.extend(challenger_distribution)
        appearance_predictions.extend(incumbent_appearance)
        appearance_predictions.extend(challenger_appearance)
        target_distributions = [
            *incumbent_distribution,
            *challenger_distribution,
        ]
        target_appearance = [
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
                    for model in (
                        INCUMBENT_MODEL,
                        CHALLENGER_MODEL,
                    )
                ],
                "appearanceModels": [
                    _summarise_probability_model(
                        model,
                        target_appearance,
                    )
                    for model in (
                        INCUMBENT_APPEARANCE,
                        CHALLENGER_APPEARANCE,
                    )
                ],
            }
        )

    distribution_models = [
        _summarise_distribution_model(
            model,
            distribution_predictions,
        )
        for model in (INCUMBENT_MODEL, CHALLENGER_MODEL)
    ]
    appearance_models = [
        _summarise_probability_model(
            model,
            appearance_predictions,
        )
        for model in (INCUMBENT_APPEARANCE, CHALLENGER_APPEARANCE)
    ]
    screen = _screen(
        distribution_models,
        appearance_models,
        targets,
    )
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "researchStatus": "reused-opened-targets-no-promotion",
        "targetOutcomesOpened": True,
        "incumbentSource": {
            "artifactVersion": incumbent["artifactVersion"],
            "dataIdentitySha256": incumbent[
                "dataIdentitySha256"
            ],
            "runIdentitySha256": incumbent["runIdentitySha256"],
        },
        "challengerSource": {
            "artifactVersion": challenger["artifactVersion"],
            "dataIdentitySha256": challenger[
                "dataIdentitySha256"
            ],
            "runIdentitySha256": challenger[
                "runIdentitySha256"
            ],
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
                "This proper-score screen can retain a prospective "
                "distribution challenger but cannot promote it."
            ),
            (
                "Historical opening-day injury and status captures remain "
                "unavailable, and target fixture structure is the registered "
                "final-archive proxy."
            ),
            (
                "Marginal CRPS and appearance calibration do not fully test "
                "cross-player or cross-Gameweek dependence. The separate "
                "opening-policy evaluation measures the combined decision."
            ),
            (
                "The empirical paths contain only 37 or 38 latest-prior-"
                "season donor rows. Quantile resolution and tail estimates "
                "remain finite-sample limited."
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
            "targets": artifact["targets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _predictions_for_target(
    target: Mapping[str, Any],
    fold: OpeningFold,
    distribution_model: str,
    appearance_model: str,
) -> tuple[
    List[DistributionPrediction],
    List[ProbabilityPrediction],
]:
    season = str(target["targetSeasonCode"])
    _require(
        season == fold.target_capture.season_code
        and int(target["targetCaptureId"])
        == fold.target_capture.capture_id,
        "hurdle-distribution.target",
        "The scenario and outcome target differ.",
    )
    players = list(target["players"])
    _require(
        [int(player["columnIndex"]) for player in players]
        == list(range(len(players))),
        "hurdle-distribution.columns",
        "Scenario player columns are incomplete or unordered.",
    )
    outcome_by_code = {
        player.player_code: player for player in fold.players
    }
    _require(
        set(outcome_by_code)
        == {int(player["playerCode"]) for player in players},
        "hurdle-distribution.players",
        "Scenario and outcome player cohorts differ.",
    )
    weeks = list(target["weeks"])
    _require(
        [int(week["gameweek"]) for week in weeks]
        == list(range(1, 9)),
        "hurdle-distribution.gameweeks",
        "A scenario target does not contain Gameweeks 1 through 8.",
    )
    distributions: List[DistributionPrediction] = []
    appearances: List[ProbabilityPrediction] = []
    for week_index, week in enumerate(weeks):
        points = np.asarray(week["pointRows"], dtype=np.int64)
        played = np.asarray(week["playedRows"], dtype=np.bool_)
        _require(
            points.ndim == 2
            and points.shape == played.shape
            and points.shape[1] == len(players)
            and points.shape[0] == int(target["scenarioCount"]),
            "hurdle-distribution.matrix",
            "A scenario point/appearance matrix is misaligned.",
        )
        _require(
            not np.any((points != 0) & ~played),
            "hurdle-distribution.nonplayer-points",
            "A non-playing scenario row contains non-zero points.",
        )
        for column, player in enumerate(players):
            player_code = int(player["playerCode"])
            outcome = outcome_by_code[player_code]
            distributions.append(
                DistributionPrediction(
                    model=distribution_model,
                    season_code=season,
                    gameweek=week_index + 1,
                    player_id=player_code,
                    position=str(player["position"]),
                    distribution=_empirical_distribution(
                        points[:, column].tolist()
                    ),
                    actual=int(outcome.points[week_index]),
                )
            )
            appearances.append(
                ProbabilityPrediction(
                    model=appearance_model,
                    season_code=season,
                    gameweek=week_index + 1,
                    player_id=player_code,
                    position=str(player["position"]),
                    probability=float(
                        np.mean(played[:, column])
                    ),
                    actual=int(outcome.minutes[week_index] > 0),
                )
            )
    return distributions, appearances


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
        set(distributions) == {INCUMBENT_MODEL, CHALLENGER_MODEL}
        and set(appearances)
        == {INCUMBENT_APPEARANCE, CHALLENGER_APPEARANCE},
        "hurdle-distribution.models",
        "The fixed distribution or appearance model pair is incomplete.",
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
        "hurdle-distribution.positions",
        "Incumbent and challenger position slices differ.",
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
        "gates": gates,
        "passes": all(gates.values()),
    }


def _require_scenarios(
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
        != HURDLE_SCENARIO_ARTIFACT_VERSION
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
            "hurdle-distribution.scenarios",
            "The fixed target-outcome-free opening scenario pair is "
            "unavailable.",
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
            "Evaluate direct-tree and appearance-hurdle historical opening "
            "distributions with proper scores and calibration."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = (
            build_historical_appearance_hurdle_opening_distribution_evaluation(
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
