from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

import numpy as np
import scipy
from scipy.optimize import Bounds, LinearConstraint, milp
from scipy.sparse import lil_matrix

from .current_initial_squad_candidate import (
    POSITION_QUOTAS,
    STARTER_BOUNDS,
)
from .current_multi_horizon_joint_scenarios import (
    ARTIFACT_VERSION as SCENARIO_ARTIFACT_VERSION,
    STATUS as SCENARIO_STATUS,
    build_current_multi_horizon_joint_scenarios,
)
from .scenario_reference import (
    SelectionDefinition,
    score_selection_scenarios,
    summarise_scores,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-multi-horizon-initial-squad-shadow"
ARTIFACT_VERSION = "current-multi-horizon-initial-squad-shadow-v1"
STATUS = "prospective-shadow-unscored"
OPTIMIZER_VERSION = "scipy-highs-multi-horizon-mean-cvar-v1"
MAXIMUM_NUMERICAL_MIP_GAP = 1e-12
BENCH_WEIGHT = 0.08
LOWER_TAIL_FRACTION = 0.20
POLICIES = (
    {
        "policyKey": "expected-points",
        "cvarWeight": 0.0,
    },
    {
        "policyKey": "downside-balanced",
        "cvarWeight": 0.15,
    },
)
HORIZONS = (3, 6, 8)


def build_current_multi_horizon_initial_squad(
    database_path: Path,
) -> Dict[str, Any]:
    scenario = build_current_multi_horizon_joint_scenarios(
        Path(database_path)
    )
    return _build_from_scenario(Path(database_path), scenario)


def _build_from_scenario(
    database_path: Path,
    scenario: Mapping[str, Any],
) -> Dict[str, Any]:
    _require_scenario(scenario)
    official = _load_official(
        Path(database_path),
        int(scenario["officialCaptureId"]),
    )
    players = scenario["players"]
    candidates = []
    for player in players:
        player_id = int(player["playerId"])
        current = official.get(player_id)
        _require(
            current is not None,
            "multi-squad.official-player",
            "A scenario player is missing from the exact official capture.",
        )
        _require(
            int(current["teamId"]) == int(player["teamId"])
            and str(current["position"]) == str(player["position"]),
            "multi-squad.official-identity",
            "A scenario player differs from the exact official capture.",
        )
        _require(
            str(current["status"]) != "u",
            "multi-squad.ineligible-player",
            "The scenario cohort contains an officially unavailable player.",
        )
        candidates.append(
            {
                "playerId": player_id,
                "columnIndex": int(player["columnIndex"]),
                "webName": str(player["webName"]),
                "teamId": int(player["teamId"]),
                "teamName": str(player["teamName"]),
                "position": str(player["position"]),
                "priceTenths": int(current["priceTenths"]),
                "officialStatus": str(current["status"]),
                "officialChanceOfPlayingNextRound": current[
                    "chanceNextRound"
                ],
            }
        )
    candidates.sort(key=lambda player: int(player["columnIndex"]))
    _require(
        [int(player["columnIndex"]) for player in candidates]
        == list(range(int(scenario["playerCount"]))),
        "multi-squad.player-column-order",
        "The scenario player columns are incomplete or out of order.",
    )
    _require(
        len(candidates) >= 15,
        "multi-squad.candidate-pool",
        "At least 15 exact-capture candidates are required.",
    )

    week_matrices = _week_matrices(scenario)
    policy_results = []
    for horizon in HORIZONS:
        for policy in POLICIES:
            selection, solver = _optimise_horizon(
                candidates,
                week_matrices,
                horizon,
                float(policy["cvarWeight"]),
            )
            scored = _score_horizon(
                selection,
                candidates,
                week_matrices,
                horizon,
            )
            policy_results.append(
                {
                    "horizonGameweeks": horizon,
                    "policyKey": policy["policyKey"],
                    "objective": {
                        "surrogate": (
                            "weekly-points-bench-weighted-with-captain"
                        ),
                        "benchWeight": BENCH_WEIGHT,
                        "expectedScoreWeight": 1.0,
                        "lowerTailFraction": LOWER_TAIL_FRACTION,
                        "lowerTailCvarWeight": policy["cvarWeight"],
                    },
                    "solver": solver,
                    "selection": selection,
                    "exactScenarioScore": scored,
                }
            )

    expected_by_horizon = {
        int(result["horizonGameweeks"]): result
        for result in policy_results
        if result["policyKey"] == "expected-points"
    }
    for result in policy_results:
        incumbent = expected_by_horizon[int(result["horizonGameweeks"])]
        candidate_scores = np.asarray(
            result["exactScenarioScore"]["pathTotalPoints"],
            dtype=float,
        )
        incumbent_scores = np.asarray(
            incumbent["exactScenarioScore"]["pathTotalPoints"],
            dtype=float,
        )
        result["versusExpectedPolicy"] = {
            "meanPointDifference": _round(
                float(np.mean(candidate_scores - incumbent_scores))
            ),
            "winProbability": _round(
                float(np.mean(candidate_scores > incumbent_scores))
            ),
            "tieProbability": _round(
                float(np.mean(candidate_scores == incumbent_scores))
            ),
            "lowerTailCvarDifference": _round(
                _lower_tail_cvar(candidate_scores)
                - _lower_tail_cvar(incumbent_scores)
            ),
        }

    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "seasonCode": scenario["seasonCode"],
        "openingGameweek": scenario["openingGameweek"],
        "deadlineUtc": scenario["deadlineUtc"],
        "decisionCutoffUtc": scenario["decisionCutoffUtc"],
        "officialCaptureId": scenario["officialCaptureId"],
        "scenarioCount": scenario["scenarioCount"],
        "candidatePoolCount": len(candidates),
        "registeredHorizons": list(HORIZONS),
        "registeredPolicies": [dict(policy) for policy in POLICIES],
        "scenarioSource": {
            "artifactVersion": scenario["artifactVersion"],
            "scenarioContentSha256": scenario["scenarioContentSha256"],
            "runIdentitySha256": scenario["runIdentitySha256"],
        },
        "policies": policy_results,
        "policySelectionStatus": (
            "awaiting-retrospective-horizon-and-risk-policy-evaluation"
        ),
        "recommendedPolicyKey": None,
        "limitations": [
            (
                "Every solver result is a global optimum for its declared "
                "linear mean/CVaR surrogate; exact auto-substitution and "
                "captaincy are scored afterwards on the paired paths."
            ),
            (
                "The horizon and CVaR weight are registered candidates, not "
                "selected using this current prospective artifact."
            ),
            (
                "Transfers, chips and future price changes are excluded from "
                "this opening-squad comparison."
            ),
            (
                "No policy can influence served advice until retrospective "
                "selection and prospective outcome gates pass."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "scenarioSource": artifact["scenarioSource"],
            "optimizerVersion": OPTIMIZER_VERSION,
            "benchWeight": BENCH_WEIGHT,
            "lowerTailFraction": LOWER_TAIL_FRACTION,
            "registeredPolicies": artifact["registeredPolicies"],
            "registeredHorizons": artifact["registeredHorizons"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _optimise_horizon(
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
    horizon: int,
    cvar_weight: float,
) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    _require(
        horizon in HORIZONS and len(week_matrices) >= horizon,
        "multi-squad.horizon",
        "The optimiser horizon is not registered or available.",
    )
    count = len(candidates)
    scenario_count = week_matrices[0][0].shape[0]
    binary_count = count * (1 + 2 * horizon)
    has_cvar = cvar_weight > 0.0
    eta_index = binary_count if has_cvar else None
    shortfall_start = binary_count + 1 if has_cvar else None
    variable_count = (
        binary_count + 1 + scenario_count if has_cvar else binary_count
    )
    objective = np.zeros(variable_count, dtype=np.float64)
    path_coefficients = np.zeros(
        (scenario_count, binary_count),
        dtype=np.float64,
    )
    for gameweek_index in range(horizon):
        points = week_matrices[gameweek_index][0]
        means = np.mean(points, axis=0)
        squad_slice = slice(0, count)
        starter_start = count + gameweek_index * count
        captain_start = count + horizon * count + gameweek_index * count
        objective[squad_slice] -= BENCH_WEIGHT * means
        objective[
            starter_start : starter_start + count
        ] -= (1.0 - BENCH_WEIGHT) * means
        objective[
            captain_start : captain_start + count
        ] -= means
        path_coefficients[:, squad_slice] += BENCH_WEIGHT * points
        path_coefficients[
            :, starter_start : starter_start + count
        ] += (1.0 - BENCH_WEIGHT) * points
        path_coefficients[
            :, captain_start : captain_start + count
        ] += points
    if has_cvar:
        assert eta_index is not None
        assert shortfall_start is not None
        objective[eta_index] = -cvar_weight
        objective[
            shortfall_start : shortfall_start + scenario_count
        ] = cvar_weight / (LOWER_TAIL_FRACTION * scenario_count)

    rows: List[Dict[int, float]] = []
    lower: List[float] = []
    upper: List[float] = []

    def add(
        values: Mapping[int, float],
        minimum: float,
        maximum: float,
    ) -> None:
        rows.append(dict(values))
        lower.append(minimum)
        upper.append(maximum)

    add({index: 1.0 for index in range(count)}, 15, 15)
    for position, quota in POSITION_QUOTAS.items():
        add(
            {
                index: 1.0
                for index, player in enumerate(candidates)
                if player["position"] == position
            },
            quota,
            quota,
        )
    add(
        {
            index: float(player["priceTenths"])
            for index, player in enumerate(candidates)
        },
        0,
        1000,
    )
    for team_id in sorted({int(row["teamId"]) for row in candidates}):
        add(
            {
                index: 1.0
                for index, player in enumerate(candidates)
                if int(player["teamId"]) == team_id
            },
            0,
            3,
        )

    for gameweek_index in range(horizon):
        starter_start = count + gameweek_index * count
        captain_start = count + horizon * count + gameweek_index * count
        add(
            {starter_start + index: 1.0 for index in range(count)},
            11,
            11,
        )
        for position, (minimum, maximum) in STARTER_BOUNDS.items():
            add(
                {
                    starter_start + index: 1.0
                    for index, player in enumerate(candidates)
                    if player["position"] == position
                },
                minimum,
                maximum,
            )
        add(
            {captain_start + index: 1.0 for index in range(count)},
            1,
            1,
        )
        for index in range(count):
            add(
                {index: -1.0, starter_start + index: 1.0},
                -np.inf,
                0,
            )
            add(
                {
                    starter_start + index: -1.0,
                    captain_start + index: 1.0,
                },
                -np.inf,
                0,
            )

    if has_cvar:
        assert eta_index is not None
        assert shortfall_start is not None
        for path_index in range(scenario_count):
            values = {
                index: -float(value)
                for index, value in enumerate(
                    path_coefficients[path_index]
                )
                if value != 0.0
            }
            values[eta_index] = 1.0
            values[shortfall_start + path_index] = -1.0
            add(values, -np.inf, 0.0)

    matrix = lil_matrix((len(rows), variable_count), dtype=np.float64)
    for row_index, values in enumerate(rows):
        for column_index, value in values.items():
            matrix[row_index, column_index] = value
    integrality = np.zeros(variable_count, dtype=np.int8)
    integrality[:binary_count] = 1
    bounds_lower = np.zeros(variable_count, dtype=np.float64)
    bounds_upper = np.full(variable_count, np.inf, dtype=np.float64)
    bounds_upper[:binary_count] = 1.0
    if has_cvar:
        assert eta_index is not None
        bounds_lower[eta_index] = -np.inf

    result = milp(
        c=objective,
        integrality=integrality,
        bounds=Bounds(bounds_lower, bounds_upper),
        constraints=LinearConstraint(
            matrix.tocsc(),
            np.asarray(lower),
            np.asarray(upper),
        ),
        options={
            "presolve": True,
            "time_limit": 120.0,
            "mip_rel_gap": 0.0,
        },
    )
    _require(
        result.success
        and result.status == 0
        and result.x is not None
        and result.mip_gap is not None
        and 0.0 <= float(result.mip_gap) <= MAXIMUM_NUMERICAL_MIP_GAP,
        "multi-squad.optimizer",
        (
            "The global multi-horizon surrogate did not reach an exact "
            f"optimum (status={result.status}, success={result.success}, "
            f"mipGap={result.mip_gap}, message={result.message})."
        ),
    )
    values = np.rint(result.x[:binary_count]).astype(np.int8)
    squad_indices = [
        index for index in range(count) if values[index] == 1
    ]
    by_id = {int(player["playerId"]): player for player in candidates}
    squad_ids = [int(candidates[index]["playerId"]) for index in squad_indices]
    gameweeks = []
    for gameweek_index in range(horizon):
        starter_start = count + gameweek_index * count
        captain_start = count + horizon * count + gameweek_index * count
        starting_ids = [
            int(candidates[index]["playerId"])
            for index in range(count)
            if values[starter_start + index] == 1
        ]
        captain_id = next(
            int(candidates[index]["playerId"])
            for index in range(count)
            if values[captain_start + index] == 1
        )
        means = np.mean(week_matrices[gameweek_index][0], axis=0)
        mean_by_id = {
            int(player["playerId"]): float(means[index])
            for index, player in enumerate(candidates)
        }
        vice_id = max(
            (player_id for player_id in starting_ids if player_id != captain_id),
            key=lambda player_id: (mean_by_id[player_id], -player_id),
        )
        bench_gk = next(
            player_id
            for player_id in squad_ids
            if player_id not in starting_ids
            and by_id[player_id]["position"] == "goalkeeper"
        )
        outfield_bench = sorted(
            (
                player_id
                for player_id in squad_ids
                if player_id not in starting_ids
                and by_id[player_id]["position"] != "goalkeeper"
            ),
            key=lambda player_id: (-mean_by_id[player_id], player_id),
        )
        gameweeks.append(
            {
                "gameweek": gameweek_index + 1,
                "startingPlayerIds": sorted(starting_ids),
                "captainPlayerId": captain_id,
                "viceCaptainPlayerId": vice_id,
                "replacementGoalkeeperPlayerId": bench_gk,
                "outfieldSubstitutePlayerIds": outfield_bench,
            }
        )
    return (
        {
            "playerIds": sorted(squad_ids),
            "budgetTenths": sum(
                int(by_id[player_id]["priceTenths"])
                for player_id in squad_ids
            ),
            "players": [
                dict(by_id[player_id]) for player_id in sorted(squad_ids)
            ],
            "gameweeks": gameweeks,
        },
        {
            "optimizerVersion": OPTIMIZER_VERSION,
            "solver": "scipy.optimize.milp-highs",
            "scipyVersion": str(scipy.__version__),
            "status": "global-linear-mean-cvar-surrogate-optimum",
            "mipGap": (
                0.0
                if float(result.mip_gap) <= MAXIMUM_NUMERICAL_MIP_GAP
                else float(result.mip_gap)
            ),
            "reportedMipGap": float(result.mip_gap),
            "maximumNumericalMipGap": MAXIMUM_NUMERICAL_MIP_GAP,
            "objectiveValue": _round(float(-result.fun)),
            "binaryVariableCount": binary_count,
            "continuousVariableCount": variable_count - binary_count,
        },
    )


def _score_horizon(
    selection: Mapping[str, Any],
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
    horizon: int,
) -> Dict[str, Any]:
    by_id = {int(player["playerId"]): player for player in candidates}
    candidate_index = {
        int(player["playerId"]): index
        for index, player in enumerate(candidates)
    }
    squad_ids = tuple(int(value) for value in selection["playerIds"])
    positions = tuple(str(by_id[value]["position"]) for value in squad_ids)
    cumulative = np.zeros(week_matrices[0][0].shape[0], dtype=np.int64)
    weekly = []
    for gameweek_index in range(horizon):
        roles = selection["gameweeks"][gameweek_index]
        definition = SelectionDefinition.create(
            squad_ids,
            positions,
            roles["startingPlayerIds"],
            roles["replacementGoalkeeperPlayerId"],
            roles["outfieldSubstitutePlayerIds"],
            roles["captainPlayerId"],
            roles["viceCaptainPlayerId"],
        )
        columns = [candidate_index[player_id] for player_id in squad_ids]
        points, played = week_matrices[gameweek_index]
        scores = score_selection_scenarios(
            definition,
            points[:, columns],
            played[:, columns],
        )
        cumulative += scores.total_points
        weekly.append(
            {
                "gameweek": gameweek_index + 1,
                **summarise_scores(scores),
            }
        )
    return {
        "weekly": weekly,
        "cumulative": _score_vector_summary(cumulative),
        "lowerTailFraction": LOWER_TAIL_FRACTION,
        "lowerTailCvarPoints": _round(_lower_tail_cvar(cumulative)),
        "pathTotalPoints": cumulative.tolist(),
    }


def _week_matrices(
    scenario: Mapping[str, Any],
) -> List[Tuple[np.ndarray, np.ndarray]]:
    player_count = int(scenario["playerCount"])
    scenario_count = int(scenario["scenarioCount"])
    result = []
    for expected_gameweek, week in enumerate(scenario["weeks"], start=1):
        points = np.asarray(week["pointRows"], dtype=np.int64)
        played = np.asarray(week["playedRows"], dtype=np.bool_)
        _require(
            int(week["gameweek"]) == expected_gameweek
            and points.shape == (scenario_count, player_count)
            and played.shape == points.shape
            and not np.any((points != 0) & ~played),
            "multi-squad.scenario-matrix",
            "A weekly scenario matrix is incomplete or incoherent.",
        )
        result.append((points, played))
    return result


def _load_official(
    database_path: Path,
    capture_id: int,
) -> Dict[int, Dict[str, Any]]:
    try:
        connection = sqlite3.connect(
            f"file:{database_path}?mode=ro",
            uri=True,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        latest_capture_id = connection.execute(
            "SELECT MAX(capture_id) FROM official_fpl_players;"
        ).fetchone()[0]
        if latest_capture_id is None or int(latest_capture_id) != capture_id:
            raise TemporalRidgeError(
                "multi-squad.stale-scenario",
                "The scenario does not match the latest official capture.",
            )
        rows = connection.execute(
            """
            SELECT player_id, team_id, position, price_tenths,
                   status, chance_next_round
            FROM official_fpl_players
            WHERE capture_id = ?;
            """,
            (capture_id,),
        ).fetchall()
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "multi-squad.official-read",
            "The exact official player capture could not be read.",
        ) from exception
    finally:
        if "connection" in locals():
            connection.close()
    return {
        int(row["player_id"]): {
            "teamId": int(row["team_id"]),
            "position": str(row["position"]),
            "priceTenths": int(row["price_tenths"]),
            "status": str(row["status"]),
            "chanceNextRound": row["chance_next_round"],
        }
        for row in rows
    }


def _require_scenario(scenario: Mapping[str, Any]) -> None:
    _require(
        scenario.get("status") == SCENARIO_STATUS
        and scenario.get("artifactVersion") == SCENARIO_ARTIFACT_VERSION
        and not bool(scenario.get("influencesAdvice"))
        and list(scenario.get("targetGameweeks", [])) == list(range(1, 9))
        and list(scenario.get("decisionHorizons", [])) == list(HORIZONS)
        and len(scenario.get("weeks", [])) == 8
        and isinstance(scenario.get("players"), list)
        and len(scenario["players"]) == int(scenario.get("playerCount", -1)),
        "multi-squad.scenario-source",
        "The scenario source is not the fixed eight-Gameweek shadow.",
    )


def _score_vector_summary(values: np.ndarray) -> Dict[str, Any]:
    return {
        "scenarioCount": int(values.size),
        "meanPoints": _round(float(np.mean(values))),
        "standardDeviationPoints": _round(float(np.std(values))),
        "minimumPoints": int(np.min(values)),
        "p10Points": _round(float(np.quantile(values, 0.10))),
        "medianPoints": _round(float(np.median(values))),
        "p90Points": _round(float(np.quantile(values, 0.90))),
        "maximumPoints": int(np.max(values)),
    }


def _lower_tail_cvar(values: np.ndarray) -> float:
    ordered = np.sort(np.asarray(values, dtype=float))
    tail_mass = LOWER_TAIL_FRACTION * len(ordered)
    full = int(np.floor(tail_mass))
    fraction = tail_mass - full
    total = float(np.sum(ordered[:full]))
    if fraction > 0.0:
        total += fraction * float(ordered[full])
    return total / tail_mass


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Solve registered mean and lower-tail opening-squad policies "
            "over the current 3/6/8-Gameweek scenario paths."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_multi_horizon_initial_squad(
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
