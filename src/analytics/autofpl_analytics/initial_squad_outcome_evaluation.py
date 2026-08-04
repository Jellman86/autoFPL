from __future__ import annotations

import argparse
import hashlib
import json
import math
import sqlite3
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

import numpy as np

from .current_scenario_selection_score import (
    _sha256,
    _verified_document,
)
from .scenario_reference import (
    SelectionDefinition,
    ScenarioScoreBatch,
    score_selection_scenarios,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "initial-squad-prospective-outcome-evaluation"
ARTIFACT_VERSION = "initial-squad-prospective-outcome-evaluation-v1"
STATUS = "prospective-outcome-evaluated"
ENGINE_VERSION = "cpu-joint-scenario-reference-v1"
MAXIMUM_ARTIFACT_BYTES = 2 * 1024 * 1024


def build_initial_squad_outcome_evaluation(
    database_path: Path | str,
) -> Dict[str, Any]:
    path = Path(database_path)
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    connection: Optional[sqlite3.Connection] = None
    try:
        connection = sqlite3.connect(
            f"file:{path.resolve()}?mode=ro",
            uri=True,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        initial_row = connection.execute(
            """
            SELECT initial_squad_artifact_id, official_capture_id,
                   season_code, gameweek, decision_cutoff_utc,
                   document_json, content_sha256
            FROM initial_squad_quality_shadow_artifacts
            ORDER BY
                julianday(decision_cutoff_utc) DESC,
                initial_squad_artifact_id DESC
            LIMIT 1;
            """
        ).fetchone()
        if initial_row is None:
            raise TemporalRidgeError(
                "initial-squad.not-found",
                "No frozen initial-squad quality artifact is available.",
            )
        initial = _verified_document(
            initial_row["document_json"],
            initial_row["content_sha256"],
            "initial-squad.content",
        )
        initial_id = int(initial_row["initial_squad_artifact_id"])
        season_code = str(initial_row["season_code"])
        gameweek = int(initial_row["gameweek"])
        _require(
            initial.get("status") == "prospective-shadow-unscored"
            and initial.get("isPromoted") is False
            and initial.get("influencesAdvice") is False
            and int(initial.get("officialCaptureId", 0))
            == int(initial_row["official_capture_id"])
            and initial.get("seasonCode") == season_code
            and int(initial.get("gameweek", 0)) == gameweek,
            "initial-squad.lineage",
            "The frozen initial-squad identity is inconsistent.",
        )

        scenario_id = int(initial["scenarioSource"]["scenarioArtifactId"])
        scenario_row = connection.execute(
            """
            SELECT document_json, content_sha256
            FROM joint_scenario_shadow_artifacts
            WHERE scenario_artifact_id = ?;
            """,
            (scenario_id,),
        ).fetchone()
        if scenario_row is None:
            raise TemporalRidgeError(
                "scenario.not-found",
                "The frozen candidate's joint scenario is unavailable.",
            )
        scenario = _verified_document(
            scenario_row["document_json"],
            scenario_row["content_sha256"],
            "scenario.content",
        )
        _require(
            str(scenario_row["content_sha256"])
            == initial["scenarioSource"][
                "scenarioArtifactContentSha256"
            ],
            "scenario.lineage",
            "The frozen candidate does not match its joint scenario.",
        )

        baseline_row = connection.execute(
            """
            SELECT forecast_artifact_id, document_json, content_sha256
            FROM player_gameweek_forecast_artifacts
            WHERE official_capture_id = ?
              AND season_code = ?
              AND gameweek = ?
            ORDER BY forecast_artifact_id DESC
            LIMIT 1;
            """,
            (
                int(initial_row["official_capture_id"]),
                season_code,
                gameweek,
            ),
        ).fetchone()
        if baseline_row is None:
            raise TemporalRidgeError(
                "baseline.not-found",
                "The exact full-cohort Baseline v0 artifact is unavailable.",
            )
        baseline = _verified_document(
            baseline_row["document_json"],
            baseline_row["content_sha256"],
            "baseline.content",
        )

        outcome_row = connection.execute(
            """
            SELECT outcome_capture_id, reference_capture_id,
                   available_at_utc, live_sha256, live_json, player_count
            FROM official_fpl_outcome_captures
            WHERE season_code = ?
              AND gameweek = ?
              AND julianday(available_at_utc)
                    > julianday(?)
            ORDER BY
                julianday(available_at_utc) DESC,
                outcome_capture_id DESC
            LIMIT 1;
            """,
            (season_code, gameweek, initial["deadlineUtc"]),
        ).fetchone()
        if outcome_row is None:
            raise TemporalRidgeError(
                "outcome.not-found",
                "The final official outcome is not available yet.",
            )
        live_bytes = bytes(outcome_row["live_json"])
        _require(
            live_bytes
            and len(live_bytes) <= 8 * MAXIMUM_ARTIFACT_BYTES
            and hashlib.sha256(live_bytes).hexdigest()
            == str(outcome_row["live_sha256"]),
            "outcome.content",
            "The official outcome content hash is invalid.",
        )
        outcome_id = int(outcome_row["outcome_capture_id"])
        outcome_rows = connection.execute(
            """
            SELECT player_id, minutes, total_points
            FROM official_fpl_player_outcomes
            WHERE outcome_capture_id = ?
            ORDER BY player_id;
            """,
            (outcome_id,),
        ).fetchall()
        _require(
            len(outcome_rows) == int(outcome_row["player_count"])
            and len({int(row["player_id"]) for row in outcome_rows})
            == len(outcome_rows),
            "outcome.coverage",
            "The official outcome player coverage is incomplete.",
        )
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "database.read",
            "The prospective outcome inputs could not be read.",
        ) from exception
    finally:
        if connection is not None:
            connection.close()

    outcomes = {
        int(row["player_id"]): {
            "minutes": int(row["minutes"]),
            "totalPoints": int(row["total_points"]),
        }
        for row in outcome_rows
    }
    scenario_players = scenario.get("players")
    point_rows = np.asarray(scenario.get("pointRows"), dtype=np.int64)
    _require(
        isinstance(scenario_players, list)
        and len(scenario_players) == int(scenario["playerCount"])
        and point_rows.shape
        == (
            int(scenario["scenarioCount"]),
            int(scenario["playerCount"]),
        ),
        "scenario.shape",
        "The frozen scenario player index or matrix is invalid.",
    )
    scenario_ids = [int(player["playerId"]) for player in scenario_players]
    _require(
        len(set(scenario_ids)) == len(scenario_ids)
        and set(scenario_ids).issubset(outcomes),
        "outcome.scenario-coverage",
        "The official outcome does not cover every frozen scenario player.",
    )
    baseline_players = baseline.get("players")
    _require(
        isinstance(baseline_players, list)
        and baseline_players
        and {
            int(player["playerId"]) for player in baseline_players
        }.issubset(outcomes),
        "outcome.baseline-coverage",
        "The official outcome does not cover every Baseline v0 player.",
    )

    model_score = _score_actual(initial["model"]["selection"], outcomes)
    candidate_score = _score_actual(
        initial["candidate"]["selection"],
        outcomes,
    )
    baseline_metrics = _point_metrics(
        np.asarray(
            [
                float(player["expectedPoints"])
                for player in baseline_players
            ],
            dtype=np.float64,
        ),
        np.asarray(
            [
                outcomes[int(player["playerId"])]["totalPoints"]
                for player in baseline_players
            ],
            dtype=np.float64,
        ),
    )
    scenario_actual = np.asarray(
        [outcomes[player_id]["totalPoints"] for player_id in scenario_ids],
        dtype=np.float64,
    )
    scenario_means = point_rows.mean(axis=0)
    point_metrics = _point_metrics(scenario_means, scenario_actual)
    point_metrics["meanCrps"] = _round(
        float(
            np.mean(
                [
                    _empirical_crps(
                        point_rows[:, column].astype(np.float64),
                        scenario_actual[column],
                    )
                    for column in range(len(scenario_ids))
                ]
            )
        )
    )
    appearance_probabilities = np.asarray(
        [
            float(player["appearanceProbability"])
            for player in scenario_players
        ],
        dtype=np.float64,
    )
    appeared = np.asarray(
        [
            outcomes[player_id]["minutes"] > 0
            for player_id in scenario_ids
        ],
        dtype=np.float64,
    )
    appearance_metrics = _probability_metrics(
        appearance_probabilities,
        appeared,
    )

    result: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromotionDecision": False,
        "seasonCode": season_code,
        "gameweek": gameweek,
        "deadlineUtc": initial["deadlineUtc"],
        "initialSquadSource": {
            "initialSquadArtifactId": initial_id,
            "initialSquadArtifactContentSha256": str(
                initial_row["content_sha256"]
            ),
            "decisionCutoffUtc": str(initial_row["decision_cutoff_utc"]),
        },
        "scenarioSource": {
            "scenarioArtifactId": scenario_id,
            "scenarioArtifactContentSha256": str(
                scenario_row["content_sha256"]
            ),
            "scenarioCount": int(scenario["scenarioCount"]),
        },
        "baselineSource": {
            "forecastArtifactId": int(
                baseline_row["forecast_artifact_id"]
            ),
            "forecastArtifactContentSha256": str(
                baseline_row["content_sha256"]
            ),
            "modelKey": baseline["modelKey"],
        },
        "outcomeSource": {
            "outcomeCaptureId": outcome_id,
            "referenceCaptureId": int(
                outcome_row["reference_capture_id"]
            ),
            "availableAtUtc": str(outcome_row["available_at_utc"]),
            "liveSha256": str(outcome_row["live_sha256"]),
            "playerCount": int(outcome_row["player_count"]),
        },
        "selectionOutcome": {
            "engineVersion": ENGINE_VERSION,
            "model": model_score,
            "candidate": candidate_score,
            "candidatePointsDelta": (
                candidate_score["totalPoints"]
                - model_score["totalPoints"]
            ),
        },
        "componentEvaluation": {
            "baselinePointMean": {
                "cohortCount": len(baseline_players),
                **baseline_metrics,
            },
            "jointScenarioPointDistribution": {
                "cohortCount": len(scenario_ids),
                **point_metrics,
            },
            "jointScenarioAppearanceProbability": {
                "cohortCount": len(scenario_ids),
                **appearance_metrics,
            },
        },
        "limitations": [
            (
                "This is the first untouched prospective outcome fold and "
                "cannot by itself promote a model or decision policy."
            ),
            (
                "Selection utility is the realised score of the frozen GW1 "
                "roles; it does not estimate a multi-Gameweek squad value."
            ),
            (
                "Component cohorts are reported separately because Baseline "
                "v0 and the scenario artifact have different eligibility "
                "boundaries."
            ),
        ],
    }
    result["dataIdentitySha256"] = _sha256(
        {
            "baselineArtifactContentSha256": result["baselineSource"][
                "forecastArtifactContentSha256"
            ],
            "initialSquadArtifactContentSha256": result[
                "initialSquadSource"
            ]["initialSquadArtifactContentSha256"],
            "outcomeLiveSha256": result["outcomeSource"]["liveSha256"],
            "scenarioArtifactContentSha256": result["scenarioSource"][
                "scenarioArtifactContentSha256"
            ],
        }
    )
    result["runIdentitySha256"] = _sha256(
        {
            "artifactVersion": ARTIFACT_VERSION,
            "dataIdentitySha256": result["dataIdentitySha256"],
        }
    )
    return result


def _score_actual(
    selection: Mapping[str, Any],
    outcomes: Mapping[int, Mapping[str, int]],
) -> Dict[str, Any]:
    player_ids = tuple(int(value) for value in selection["playerIds"])
    _require(
        set(player_ids).issubset(outcomes),
        "outcome.selection-coverage",
        "The official outcome does not cover a selected player.",
    )
    definition = SelectionDefinition.create(
        player_ids=player_ids,
        positions=tuple(str(value) for value in selection["positions"]),
        starting_player_ids=tuple(
            int(value) for value in selection["startingPlayerIds"]
        ),
        replacement_goalkeeper_player_id=int(
            selection["replacementGoalkeeperPlayerId"]
        ),
        outfield_substitute_player_ids=tuple(
            int(value)
            for value in selection["outfieldSubstitutePlayerIds"]
        ),
        captain_player_id=int(selection["captainPlayerId"]),
        vice_captain_player_id=int(selection["viceCaptainPlayerId"]),
    )
    points = np.asarray(
        [[outcomes[player_id]["totalPoints"] for player_id in player_ids]],
        dtype=np.int64,
    )
    played = np.asarray(
        [[outcomes[player_id]["minutes"] > 0 for player_id in player_ids]],
        dtype=np.bool_,
    )
    score = score_selection_scenarios(definition, points, played)
    return _score_document(score)


def _score_document(score: ScenarioScoreBatch) -> Dict[str, Any]:
    return {
        "totalPoints": int(score.total_points[0]),
        "captainBonusPoints": int(score.captain_bonus_points[0]),
        "activatedSubstituteCount": int(
            score.activated_substitute_count[0]
        ),
        "unreplacedStarterCount": int(score.unreplaced_starter_count[0]),
    }


def _point_metrics(
    predictions: np.ndarray,
    actual: np.ndarray,
) -> Dict[str, float]:
    _require(
        predictions.shape == actual.shape
        and predictions.ndim == 1
        and predictions.size > 0
        and np.all(np.isfinite(predictions))
        and np.all(np.isfinite(actual)),
        "metric.point-input",
        "Point metric inputs are invalid.",
    )
    errors = predictions - actual
    return {
        "mae": _round(float(np.mean(np.abs(errors)))),
        "rmse": _round(float(np.sqrt(np.mean(np.square(errors))))),
        "bias": _round(float(np.mean(errors))),
    }


def _probability_metrics(
    probabilities: np.ndarray,
    actual: np.ndarray,
) -> Dict[str, float]:
    _require(
        probabilities.shape == actual.shape
        and probabilities.ndim == 1
        and probabilities.size > 0
        and np.all(np.isfinite(probabilities))
        and np.all((probabilities >= 0) & (probabilities <= 1))
        and np.all((actual == 0) | (actual == 1)),
        "metric.probability-input",
        "Probability metric inputs are invalid.",
    )
    clipped = np.clip(probabilities, 1e-15, 1 - 1e-15)
    return {
        "brierScore": _round(
            float(np.mean(np.square(probabilities - actual)))
        ),
        "logLoss": _round(
            float(
                -np.mean(
                    actual * np.log(clipped)
                    + (1 - actual) * np.log(1 - clipped)
                )
            )
        ),
        "predictedRate": _round(float(np.mean(probabilities))),
        "observedRate": _round(float(np.mean(actual))),
    }


def _empirical_crps(samples: np.ndarray, actual: float) -> float:
    _require(
        samples.ndim == 1
        and samples.size > 0
        and np.all(np.isfinite(samples))
        and math.isfinite(actual),
        "metric.crps-input",
        "CRPS inputs are invalid.",
    )
    absolute_error = np.mean(np.abs(samples - actual))
    pairwise = np.mean(np.abs(samples[:, None] - samples[None, :]))
    return float(absolute_error - 0.5 * pairwise)


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Score the frozen initial-squad candidate on its first later "
            "official outcome without retraining."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = build_initial_squad_outcome_evaluation(options.database)
        output = report
        _write_report(output, options.output)
    except TemporalRidgeError as exception:
        status = (
            "waiting-for-official-outcome"
            if exception.code == "outcome.not-found"
            else "failed"
        )
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": status,
                    "errorCode": exception.code,
                },
                sort_keys=True,
            ),
            file=sys.stderr,
        )
        return 0 if status == "waiting-for-official-outcome" else 2
    print(
        json.dumps(
            {
                "schemaVersion": SCHEMA_VERSION,
                "status": output["status"],
                "seasonCode": output["seasonCode"],
                "gameweek": output["gameweek"],
                "outcomeCaptureId": output["outcomeSource"][
                    "outcomeCaptureId"
                ],
                "candidatePointsDelta": output["selectionOutcome"][
                    "candidatePointsDelta"
                ],
                "outputFile": None
                if options.output is None
                else str(options.output),
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
