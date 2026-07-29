from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

from .current_multi_horizon_initial_squad import (
    ARTIFACT_VERSION as MULTI_SQUAD_ARTIFACT_VERSION,
    SELECTED_POLICY_KEY,
    STATUS as MULTI_SQUAD_STATUS,
    _build_candidates,
    _build_from_scenario as build_multi_squad_from_scenario,
    _score_horizon,
    _week_matrices,
)
from .current_multi_horizon_joint_scenarios import (
    build_current_multi_horizon_joint_scenarios,
)
from .historical_opening_policy_evaluation import _complete_roles
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-selected-opening-squad-shadow"
ARTIFACT_VERSION = "current-selected-opening-squad-shadow-v1"
BEST_SUPPORTED_ARTIFACT_VERSION = (
    "current-selected-opening-squad-shadow-v2"
)
STATUS = "prospective-shadow-unscored"
BEST_SUPPORTED_STATUS = (
    "best-supported-current-prospective-unscored"
)
OUTCOME_GAMEWEEKS = tuple(range(1, 9))


def build_current_selected_opening_squad(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    scenario = build_current_multi_horizon_joint_scenarios(path)
    return _build_from_scenario(path, scenario)


def _build_from_scenario(
    database_path: Path,
    scenario: Mapping[str, Any],
    *,
    artifact_version: str = ARTIFACT_VERSION,
    status: str = STATUS,
    influences_advice: bool = False,
    model_evaluation_source: Optional[Mapping[str, Any]] = None,
    additional_limitations: Sequence[str] = (),
) -> Dict[str, Any]:
    multi = build_multi_squad_from_scenario(
        Path(database_path),
        scenario,
    )
    _require_serving_boundary(
        artifact_version,
        status,
        influences_advice,
        model_evaluation_source,
    )
    _require_multi_squad(multi)
    selected = [
        policy
        for policy in multi["policies"]
        if bool(policy["isSelectedForProspectiveScoring"])
    ]
    if len(selected) != 1:
        raise TemporalRidgeError(
            "selected-opening.policy-count",
            "The current artifact does not select exactly one policy.",
        )
    policy = selected[0]
    if str(policy["evaluationPolicyKey"]) != SELECTED_POLICY_KEY:
        raise TemporalRidgeError(
            "selected-opening.policy-identity",
            "The current artifact selected an unsupported policy.",
        )
    candidates = _build_candidates(Path(database_path), scenario)
    week_matrices = _week_matrices(scenario)
    completed = _complete_roles(
        policy["selection"],
        candidates,
        week_matrices,
        int(policy["horizonGameweeks"]),
    )
    score = _score_horizon(
        completed,
        candidates,
        week_matrices,
        len(OUTCOME_GAMEWEEKS),
    )
    selected_policy = {
        "evaluationPolicyKey": policy["evaluationPolicyKey"],
        "horizonGameweeks": policy["horizonGameweeks"],
        "optimizerPolicyKey": policy["policyKey"],
        "objective": policy["objective"],
        "solver": policy["solver"],
        "retrospectiveEvaluationSource": multi[
            "selectedProspectivePolicy"
        ]["retrospectiveEvaluationSource"],
    }
    if model_evaluation_source is not None:
        _require_model_evaluation_source(model_evaluation_source)
        selected_policy["modelEvaluationSource"] = dict(
            model_evaluation_source
        )
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": artifact_version,
        "status": status,
        "isPromoted": False,
        "influencesAdvice": influences_advice,
        "seasonCode": multi["seasonCode"],
        "openingGameweek": multi["openingGameweek"],
        "deadlineUtc": multi["deadlineUtc"],
        "decisionCutoffUtc": multi["decisionCutoffUtc"],
        "officialCaptureId": multi["officialCaptureId"],
        "candidatePoolCount": multi["candidatePoolCount"],
        "scenarioCount": multi["scenarioCount"],
        "selectedPolicy": selected_policy,
        "selection": {
            "playerIds": completed["playerIds"],
            "budgetTenths": completed["budgetTenths"],
            "players": _enrich_selection_players(
                completed["players"],
                scenario,
            ),
            "gameweeks": completed["gameweeks"],
        },
        "prospectiveScoreRegistration": {
            "outcomeGameweeks": list(OUTCOME_GAMEWEEKS),
            "squadMembership": (
                "fixed-opening-squad-no-transfers-for-all-eight-gameweeks"
            ),
            "roles": (
                "all-eight-weeks-frozen-from-preseason-scenario-means"
            ),
            "realisedScorer": (
                "exact-fpl-captain-fallback-and-ordered-auto-substitution"
            ),
            "outcomeStatus": "waiting-for-official-2026-27-outcomes",
        },
        "preseasonScenarioScore": score,
        "source": {
            "artifactVersion": multi["artifactVersion"],
            "dataIdentitySha256": multi["dataIdentitySha256"],
            "runIdentitySha256": multi["runIdentitySha256"],
            "scenarioSource": multi["scenarioSource"],
        },
        "limitations": [
            (
                "This is the retrospectively selected policy applied to "
                "current preseason evidence. It is unscored prospectively "
                "and cannot influence served advice."
            ),
            (
                "All eight weekly role decisions are frozen now so later "
                "outcomes cannot change the evaluated policy."
            ),
            (
                "The squad is held without transfers, chips or price changes "
                "for the registered eight-Gameweek evaluation."
            ),
            *additional_limitations,
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "selectedPolicy": artifact["selectedPolicy"],
            "selection": artifact["selection"],
            "prospectiveScoreRegistration": artifact[
                "prospectiveScoreRegistration"
            ],
            "source": artifact["source"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _require_multi_squad(multi: Mapping[str, Any]) -> None:
    if (
        multi.get("artifactVersion") != MULTI_SQUAD_ARTIFACT_VERSION
        or multi.get("status") != MULTI_SQUAD_STATUS
        or bool(multi.get("isPromoted"))
        or bool(multi.get("influencesAdvice"))
        or multi.get("recommendedPolicyKey") != SELECTED_POLICY_KEY
    ):
        raise TemporalRidgeError(
            "selected-opening.multi-squad-source",
            "The current multi-horizon source is unsupported.",
        )


def _require_model_evaluation_source(
    source: Mapping[str, Any],
) -> None:
    if (
        set(source)
        != {
            "artifactVersion",
            "dataIdentitySha256",
            "runIdentitySha256",
        }
        or not all(
            isinstance(source[key], str) and source[key]
            for key in source
        )
    ):
        raise TemporalRidgeError(
            "selected-opening.model-evaluation-source",
            "The point-model evaluation identity is incomplete.",
        )


def _require_serving_boundary(
    artifact_version: str,
    status: str,
    influences_advice: bool,
    model_evaluation_source: Optional[Mapping[str, Any]],
) -> None:
    if artifact_version == BEST_SUPPORTED_ARTIFACT_VERSION:
        valid = (
            status == BEST_SUPPORTED_STATUS
            and influences_advice
            and model_evaluation_source is not None
        )
    else:
        valid = (
            artifact_version == ARTIFACT_VERSION
            and status == STATUS
            and not influences_advice
            and model_evaluation_source is None
        )
    if not valid:
        raise TemporalRidgeError(
            "selected-opening.serving-boundary",
            "The selected opening-squad version and serving state differ.",
        )


def _enrich_selection_players(
    players: Sequence[Mapping[str, Any]],
    scenario: Mapping[str, Any],
) -> list[Dict[str, Any]]:
    scenario_by_id = {
        int(player["playerId"]): player
        for player in scenario["players"]
    }
    enriched = []
    for player in players:
        player_id = int(player["playerId"])
        scenario_player = scenario_by_id[player_id]
        column_index = int(scenario_player["columnIndex"])
        weeks = scenario["weeks"]
        if [int(row["gameweek"]) for row in weeks[:6]] != list(
            range(1, 7)
        ):
            raise TemporalRidgeError(
                "selected-opening.player-horizon",
                "A selected player lacks the registered six-week horizon.",
            )
        point_means = [
            sum(float(row[column_index]) for row in week["pointRows"])
            / len(week["pointRows"])
            for week in weeks[:6]
        ]
        appearance_probabilities = [
            sum(
                1.0 if bool(row[column_index]) else 0.0
                for row in week["playedRows"]
            )
            / len(week["playedRows"])
            for week in weeks[:6]
        ]
        enriched.append(
            {
                **player,
                "modelExpectedPoints": _round(point_means[0]),
                "modelAppearanceProbability": _round(
                    appearance_probabilities[0]
                ),
                "modelSixGameweekExpectedPoints": _round(
                    sum(point_means)
                ),
            }
        )
    return enriched


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Freeze the retrospectively selected current opening squad and "
            "all eight preseason role decisions for prospective scoring."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_selected_opening_squad(options.database)
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
