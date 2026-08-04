from __future__ import annotations

import argparse
import json
import math
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

import numpy as np

from .current_appearance_hurdle_joint_scenarios import (
    build_current_appearance_hurdle_joint_scenarios,
)
from .current_best_supported_opening_squad import (
    build_current_best_supported_opening_squad,
)
from .current_multi_horizon_initial_squad import (
    BENCH_WEIGHT,
    SELECTED_POLICY_KEY,
    _build_candidates,
    _optimise_horizon,
    _score_horizon,
    _week_matrices,
)
from .current_public_projection_opening_squad import (
    _base_mean,
    _projection_overlay_matrices,
)
from .historical_opening_policy_evaluation import _complete_roles
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-official-published-opening-squad-shadow"
ARTIFACT_VERSION = "current-official-published-opening-squad-shadow-v1"
STATUS = "prospective-official-baseline-unscored"
SOURCE_KEY = "official-fpl-ep-next"
SOURCE_EVALUATOR_VERSION = "official-fpl-published-expected-points-evaluation-v1"
METHOD_KEY = "official-ep-next-gw1-mean-overlay-on-retained-six-week-policy-v1"
HORIZON_GAMEWEEKS = 6
OUTCOME_GAMEWEEKS = tuple(range(1, 9))


def build_current_official_published_opening_squad(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    scenario = build_current_appearance_hurdle_joint_scenarios(path)
    incumbent = build_current_best_supported_opening_squad(path)
    return _build_from_scenario(path, scenario, incumbent)


def _build_from_scenario(
    database_path: Path,
    scenario: Mapping[str, Any],
    incumbent: Mapping[str, Any],
) -> Dict[str, Any]:
    _require(
        int(scenario["officialCaptureId"]) == int(incumbent["officialCaptureId"])
        and incumbent["selectedPolicy"]["evaluationPolicyKey"] == SELECTED_POLICY_KEY,
        "official-published.incumbent",
        "The best-supported squad does not share the scenario cutoff.",
    )
    candidates = _build_candidates(database_path, scenario)
    source, projections = _load_projections(
        database_path,
        scenario,
        candidates,
    )
    base_matrices = _week_matrices(scenario)
    overlay_matrices = _projection_overlay_matrices(
        base_matrices,
        candidates,
        {
            player_id: {"projectedPoints": points}
            for player_id, points in projections.items()
        },
    )
    optimised_challenger, solver = _optimise_horizon(
        candidates,
        overlay_matrices,
        HORIZON_GAMEWEEKS,
        0.0,
    )
    challenger = _complete_roles(
        optimised_challenger,
        candidates,
        overlay_matrices,
        HORIZON_GAMEWEEKS,
    )
    incumbent_score = _score_horizon(
        incumbent["selection"],
        candidates,
        base_matrices,
        len(OUTCOME_GAMEWEEKS),
    )
    challenger_score = _score_horizon(
        challenger,
        candidates,
        base_matrices,
        len(OUTCOME_GAMEWEEKS),
    )
    incumbent_surrogate = _surrogate_objective(
        incumbent["selection"],
        overlay_matrices,
        candidates,
    )
    challenger_surrogate = _surrogate_objective(
        challenger,
        overlay_matrices,
        candidates,
    )
    _require(
        math.isclose(
            challenger_surrogate,
            float(solver["objectiveValue"]),
            abs_tol=1e-6,
        ),
        "official-published.surrogate-score",
        "The solved challenger objective could not be reproduced.",
    )
    candidate_by_id = {int(player["playerId"]): player for player in candidates}
    incumbent_ids = {int(value) for value in incumbent["selection"]["playerIds"]}
    challenger_ids = {int(value) for value in challenger["playerIds"]}
    values = np.asarray(list(projections.values()), dtype=float)
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
        "source": {
            **source,
            "sourceKey": SOURCE_KEY,
            "publishedPlayerCount": len(projections),
            "candidateCoverageCount": len(projections),
            "candidateCoverageFraction": 1.0,
            "nonZeroProjectionCount": int(np.count_nonzero(values)),
            "minimumProjectedPoints": _round(float(np.min(values))),
            "medianProjectedPoints": _round(float(np.median(values))),
            "meanProjectedPoints": _round(float(np.mean(values))),
            "maximumProjectedPoints": _round(float(np.max(values))),
        },
        "method": {
            "methodKey": METHOD_KEY,
            "horizonGameweeks": HORIZON_GAMEWEEKS,
            "laterGameweeksUnchanged": True,
            "distributionPolicy": (
                "published-values-affect-expected-value-surrogate-only"
            ),
            "evaluationPolicyKey": SELECTED_POLICY_KEY,
            "optimizerVersion": solver["optimizerVersion"],
            "sourceEvaluationVersion": SOURCE_EVALUATOR_VERSION,
        },
        "prospectiveScoreRegistration": {
            "outcomeGameweeks": list(OUTCOME_GAMEWEEKS),
            "squadMembership": (
                "fixed-opening-squad-no-transfers-for-all-eight-gameweeks"
            ),
            "roles": "all-eight-weeks-frozen-from-preseason-scenario-means",
            "realisedScorer": (
                "exact-fpl-captain-fallback-and-ordered-auto-substitution"
            ),
            "outcomeStatus": "waiting-for-official-2026-27-outcomes",
        },
        "incumbent": {
            "selection": incumbent["selection"],
            "exactScoreOnRetainedScenarios": incumbent_score,
            "sourceRunIdentitySha256": incumbent["runIdentitySha256"],
        },
        "challenger": {
            "selection": challenger,
            "solver": solver,
            "exactScoreOnRetainedScenarios": challenger_score,
        },
        "surrogateComparison": {
            "incumbentObjectiveValue": _round(incumbent_surrogate),
            "challengerObjectiveValue": _round(challenger_surrogate),
            "challengerDelta": _round(challenger_surrogate - incumbent_surrogate),
        },
        "selectionChange": {
            "overlapPlayerCount": len(incumbent_ids & challenger_ids),
            "removedPlayers": [
                _changed_player(
                    candidate_by_id[player_id],
                    projections[player_id],
                    base_matrices,
                    candidates,
                )
                for player_id in sorted(incumbent_ids - challenger_ids)
            ],
            "addedPlayers": [
                _changed_player(
                    candidate_by_id[player_id],
                    projections[player_id],
                    base_matrices,
                    candidates,
                )
                for player_id in sorted(challenger_ids - incumbent_ids)
            ],
        },
        "decision": "retain-as-prospective-official-baseline-only",
        "limitations": [
            (
                "Official FPL publishes a point estimate without a retained "
                "predictive distribution or public methodology."
            ),
            (
                "Only Gameweek 1 receives the published expected value. "
                "Gameweeks 2 through 8 remain unchanged."
            ),
            (
                "The published values affect the linear expected-value "
                "surrogate only. Exact scenario comparisons remain on the "
                "retained autoFPL paths."
            ),
            (
                "Current-season outcomes do not yet exist, so this baseline "
                "cannot replace or blend into the serving forecast."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "source": artifact["source"],
            "method": artifact["method"],
            "scenarioRunIdentitySha256": scenario["runIdentitySha256"],
            "incumbentRunIdentitySha256": incumbent["runIdentitySha256"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _load_projections(
    database_path: Path,
    scenario: Mapping[str, Any],
    candidates: Sequence[Mapping[str, Any]],
) -> tuple[Dict[str, Any], Dict[int, float]]:
    connection = sqlite3.connect(
        f"file:{Path(database_path).resolve()}?mode=ro",
        uri=True,
    )
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA query_only = ON;")
    try:
        header = connection.execute(
            """
            SELECT capture.available_at_utc,
                   capture.next_gameweek_number,
                   capture.bootstrap_sha256,
                   capture.fixtures_sha256,
                   event.deadline_utc
            FROM official_fpl_captures AS capture
            JOIN official_fpl_events AS event
              ON event.capture_id = capture.capture_id
             AND event.event_id = capture.next_gameweek_number
            WHERE capture.capture_id = ?
              AND capture.season_code = ?;
            """,
            (
                int(scenario["officialCaptureId"]),
                str(scenario["seasonCode"]),
            ),
        ).fetchone()
        if header is None:
            raise TemporalRidgeError(
                "official-published.capture-not-found",
                "The exact official projection capture is unavailable.",
            )
        rows = connection.execute(
            """
            SELECT player_id, expected_points_next
            FROM official_fpl_players
            WHERE capture_id = ?
              AND status != 'u'
            ORDER BY player_id;
            """,
            (int(scenario["officialCaptureId"]),),
        ).fetchall()
    except sqlite3.Error as exc:
        raise TemporalRidgeError(
            "official-published.database-read",
            "The official published projection inputs could not be read.",
        ) from exc
    finally:
        connection.close()
    _require(
        int(header["next_gameweek_number"]) == int(scenario["openingGameweek"])
        and _instant(header["available_at_utc"])
        == _instant(scenario["decisionCutoffUtc"])
        and _instant(header["deadline_utc"]) == _instant(scenario["deadlineUtc"])
        and _instant(header["available_at_utc"]) <= _instant(header["deadline_utc"]),
        "official-published.cutoff",
        "The official projection does not share the scenario cutoff.",
    )
    candidate_ids = {int(player["playerId"]) for player in candidates}
    projections: Dict[int, float] = {}
    for row in rows:
        player_id = int(row["player_id"])
        if player_id not in candidate_ids:
            continue
        try:
            value = float(row["expected_points_next"])
        except (TypeError, ValueError) as exc:
            raise TemporalRidgeError(
                "official-published.coverage",
                "A candidate is missing a published expected-points value.",
            ) from exc
        _require(
            math.isfinite(value) and -20.0 <= value <= 100.0,
            "official-published.projection",
            "A published expected-points value is invalid.",
        )
        projections[player_id] = value
    _require(
        set(projections) == candidate_ids,
        "official-published.coverage",
        "The published expected-points candidate coverage is incomplete.",
    )
    return (
        {
            "availableAtUtc": str(header["available_at_utc"]),
            "deadlineUtc": str(header["deadline_utc"]),
            "bootstrapSha256": str(header["bootstrap_sha256"]),
            "fixturesSha256": str(header["fixtures_sha256"]),
        },
        projections,
    )


def _changed_player(
    player: Mapping[str, Any],
    projected_points: float,
    matrices: Sequence[tuple[np.ndarray, np.ndarray]],
    candidates: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    player_id = int(player["playerId"])
    base_mean = _base_mean(matrices, candidates, player_id)
    return {
        "playerId": player_id,
        "webName": str(player["webName"]),
        "teamId": int(player["teamId"]),
        "teamName": str(player["teamName"]),
        "position": str(player["position"]),
        "priceTenths": int(player["priceTenths"]),
        "autoFplGameweekOneMean": _round(base_mean),
        "officialPublishedExpectedPoints": _round(projected_points),
        "publishedProjectionDelta": _round(projected_points - base_mean),
    }


def _surrogate_objective(
    selection: Mapping[str, Any],
    matrices: Sequence[tuple[np.ndarray, np.ndarray]],
    candidates: Sequence[Mapping[str, Any]],
) -> float:
    index_by_id = {
        int(player["playerId"]): index for index, player in enumerate(candidates)
    }
    player_ids = [int(value) for value in selection["playerIds"]]
    _require(
        len(selection["gameweeks"]) >= HORIZON_GAMEWEEKS
        and set(player_ids) <= set(index_by_id),
        "official-published.surrogate-selection",
        "A selection cannot be scored on the published surrogate.",
    )
    objective = 0.0
    for index, roles in enumerate(selection["gameweeks"][:HORIZON_GAMEWEEKS]):
        means = np.mean(matrices[index][0], axis=0)
        objective += BENCH_WEIGHT * sum(
            means[index_by_id[player_id]] for player_id in player_ids
        )
        objective += (1.0 - BENCH_WEIGHT) * sum(
            means[index_by_id[int(player_id)]]
            for player_id in roles["startingPlayerIds"]
        )
        objective += means[index_by_id[int(roles["captainPlayerId"])]]
    return float(objective)


def _instant(value: Any) -> datetime:
    text = str(value)
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    try:
        parsed = datetime.fromisoformat(text)
    except ValueError as exc:
        raise TemporalRidgeError(
            "official-published.instant",
            "An official projection timestamp is invalid.",
        ) from exc
    if parsed.tzinfo is None:
        raise TemporalRidgeError(
            "official-published.instant",
            "An official projection timestamp must include a timezone.",
        )
    return parsed.astimezone(timezone.utc)


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Build a quarantined opening-squad baseline from official FPL's "
            "published next-Gameweek expected points."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_official_published_opening_squad(options.database)
        _write_report(artifact, options.output)
    except (TemporalRidgeError, OSError, ValueError) as exc:
        sys.stderr.write(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "error",
                    "errorCode": getattr(exc, "code", "forecast.failed"),
                    "message": str(exc),
                },
                sort_keys=True,
            )
            + "\n"
        )
        return 1
    if options.output is not None:
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": artifact["status"],
                    "officialCaptureId": artifact["officialCaptureId"],
                    "candidateCoverageCount": artifact["source"][
                        "candidateCoverageCount"
                    ],
                    "overlapPlayerCount": artifact["selectionChange"][
                        "overlapPlayerCount"
                    ],
                    "outputFile": str(options.output),
                },
                sort_keys=True,
            )
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
