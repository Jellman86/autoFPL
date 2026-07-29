from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

import numpy as np

from .current_multi_horizon_initial_squad import (
    _optimise_horizon,
    _week_matrices,
)
from .historical_opening_policy_data import (
    REGISTERED_SEASONS,
    OpeningFold,
    _load_opening_fold,
    _required_capture,
)
from .historical_opening_policy_evaluation import (
    _complete_roles,
    _score_actual,
    _select_roles,
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
from .transfer_aware_opening_squad import (
    OPTIMIZER_VERSION,
    TRANSFER_POLICY,
    optimise_transfer_aware_horizon,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-transfer-aware-opening-challenger-evaluation"
ARTIFACT_VERSION = (
    "historical-transfer-aware-opening-challenger-evaluation-v1"
)
STATUS = "retrospective-reused-holdout-challenger-screen"
HORIZON = 6
MINIMUM_MEAN_IMPROVEMENT_POINTS = 2.0
MINIMUM_TARGET_WINS = 2
MAXIMUM_WORST_TARGET_REGRESSION_POINTS = 2


def build_historical_transfer_aware_opening_evaluation(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    scenario = build_historical_opening_scenario_reconstruction(path)
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

    targets = [
        _evaluate_target(
            target,
            folds[str(target["targetSeasonCode"])],
        )
        for target in scenario["targets"]
    ]
    screen = _screen(targets)
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "targetOutcomesOpened": True,
        "incumbent": {
            "evaluationPolicyKey": "6-expected-points",
            "squadPolicy": "fixed-opening-squad-for-eight-gameweeks",
            "optimizerVersion": (
                "scipy-highs-multi-horizon-mean-cvar-v1"
            ),
        },
        "challenger": {
            "evaluationPolicyKey": "6-expected-points-transfer-aware-v1",
            "horizonGameweeks": HORIZON,
            "optimizerVersion": OPTIMIZER_VERSION,
            "transferPolicy": TRANSFER_POLICY,
            "pricePolicy": "constant-opening-prices",
            "chipPolicy": "no-chips",
            "hitPolicy": "no-point-cost-transfers",
            "postHorizonPolicy": (
                "hold-gameweek-6-squad-and-select-preseason-mean-roles"
            ),
        },
        "commonOutcomeScoring": {
            "outcomeGameweeks": list(range(1, 9)),
            "scorer": "cpu-joint-scenario-reference-v1",
            "captaincy": "exact-captain-fallback",
            "substitutions": (
                "ordered-formation-preserving-auto-substitution"
            ),
        },
        "targets": targets,
        "screen": screen,
        "prospectiveBoundary": {
            "isPromoted": False,
            "mayInfluenceAdvice": False,
            "mayReplaceSelectedPolicy": False,
            "mayEnterCurrentProspectiveComparison": bool(
                screen["passes"]
            ),
            "reason": (
                "historical-target-outcomes-were-already-opened-before-"
                "this-transfer-policy-was-specified"
            ),
        },
        "limitations": [
            (
                "The three target outcomes were reused after the fixed-squad "
                "policy comparison; this screen can reject or retain a "
                "prospective challenger but cannot select a new incumbent."
            ),
            (
                "The challenger permits at most one free transfer before "
                "each Gameweek from 2 through 6. It does not model banking "
                "up to five free transfers or four-point hits."
            ),
            (
                "All transfers are fixed from preseason information. The "
                "evaluation does not adapt to target-season injuries, form "
                "or price changes."
            ),
            (
                "Opening prices remain constant, Gameweek 6's squad is held "
                "for Gameweeks 7-8 and no chips are used."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "scenarioDataIdentitySha256": scenario[
                "dataIdentitySha256"
            ],
            "incumbent": artifact["incumbent"],
            "challenger": artifact["challenger"],
            "targets": targets,
            "screen": screen,
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _evaluate_target(
    target: Mapping[str, Any],
    fold: OpeningFold,
) -> Dict[str, Any]:
    season = str(target["targetSeasonCode"])
    _require(
        fold.target_capture.season_code == season
        and int(target["targetCaptureId"])
        == fold.target_capture.capture_id,
        "transfer-evaluation.target-alignment",
        "The outcome fold differs from the scenario target.",
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
    _require(
        [int(row["columnIndex"]) for row in candidates]
        == list(range(int(target["playerCount"])))
        and set(outcome_by_code)
        == {int(row["playerId"]) for row in candidates},
        "transfer-evaluation.player-alignment",
        "The outcome and scenario player cohorts differ.",
    )
    week_matrices = _week_matrices(target)
    incumbent, incumbent_solver = _optimise_horizon(
        candidates,
        week_matrices,
        HORIZON,
        0.0,
    )
    incumbent_complete = _complete_roles(
        incumbent,
        candidates,
        week_matrices,
        HORIZON,
    )
    incumbent_score = _score_actual(
        incumbent_complete,
        candidates,
        outcome_by_code,
    )
    challenger, challenger_solver = optimise_transfer_aware_horizon(
        candidates,
        week_matrices,
        HORIZON,
    )
    challenger_complete = _complete_challenger(
        challenger,
        candidates,
        week_matrices,
    )
    challenger_score = _score_challenger_actual(
        challenger_complete,
        candidates,
        outcome_by_code,
    )
    incumbent_ids = set(int(value) for value in incumbent["playerIds"])
    challenger_ids = set(
        int(value) for value in challenger["initialPlayerIds"]
    )
    return {
        "targetSeasonCode": season,
        "targetCaptureId": fold.target_capture.capture_id,
        "rawGameweeksSha256": fold.raw_gameweeks_sha256,
        "playerCount": len(candidates),
        "scenarioCount": int(target["scenarioCount"]),
        "scenarioContentSha256": target["scenarioContentSha256"],
        "incumbent": {
            "solver": incumbent_solver,
            "openingPlayerIds": incumbent["playerIds"],
            "openingBudgetTenths": incumbent["budgetTenths"],
            "realisedOutcome": incumbent_score,
        },
        "challenger": {
            "solver": challenger_solver,
            "openingPlayerIds": challenger["initialPlayerIds"],
            "openingBudgetTenths": challenger["initialBudgetTenths"],
            "plannedTransferCount": challenger[
                "plannedTransferCount"
            ],
            "gameweeks": [
                {
                    "gameweek": row["gameweek"],
                    "squadPlayerIds": row["squadPlayerIds"],
                    "budgetTenths": row["budgetTenths"],
                    "plannedTransfer": row["plannedTransfer"],
                }
                for row in challenger_complete["gameweeks"]
            ],
            "realisedOutcome": challenger_score,
        },
        "openingSquadOverlapCount": len(incumbent_ids & challenger_ids),
        "realisedPointDifference": (
            challenger_score["totalPoints"]
            - incumbent_score["totalPoints"]
        ),
    }


def _complete_challenger(
    plan: Mapping[str, Any],
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[tuple[np.ndarray, np.ndarray]],
) -> Dict[str, Any]:
    completed = {
        **plan,
        "gameweeks": [dict(row) for row in plan["gameweeks"]],
    }
    by_id = {int(row["playerId"]): row for row in candidates}
    candidate_index = {
        int(row["playerId"]): index
        for index, row in enumerate(candidates)
    }
    final_ids = tuple(
        int(value) for value in completed["gameweeks"][-1]["squadPlayerIds"]
    )
    for gameweek_index in range(HORIZON, 8):
        means = np.mean(week_matrices[gameweek_index][0], axis=0)
        mean_by_id = {
            player_id: float(means[candidate_index[player_id]])
            for player_id in final_ids
        }
        roles = _select_roles(
            final_ids,
            by_id,
            mean_by_id,
            gameweek_index + 1,
        )
        completed["gameweeks"].append(
            {
                **roles,
                "squadPlayerIds": sorted(final_ids),
                "budgetTenths": sum(
                    int(by_id[player_id]["priceTenths"])
                    for player_id in final_ids
                ),
                "plannedTransfer": None,
            }
        )
    _require(
        len(completed["gameweeks"]) == 8,
        "transfer-evaluation.role-coverage",
        "The challenger does not define all eight Gameweeks.",
    )
    return completed


def _score_challenger_actual(
    plan: Mapping[str, Any],
    candidates: Sequence[Mapping[str, Any]],
    outcome_by_code: Mapping[int, Any],
) -> Dict[str, Any]:
    by_id = {int(row["playerId"]): row for row in candidates}
    weekly = []
    total = 0
    for gameweek_index, roles in enumerate(plan["gameweeks"]):
        squad_ids = tuple(int(value) for value in roles["squadPlayerIds"])
        definition = SelectionDefinition.create(
            squad_ids,
            tuple(str(by_id[value]["position"]) for value in squad_ids),
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
        points_total = int(score.total_points[0])
        total += points_total
        weekly.append(
            {
                "gameweek": gameweek_index + 1,
                "points": points_total,
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
    return {"weekly": weekly, "totalPoints": total}


def _screen(targets: Sequence[Mapping[str, Any]]) -> Dict[str, Any]:
    differences = [
        int(target["realisedPointDifference"]) for target in targets
    ]
    _require(
        len(differences) == 3,
        "transfer-evaluation.target-count",
        "The transfer-aware screen requires three targets.",
    )
    mean = float(np.mean(differences))
    wins = sum(value > 0 for value in differences)
    worst = min(differences)
    gates = {
        "meanImprovement": mean >= MINIMUM_MEAN_IMPROVEMENT_POINTS,
        "targetWins": wins >= MINIMUM_TARGET_WINS,
        "worstTargetRegression": (
            worst >= -MAXIMUM_WORST_TARGET_REGRESSION_POINTS
        ),
    }
    return {
        "thresholds": {
            "minimumMeanImprovementPoints": (
                MINIMUM_MEAN_IMPROVEMENT_POINTS
            ),
            "minimumTargetWins": MINIMUM_TARGET_WINS,
            "maximumWorstTargetRegressionPoints": (
                MAXIMUM_WORST_TARGET_REGRESSION_POINTS
            ),
        },
        "targetDifferences": differences,
        "meanDifferencePoints": _round(mean),
        "targetWins": wins,
        "worstTargetDifferencePoints": worst,
        "gates": gates,
        "passes": all(gates.values()),
        "decision": (
            "retain-for-2026-27-prospective-shadow"
            if all(gates.values())
            else "do-not-retain"
        ),
    }


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Screen a fixed six-Gameweek one-free-transfer-per-week opening "
            "challenger against the retained fixed-squad policy."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_historical_transfer_aware_opening_evaluation(
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
