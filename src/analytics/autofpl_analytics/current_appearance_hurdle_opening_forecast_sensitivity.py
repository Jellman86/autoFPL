from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence, Tuple

import numpy as np

from .current_appearance_hurdle_joint_scenarios import (
    build_current_appearance_hurdle_joint_scenarios,
)
from .current_appearance_hurdle_opening_optimality_audit import (
    ARTIFACT_VERSION as OPTIMALITY_AUDIT_ARTIFACT_VERSION,
    _require_hurdle_scenario,
)
from .current_multi_horizon_initial_squad import (
    BENCH_WEIGHT,
    OPTIMIZER_VERSION,
    SELECTED_POLICY_KEY,
    _build_candidates,
    _optimise_horizon,
    _week_matrices,
)
from .current_opening_squad_optimality_audit import _player_identity
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-opening-squad-forecast-sensitivity"
ARTIFACT_VERSION = "current-opening-squad-forecast-sensitivity-v1"
STATUS = "conditional-forecast-sensitivity-diagnostic"
HORIZON_GAMEWEEKS = 6
CVAR_WEIGHT = 0.0
THRESHOLD_ITERATIONS = 16
MAXIMUM_CHALLENGER_MULTIPLIER = 3.0
SHORTLIST_PER_POSITION = 5
POSITIONS = ("goalkeeper", "defender", "midfielder", "forward")
PERTURBATION_METHOD = (
    "coherent-player-six-gameweek-scenario-point-multiplier"
)


def build_current_appearance_hurdle_opening_forecast_sensitivity(
    database_path: Path,
    *,
    threshold_iterations: int = THRESHOLD_ITERATIONS,
    shortlist_per_position: int = SHORTLIST_PER_POSITION,
) -> Dict[str, Any]:
    scenario = build_current_appearance_hurdle_joint_scenarios(
        Path(database_path)
    )
    return _build_from_scenario(
        Path(database_path),
        scenario,
        threshold_iterations=threshold_iterations,
        shortlist_per_position=shortlist_per_position,
    )


def _build_from_scenario(
    database_path: Path,
    scenario: Mapping[str, Any],
    *,
    threshold_iterations: int = THRESHOLD_ITERATIONS,
    shortlist_per_position: int = SHORTLIST_PER_POSITION,
) -> Dict[str, Any]:
    _require_hurdle_scenario(scenario)
    _require(
        threshold_iterations > 0,
        "forecast-sensitivity.threshold-iterations",
        "At least one threshold-search iteration is required.",
    )
    _require(
        shortlist_per_position > 0,
        "forecast-sensitivity.shortlist-count",
        "At least one challenger per position must be shortlisted.",
    )
    candidates = _build_candidates(Path(database_path), scenario)
    week_matrices = _week_matrices(scenario)
    incumbent, incumbent_solver = _optimise_horizon(
        candidates,
        week_matrices,
        HORIZON_GAMEWEEKS,
        CVAR_WEIGHT,
    )
    incumbent_ids = {int(value) for value in incumbent["playerIds"]}
    by_id = {
        int(player["playerId"]): dict(player) for player in candidates
    }
    baseline_forecasts = _six_week_mean_forecasts(
        candidates,
        week_matrices,
    )

    exclusion_replacements: set[int] = set()
    for player_id in sorted(incumbent_ids):
        selection, _ = _optimise_horizon(
            candidates,
            week_matrices,
            HORIZON_GAMEWEEKS,
            CVAR_WEIGHT,
            excluded_player_ids=(player_id,),
        )
        exclusion_replacements.update(
            int(value)
            for value in selection["playerIds"]
            if int(value) not in incumbent_ids
        )

    forced_in = []
    for player in candidates:
        player_id = int(player["playerId"])
        if player_id in incumbent_ids:
            continue
        selection, solver = _optimise_horizon(
            candidates,
            week_matrices,
            HORIZON_GAMEWEEKS,
            CVAR_WEIGHT,
            required_player_ids=(player_id,),
        )
        forced_in.append(
            {
                "player": _player_identity(player),
                "forcedSelectionSurrogateRegretPoints": _round(
                    float(incumbent_solver["objectiveValue"])
                    - float(solver["objectiveValue"])
                ),
                "forcedSelectionPlayerIds": selection["playerIds"],
            }
        )
    forced_in.sort(
        key=lambda row: (
            float(row["forcedSelectionSurrogateRegretPoints"]),
            str(row["player"]["position"]),
            str(row["player"]["webName"]),
            int(row["player"]["playerId"]),
        )
    )

    shortlist_ids = set(exclusion_replacements)
    for position in POSITIONS:
        position_rows = [
            row
            for row in forced_in
            if str(row["player"]["position"]) == position
        ]
        shortlist_ids.update(
            int(row["player"]["playerId"])
            for row in position_rows[:shortlist_per_position]
        )

    selected_thresholds = []
    for player_id in sorted(
        incumbent_ids,
        key=lambda value: (
            str(by_id[value]["position"]),
            str(by_id[value]["webName"]),
            value,
        ),
    ):
        threshold = _selected_exit_threshold(
            candidates,
            week_matrices,
            player_id,
            threshold_iterations,
        )
        boundary_ids = set(
            int(value)
            for value in threshold["boundarySelection"]["playerIds"]
        )
        replacement_ids = boundary_ids - incumbent_ids
        reduction = (
            None
            if threshold["minimumRetainedMultiplier"] is None
            else 1.0
            - float(threshold["minimumRetainedMultiplier"])
        )
        selected_thresholds.append(
            {
                "player": _player_identity(by_id[player_id]),
                "baselineSixGameweekMeanPoints": _round(
                    baseline_forecasts[player_id]
                ),
                "minimumRetainedMultiplier": (
                    threshold["minimumRetainedMultiplier"]
                ),
                "maximumForecastReductionFraction": (
                    None if reduction is None else _round(reduction)
                ),
                "maximumForecastReductionPercent": (
                    None
                    if reduction is None
                    else _round(100.0 * reduction)
                ),
                "survivesZeroPointForecast": bool(
                    threshold["minimumRetainedMultiplier"] is None
                ),
                "boundaryRemovedPlayerIds": sorted(
                    incumbent_ids - boundary_ids
                ),
                "boundaryAddedPlayers": [
                    _player_identity(by_id[value])
                    for value in sorted(
                        replacement_ids,
                        key=lambda candidate_id: (
                            str(by_id[candidate_id]["position"]),
                            str(by_id[candidate_id]["webName"]),
                            candidate_id,
                        ),
                    )
                ],
                "boundarySelectionPlayerIds": sorted(boundary_ids),
            }
        )

    forced_by_id = {
        int(row["player"]["playerId"]): row for row in forced_in
    }
    challenger_thresholds = []
    for player_id in sorted(
        shortlist_ids,
        key=lambda value: (
            float(
                forced_by_id[value][
                    "forcedSelectionSurrogateRegretPoints"
                ]
            ),
            str(by_id[value]["position"]),
            str(by_id[value]["webName"]),
            value,
        ),
    ):
        threshold = _challenger_entry_threshold(
            candidates,
            week_matrices,
            player_id,
            threshold_iterations,
        )
        entry_multiplier = threshold["minimumEntryMultiplier"]
        boundary_ids = set(
            int(value)
            for value in threshold["boundarySelection"]["playerIds"]
        )
        increase = (
            None
            if entry_multiplier is None
            else float(entry_multiplier) - 1.0
        )
        challenger_thresholds.append(
            {
                "player": _player_identity(by_id[player_id]),
                "baselineSixGameweekMeanPoints": _round(
                    baseline_forecasts[player_id]
                ),
                "forcedSelectionSurrogateRegretPoints": forced_by_id[
                    player_id
                ]["forcedSelectionSurrogateRegretPoints"],
                "isDirectExclusionReplacement": (
                    player_id in exclusion_replacements
                ),
                "minimumEntryMultiplier": entry_multiplier,
                "requiredForecastIncreaseFraction": (
                    None if increase is None else _round(increase)
                ),
                "requiredForecastIncreasePercent": (
                    None
                    if increase is None
                    else _round(100.0 * increase)
                ),
                "doesNotEnterByMaximumMultiplier": (
                    entry_multiplier is None
                ),
                "boundaryRemovedPlayers": [
                    _player_identity(by_id[value])
                    for value in sorted(
                        incumbent_ids - boundary_ids,
                        key=lambda candidate_id: (
                            str(by_id[candidate_id]["position"]),
                            str(by_id[candidate_id]["webName"]),
                            candidate_id,
                        ),
                    )
                ],
                "boundaryAddedPlayerIds": sorted(
                    boundary_ids - incumbent_ids
                ),
                "boundarySelectionPlayerIds": sorted(boundary_ids),
            }
        )

    selected_thresholds.sort(
        key=lambda row: (
            (
                float("inf")
                if row["maximumForecastReductionFraction"] is None
                else float(row["maximumForecastReductionFraction"])
            ),
            str(row["player"]["position"]),
            str(row["player"]["webName"]),
            int(row["player"]["playerId"]),
        )
    )
    challenger_thresholds.sort(
        key=lambda row: (
            (
                float("inf")
                if row["requiredForecastIncreaseFraction"] is None
                else float(row["requiredForecastIncreaseFraction"])
            ),
            float(row["forcedSelectionSurrogateRegretPoints"]),
            str(row["player"]["position"]),
            str(row["player"]["webName"]),
            int(row["player"]["playerId"]),
        )
    )

    resolution = 1.0 / (2**threshold_iterations)
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "seasonCode": scenario["seasonCode"],
        "openingGameweek": scenario["openingGameweek"],
        "decisionCutoffUtc": scenario["decisionCutoffUtc"],
        "officialCaptureId": scenario["officialCaptureId"],
        "scenarioCount": scenario["scenarioCount"],
        "candidatePoolCount": len(candidates),
        "policy": {
            "evaluationPolicyKey": SELECTED_POLICY_KEY,
            "horizonGameweeks": HORIZON_GAMEWEEKS,
            "optimizerPolicyKey": "expected-points",
            "surrogate": (
                "weekly-points-bench-weighted-with-captain"
            ),
            "benchWeight": BENCH_WEIGHT,
            "cvarWeight": CVAR_WEIGHT,
        },
        "perturbation": {
            "method": PERTURBATION_METHOD,
            "thresholdSearchIterations": threshold_iterations,
            "selectedMultiplierInterval": [0.0, 1.0],
            "challengerMultiplierInterval": [
                1.0,
                MAXIMUM_CHALLENGER_MULTIPLIER,
            ],
            "selectedThresholdMaximumWidth": _round(resolution),
            "challengerThresholdMaximumWidth": _round(2.0 * resolution),
            "shortlistPerPositionByForcedSelectionRegret": (
                shortlist_per_position
            ),
            "directExclusionReplacementsAlwaysShortlisted": True,
        },
        "incumbent": {
            "selection": incumbent,
            "solver": incumbent_solver,
        },
        "selectedPlayerThresholds": selected_thresholds,
        "challengerScreen": {
            "screenedUnselectedPlayerCount": len(forced_in),
            "shortlistedPlayerCount": len(shortlist_ids),
            "allForcedSelectionRegrets": forced_in,
            "shortlistedThresholds": challenger_thresholds,
        },
        "source": {
            "scenarioArtifactVersion": scenario["artifactVersion"],
            "scenarioContentSha256": scenario[
                "scenarioContentSha256"
            ],
            "scenarioRunIdentitySha256": scenario[
                "runIdentitySha256"
            ],
            "optimizerVersion": OPTIMIZER_VERSION,
            "optimalityAuditArtifactVersion": (
                OPTIMALITY_AUDIT_ARTIFACT_VERSION
            ),
        },
        "limitations": [
            (
                "Each threshold is conditional on the retained model, "
                "scenario construction, official capture, prices, FPL "
                "constraints and six-Gameweek expected-points objective."
            ),
            (
                "A multiplier scales every point outcome for one player "
                "coherently across all six Gameweeks while preserving "
                "appearance rows. It is an interpretable forecast stress, "
                "not a literal injury or availability model."
            ),
            (
                "All unselected players receive an exact forced-selection "
                "regret screen. Multiplier thresholds are then solved for "
                "the nearest five candidates per position and every direct "
                "replacement from a selected-player exclusion."
            ),
            (
                "The MILP objective uses expected scenario points and "
                "linear bench/captain weights. Automatic substitutions are "
                "not part of the threshold search."
            ),
            (
                "This current diagnostic cannot promote a model or establish "
                "prospective 2026/27 accuracy."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "policy": artifact["policy"],
            "perturbation": artifact["perturbation"],
            "source": artifact["source"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _selected_exit_threshold(
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
    player_id: int,
    iterations: int,
) -> Dict[str, Any]:
    at_zero, _ = _optimise_with_multiplier(
        candidates,
        week_matrices,
        player_id,
        0.0,
    )
    if player_id in {int(value) for value in at_zero["playerIds"]}:
        return {
            "minimumRetainedMultiplier": None,
            "boundarySelection": at_zero,
        }
    low = 0.0
    high = 1.0
    boundary = at_zero
    for _ in range(iterations):
        middle = (low + high) / 2.0
        selection, _ = _optimise_with_multiplier(
            candidates,
            week_matrices,
            player_id,
            middle,
        )
        if player_id in {
            int(value) for value in selection["playerIds"]
        }:
            high = middle
        else:
            low = middle
            boundary = selection
    return {
        "minimumRetainedMultiplier": _round(high),
        "boundarySelection": boundary,
    }


def _challenger_entry_threshold(
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
    player_id: int,
    iterations: int,
) -> Dict[str, Any]:
    at_maximum, _ = _optimise_with_multiplier(
        candidates,
        week_matrices,
        player_id,
        MAXIMUM_CHALLENGER_MULTIPLIER,
    )
    if player_id not in {
        int(value) for value in at_maximum["playerIds"]
    }:
        return {
            "minimumEntryMultiplier": None,
            "boundarySelection": at_maximum,
        }
    low = 1.0
    high = MAXIMUM_CHALLENGER_MULTIPLIER
    boundary = at_maximum
    for _ in range(iterations):
        middle = (low + high) / 2.0
        selection, _ = _optimise_with_multiplier(
            candidates,
            week_matrices,
            player_id,
            middle,
        )
        if player_id in {
            int(value) for value in selection["playerIds"]
        }:
            high = middle
            boundary = selection
        else:
            low = middle
    return {
        "minimumEntryMultiplier": _round(high),
        "boundarySelection": boundary,
    }


def _optimise_with_multiplier(
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
    player_id: int,
    multiplier: float,
) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    index_by_id = {
        int(player["playerId"]): index
        for index, player in enumerate(candidates)
    }
    _require(
        player_id in index_by_id,
        "forecast-sensitivity.player",
        "A perturbation references an unknown player.",
    )
    index = index_by_id[player_id]
    perturbed = []
    for points, played in week_matrices:
        changed = np.asarray(points, dtype=np.float64).copy()
        changed[:, index] *= multiplier
        perturbed.append((changed, played))
    return _optimise_horizon(
        candidates,
        perturbed,
        HORIZON_GAMEWEEKS,
        CVAR_WEIGHT,
    )


def _six_week_mean_forecasts(
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
) -> Dict[int, float]:
    totals = np.zeros(len(candidates), dtype=np.float64)
    for points, _ in week_matrices[:HORIZON_GAMEWEEKS]:
        totals += np.mean(points, axis=0)
    return {
        int(player["playerId"]): float(totals[index])
        for index, player in enumerate(candidates)
    }


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Measure the coherent six-Gameweek forecast changes required "
            "to alter the retained appearance-hurdle opening squad."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = (
            build_current_appearance_hurdle_opening_forecast_sensitivity(
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
