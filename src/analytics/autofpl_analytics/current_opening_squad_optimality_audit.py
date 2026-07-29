from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

import numpy as np

from .current_multi_horizon_initial_squad import (
    BENCH_WEIGHT,
    OPTIMIZER_VERSION,
    SELECTED_POLICY_KEY,
    _build_candidates,
    _optimise_horizon,
    _score_horizon,
    _week_matrices,
)
from .current_multi_horizon_joint_scenarios import (
    build_current_multi_horizon_joint_scenarios,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-opening-squad-optimality-audit"
ARTIFACT_VERSION = "current-opening-squad-optimality-audit-v1"
STATUS = "conditional-optimality-diagnostic"
HORIZON_GAMEWEEKS = 6
CVAR_WEIGHT = 0.0
BOOTSTRAP_METHOD = "paired-scenario-path-nonparametric-bootstrap"
BOOTSTRAP_SEED = 2_026_080_1
BOOTSTRAP_REPLICATES = 200
CORE_FREQUENCY_THRESHOLD = 0.80
FRAGILE_FREQUENCY_THRESHOLD = 0.50


def build_current_opening_squad_optimality_audit(
    database_path: Path,
    *,
    bootstrap_replicates: int = BOOTSTRAP_REPLICATES,
) -> Dict[str, Any]:
    scenario = build_current_multi_horizon_joint_scenarios(
        Path(database_path)
    )
    return _build_from_scenario(
        Path(database_path),
        scenario,
        bootstrap_replicates=bootstrap_replicates,
    )


def _build_from_scenario(
    database_path: Path,
    scenario: Mapping[str, Any],
    *,
    bootstrap_replicates: int = BOOTSTRAP_REPLICATES,
) -> Dict[str, Any]:
    _require(
        bootstrap_replicates > 0,
        "opening-audit.bootstrap-replicates",
        "At least one bootstrap replicate is required.",
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
    incumbent_score = _score_horizon(
        incumbent,
        candidates,
        week_matrices,
        HORIZON_GAMEWEEKS,
    )

    nearest, nearest_solver = _optimise_horizon(
        candidates,
        week_matrices,
        HORIZON_GAMEWEEKS,
        CVAR_WEIGHT,
        maximum_overlap_player_ids=tuple(sorted(incumbent_ids)),
        maximum_overlap_count=len(incumbent_ids) - 1,
    )
    nearest_score = _score_horizon(
        nearest,
        candidates,
        week_matrices,
        HORIZON_GAMEWEEKS,
    )

    by_id = {
        int(player["playerId"]): dict(player) for player in candidates
    }
    exclusions = []
    for player_id in sorted(
        incumbent_ids,
        key=lambda value: (
            str(by_id[value]["position"]),
            str(by_id[value]["webName"]),
            value,
        ),
    ):
        conditional, solver = _optimise_horizon(
            candidates,
            week_matrices,
            HORIZON_GAMEWEEKS,
            CVAR_WEIGHT,
            excluded_player_ids=(player_id,),
        )
        conditional_score = _score_horizon(
            conditional,
            candidates,
            week_matrices,
            HORIZON_GAMEWEEKS,
        )
        conditional_ids = {
            int(value) for value in conditional["playerIds"]
        }
        exclusions.append(
            {
                "excludedPlayer": _player_identity(by_id[player_id]),
                "surrogateObjectiveRegretPoints": _round(
                    float(incumbent_solver["objectiveValue"])
                    - float(solver["objectiveValue"])
                ),
                "exactScenarioMeanDifferencePoints": _round(
                    _mean_score(conditional_score)
                    - _mean_score(incumbent_score)
                ),
                "exactScenarioWinProbability": _round(
                    _score_win_probability(
                        conditional_score,
                        incumbent_score,
                    )
                ),
                "removedPlayerIds": sorted(
                    incumbent_ids - conditional_ids
                ),
                "addedPlayers": [
                    _player_identity(by_id[value])
                    for value in sorted(
                        conditional_ids - incumbent_ids,
                        key=lambda candidate_id: (
                            str(by_id[candidate_id]["position"]),
                            str(by_id[candidate_id]["webName"]),
                            candidate_id,
                        ),
                    )
                ],
                "conditionalSelectionPlayerIds": sorted(conditional_ids),
                "conditionalBudgetTenths": conditional["budgetTenths"],
                "solver": solver,
            }
        )

    bootstrap = _bootstrap_stability(
        candidates,
        week_matrices,
        incumbent_ids,
        bootstrap_replicates,
    )
    selected_stability = [
        row
        for row in bootstrap["playerSelectionFrequencies"]
        if int(row["playerId"]) in incumbent_ids
    ]
    selected_stability.sort(
        key=lambda row: (
            float(row["selectionFrequency"]),
            str(row["position"]),
            str(row["webName"]),
            int(row["playerId"]),
        )
    )
    core = [
        row
        for row in selected_stability
        if float(row["selectionFrequency"]) >= CORE_FREQUENCY_THRESHOLD
    ]
    fragile = [
        row
        for row in selected_stability
        if float(row["selectionFrequency"]) < FRAGILE_FREQUENCY_THRESHOLD
    ]
    nearest_ids = {int(value) for value in nearest["playerIds"]}
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
        "incumbent": {
            "selection": incumbent,
            "solver": incumbent_solver,
            "exactScenarioScore": incumbent_score,
        },
        "bestDistinctSquad": {
            "selection": nearest,
            "solver": nearest_solver,
            "incumbentPlayerOverlapCount": len(
                incumbent_ids & nearest_ids
            ),
            "surrogateObjectiveRegretPoints": _round(
                float(incumbent_solver["objectiveValue"])
                - float(nearest_solver["objectiveValue"])
            ),
            "exactScenarioMeanDifferencePoints": _round(
                _mean_score(nearest_score)
                - _mean_score(incumbent_score)
            ),
            "exactScenarioWinProbability": _round(
                _score_win_probability(nearest_score, incumbent_score)
            ),
            "removedPlayers": [
                _player_identity(by_id[value])
                for value in sorted(incumbent_ids - nearest_ids)
            ],
            "addedPlayers": [
                _player_identity(by_id[value])
                for value in sorted(nearest_ids - incumbent_ids)
            ],
        },
        "selectedPlayerExclusionAudits": exclusions,
        "scenarioPathBootstrap": bootstrap,
        "stabilitySummary": {
            "coreFrequencyThreshold": CORE_FREQUENCY_THRESHOLD,
            "fragileFrequencyThreshold": FRAGILE_FREQUENCY_THRESHOLD,
            "coreSelectedPlayerCount": len(core),
            "fragileSelectedPlayerCount": len(fragile),
            "coreSelectedPlayers": core,
            "fragileSelectedPlayers": fragile,
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
        },
        "limitations": [
            (
                "The incumbent is globally optimal only for the declared "
                "six-Gameweek linear expected-points surrogate and its "
                "current forecast inputs and constraints."
            ),
            (
                "Exclusion regret is a conditional model quantity, not a "
                "causal estimate of a player's realised FPL value."
            ),
            (
                "The bootstrap resamples only the retained paired scenario "
                "paths. It measures finite-path sensitivity, not uncertainty "
                "from model choice, injuries, lineups, prices or data errors."
            ),
            (
                "Exact FPL scenario rescoring occurs after optimisation, so "
                "a conditional squad can occasionally score better under "
                "the nonlinear scorer despite a worse surrogate objective."
            ),
            (
                "This diagnostic cannot promote a policy or influence "
                "served advice."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "policy": artifact["policy"],
            "source": artifact["source"],
            "bootstrapMethod": BOOTSTRAP_METHOD,
            "bootstrapSeed": BOOTSTRAP_SEED,
            "bootstrapReplicates": bootstrap_replicates,
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _bootstrap_stability(
    candidates: Sequence[Mapping[str, Any]],
    week_matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
    incumbent_ids: set[int],
    bootstrap_replicates: int,
) -> Dict[str, Any]:
    scenario_count = int(week_matrices[0][0].shape[0])
    rng = np.random.default_rng(BOOTSTRAP_SEED)
    selection_counts: Dict[int, int] = {}
    overlaps: List[int] = []
    selection_hashes: List[str] = []
    for _ in range(bootstrap_replicates):
        indices = rng.integers(
            0,
            scenario_count,
            size=scenario_count,
            endpoint=False,
        )
        resampled = [
            (points[indices, :], played[indices, :])
            for points, played in week_matrices
        ]
        selection, _ = _optimise_horizon(
            candidates,
            resampled,
            HORIZON_GAMEWEEKS,
            CVAR_WEIGHT,
        )
        selected = {int(value) for value in selection["playerIds"]}
        overlaps.append(len(incumbent_ids & selected))
        selection_hashes.append(_sha256(sorted(selected)))
        for player_id in selected:
            selection_counts[player_id] = (
                selection_counts.get(player_id, 0) + 1
            )

    by_id = {
        int(player["playerId"]): player for player in candidates
    }
    frequencies = []
    for player_id, count in selection_counts.items():
        player = by_id[player_id]
        frequencies.append(
            {
                **_player_identity(player),
                "selectionCount": count,
                "selectionFrequency": _round(
                    count / bootstrap_replicates
                ),
                "isInIncumbent": player_id in incumbent_ids,
            }
        )
    frequencies.sort(
        key=lambda row: (
            -float(row["selectionFrequency"]),
            str(row["position"]),
            str(row["webName"]),
            int(row["playerId"]),
        )
    )
    overlap_values = np.asarray(overlaps, dtype=float)
    return {
        "method": BOOTSTRAP_METHOD,
        "seed": BOOTSTRAP_SEED,
        "replicateCount": bootstrap_replicates,
        "resampledPathCountPerReplicate": scenario_count,
        "pairedAcrossGameweeks": True,
        "uniqueSquadCount": len(set(selection_hashes)),
        "incumbentExactReproductionFrequency": _round(
            float(np.mean(overlap_values == len(incumbent_ids)))
        ),
        "incumbentSquadOverlap": {
            "minimumPlayers": int(np.min(overlap_values)),
            "medianPlayers": _round(float(np.median(overlap_values))),
            "meanPlayers": _round(float(np.mean(overlap_values))),
            "maximumPlayers": int(np.max(overlap_values)),
        },
        "playerSelectionFrequencies": frequencies,
    }


def _player_identity(player: Mapping[str, Any]) -> Dict[str, Any]:
    return {
        "playerId": int(player["playerId"]),
        "webName": str(player["webName"]),
        "teamId": int(player["teamId"]),
        "teamName": str(player["teamName"]),
        "position": str(player["position"]),
        "priceTenths": int(player["priceTenths"]),
    }


def _mean_score(score: Mapping[str, Any]) -> float:
    return float(score["cumulative"]["meanPoints"])


def _score_win_probability(
    candidate: Mapping[str, Any],
    incumbent: Mapping[str, Any],
) -> float:
    candidate_values = np.asarray(
        candidate["pathTotalPoints"],
        dtype=float,
    )
    incumbent_values = np.asarray(
        incumbent["pathTotalPoints"],
        dtype=float,
    )
    _require(
        candidate_values.shape == incumbent_values.shape,
        "opening-audit.score-shape",
        "Conditional and incumbent scenario scores are not paired.",
    )
    return float(np.mean(candidate_values > incumbent_values))


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Measure conditional optimality, player exclusion regret and "
            "paired-path bootstrap stability for the selected current "
            "opening squad."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_opening_squad_optimality_audit(
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
