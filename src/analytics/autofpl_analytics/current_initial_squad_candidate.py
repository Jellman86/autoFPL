from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

import numpy as np
from scipy.optimize import Bounds, LinearConstraint, milp

from .current_scenario_selection_score import (
    _model_selection,
    _public_result,
    _scenario_player_index,
    _score_selection,
    _sha256,
    _utc_instant,
    _verified_document,
)
from .scenario_reference import compare_paired_scores
from .temporal_ridge import TemporalRidgeError

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-initial-squad-quality-shadow"
ARTIFACT_VERSION = "current-initial-squad-quality-shadow-v1"
STATUS = "prospective-shadow-unscored"
OPTIMIZER_VERSION = "scipy-highs-linear-squad-surrogate-v1"
BENCH_WEIGHT = 0.08
MAXIMUM_ARTIFACT_BYTES = 2 * 1024 * 1024

POSITION_QUOTAS = {
    "goalkeeper": 2,
    "defender": 5,
    "midfielder": 5,
    "forward": 3,
}
STARTER_BOUNDS = {
    "goalkeeper": (1, 1),
    "defender": (3, 5),
    "midfielder": (2, 5),
    "forward": (1, 3),
}


def build_current_initial_squad_candidate(
    database_path: Path | str,
) -> Dict[str, Any]:
    scenario, scenario_source, forecast, forecast_source, official = (
        _read_inputs(database_path)
    )
    player_columns, positions = _scenario_player_index(scenario)
    point_rows = np.asarray(scenario.get("pointRows"), dtype=np.int64)
    played_rows = np.asarray(scenario.get("playedRows"), dtype=np.bool_)
    _require(
        point_rows.ndim == 2
        and point_rows.shape == played_rows.shape
        and point_rows.shape
        == (
            int(scenario["scenarioCount"]),
            int(scenario["playerCount"]),
        ),
        "initial-squad.matrix-shape",
        "The current scenario matrices do not match their declared shape.",
    )

    scenario_players = scenario.get("players")
    _require(
        isinstance(scenario_players, list)
        and len(scenario_players) == len(player_columns),
        "initial-squad.player-index",
        "The current scenario player index is incomplete.",
    )
    empirical_means = point_rows.mean(axis=0)
    candidates: List[Dict[str, Any]] = []
    for player in scenario_players:
        player_id = int(player["playerId"])
        row = official.get(player_id)
        _require(
            row is not None
            and int(row["teamId"]) == int(player["teamId"])
            and str(row["position"]) == str(player["position"]),
            "initial-squad.official-identity",
            "A scenario player does not match the exact official capture.",
        )
        if str(row["status"]) == "u":
            continue
        column = player_columns[player_id]
        candidates.append(
            {
                "playerId": player_id,
                "columnIndex": column,
                "webName": str(player.get("webName", player_id)),
                "teamId": int(row["teamId"]),
                "teamName": str(player.get("teamName", row["teamId"])),
                "position": str(row["position"]),
                "priceTenths": int(row["priceTenths"]),
                "officialStatus": str(row["status"]),
                "officialChanceOfPlayingNextRound": row["chanceNextRound"],
                "scenarioMeanPoints": float(empirical_means[column]),
                "pointModelMean": float(player["pointMean"]),
                "appearanceProbability": float(
                    player["appearanceProbability"]
                ),
                "pointHistoryIdentityStatus": str(
                    player["pointHistoryIdentityStatus"]
                ),
                "participationHistoryIdentityStatus": str(
                    player["participationHistoryIdentityStatus"]
                ),
            }
        )

    selection, solver = _optimise(candidates)
    candidate_result = _score_selection(
        selection,
        player_columns,
        positions,
        point_rows,
        played_rows,
    )
    model_result = _score_selection(
        _model_selection(forecast),
        player_columns,
        positions,
        point_rows,
        played_rows,
    )
    # Bind the source to the complete, canonical scored selection rather than
    # the smaller forecast input shape. The importer independently rebuilds
    # this definition from the immutable forecast before accepting it.
    forecast_source["selectionContentSha256"] = _sha256(
        model_result["selection"]
    )
    selected_ids = set(candidate_result["selection"]["playerIds"])
    selected_players = [
        {
            key: player[key]
            for key in (
                "playerId",
                "webName",
                "teamId",
                "teamName",
                "position",
                "priceTenths",
                "officialStatus",
                "officialChanceOfPlayingNextRound",
                "scenarioMeanPoints",
                "pointModelMean",
                "appearanceProbability",
                "pointHistoryIdentityStatus",
                "participationHistoryIdentityStatus",
            )
        }
        for player in candidates
        if player["playerId"] in selected_ids
    ]
    selected_players.sort(
        key=lambda player: candidate_result["selection"][
            "playerIds"
        ].index(player["playerId"])
    )

    data_identity = _sha256(
        {
            "scenarioArtifactContentSha256": scenario_source[
                "scenarioArtifactContentSha256"
            ],
            "forecastArtifactContentSha256": forecast_source[
                "forecastArtifactContentSha256"
            ],
            "optimizerVersion": OPTIMIZER_VERSION,
            "benchWeight": BENCH_WEIGHT,
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
        "seasonCode": scenario["seasonCode"],
        "gameweek": scenario["gameweek"],
        "deadlineUtc": scenario["deadlineUtc"],
        "decisionCutoffUtc": scenario["decisionCutoffUtc"],
        "officialCaptureId": scenario["officialCaptureId"],
        "scenarioCount": scenario["scenarioCount"],
        "scenarioSource": scenario_source,
        "modelSource": forecast_source,
        "objective": {
            "objectiveKey": "single-gameweek-linear-mean-surrogate",
            "starterWeight": 1.0,
            "benchWeight": BENCH_WEIGHT,
            "captainBonusWeight": 1.0,
        },
        "optimizer": solver,
        "candidatePoolCount": len(candidates),
        "budgetTenths": sum(
            int(player["priceTenths"])
            for player in selected_players
        ),
        "players": selected_players,
        "model": _public_result(model_result),
        "candidate": _public_result(candidate_result),
        "candidateVsModel": compare_paired_scores(
            model_result["_scores"],
            candidate_result["_scores"],
        ),
        "limitations": [
            (
                "This is prospective shadow evidence and cannot influence "
                "the served squad or mutate an owner selection."
            ),
            (
                "The solver is globally optimal only for its fixed linear "
                "single-Gameweek surrogate; auto-substitution value is "
                "measured afterwards on the retained joint scenarios."
            ),
            (
                "The 38 empirical joint rows and their point, participation "
                "and availability inputs remain unpromoted."
            ),
            (
                "Multi-Gameweek transfers, price changes and squad "
                "flexibility are not part of this artifact version."
            ),
        ],
        "dataIdentitySha256": data_identity,
        "runIdentitySha256": run_identity,
    }


def _read_inputs(
    database_path: Path | str,
) -> Tuple[
    Dict[str, Any],
    Dict[str, Any],
    Dict[str, Any],
    Dict[str, Any],
    Dict[int, Dict[str, Any]],
]:
    connection: Optional[sqlite3.Connection] = None
    try:
        connection = sqlite3.connect(
            f"file:{Path(database_path)}?mode=ro",
            uri=True,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        scenario_row = connection.execute(
            """
            SELECT scenario_artifact_id, official_capture_id,
                   document_json, content_sha256
            FROM joint_scenario_shadow_artifacts
            ORDER BY
                julianday(decision_cutoff_utc) DESC,
                scenario_artifact_id DESC
            LIMIT 1;
            """
        ).fetchone()
        if scenario_row is None:
            raise TemporalRidgeError(
                "initial-squad.scenario-not-found",
                "The current joint scenario artifact is unavailable.",
            )
        scenario = _verified_document(
            scenario_row["document_json"],
            scenario_row["content_sha256"],
            "initial-squad.scenario-content",
        )
        scenario_artifact_id = int(
            scenario_row["scenario_artifact_id"]
        )
        capture_id = int(scenario_row["official_capture_id"])
        _require(
            scenario.get("scenarioArtifactId")
            in (None, scenario_artifact_id)
            and scenario.get("officialCaptureId") == capture_id
            and scenario.get("scenarioArtifactContentSha256")
            in (None, str(scenario_row["content_sha256"])),
            "initial-squad.scenario-lineage",
            "The stored scenario identity does not match its document.",
        )

        forecast_row = connection.execute(
            """
            SELECT artifact_id, capture_id, document_json, content_sha256
            FROM baseline_forecast_artifacts
            WHERE capture_id = ?
            ORDER BY artifact_id DESC
            LIMIT 1;
            """,
            (capture_id,),
        ).fetchone()
        if forecast_row is None:
            raise TemporalRidgeError(
                "initial-squad.forecast-not-found",
                "The exact served model forecast is unavailable.",
            )
        forecast = _verified_document(
            forecast_row["document_json"],
            forecast_row["content_sha256"],
            "initial-squad.forecast-content",
        )
        _require(
            forecast.get("gameweek") == scenario.get("gameweek")
            and _utc_instant(forecast.get("decisionCutoffUtc"))
            == _utc_instant(scenario.get("decisionCutoffUtc"))
            and forecast.get("isSynthetic") is False,
            "initial-squad.forecast-lineage",
            "The served model forecast does not match the scenario target.",
        )

        latest_capture_id = connection.execute(
            "SELECT MAX(capture_id) FROM official_fpl_players;"
        ).fetchone()[0]
        _require(
            latest_capture_id is not None
            and int(latest_capture_id) == capture_id,
            "initial-squad.stale-scenario",
            "The joint scenario does not match the latest official capture.",
        )
        official_rows = connection.execute(
            """
            SELECT player_id, team_id, position, price_tenths,
                   status, chance_next_round
            FROM official_fpl_players
            WHERE capture_id = ?;
            """,
            (capture_id,),
        ).fetchall()
        official = {
            int(row["player_id"]): {
                "teamId": int(row["team_id"]),
                "position": str(row["position"]),
                "priceTenths": int(row["price_tenths"]),
                "status": str(row["status"]),
                "chanceNextRound": (
                    None
                    if row["chance_next_round"] is None
                    else int(row["chance_next_round"])
                ),
            }
            for row in official_rows
        }
        _require(
            official,
            "initial-squad.official-capture",
            "The exact official player capture is unavailable.",
        )
        return (
            scenario,
            {
                "scenarioArtifactId": scenario_artifact_id,
                "scenarioArtifactContentSha256": str(
                    scenario_row["content_sha256"]
                ),
                "scenarioContentSha256": scenario[
                    "scenarioContentSha256"
                ],
                "scenarioRunIdentitySha256": scenario[
                    "runIdentitySha256"
                ],
            },
            forecast,
            {
                "forecastArtifactId": int(forecast_row["artifact_id"]),
                "forecastArtifactContentSha256": str(
                    forecast_row["content_sha256"]
                ),
                "modelLabel": forecast["modelLabel"],
                "selectionContentSha256": "",
            },
            official,
        )
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "initial-squad.database-read",
            "The initial-squad inputs could not be read.",
        ) from exception
    finally:
        if connection is not None:
            connection.close()


def _optimise(
    candidates: Sequence[Mapping[str, Any]],
) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    _require(
        len(candidates) >= 15,
        "initial-squad.candidate-pool",
        "At least 15 exact-capture candidates are required.",
    )
    count = len(candidates)
    variable_count = count * 3
    objective = np.zeros(variable_count, dtype=np.float64)
    means = np.asarray(
        [float(player["scenarioMeanPoints"]) for player in candidates],
        dtype=np.float64,
    )
    objective[:count] = -(BENCH_WEIGHT * means)
    objective[count : 2 * count] = -((1.0 - BENCH_WEIGHT) * means)
    objective[2 * count :] = -means

    rows: List[np.ndarray] = []
    lower: List[float] = []
    upper: List[float] = []

    def add(
        coefficients: np.ndarray,
        minimum: float,
        maximum: float,
    ) -> None:
        rows.append(coefficients)
        lower.append(minimum)
        upper.append(maximum)

    squad = np.zeros(variable_count)
    squad[:count] = 1.0
    add(squad, 15, 15)
    for position, quota in POSITION_QUOTAS.items():
        row = np.zeros(variable_count)
        row[:count] = [
            1.0 if player["position"] == position else 0.0
            for player in candidates
        ]
        add(row, quota, quota)

    budget = np.zeros(variable_count)
    budget[:count] = [
        float(player["priceTenths"]) for player in candidates
    ]
    add(budget, 0, 1000)

    for team_id in sorted(
        {int(player["teamId"]) for player in candidates}
    ):
        row = np.zeros(variable_count)
        row[:count] = [
            1.0 if int(player["teamId"]) == team_id else 0.0
            for player in candidates
        ]
        add(row, 0, 3)

    starters = np.zeros(variable_count)
    starters[count : 2 * count] = 1.0
    add(starters, 11, 11)
    for position, (minimum, maximum) in STARTER_BOUNDS.items():
        row = np.zeros(variable_count)
        row[count : 2 * count] = [
            1.0 if player["position"] == position else 0.0
            for player in candidates
        ]
        add(row, minimum, maximum)

    captains = np.zeros(variable_count)
    captains[2 * count :] = 1.0
    add(captains, 1, 1)
    for index in range(count):
        starter_in_squad = np.zeros(variable_count)
        starter_in_squad[index] = -1.0
        starter_in_squad[count + index] = 1.0
        add(starter_in_squad, -np.inf, 0)

        captain_is_starter = np.zeros(variable_count)
        captain_is_starter[count + index] = -1.0
        captain_is_starter[2 * count + index] = 1.0
        add(captain_is_starter, -np.inf, 0)

    result = milp(
        c=objective,
        integrality=np.ones(variable_count),
        bounds=Bounds(
            np.zeros(variable_count),
            np.ones(variable_count),
        ),
        constraints=LinearConstraint(
            np.vstack(rows),
            np.asarray(lower),
            np.asarray(upper),
        ),
        options={
            "presolve": True,
            "time_limit": 30.0,
            "mip_rel_gap": 0.0,
        },
    )
    _require(
        result.success
        and result.x is not None
        and result.mip_gap is not None
        and float(result.mip_gap) <= 0.0,
        "initial-squad.optimizer",
        "The global linear squad surrogate did not reach an exact optimum.",
    )
    values = np.rint(result.x).astype(np.int8)
    squad_ids = [
        int(candidates[index]["playerId"])
        for index in range(count)
        if values[index] == 1
    ]
    starting_ids = [
        int(candidates[index]["playerId"])
        for index in range(count)
        if values[count + index] == 1
    ]
    captain_ids = [
        int(candidates[index]["playerId"])
        for index in range(count)
        if values[2 * count + index] == 1
    ]
    _require(
        len(squad_ids) == 15
        and len(starting_ids) == 11
        and len(captain_ids) == 1,
        "initial-squad.optimizer-result",
        "The optimizer returned an incomplete selection.",
    )
    by_id = {
        int(player["playerId"]): player for player in candidates
    }
    vice = max(
        (
            player_id
            for player_id in starting_ids
            if player_id != captain_ids[0]
        ),
        key=lambda player_id: (
            float(by_id[player_id]["scenarioMeanPoints"]),
            -player_id,
        ),
    )
    bench_goalkeepers = [
        player_id
        for player_id in squad_ids
        if player_id not in starting_ids
        and by_id[player_id]["position"] == "goalkeeper"
    ]
    substitutes = sorted(
        (
            player_id
            for player_id in squad_ids
            if player_id not in starting_ids
            and by_id[player_id]["position"] != "goalkeeper"
        ),
        key=lambda player_id: (
            -float(by_id[player_id]["scenarioMeanPoints"]),
            player_id,
        ),
    )
    _require(
        len(bench_goalkeepers) == 1 and len(substitutes) == 3,
        "initial-squad.optimizer-result",
        "The optimizer returned an invalid bench.",
    )
    return (
        {
            "startingPlayerIds": sorted(starting_ids),
            "captainPlayerId": captain_ids[0],
            "viceCaptainPlayerId": vice,
            "replacementGoalkeeperPlayerId": bench_goalkeepers[0],
            "outfieldSubstitutePlayerIds": substitutes,
        },
        {
            "optimizerVersion": OPTIMIZER_VERSION,
            "solver": "scipy.optimize.milp-highs",
            "status": "global-linear-surrogate-optimum",
            "mipGap": float(result.mip_gap),
            "benchWeight": BENCH_WEIGHT,
        },
    )


def _require(
    condition: bool,
    code: str,
    message: str,
) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Build the current globally constrained initial-squad "
            "quality shadow."
        )
    )
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser


def main(arguments: Optional[Sequence[str]] = None) -> int:
    args = _parser().parse_args(arguments)
    if args.output.exists():
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
        document = build_current_initial_squad_candidate(
            args.database
        )
    except TemporalRidgeError as exception:
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "failed",
                    "errorCode": exception.code,
                },
                sort_keys=True,
            ),
            file=sys.stderr,
        )
        return 1
    encoded = json.dumps(
        document,
        ensure_ascii=False,
        indent=2,
        sort_keys=True,
    )
    if len(encoded.encode("utf-8")) > MAXIMUM_ARTIFACT_BYTES:
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "failed",
                    "errorCode": "output.too-large",
                },
                sort_keys=True,
            ),
            file=sys.stderr,
        )
        return 1
    args.output.write_text(encoded + "\n", encoding="utf-8")
    print(
        json.dumps(
            {
                "schemaVersion": SCHEMA_VERSION,
                "status": document["status"],
                "officialCaptureId": document[
                    "officialCaptureId"
                ],
                "candidatePoolCount": document[
                    "candidatePoolCount"
                ],
                "scenarioCount": document["scenarioCount"],
                "outputFile": args.output.name,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
