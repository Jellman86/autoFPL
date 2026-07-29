from __future__ import annotations

from typing import Any, Dict, List, Mapping, Sequence, Tuple

import numpy as np
import scipy
from scipy.optimize import Bounds, LinearConstraint, milp
from scipy.sparse import lil_matrix

from .current_initial_squad_candidate import (
    POSITION_QUOTAS,
    STARTER_BOUNDS,
)
from .current_multi_horizon_initial_squad import (
    BENCH_WEIGHT,
    MAXIMUM_NUMERICAL_MIP_GAP,
    _score_vector_summary,
)
from .scenario_reference import (
    SelectionDefinition,
    score_selection_scenarios,
    summarise_scores,
)
from .temporal_ridge import TemporalRidgeError, _round

OPTIMIZER_VERSION = "scipy-highs-transfer-aware-opening-v1"
TRANSFER_POLICY = "at-most-one-free-transfer-before-each-later-gameweek"


def optimise_transfer_aware_horizon(
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
    horizon: int,
) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    _require(
        2 <= horizon <= len(week_matrices),
        "transfer-aware.horizon",
        "The transfer-aware horizon is unavailable.",
    )
    count = len(candidates)
    _require(
        count >= 15,
        "transfer-aware.candidate-pool",
        "At least 15 candidates are required.",
    )
    scenario_count = week_matrices[0][0].shape[0]
    _require(
        all(
            points.shape == (scenario_count, count)
            and played.shape == points.shape
            for points, played in week_matrices[:horizon]
        ),
        "transfer-aware.matrix-shape",
        "The transfer-aware scenario matrices are misaligned.",
    )

    squad_start = 0
    starter_start = horizon * count
    captain_start = 2 * horizon * count
    transfer_in_start = 3 * horizon * count
    transfer_out_start = transfer_in_start + (horizon - 1) * count
    binary_count = (5 * horizon - 2) * count

    def squad_index(week: int, player: int) -> int:
        return squad_start + week * count + player

    def starter_index(week: int, player: int) -> int:
        return starter_start + week * count + player

    def captain_index(week: int, player: int) -> int:
        return captain_start + week * count + player

    def transfer_in_index(week: int, player: int) -> int:
        return transfer_in_start + (week - 1) * count + player

    def transfer_out_index(week: int, player: int) -> int:
        return transfer_out_start + (week - 1) * count + player

    objective = np.zeros(binary_count, dtype=np.float64)
    for week in range(horizon):
        means = np.mean(week_matrices[week][0], axis=0)
        for player, mean in enumerate(means):
            objective[squad_index(week, player)] = -BENCH_WEIGHT * mean
            objective[starter_index(week, player)] = (
                -(1.0 - BENCH_WEIGHT) * mean
            )
            objective[captain_index(week, player)] = -mean

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

    team_ids = sorted({int(row["teamId"]) for row in candidates})
    for week in range(horizon):
        add(
            {
                squad_index(week, player): 1.0
                for player in range(count)
            },
            15,
            15,
        )
        for position, quota in POSITION_QUOTAS.items():
            add(
                {
                    squad_index(week, player): 1.0
                    for player, candidate in enumerate(candidates)
                    if candidate["position"] == position
                },
                quota,
                quota,
            )
        add(
            {
                squad_index(week, player): float(
                    candidate["priceTenths"]
                )
                for player, candidate in enumerate(candidates)
            },
            0,
            1000,
        )
        for team_id in team_ids:
            add(
                {
                    squad_index(week, player): 1.0
                    for player, candidate in enumerate(candidates)
                    if int(candidate["teamId"]) == team_id
                },
                0,
                3,
            )
        add(
            {
                starter_index(week, player): 1.0
                for player in range(count)
            },
            11,
            11,
        )
        for position, (minimum, maximum) in STARTER_BOUNDS.items():
            add(
                {
                    starter_index(week, player): 1.0
                    for player, candidate in enumerate(candidates)
                    if candidate["position"] == position
                },
                minimum,
                maximum,
            )
        add(
            {
                captain_index(week, player): 1.0
                for player in range(count)
            },
            1,
            1,
        )
        for player in range(count):
            add(
                {
                    squad_index(week, player): -1.0,
                    starter_index(week, player): 1.0,
                },
                -np.inf,
                0,
            )
            add(
                {
                    starter_index(week, player): -1.0,
                    captain_index(week, player): 1.0,
                },
                -np.inf,
                0,
            )

    for week in range(1, horizon):
        add(
            {
                transfer_in_index(week, player): 1.0
                for player in range(count)
            },
            0,
            1,
        )
        add(
            {
                transfer_out_index(week, player): 1.0
                for player in range(count)
            },
            0,
            1,
        )
        add(
            {
                **{
                    transfer_in_index(week, player): 1.0
                    for player in range(count)
                },
                **{
                    transfer_out_index(week, player): -1.0
                    for player in range(count)
                },
            },
            0,
            0,
        )
        for player in range(count):
            add(
                {
                    squad_index(week, player): 1.0,
                    squad_index(week - 1, player): -1.0,
                    transfer_in_index(week, player): -1.0,
                    transfer_out_index(week, player): 1.0,
                },
                0,
                0,
            )
            add(
                {
                    squad_index(week - 1, player): 1.0,
                    transfer_in_index(week, player): 1.0,
                },
                -np.inf,
                1,
            )
            add(
                {
                    squad_index(week - 1, player): -1.0,
                    transfer_out_index(week, player): 1.0,
                },
                -np.inf,
                0,
            )

    matrix = lil_matrix((len(rows), binary_count), dtype=np.float64)
    for row_index, values in enumerate(rows):
        for column_index, value in values.items():
            matrix[row_index, column_index] = value
    result = milp(
        c=objective,
        integrality=np.ones(binary_count, dtype=np.int8),
        bounds=Bounds(
            np.zeros(binary_count, dtype=np.float64),
            np.ones(binary_count, dtype=np.float64),
        ),
        constraints=LinearConstraint(
            matrix.tocsc(),
            np.asarray(lower),
            np.asarray(upper),
        ),
        options={
            "presolve": True,
            "time_limit": 180.0,
            "mip_rel_gap": 0.0,
        },
    )
    _require(
        result.success
        and result.status == 0
        and result.x is not None
        and result.mip_gap is not None
        and 0.0 <= float(result.mip_gap) <= MAXIMUM_NUMERICAL_MIP_GAP,
        "transfer-aware.optimizer",
        (
            "The transfer-aware surrogate did not reach an exact optimum "
            f"(status={result.status}, success={result.success}, "
            f"mipGap={result.mip_gap}, message={result.message})."
        ),
    )
    values = np.rint(result.x).astype(np.int8)
    by_id = {int(player["playerId"]): player for player in candidates}
    gameweeks = []
    previous_squad: set[int] | None = None
    for week in range(horizon):
        squad_ids = {
            int(candidates[player]["playerId"])
            for player in range(count)
            if values[squad_index(week, player)] == 1
        }
        starting_ids = [
            int(candidates[player]["playerId"])
            for player in range(count)
            if values[starter_index(week, player)] == 1
        ]
        captain_id = next(
            int(candidates[player]["playerId"])
            for player in range(count)
            if values[captain_index(week, player)] == 1
        )
        mean_by_id = {
            int(candidate["playerId"]): float(
                np.mean(week_matrices[week][0][:, player])
            )
            for player, candidate in enumerate(candidates)
            if int(candidate["playerId"]) in squad_ids
        }
        roles = _roles_document(
            squad_ids,
            starting_ids,
            captain_id,
            by_id,
            mean_by_id,
            week + 1,
        )
        transfer = None
        if previous_squad is not None:
            incoming = sorted(squad_ids - previous_squad)
            outgoing = sorted(previous_squad - squad_ids)
            _require(
                len(incoming) == len(outgoing)
                and len(incoming) <= 1,
                "transfer-aware.transfer-count",
                "A solved transition exceeds one transfer.",
            )
            if incoming:
                _require(
                    by_id[incoming[0]]["position"]
                    == by_id[outgoing[0]]["position"],
                    "transfer-aware.transfer-position",
                    "A solved transfer changes player position.",
                )
                transfer = {
                    "playerOutId": outgoing[0],
                    "playerInId": incoming[0],
                    "pointCost": 0,
                }
        gameweeks.append(
            {
                **roles,
                "squadPlayerIds": sorted(squad_ids),
                "budgetTenths": sum(
                    int(by_id[player_id]["priceTenths"])
                    for player_id in squad_ids
                ),
                "plannedTransfer": transfer,
            }
        )
        previous_squad = squad_ids

    initial_ids = gameweeks[0]["squadPlayerIds"]
    return (
        {
            "initialPlayerIds": initial_ids,
            "initialBudgetTenths": gameweeks[0]["budgetTenths"],
            "initialPlayers": [
                dict(by_id[player_id]) for player_id in initial_ids
            ],
            "gameweeks": gameweeks,
            "plannedTransferCount": sum(
                row["plannedTransfer"] is not None for row in gameweeks
            ),
        },
        {
            "optimizerVersion": OPTIMIZER_VERSION,
            "solver": "scipy.optimize.milp-highs",
            "scipyVersion": str(scipy.__version__),
            "status": "global-linear-transfer-aware-surrogate-optimum",
            "mipGap": (
                0.0
                if float(result.mip_gap) <= MAXIMUM_NUMERICAL_MIP_GAP
                else float(result.mip_gap)
            ),
            "reportedMipGap": float(result.mip_gap),
            "maximumNumericalMipGap": MAXIMUM_NUMERICAL_MIP_GAP,
            "objectiveValue": _round(float(-result.fun)),
            "binaryVariableCount": binary_count,
            "constraintCount": len(rows),
        },
    )


def score_transfer_aware_plan(
    plan: Mapping[str, Any],
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
) -> Dict[str, Any]:
    by_id = {int(player["playerId"]): player for player in candidates}
    candidate_index = {
        int(player["playerId"]): index
        for index, player in enumerate(candidates)
    }
    scenario_count = week_matrices[0][0].shape[0]
    cumulative = np.zeros(scenario_count, dtype=np.int64)
    weekly = []
    for week_index, roles in enumerate(plan["gameweeks"]):
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
        columns = [candidate_index[player_id] for player_id in squad_ids]
        points, played = week_matrices[week_index]
        scores = score_selection_scenarios(
            definition,
            points[:, columns],
            played[:, columns],
        )
        cumulative += scores.total_points
        weekly.append(
            {
                "gameweek": week_index + 1,
                **summarise_scores(scores),
            }
        )
    return {
        "weekly": weekly,
        "cumulative": _score_vector_summary(cumulative),
        "pathTotalPoints": cumulative.tolist(),
    }


def _roles_document(
    squad_ids: set[int],
    starting_ids: Sequence[int],
    captain_id: int,
    by_id: Mapping[int, Mapping[str, Any]],
    mean_by_id: Mapping[int, float],
    gameweek: int,
) -> Dict[str, Any]:
    starters = set(int(value) for value in starting_ids)
    vice_id = min(
        (value for value in starters if value != captain_id),
        key=lambda player_id: (-mean_by_id[player_id], player_id),
    )
    bench_gk = next(
        value
        for value in squad_ids - starters
        if by_id[value]["position"] == "goalkeeper"
    )
    outfield_bench = sorted(
        (
            value
            for value in squad_ids - starters
            if by_id[value]["position"] != "goalkeeper"
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


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)
