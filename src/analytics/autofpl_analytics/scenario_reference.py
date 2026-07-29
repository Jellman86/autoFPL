from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, Optional, Sequence, Tuple

import numpy as np

SCHEMA_VERSION = "1.0"
ENGINE_VERSION = "cpu-joint-scenario-reference-v1"
RANDOM_STREAM = "numpy-pcg64-v1"
POSITIONS = ("goalkeeper", "defender", "midfielder", "forward")
SQUAD_POSITION_COUNTS = {
    "goalkeeper": 2,
    "defender": 5,
    "midfielder": 5,
    "forward": 3,
}
MINIMUM_LINEUP_COUNTS = {
    "goalkeeper": 1,
    "defender": 3,
    "midfielder": 2,
    "forward": 1,
}
MAXIMUM_DRAWS = 1_000_000


class ScenarioValidationError(ValueError):
    def __init__(self, code: str, field: str) -> None:
        super().__init__(code)
        self.code = code
        self.field = field


@dataclass(frozen=True)
class SelectionDefinition:
    player_ids: Tuple[int, ...]
    positions: Tuple[str, ...]
    starting_player_ids: Tuple[int, ...]
    replacement_goalkeeper_player_id: int
    outfield_substitute_player_ids: Tuple[int, ...]
    captain_player_id: int
    vice_captain_player_id: int

    @classmethod
    def create(
        cls,
        player_ids: Sequence[int],
        positions: Sequence[str],
        starting_player_ids: Sequence[int],
        replacement_goalkeeper_player_id: int,
        outfield_substitute_player_ids: Sequence[int],
        captain_player_id: int,
        vice_captain_player_id: int,
    ) -> "SelectionDefinition":
        selection = cls(
            tuple(player_ids),
            tuple(positions),
            tuple(starting_player_ids),
            replacement_goalkeeper_player_id,
            tuple(outfield_substitute_player_ids),
            captain_player_id,
            vice_captain_player_id,
        )
        _validate_selection(selection)
        return selection


@dataclass(frozen=True)
class JointScenarioBatch:
    points: np.ndarray
    played: np.ndarray
    source_indices: np.ndarray
    seed: int


@dataclass(frozen=True)
class ScenarioScoreBatch:
    total_points: np.ndarray
    captain_bonus_points: np.ndarray
    activated_substitute_count: np.ndarray
    unreplaced_starter_count: np.ndarray


def sample_joint_scenarios(
    points_support: np.ndarray,
    played_support: np.ndarray,
    draw_count: int,
    seed: int,
    weights: Optional[Sequence[float]] = None,
) -> JointScenarioBatch:
    """Sample complete joint rows without breaking player dependence."""
    points, played = _validate_scenario_matrices(
        points_support,
        played_support,
    )
    if (
        not isinstance(draw_count, int)
        or isinstance(draw_count, bool)
        or not 1 <= draw_count <= MAXIMUM_DRAWS
    ):
        raise ScenarioValidationError(
            "scenario.draw_count.invalid",
            "drawCount",
        )
    if (
        not isinstance(seed, int)
        or isinstance(seed, bool)
        or not 0 <= seed <= np.iinfo(np.uint64).max
    ):
        raise ScenarioValidationError("scenario.seed.invalid", "seed")

    probabilities = _normalise_weights(weights, points.shape[0])
    generator = np.random.Generator(np.random.PCG64(seed))
    indices = generator.choice(
        points.shape[0],
        size=draw_count,
        replace=True,
        p=probabilities,
    )
    return JointScenarioBatch(
        points=np.array(points[indices], copy=True),
        played=np.array(played[indices], copy=True),
        source_indices=np.asarray(indices, dtype=np.int64),
        seed=seed,
    )


def score_selection_scenarios(
    selection: SelectionDefinition,
    points: np.ndarray,
    played: np.ndarray,
) -> ScenarioScoreBatch:
    """Resolve official auto-sub and captain rules for every supplied row."""
    _validate_selection(selection)
    point_rows, played_rows = _validate_scenario_matrices(points, played)
    if point_rows.shape[1] != len(selection.player_ids):
        raise ScenarioValidationError(
            "scenario.player_count.invalid",
            "points",
        )

    index_by_player_id = {
        player_id: index
        for index, player_id in enumerate(selection.player_ids)
    }
    position_by_index = np.asarray(selection.positions)
    starting_indices = np.asarray(
        [
            index_by_player_id[player_id]
            for player_id in selection.starting_player_ids
        ],
        dtype=np.int64,
    )
    scenario_count = point_rows.shape[0]
    effective = np.zeros(
        (scenario_count, len(selection.player_ids)),
        dtype=np.bool_,
    )
    effective[:, starting_indices] = True

    starting_goalkeeper_index = next(
        index
        for index in starting_indices
        if position_by_index[index] == "goalkeeper"
    )
    replacement_goalkeeper_index = index_by_player_id[
        selection.replacement_goalkeeper_player_id
    ]
    goalkeeper_replacement = (
        ~played_rows[:, starting_goalkeeper_index]
        & played_rows[:, replacement_goalkeeper_index]
    )
    effective[goalkeeper_replacement, starting_goalkeeper_index] = False
    effective[goalkeeper_replacement, replacement_goalkeeper_index] = True

    missing_outfield_indices = np.asarray(
        [
            index
            for index in starting_indices
            if index != starting_goalkeeper_index
        ],
        dtype=np.int64,
    )
    replaced = np.zeros(
        (scenario_count, len(missing_outfield_indices)),
        dtype=np.bool_,
    )
    activated_outfield_count = np.zeros(scenario_count, dtype=np.int64)
    for substitute_player_id in selection.outfield_substitute_player_ids:
        substitute_index = index_by_player_id[substitute_player_id]
        substitute_activated = np.zeros(scenario_count, dtype=np.bool_)
        for missing_offset, missing_index in enumerate(
            missing_outfield_indices
        ):
            eligible = (
                played_rows[:, substitute_index]
                & ~substitute_activated
                & ~played_rows[:, missing_index]
                & ~replaced[:, missing_offset]
            )
            if not np.any(eligible):
                continue
            valid_formation = _valid_after_replacement(
                effective,
                position_by_index,
                missing_index,
                substitute_index,
            )
            activate = eligible & valid_formation
            if not np.any(activate):
                continue
            effective[activate, missing_index] = False
            effective[activate, substitute_index] = True
            replaced[activate, missing_offset] = True
            substitute_activated[activate] = True
            activated_outfield_count[activate] += 1

    base_points = np.sum(
        np.where(effective, point_rows, 0),
        axis=1,
        dtype=np.int64,
    )
    captain_index = index_by_player_id[selection.captain_player_id]
    vice_captain_index = index_by_player_id[
        selection.vice_captain_player_id
    ]
    captain_bonus = np.where(
        played_rows[:, captain_index],
        point_rows[:, captain_index],
        np.where(
            played_rows[:, vice_captain_index],
            point_rows[:, vice_captain_index],
            0,
        ),
    ).astype(np.int64, copy=False)
    unreplaced = np.sum(
        ~played_rows[:, starting_indices] & effective[:, starting_indices],
        axis=1,
        dtype=np.int64,
    )
    return ScenarioScoreBatch(
        total_points=base_points + captain_bonus,
        captain_bonus_points=captain_bonus,
        activated_substitute_count=(
            activated_outfield_count
            + goalkeeper_replacement.astype(np.int64)
        ),
        unreplaced_starter_count=unreplaced,
    )


def summarise_scores(scores: ScenarioScoreBatch) -> Dict[str, object]:
    values = _validate_score_vector(scores.total_points, "scores")
    return {
        "schemaVersion": SCHEMA_VERSION,
        "engineVersion": ENGINE_VERSION,
        "scenarioCount": int(values.size),
        "meanPoints": float(np.mean(values)),
        "standardDeviationPoints": float(np.std(values, ddof=0)),
        "minimumPoints": int(np.min(values)),
        "p10Points": float(np.quantile(values, 0.10, method="linear")),
        "medianPoints": float(np.quantile(values, 0.50, method="linear")),
        "p90Points": float(np.quantile(values, 0.90, method="linear")),
        "maximumPoints": int(np.max(values)),
        "meanActivatedSubstitutes": float(
            np.mean(scores.activated_substitute_count)
        ),
        "probabilityOfUnreplacedStarter": float(
            np.mean(scores.unreplaced_starter_count > 0)
        ),
    }


def compare_paired_scores(
    reference: ScenarioScoreBatch,
    candidate: ScenarioScoreBatch,
) -> Dict[str, object]:
    reference_values = _validate_score_vector(
        reference.total_points,
        "reference",
    )
    candidate_values = _validate_score_vector(
        candidate.total_points,
        "candidate",
    )
    if reference_values.shape != candidate_values.shape:
        raise ScenarioValidationError(
            "scenario.comparison.shape-mismatch",
            "candidate",
        )
    difference = candidate_values - reference_values
    return {
        "schemaVersion": SCHEMA_VERSION,
        "engineVersion": ENGINE_VERSION,
        "scenarioCount": int(difference.size),
        "meanPointsDelta": float(np.mean(difference)),
        "p10PointsDelta": float(
            np.quantile(difference, 0.10, method="linear")
        ),
        "medianPointsDelta": float(
            np.quantile(difference, 0.50, method="linear")
        ),
        "p90PointsDelta": float(
            np.quantile(difference, 0.90, method="linear")
        ),
        "probabilityCandidateWins": float(np.mean(difference > 0)),
        "probabilityTie": float(np.mean(difference == 0)),
        "probabilityCandidateLoses": float(np.mean(difference < 0)),
    }


def _validate_selection(selection: SelectionDefinition) -> None:
    if len(selection.player_ids) != 15:
        raise ScenarioValidationError(
            "selection.squad.count",
            "playerIds",
        )
    if (
        any(
            not isinstance(player_id, int)
            or isinstance(player_id, bool)
            or player_id <= 0
            for player_id in selection.player_ids
        )
        or len(set(selection.player_ids)) != len(selection.player_ids)
    ):
        raise ScenarioValidationError(
            "selection.squad.player.invalid",
            "playerIds",
        )
    if len(selection.positions) != len(selection.player_ids):
        raise ScenarioValidationError(
            "selection.position.count",
            "positions",
        )
    if any(position not in POSITIONS for position in selection.positions):
        raise ScenarioValidationError(
            "selection.position.invalid",
            "positions",
        )
    position_counts = {
        position: selection.positions.count(position)
        for position in POSITIONS
    }
    if position_counts != SQUAD_POSITION_COUNTS:
        raise ScenarioValidationError(
            "selection.squad.composition",
            "positions",
        )

    if (
        len(selection.starting_player_ids) != 11
        or len(set(selection.starting_player_ids)) != 11
    ):
        raise ScenarioValidationError(
            "selection.starting.count",
            "startingPlayerIds",
        )
    squad_ids = set(selection.player_ids)
    starting_ids = set(selection.starting_player_ids)
    if not starting_ids.issubset(squad_ids):
        raise ScenarioValidationError(
            "selection.starting.not-in-squad",
            "startingPlayerIds",
        )
    if not _valid_lineup(
        selection.starting_player_ids,
        selection.player_ids,
        selection.positions,
    ):
        raise ScenarioValidationError(
            "selection.starting.formation",
            "startingPlayerIds",
        )

    substitutes = (
        selection.replacement_goalkeeper_player_id,
        *selection.outfield_substitute_player_ids,
    )
    if (
        len(selection.outfield_substitute_player_ids) != 3
        or len(set(substitutes)) != 4
        or set(substitutes) | starting_ids != squad_ids
        or set(substitutes) & starting_ids
    ):
        raise ScenarioValidationError(
            "selection.substitute.partition",
            "outfieldSubstitutePlayerIds",
        )
    position_by_player_id = dict(
        zip(selection.player_ids, selection.positions)
    )
    if (
        position_by_player_id[
            selection.replacement_goalkeeper_player_id
        ]
        != "goalkeeper"
        or any(
            position_by_player_id[player_id] == "goalkeeper"
            for player_id in selection.outfield_substitute_player_ids
        )
    ):
        raise ScenarioValidationError(
            "selection.substitute.position",
            "replacementGoalkeeperPlayerId",
        )
    if (
        selection.captain_player_id
        == selection.vice_captain_player_id
        or selection.captain_player_id not in starting_ids
        or selection.vice_captain_player_id not in starting_ids
    ):
        raise ScenarioValidationError(
            "selection.captaincy.invalid",
            "captainPlayerId",
        )


def _valid_lineup(
    lineup_player_ids: Sequence[int],
    player_ids: Sequence[int],
    positions: Sequence[str],
) -> bool:
    position_by_player_id = dict(zip(player_ids, positions))
    counts = {
        position: sum(
            position_by_player_id[player_id] == position
            for player_id in lineup_player_ids
        )
        for position in POSITIONS
    }
    return (
        len(lineup_player_ids) == 11
        and counts["goalkeeper"] == 1
        and counts["defender"] >= MINIMUM_LINEUP_COUNTS["defender"]
        and counts["midfielder"] >= MINIMUM_LINEUP_COUNTS["midfielder"]
        and counts["forward"] >= MINIMUM_LINEUP_COUNTS["forward"]
    )


def _valid_after_replacement(
    effective: np.ndarray,
    positions: np.ndarray,
    missing_index: int,
    substitute_index: int,
) -> np.ndarray:
    valid = np.ones(effective.shape[0], dtype=np.bool_)
    for position in POSITIONS:
        count = np.sum(
            effective[:, positions == position],
            axis=1,
            dtype=np.int64,
        )
        count = (
            count
            - int(positions[missing_index] == position)
            + int(positions[substitute_index] == position)
        )
        if position == "goalkeeper":
            valid &= count == 1
        else:
            valid &= count >= MINIMUM_LINEUP_COUNTS[position]
    return valid


def _validate_scenario_matrices(
    points: np.ndarray,
    played: np.ndarray,
) -> Tuple[np.ndarray, np.ndarray]:
    point_rows = np.asarray(points)
    played_rows = np.asarray(played)
    if (
        point_rows.ndim != 2
        or point_rows.shape[0] == 0
        or point_rows.shape[1] == 0
        or not np.issubdtype(point_rows.dtype, np.integer)
        or np.issubdtype(point_rows.dtype, np.bool_)
    ):
        raise ScenarioValidationError(
            "scenario.points.invalid",
            "points",
        )
    if (
        np.any(point_rows < np.iinfo(np.int32).min)
        or np.any(point_rows > np.iinfo(np.int32).max)
    ):
        raise ScenarioValidationError(
            "scenario.points.out-of-range",
            "points",
        )
    if (
        played_rows.shape != point_rows.shape
        or played_rows.dtype != np.bool_
    ):
        raise ScenarioValidationError(
            "scenario.played.invalid",
            "played",
        )
    if np.any((point_rows != 0) & ~played_rows):
        raise ScenarioValidationError(
            "scenario.nonplayer-points.nonzero",
            "points",
        )
    return (
        point_rows.astype(np.int64, copy=False),
        played_rows.astype(np.bool_, copy=False),
    )


def _normalise_weights(
    weights: Optional[Sequence[float]],
    support_count: int,
) -> Optional[np.ndarray]:
    if weights is None:
        return None
    probabilities = np.asarray(weights, dtype=np.float64)
    total = float(np.sum(probabilities))
    if (
        probabilities.shape != (support_count,)
        or not np.all(np.isfinite(probabilities))
        or np.any(probabilities < 0)
        or not np.isfinite(total)
        or total <= 0
    ):
        raise ScenarioValidationError(
            "scenario.weights.invalid",
            "weights",
        )
    return probabilities / total


def _validate_score_vector(
    values: np.ndarray,
    field: str,
) -> np.ndarray:
    score_values = np.asarray(values)
    if (
        score_values.ndim != 1
        or score_values.size == 0
        or not np.issubdtype(score_values.dtype, np.integer)
    ):
        raise ScenarioValidationError(
            "scenario.scores.invalid",
            field,
        )
    return score_values.astype(np.int64, copy=False)
