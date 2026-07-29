from __future__ import annotations

import argparse
import itertools
import json
import sqlite3
import sys
from pathlib import Path
from typing import Any, Dict, Iterable, Mapping, Optional, Sequence, Tuple

import numpy as np

from .current_scenario_selection_score import (
    _public_result,
    _scenario_player_index,
    _score_selection,
    _sha256,
    _verified_document,
    build_current_scenario_selection_score,
)
from .scenario_reference import (
    ScenarioValidationError,
    compare_paired_scores,
)
from .temporal_ridge import TemporalRidgeError

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-selection-role-strategy-shadow"
ARTIFACT_VERSION = "current-selection-role-strategies-v1"
STATUS = "prospective-shadow-unscored"
SEARCH_VERSION = "deterministic-role-beam-v1"
BEAM_WIDTH = 12
SEARCH_ITERATIONS = 3
TAIL_FRACTION = 0.20
MAXIMUM_ARTIFACT_BYTES = 2 * 1024 * 1024

StrategyKey = Tuple[
    Tuple[int, ...],
    int,
    Tuple[int, ...],
    int,
    int,
]


def build_current_selection_strategies(
    database_path: Path | str,
) -> Dict[str, Any]:
    score_artifact = build_current_scenario_selection_score(database_path)
    scenario_artifact_id = int(
        score_artifact["scenarioSource"]["scenarioArtifactId"]
    )
    connection: Optional[sqlite3.Connection] = None
    try:
        connection = sqlite3.connect(
            f"file:{Path(database_path)}?mode=ro",
            uri=True,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        row = connection.execute(
            """
            SELECT document_json, content_sha256
            FROM joint_scenario_shadow_artifacts
            WHERE scenario_artifact_id = ?;
            """,
            (scenario_artifact_id,),
        ).fetchone()
        if row is None:
            raise TemporalRidgeError(
                "selection-strategies.scenario-not-found",
                "The bound joint scenario artifact is unavailable.",
            )
        scenario = _verified_document(
            row["document_json"],
            row["content_sha256"],
            "selection-strategies.scenario-content",
        )
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "selection-strategies.database-read",
            "The strategy inputs could not be read.",
        ) from exception
    finally:
        if connection is not None:
            connection.close()

    player_columns, positions = _scenario_player_index(scenario)
    point_rows = np.asarray(scenario["pointRows"])
    played_rows = np.asarray(scenario["playedRows"])
    base_selection = dict(score_artifact["model"]["selection"])
    base_result = _score_selection(
        base_selection,
        player_columns,
        positions,
        point_rows,
        played_rows,
    )
    base_key = _selection_key(base_selection)
    cache: Dict[StrategyKey, Dict[str, Any]] = {
        base_key: base_result,
    }
    strategies: Dict[str, Dict[str, Any]] = {}
    evaluated_by_strategy: Dict[str, int] = {}
    for strategy_id, objective_key in (
        ("balanced", "mean"),
        ("safer", "lower-tail-mean"),
        ("higherCeiling", "upper-tail-mean"),
    ):
        before = len(cache)
        selected = _search(
            base_selection,
            objective_key,
            player_columns,
            positions,
            point_rows,
            played_rows,
            cache,
        )
        evaluated_by_strategy[strategy_id] = len(cache) - before
        selected_result = cache[_selection_key(selected)]
        strategies[strategy_id] = {
            "strategyId": strategy_id,
            "objective": {
                "objectiveKey": objective_key,
                "tailFraction": (
                    None
                    if objective_key == "mean"
                    else TAIL_FRACTION
                ),
                "objectiveValue": _objective(
                    selected_result,
                    objective_key,
                ),
            },
            "isDistinctFromModel": (
                _selection_key(selected) != base_key
            ),
            "result": _public_result(selected_result),
            "vsModel": compare_paired_scores(
                base_result["_scores"],
                selected_result["_scores"],
            ),
        }

    data_identity = _sha256(
        {
            "selectionScoreRunIdentitySha256": score_artifact[
                "runIdentitySha256"
            ],
            "searchVersion": SEARCH_VERSION,
            "beamWidth": BEAM_WIDTH,
            "searchIterations": SEARCH_ITERATIONS,
            "tailFraction": TAIL_FRACTION,
        }
    )
    run_identity = _sha256(
        {
            "artifactVersion": ARTIFACT_VERSION,
            "dataIdentitySha256": data_identity,
        }
    )
    return {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "seasonCode": score_artifact["seasonCode"],
        "gameweek": score_artifact["gameweek"],
        "deadlineUtc": score_artifact["deadlineUtc"],
        "decisionCutoffUtc": score_artifact["decisionCutoffUtc"],
        "officialCaptureId": score_artifact["officialCaptureId"],
        "scenarioCount": score_artifact["scenarioCount"],
        "selectionScoreSource": {
            "dataIdentitySha256": score_artifact[
                "dataIdentitySha256"
            ],
            "runIdentitySha256": score_artifact[
                "runIdentitySha256"
            ],
            "scenarioSource": score_artifact["scenarioSource"],
            "modelSource": score_artifact["modelSource"],
        },
        "fixedSquadPlayerIds": base_result["selection"]["playerIds"],
        "model": _public_result(base_result),
        "strategies": strategies,
        "search": {
            "searchVersion": SEARCH_VERSION,
            "beamWidth": BEAM_WIDTH,
            "iterations": SEARCH_ITERATIONS,
            "tailFraction": TAIL_FRACTION,
            "uniqueCandidatesEvaluated": len(cache),
            "newCandidatesByStrategy": evaluated_by_strategy,
            "searchStatus": "bounded-heuristic-not-global-optimum",
        },
        "limitations": [
            (
                "Strategies change XI, bench order and captaincy only; "
                "the 15-player squad is fixed."
            ),
            (
                "The bounded beam search is deterministic but does not "
                "claim a global optimum."
            ),
            (
                "Objectives are selected on 38 retained empirical rows, "
                "so risk and tail estimates are coarse and exploratory."
            ),
            (
                "This prospective shadow cannot influence advice or "
                "mutate a user selection."
            ),
        ],
        "dataIdentitySha256": data_identity,
        "runIdentitySha256": run_identity,
    }


def _search(
    base_selection: Mapping[str, Any],
    objective_key: str,
    player_columns: Mapping[int, int],
    positions: Mapping[int, str],
    point_rows: np.ndarray,
    played_rows: np.ndarray,
    cache: Dict[StrategyKey, Dict[str, Any]],
) -> Dict[str, Any]:
    base = _normalise_selection(base_selection)
    beam = [base]
    for _ in range(SEARCH_ITERATIONS):
        pool: Dict[StrategyKey, Dict[str, Any]] = {}
        for selection in beam:
            for candidate in itertools.chain(
                (selection,),
                _neighbours(selection, positions),
            ):
                key = _selection_key(candidate)
                if key in pool:
                    continue
                if key not in cache:
                    try:
                        cache[key] = _score_selection(
                            candidate,
                            player_columns,
                            positions,
                            point_rows,
                            played_rows,
                        )
                    except (
                        ScenarioValidationError,
                        TemporalRidgeError,
                        KeyError,
                    ):
                        continue
                pool[key] = candidate
        ranked = sorted(
            pool.values(),
            key=lambda selection: (
                -_objective(
                    cache[_selection_key(selection)],
                    objective_key,
                ),
                -float(
                    cache[
                        _selection_key(selection)
                    ]["summary"]["meanPoints"]
                ),
                _selection_key(selection),
            ),
        )
        beam = ranked[:BEAM_WIDTH]
        if not beam:
            raise TemporalRidgeError(
                "selection-strategies.search-empty",
                "The bounded strategy search found no legal selection.",
            )
    return beam[0]


def _neighbours(
    selection: Mapping[str, Any],
    positions: Mapping[int, str],
) -> Iterable[Dict[str, Any]]:
    current = _normalise_selection(selection)
    starting = list(current["startingPlayerIds"])
    goalkeeper = int(current["replacementGoalkeeperPlayerId"])
    substitutes = list(current["outfieldSubstitutePlayerIds"])
    captain = int(current["captainPlayerId"])
    vice_captain = int(current["viceCaptainPlayerId"])

    for candidate_captain in starting:
        for candidate_vice in starting:
            if candidate_captain == candidate_vice:
                continue
            candidate = dict(current)
            candidate["captainPlayerId"] = candidate_captain
            candidate["viceCaptainPlayerId"] = candidate_vice
            yield candidate

    for order in itertools.permutations(substitutes):
        candidate = dict(current)
        candidate["outfieldSubstitutePlayerIds"] = list(order)
        yield candidate

    starting_goalkeepers = [
        player_id
        for player_id in starting
        if positions[player_id] == "goalkeeper"
    ]
    if len(starting_goalkeepers) == 1:
        yield _swap(
            current,
            starting_goalkeepers[0],
            goalkeeper,
            goalkeeper_swap=True,
        )

    for starter in starting:
        if positions[starter] == "goalkeeper":
            continue
        for substitute in substitutes:
            yield _swap(
                current,
                starter,
                substitute,
                goalkeeper_swap=False,
            )


def _swap(
    selection: Mapping[str, Any],
    starter: int,
    substitute: int,
    goalkeeper_swap: bool,
) -> Dict[str, Any]:
    candidate = _normalise_selection(selection)
    starting = list(candidate["startingPlayerIds"])
    starting[starting.index(starter)] = substitute
    candidate["startingPlayerIds"] = starting
    if goalkeeper_swap:
        candidate["replacementGoalkeeperPlayerId"] = starter
    else:
        substitutes = list(candidate["outfieldSubstitutePlayerIds"])
        substitutes[substitutes.index(substitute)] = starter
        candidate["outfieldSubstitutePlayerIds"] = substitutes
    if candidate["captainPlayerId"] == starter:
        candidate["captainPlayerId"] = substitute
    if candidate["viceCaptainPlayerId"] == starter:
        candidate["viceCaptainPlayerId"] = substitute
    return candidate


def _objective(
    result: Mapping[str, Any],
    objective_key: str,
) -> float:
    summary = result["summary"]
    mean = float(summary["meanPoints"])
    if objective_key == "mean":
        return mean
    values = np.sort(
        np.asarray(result["_scores"].total_points, dtype=np.float64)
    )
    tail_count = max(1, int(np.ceil(values.size * TAIL_FRACTION)))
    if objective_key == "lower-tail-mean":
        return float(np.mean(values[:tail_count]))
    if objective_key == "upper-tail-mean":
        return float(np.mean(values[-tail_count:]))
    raise ValueError(f"Unknown objective: {objective_key}")


def _normalise_selection(
    selection: Mapping[str, Any],
) -> Dict[str, Any]:
    return {
        "startingPlayerIds": [
            int(value)
            for value in selection["startingPlayerIds"]
        ],
        "captainPlayerId": int(selection["captainPlayerId"]),
        "viceCaptainPlayerId": int(selection["viceCaptainPlayerId"]),
        "replacementGoalkeeperPlayerId": int(
            selection["replacementGoalkeeperPlayerId"]
        ),
        "outfieldSubstitutePlayerIds": [
            int(value)
            for value in selection["outfieldSubstitutePlayerIds"]
        ],
    }


def _selection_key(
    selection: Mapping[str, Any],
) -> StrategyKey:
    return (
        tuple(int(value) for value in selection["startingPlayerIds"]),
        int(selection["replacementGoalkeeperPlayerId"]),
        tuple(
            int(value)
            for value in selection["outfieldSubstitutePlayerIds"]
        ),
        int(selection["captainPlayerId"]),
        int(selection["viceCaptainPlayerId"]),
    )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Generate bounded legal balanced, safer and higher-ceiling "
            "role strategies on the current fixed squad."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    options = parser.parse_args(arguments)
    if options.output.exists():
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "failed",
                    "errorCode": "output.already-exists",
                },
                sort_keys=True,
            ),
            file=sys.stderr,
        )
        return 1
    try:
        artifact = build_current_selection_strategies(
            options.database
        )
        encoded = (
            json.dumps(artifact, indent=2, sort_keys=True) + "\n"
        ).encode("utf-8")
        if len(encoded) > MAXIMUM_ARTIFACT_BYTES:
            raise TemporalRidgeError(
                "selection-strategies.output-too-large",
                "The strategy artifact exceeds its output boundary.",
            )
        options.output.write_bytes(encoded)
    except (OSError, TemporalRidgeError, ValueError) as exception:
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "failed",
                    "errorCode": getattr(
                        exception,
                        "code",
                        "selection-strategies.failed",
                    ),
                },
                sort_keys=True,
            ),
            file=sys.stderr,
        )
        return 1
    print(
        json.dumps(
            {
                "schemaVersion": SCHEMA_VERSION,
                "status": artifact["status"],
                "officialCaptureId": artifact[
                    "officialCaptureId"
                ],
                "scenarioCount": artifact["scenarioCount"],
                "candidateCount": artifact[
                    "search"
                ]["uniqueCandidatesEvaluated"],
                "outputFile": options.output.name,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
