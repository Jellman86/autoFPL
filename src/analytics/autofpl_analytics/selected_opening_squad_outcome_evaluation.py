from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

from .current_scenario_selection_score import _verified_document
from .initial_squad_outcome_evaluation import _score_actual
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "selected-opening-squad-prospective-outcome-evaluation"
ARTIFACT_VERSION = (
    "selected-opening-squad-prospective-outcome-evaluation-v1"
)
REGISTRATION_ARTIFACT_VERSION = (
    "selected-opening-squad-prospective-registration-v1"
)
REGISTRATION_DATA_IDENTITY = (
    "7b875e15eef7672d2b177f35c74c85bc59151cbce930abd8ed2e5ce01c09c887"
)
ENGINE_VERSION = "cpu-joint-scenario-reference-v1"
OUTCOME_GAMEWEEKS = tuple(range(1, 9))
PRIMARY_BENCHMARK_KEY = "served-baseline-v0-gw1-roles-held"
DIAGNOSTIC_BENCHMARK_KEY = "single-gameweek-optimizer-gw1-roles-held"
MINIMUM_PRIMARY_DELTA = 2
MAXIMUM_ARTIFACT_BYTES = 2 * 1024 * 1024


def build_selected_opening_squad_outcome_evaluation(
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
        selected_row = connection.execute(
            """
            SELECT selected_opening_squad_artifact_id,
                   official_capture_id, season_code, opening_gameweek,
                   decision_cutoff_utc, document_json, content_sha256
            FROM selected_opening_squad_shadow_artifacts
            WHERE season_code = '2026-27'
              AND opening_gameweek = 1
              AND julianday(decision_cutoff_utc) < julianday(
                    json_extract(document_json, '$.deadlineUtc')
              )
            ORDER BY
                julianday(decision_cutoff_utc) DESC,
                selected_opening_squad_artifact_id DESC
            LIMIT 1;
            """
        ).fetchone()
        if selected_row is None:
            raise TemporalRidgeError(
                "selected-opening.not-found",
                "No frozen selected opening squad is available.",
            )
        selected = _verified_document(
            selected_row["document_json"],
            selected_row["content_sha256"],
            "selected-opening.content",
        )
        _require_selected(selected, selected_row)

        benchmark_row = connection.execute(
            """
            SELECT initial_squad_artifact_id, document_json, content_sha256
            FROM initial_squad_quality_shadow_artifacts
            WHERE official_capture_id = ?
              AND season_code = ?
              AND gameweek = 1
            ORDER BY initial_squad_artifact_id DESC
            LIMIT 1;
            """,
            (
                int(selected_row["official_capture_id"]),
                str(selected_row["season_code"]),
            ),
        ).fetchone()
        if benchmark_row is None:
            raise TemporalRidgeError(
                "benchmark.not-found",
                "The exact predeadline incumbent benchmark is unavailable.",
            )
        benchmark = _verified_document(
            benchmark_row["document_json"],
            benchmark_row["content_sha256"],
            "benchmark.content",
        )
        _require_benchmark(
            benchmark,
            int(selected_row["official_capture_id"]),
        )
        outcome_rows = connection.execute(
            """
            SELECT outcome.outcome_capture_id,
                   outcome.gameweek,
                   outcome.reference_capture_id,
                   outcome.available_at_utc,
                   outcome.live_sha256,
                   outcome.live_json,
                   outcome.player_count,
                   event.deadline_utc
            FROM official_fpl_outcome_captures AS outcome
            JOIN official_fpl_events AS event
              ON event.capture_id = outcome.reference_capture_id
             AND event.event_id = outcome.gameweek
            WHERE outcome.season_code = ?
              AND outcome.gameweek BETWEEN 1 AND 8
              AND julianday(outcome.available_at_utc)
                    > julianday(event.deadline_utc)
            ORDER BY
                outcome.gameweek,
                julianday(outcome.available_at_utc) DESC,
                outcome.outcome_capture_id DESC;
            """,
            (str(selected_row["season_code"]),),
        ).fetchall()
        latest_outcomes: Dict[int, sqlite3.Row] = {}
        for row in outcome_rows:
            latest_outcomes.setdefault(int(row["gameweek"]), row)
        if not latest_outcomes:
            raise TemporalRidgeError(
                "outcome.not-found",
                "No eligible official opening evaluation outcome is available.",
            )
        _require(
            set(latest_outcomes).issubset(OUTCOME_GAMEWEEKS),
            "outcome.gameweek",
            "An outcome lies outside the registered evaluation window.",
        )
        outcomes_by_gameweek = {
            gameweek: _read_outcome(connection, row)
            for gameweek, row in latest_outcomes.items()
        }
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "database.read",
            "The selected opening outcome inputs could not be read.",
        ) from exception
    finally:
        if connection is not None:
            connection.close()

    selected_positions = {
        int(player["playerId"]): str(player["position"])
        for player in selected["selection"]["players"]
    }
    selected_roles = {
        int(row["gameweek"]): _selected_role_definition(
            selected["selection"]["playerIds"],
            selected_positions,
            row,
        )
        for row in selected["selection"]["gameweeks"]
    }
    benchmarks = {
        PRIMARY_BENCHMARK_KEY: benchmark["model"]["selection"],
        DIAGNOSTIC_BENCHMARK_KEY: benchmark["candidate"]["selection"],
    }
    weekly = []
    totals = {
        "selectedPolicy": 0,
        PRIMARY_BENCHMARK_KEY: 0,
        DIAGNOSTIC_BENCHMARK_KEY: 0,
    }
    outcome_sources = []
    for gameweek in sorted(outcomes_by_gameweek):
        source, outcomes = outcomes_by_gameweek[gameweek]
        selected_score = _score_actual(
            selected_roles[gameweek],
            outcomes,
        )
        benchmark_scores = {
            key: _score_actual(selection, outcomes)
            for key, selection in benchmarks.items()
        }
        selected_points = int(selected_score["totalPoints"])
        totals["selectedPolicy"] += selected_points
        for key, score in benchmark_scores.items():
            totals[key] += int(score["totalPoints"])
        weekly.append(
            {
                "gameweek": gameweek,
                "selectedPolicy": selected_score,
                "benchmarks": benchmark_scores,
                "primaryBenchmarkDelta": (
                    selected_points
                    - int(
                        benchmark_scores[PRIMARY_BENCHMARK_KEY][
                            "totalPoints"
                        ]
                    )
                ),
            }
        )
        outcome_sources.append(source)

    observed_gameweeks = [row["gameweek"] for row in weekly]
    is_complete = observed_gameweeks == list(OUTCOME_GAMEWEEKS)
    primary_delta = (
        totals["selectedPolicy"] - totals[PRIMARY_BENCHMARK_KEY]
    )
    diagnostic_delta = (
        totals["selectedPolicy"] - totals[DIAGNOSTIC_BENCHMARK_KEY]
    )
    preseason_p10 = float(
        selected["preseasonScenarioScore"]["cumulative"]["p10Points"]
    )
    gate = {
        "status": (
            "complete" if is_complete else "waiting-for-eight-outcomes"
        ),
        "requiredOutcomeGameweeks": list(OUTCOME_GAMEWEEKS),
        "observedOutcomeGameweeks": observed_gameweeks,
        "primaryBenchmarkKey": PRIMARY_BENCHMARK_KEY,
        "minimumPrimaryBenchmarkDelta": MINIMUM_PRIMARY_DELTA,
        "preseasonCumulativeP10Points": _round(preseason_p10),
        "checks": {
            "allEightOutcomesAvailable": is_complete,
            "primaryBenchmarkDeltaAtLeastTwoPoints": (
                primary_delta >= MINIMUM_PRIMARY_DELTA
                if is_complete
                else None
            ),
            "selectedScoreAtOrAbovePreseasonP10": (
                totals["selectedPolicy"] >= preseason_p10
                if is_complete
                else None
            ),
        },
        "passes": (
            is_complete
            and primary_delta >= MINIMUM_PRIMARY_DELTA
            and totals["selectedPolicy"] >= preseason_p10
        ),
    }
    result: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": (
            "prospective-outcome-complete"
            if is_complete
            else "prospective-outcome-partial"
        ),
        "isPromotionDecision": False,
        "seasonCode": str(selected_row["season_code"]),
        "openingGameweek": int(selected_row["opening_gameweek"]),
        "evaluationGameweeks": list(OUTCOME_GAMEWEEKS),
        "registrationSource": {
            "artifactVersion": REGISTRATION_ARTIFACT_VERSION,
            "dataIdentitySha256": REGISTRATION_DATA_IDENTITY,
        },
        "selectedOpeningSquadSource": {
            "selectedOpeningSquadArtifactId": int(
                selected_row["selected_opening_squad_artifact_id"]
            ),
            "selectedOpeningSquadArtifactContentSha256": str(
                selected_row["content_sha256"]
            ),
            "officialCaptureId": int(
                selected_row["official_capture_id"]
            ),
            "decisionCutoffUtc": str(
                selected_row["decision_cutoff_utc"]
            ),
            "policyKey": selected["selectedPolicy"][
                "evaluationPolicyKey"
            ],
        },
        "benchmarkSource": {
            "initialSquadArtifactId": int(
                benchmark_row["initial_squad_artifact_id"]
            ),
            "initialSquadArtifactContentSha256": str(
                benchmark_row["content_sha256"]
            ),
            "primaryBenchmarkKey": PRIMARY_BENCHMARK_KEY,
            "diagnosticBenchmarkKey": DIAGNOSTIC_BENCHMARK_KEY,
            "rolePolicy": (
                "freeze-and-hold-the-exact-gw1-squad-xi-bench-and-captaincy"
            ),
        },
        "outcomeSources": outcome_sources,
        "realisedScore": {
            "engineVersion": ENGINE_VERSION,
            "weekly": weekly,
            "cumulative": {
                "observedGameweekCount": len(observed_gameweeks),
                "selectedPolicyPoints": totals["selectedPolicy"],
                "primaryBenchmarkPoints": totals[
                    PRIMARY_BENCHMARK_KEY
                ],
                "primaryBenchmarkDelta": primary_delta,
                "diagnosticBenchmarkPoints": totals[
                    DIAGNOSTIC_BENCHMARK_KEY
                ],
                "diagnosticBenchmarkDelta": diagnostic_delta,
            },
        },
        "promotionEvidenceGate": gate,
        "limitations": [
            (
                "The primary benchmark deliberately represents the current "
                "served GW1 advice held unchanged for eight Gameweeks; it is "
                "not a separately optimised multi-Gameweek challenger."
            ),
            (
                "One prospective season can check direction and downside "
                "safety but cannot precisely estimate a policy-wide effect."
            ),
            (
                "Passing this evidence gate permits a promotion review; this "
                "artifact never promotes a policy or changes served advice."
            ),
            (
                "The evaluation holds all squads without transfers, chips or "
                "price changes and uses only frozen preseason roles."
            ),
        ],
    }
    result["dataIdentitySha256"] = _sha256(
        {
            "benchmarkArtifactContentSha256": result["benchmarkSource"][
                "initialSquadArtifactContentSha256"
            ],
            "outcomeLiveSha256": [
                row["liveSha256"] for row in outcome_sources
            ],
            "registrationDataIdentitySha256": REGISTRATION_DATA_IDENTITY,
            "selectedOpeningSquadArtifactContentSha256": result[
                "selectedOpeningSquadSource"
            ]["selectedOpeningSquadArtifactContentSha256"],
        }
    )
    result["runIdentitySha256"] = _sha256(
        {
            "artifactVersion": ARTIFACT_VERSION,
            "dataIdentitySha256": result["dataIdentitySha256"],
        }
    )
    return result


def _read_outcome(
    connection: sqlite3.Connection,
    row: sqlite3.Row,
) -> tuple[Dict[str, Any], Dict[int, Dict[str, int]]]:
    live_bytes = bytes(row["live_json"])
    _require(
        live_bytes
        and len(live_bytes) <= 8 * MAXIMUM_ARTIFACT_BYTES
        and hashlib.sha256(live_bytes).hexdigest()
        == str(row["live_sha256"]),
        "outcome.content",
        "An official outcome content hash is invalid.",
    )
    outcome_rows = connection.execute(
        """
        SELECT player_id, minutes, total_points
        FROM official_fpl_player_outcomes
        WHERE outcome_capture_id = ?
        ORDER BY player_id;
        """,
        (int(row["outcome_capture_id"]),),
    ).fetchall()
    _require(
        len(outcome_rows) == int(row["player_count"])
        and len({int(item["player_id"]) for item in outcome_rows})
        == len(outcome_rows),
        "outcome.coverage",
        "An official outcome player cohort is incomplete.",
    )
    source = {
        "gameweek": int(row["gameweek"]),
        "outcomeCaptureId": int(row["outcome_capture_id"]),
        "referenceCaptureId": int(row["reference_capture_id"]),
        "deadlineUtc": str(row["deadline_utc"]),
        "availableAtUtc": str(row["available_at_utc"]),
        "liveSha256": str(row["live_sha256"]),
        "playerCount": int(row["player_count"]),
    }
    outcomes = {
        int(item["player_id"]): {
            "minutes": int(item["minutes"]),
            "totalPoints": int(item["total_points"]),
        }
        for item in outcome_rows
    }
    return source, outcomes


def _selected_role_definition(
    player_ids: Sequence[int],
    positions: Mapping[int, str],
    roles: Mapping[str, Any],
) -> Dict[str, Any]:
    ids = [int(value) for value in player_ids]
    _require(
        len(ids) == 15
        and len(set(ids)) == 15
        and set(ids) == set(positions),
        "selected-opening.selection",
        "The selected opening squad identity is invalid.",
    )
    return {
        "playerIds": ids,
        "positions": [positions[player_id] for player_id in ids],
        "startingPlayerIds": [
            int(value) for value in roles["startingPlayerIds"]
        ],
        "replacementGoalkeeperPlayerId": int(
            roles["replacementGoalkeeperPlayerId"]
        ),
        "outfieldSubstitutePlayerIds": [
            int(value) for value in roles["outfieldSubstitutePlayerIds"]
        ],
        "captainPlayerId": int(roles["captainPlayerId"]),
        "viceCaptainPlayerId": int(roles["viceCaptainPlayerId"]),
    }


def _require_selected(
    selected: Mapping[str, Any],
    row: sqlite3.Row,
) -> None:
    _require(
        selected.get("artifactVersion")
        == "current-selected-opening-squad-shadow-v1"
        and selected.get("status") == "prospective-shadow-unscored"
        and selected.get("isPromoted") is False
        and selected.get("influencesAdvice") is False
        and int(selected.get("officialCaptureId", 0))
        == int(row["official_capture_id"])
        and selected.get("seasonCode") == str(row["season_code"])
        and int(selected.get("openingGameweek", 0))
        == int(row["opening_gameweek"])
        and selected.get("selectedPolicy", {}).get(
            "evaluationPolicyKey"
        )
        == "6-expected-points"
        and selected.get("prospectiveScoreRegistration", {}).get(
            "outcomeGameweeks"
        )
        == list(OUTCOME_GAMEWEEKS),
        "selected-opening.lineage",
        "The frozen selected opening squad lineage is inconsistent.",
    )


def _require_benchmark(
    benchmark: Mapping[str, Any],
    official_capture_id: int,
) -> None:
    selections = [
        benchmark.get(key, {}).get("selection")
        for key in ("model", "candidate")
    ]
    _require(
        benchmark.get("artifactVersion")
        == "current-initial-squad-quality-shadow-v1"
        and benchmark.get("status") == "prospective-shadow-unscored"
        and benchmark.get("isPromoted") is False
        and benchmark.get("influencesAdvice") is False
        and int(benchmark.get("officialCaptureId", 0))
        == official_capture_id
        and all(
            isinstance(selection, dict)
            and len(selection.get("playerIds", [])) == 15
            for selection in selections
        ),
        "benchmark.lineage",
        "The exact frozen opening benchmarks are inconsistent.",
    )


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Score the frozen selected opening squad incrementally against "
            "predeclared exact-capture benchmarks."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = build_selected_opening_squad_outcome_evaluation(
            options.database
        )
        output = _write_report(report, options.output)
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
                "observedGameweeks": output["promotionEvidenceGate"][
                    "observedOutcomeGameweeks"
                ],
                "primaryBenchmarkDelta": output["realisedScore"][
                    "cumulative"
                ]["primaryBenchmarkDelta"],
                "gatePasses": output["promotionEvidenceGate"]["passes"],
                "outputFile": (
                    None
                    if options.output is None
                    else str(options.output)
                ),
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
