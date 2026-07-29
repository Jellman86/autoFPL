from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

import numpy as np

from .current_multi_horizon_initial_squad import (
    HORIZONS,
    POLICIES,
    _optimise_horizon,
    _week_matrices,
)
from .historical_opening_policy_data import (
    REGISTERED_SEASONS,
    OpeningFold,
    _load_opening_fold,
    _required_capture,
)
from .historical_opening_policy_registration import (
    ARTIFACT_VERSION as REGISTRATION_ARTIFACT_VERSION,
    MAXIMUM_WORST_TARGET_REGRESSION_POINTS,
    MINIMUM_MEAN_IMPROVEMENT_POINTS,
    MINIMUM_TARGET_WINS,
    REFERENCE_POLICY_KEY,
    _build_from_scenario as build_registration_from_scenario,
    _policy_key,
)
from .historical_opening_scenario_reconstruction import (
    build_historical_opening_scenario_reconstruction,
)
from .scenario_reference import (
    SelectionDefinition,
    score_selection_scenarios,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-opening-policy-evaluation"
ARTIFACT_VERSION = "historical-opening-policy-evaluation-v1"
STATUS = "retrospective-policy-selected-prospective-gate-required"
EXPECTED_REGISTRATION_DATA_IDENTITY = (
    "b9d28cac497af35fc0762b7080db7e369759678872f78d47135e050f1920b675"
)


def build_historical_opening_policy_evaluation(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    scenario = build_historical_opening_scenario_reconstruction(path)
    registration = build_registration_from_scenario(scenario)
    _require_frozen_registration(registration)

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

    target_results = [
        _evaluate_target(
            target,
            folds[str(target["targetSeasonCode"])],
        )
        for target in scenario["targets"]
    ]
    policy_summary, decision = _select_policy(target_results)
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "targetOutcomesOpened": True,
        "registrationSource": {
            "artifactVersion": registration["artifactVersion"],
            "dataIdentitySha256": registration["dataIdentitySha256"],
            "runIdentitySha256": registration["runIdentitySha256"],
        },
        "commonOutcomeScoring": registration["commonOutcomeScoring"],
        "selectionRule": registration["selectionRule"],
        "targets": target_results,
        "policySummary": policy_summary,
        "decision": decision,
        "prospectiveBoundary": {
            "isPromoted": False,
            "mayInfluenceAdvice": False,
            "selectedPolicyFrozenForProspectiveScoring": True,
            "nextRequirement": (
                "score-the-selected-policy-on-new-2026-27-outcomes-"
                "without-reselection"
            ),
        },
        "limitations": [
            (
                "Only three expanding-origin target seasons are available. "
                "The registered stability rule therefore prefers the neutral "
                "reference unless a retrospective leader is materially and "
                "consistently better."
            ),
            (
                "The comparison holds each opening squad for eight "
                "Gameweeks. It evaluates opening construction, not a transfer "
                "policy or chip strategy."
            ),
            (
                "Historical opening status is unavailable and target fixture "
                "structure is the registered final-archive proxy."
            ),
            (
                "This retrospective selection cannot influence served "
                "advice until its frozen policy passes prospective scoring."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "registrationSource": artifact["registrationSource"],
            "targets": artifact["targets"],
            "policySummary": artifact["policySummary"],
            "decision": artifact["decision"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _evaluate_target(
    target: Mapping[str, Any],
    fold: OpeningFold,
) -> Dict[str, Any]:
    season = str(target["targetSeasonCode"])
    if (
        fold.target_capture.season_code != season
        or int(target["targetCaptureId"])
        != fold.target_capture.capture_id
    ):
        raise TemporalRidgeError(
            "policy-evaluation.target-alignment",
            "The outcome fold differs from the registered scenario target.",
        )
    candidates = [
        {
            "playerId": int(player["playerCode"]),
            "columnIndex": int(player["columnIndex"]),
            "webName": str(player["webName"]),
            "teamId": int(player["teamId"]),
            "teamName": str(player["teamName"]),
            "position": str(player["position"]),
            "priceTenths": int(player["priceTenths"]),
        }
        for player in target["players"]
    ]
    candidates.sort(key=lambda row: int(row["columnIndex"]))
    outcome_by_code = {
        player.player_code: player for player in fold.players
    }
    if (
        [int(row["columnIndex"]) for row in candidates]
        != list(range(int(target["playerCount"])))
        or set(outcome_by_code)
        != {int(row["playerId"]) for row in candidates}
    ):
        raise TemporalRidgeError(
            "policy-evaluation.player-alignment",
            "The outcome and scenario player cohorts differ.",
        )

    week_matrices = _week_matrices(target)
    policies = []
    for horizon in HORIZONS:
        for policy in POLICIES:
            selection, solver = _optimise_horizon(
                candidates,
                week_matrices,
                horizon,
                float(policy["cvarWeight"]),
            )
            completed = _complete_roles(
                selection,
                candidates,
                week_matrices,
                horizon,
            )
            actual = _score_actual(
                completed,
                candidates,
                outcome_by_code,
            )
            policies.append(
                {
                    "evaluationPolicyKey": _policy_key(
                        horizon,
                        str(policy["policyKey"]),
                    ),
                    "horizonGameweeks": horizon,
                    "optimizerPolicyKey": policy["policyKey"],
                    "solver": solver,
                    "selection": {
                        "playerIds": completed["playerIds"],
                        "budgetTenths": completed["budgetTenths"],
                        "gameweeks": completed["gameweeks"],
                    },
                    "realisedOutcome": actual,
                }
            )
    return {
        "targetSeasonCode": season,
        "targetCaptureId": fold.target_capture.capture_id,
        "rawGameweeksSha256": fold.raw_gameweeks_sha256,
        "playerCount": len(candidates),
        "scenarioCount": int(target["scenarioCount"]),
        "scenarioContentSha256": target["scenarioContentSha256"],
        "policies": policies,
    }


def _complete_roles(
    selection: Mapping[str, Any],
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
    horizon: int,
) -> Dict[str, Any]:
    completed = {
        **selection,
        "gameweeks": [dict(row) for row in selection["gameweeks"]],
    }
    squad_ids = tuple(int(value) for value in selection["playerIds"])
    by_id = {int(row["playerId"]): row for row in candidates}
    candidate_index = {
        int(row["playerId"]): index
        for index, row in enumerate(candidates)
    }
    for gameweek_index in range(horizon, 8):
        means = np.mean(week_matrices[gameweek_index][0], axis=0)
        mean_by_id = {
            player_id: float(means[candidate_index[player_id]])
            for player_id in squad_ids
        }
        completed["gameweeks"].append(
            _select_roles(
                squad_ids,
                by_id,
                mean_by_id,
                gameweek_index + 1,
            )
        )
    if len(completed["gameweeks"]) != 8:
        raise TemporalRidgeError(
            "policy-evaluation.role-coverage",
            "A policy does not define roles for all eight Gameweeks.",
        )
    return completed


def _select_roles(
    squad_ids: Sequence[int],
    by_id: Mapping[int, Mapping[str, Any]],
    mean_by_id: Mapping[int, float],
    gameweek: int,
) -> Dict[str, Any]:
    by_position = {
        position: sorted(
            (
                player_id
                for player_id in squad_ids
                if str(by_id[player_id]["position"]) == position
            ),
            key=lambda player_id: (-mean_by_id[player_id], player_id),
        )
        for position in (
            "goalkeeper",
            "defender",
            "midfielder",
            "forward",
        )
    }
    best: Optional[Tuple[float, Tuple[int, ...], int, List[int]]] = None
    for defender_count in range(3, 6):
        for midfielder_count in range(2, 6):
            forward_count = 10 - defender_count - midfielder_count
            if not 1 <= forward_count <= 3:
                continue
            starters = (
                by_position["goalkeeper"][:1]
                + by_position["defender"][:defender_count]
                + by_position["midfielder"][:midfielder_count]
                + by_position["forward"][:forward_count]
            )
            captain_id = min(
                starters,
                key=lambda player_id: (
                    -mean_by_id[player_id],
                    player_id,
                ),
            )
            objective = sum(mean_by_id[value] for value in starters)
            objective += mean_by_id[captain_id]
            signature = tuple(sorted(starters))
            candidate = (objective, signature, captain_id, starters)
            if (
                best is None
                or objective > best[0] + 1e-12
                or (
                    abs(objective - best[0]) <= 1e-12
                    and (signature, captain_id) < (best[1], best[2])
                )
            ):
                best = candidate
    if best is None:
        raise TemporalRidgeError(
            "policy-evaluation.legal-lineup",
            "The selected squad has no legal starting formation.",
        )
    _, _, captain_id, starters = best
    starting_ids = set(starters)
    vice_id = min(
        (value for value in starters if value != captain_id),
        key=lambda player_id: (-mean_by_id[player_id], player_id),
    )
    bench_gk = next(
        value
        for value in by_position["goalkeeper"]
        if value not in starting_ids
    )
    outfield_bench = sorted(
        (
            value
            for value in squad_ids
            if value not in starting_ids
            and str(by_id[value]["position"]) != "goalkeeper"
        ),
        key=lambda player_id: (-mean_by_id[player_id], player_id),
    )
    return {
        "gameweek": gameweek,
        "startingPlayerIds": sorted(starters),
        "captainPlayerId": captain_id,
        "viceCaptainPlayerId": vice_id,
        "replacementGoalkeeperPlayerId": bench_gk,
        "outfieldSubstitutePlayerIds": outfield_bench,
    }


def _score_actual(
    selection: Mapping[str, Any],
    candidates: Sequence[Mapping[str, Any]],
    outcome_by_code: Mapping[int, Any],
) -> Dict[str, Any]:
    by_id = {int(row["playerId"]): row for row in candidates}
    squad_ids = tuple(int(value) for value in selection["playerIds"])
    positions = tuple(str(by_id[value]["position"]) for value in squad_ids)
    weekly = []
    total = 0
    for gameweek_index, roles in enumerate(selection["gameweeks"]):
        definition = SelectionDefinition.create(
            squad_ids,
            positions,
            roles["startingPlayerIds"],
            roles["replacementGoalkeeperPlayerId"],
            roles["outfieldSubstitutePlayerIds"],
            roles["captainPlayerId"],
            roles["viceCaptainPlayerId"],
        )
        points = np.asarray(
            [
                [
                    int(outcome_by_code[player_id].points[gameweek_index])
                    for player_id in squad_ids
                ]
            ],
            dtype=np.int64,
        )
        played = np.asarray(
            [
                [
                    int(outcome_by_code[player_id].minutes[gameweek_index]) > 0
                    for player_id in squad_ids
                ]
            ],
            dtype=np.bool_,
        )
        score = score_selection_scenarios(definition, points, played)
        gameweek_points = int(score.total_points[0])
        total += gameweek_points
        weekly.append(
            {
                "gameweek": gameweek_index + 1,
                "points": gameweek_points,
                "captainBonusPoints": int(
                    score.captain_bonus_points[0]
                ),
                "activatedSubstituteCount": int(
                    score.activated_substitute_count[0]
                ),
                "unreplacedStarterCount": int(
                    score.unreplaced_starter_count[0]
                ),
            }
        )
    return {
        "weekly": weekly,
        "totalPoints": total,
    }


def _select_policy(
    target_results: Sequence[Mapping[str, Any]],
) -> Tuple[List[Dict[str, Any]], Dict[str, Any]]:
    scores: Dict[str, List[int]] = {}
    metadata: Dict[str, Tuple[int, str]] = {}
    seasons = [str(target["targetSeasonCode"]) for target in target_results]
    for target in target_results:
        for policy in target["policies"]:
            key = str(policy["evaluationPolicyKey"])
            scores.setdefault(key, []).append(
                int(policy["realisedOutcome"]["totalPoints"])
            )
            metadata[key] = (
                int(policy["horizonGameweeks"]),
                str(policy["optimizerPolicyKey"]),
            )
    if (
        set(scores)
        != {
            _policy_key(horizon, str(policy["policyKey"]))
            for horizon in HORIZONS
            for policy in POLICIES
        }
        or any(len(values) != len(seasons) for values in scores.values())
    ):
        raise TemporalRidgeError(
            "policy-evaluation.policy-coverage",
            "The policy score matrix is incomplete.",
        )
    reference = scores[REFERENCE_POLICY_KEY]
    summaries = []
    for key, values in sorted(scores.items()):
        differences = [
            value - reference[index]
            for index, value in enumerate(values)
        ]
        horizon, optimizer_policy = metadata[key]
        summaries.append(
            {
                "evaluationPolicyKey": key,
                "horizonGameweeks": horizon,
                "optimizerPolicyKey": optimizer_policy,
                "targetScores": [
                    {
                        "targetSeasonCode": season,
                        "totalPoints": values[index],
                        "differenceFromReferencePoints": differences[index],
                    }
                    for index, season in enumerate(seasons)
                ],
                "meanPoints": _round(float(np.mean(values))),
                "worstTargetPoints": min(values),
                "meanDifferenceFromReferencePoints": _round(
                    float(np.mean(differences))
                ),
                "targetWinsOverReference": sum(
                    value > 0 for value in differences
                ),
                "worstTargetDifferenceFromReferencePoints": min(
                    differences
                ),
            }
        )
    leader = min(
        summaries,
        key=lambda row: (
            -float(row["meanPoints"]),
            -int(row["worstTargetPoints"]),
            str(row["optimizerPolicyKey"]) != "expected-points",
            int(row["horizonGameweeks"]),
            str(row["evaluationPolicyKey"]),
        ),
    )
    gates = {
        "meanImprovement": (
            float(leader["meanDifferenceFromReferencePoints"])
            >= MINIMUM_MEAN_IMPROVEMENT_POINTS
        ),
        "targetWins": (
            int(leader["targetWinsOverReference"]) >= MINIMUM_TARGET_WINS
        ),
        "worstTargetRegression": (
            int(leader["worstTargetDifferenceFromReferencePoints"])
            >= -MAXIMUM_WORST_TARGET_REGRESSION_POINTS
        ),
    }
    selected_key = (
        str(leader["evaluationPolicyKey"])
        if str(leader["evaluationPolicyKey"]) == REFERENCE_POLICY_KEY
        or all(gates.values())
        else REFERENCE_POLICY_KEY
    )
    return summaries, {
        "rankedLeaderPolicyKey": leader["evaluationPolicyKey"],
        "referencePolicyKey": REFERENCE_POLICY_KEY,
        "stabilityGates": gates,
        "selectedPolicyKey": selected_key,
        "referenceRetained": selected_key == REFERENCE_POLICY_KEY,
        "decisionReason": (
            "ranked-leader-is-reference"
            if leader["evaluationPolicyKey"] == REFERENCE_POLICY_KEY
            else (
                "ranked-leader-passed-all-registered-stability-gates"
                if all(gates.values())
                else "ranked-leader-failed-registered-stability-gate"
            )
        ),
    }


def _require_frozen_registration(
    registration: Mapping[str, Any],
) -> None:
    if (
        registration.get("artifactVersion")
        != REGISTRATION_ARTIFACT_VERSION
        or registration.get("targetOutcomesOpened") is not False
        or registration.get("dataIdentitySha256")
        != EXPECTED_REGISTRATION_DATA_IDENTITY
    ):
        raise TemporalRidgeError(
            "policy-evaluation.registration-identity",
            "The reconstructed registration differs from the frozen result.",
        )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate the frozen historical opening-squad policies."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_historical_opening_policy_evaluation(
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
