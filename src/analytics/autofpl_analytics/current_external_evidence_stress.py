from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence, Tuple

import numpy as np

from .current_appearance_hurdle_joint_scenarios import (
    build_current_appearance_hurdle_joint_scenarios,
)
from .current_appearance_hurdle_opening_optimality_audit import (
    _require_hurdle_scenario,
)
from .current_multi_horizon_initial_squad import (
    BENCH_WEIGHT,
    OPTIMIZER_VERSION,
    SELECTED_POLICY_KEY,
    _build_candidates,
    _optimise_horizon,
    _score_horizon,
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
ARTIFACT_TYPE = "current-external-evidence-stress"
ARTIFACT_VERSION = "current-external-evidence-stress-v1"
STATUS = "external-evidence-stress-non-serving"
HORIZON_GAMEWEEKS = 6
CVAR_WEIGHT = 0.0
STRESS_METHOD = "gameweek-one-zero-appearance-extreme"
DEPENDENT_SOURCES = frozenset({"straightred-lineup-consensus"})
SUPPORTED_SOURCES = frozenset(
    {
        "ffscout-predicted-lineups",
        "premier-league-injuries",
        "straightred-lineup-consensus",
    }
)


def build_current_external_evidence_stress(
    database_path: Path,
    *,
    evidence_cutoff_utc: str,
) -> Dict[str, Any]:
    path = Path(database_path)
    scenario = build_current_appearance_hurdle_joint_scenarios(path)
    cutoff = _parse_utc(evidence_cutoff_utc, "evidenceCutoffUtc")
    deadline = _parse_utc(str(scenario["deadlineUtc"]), "deadlineUtc")
    _require(
        cutoff <= deadline,
        "evidence-stress.cutoff-after-deadline",
        "The evidence cutoff must not be later than the deadline.",
    )
    claims = _load_latest_claims(
        path,
        scenario,
        cutoff,
    )
    return _build_from_scenario_and_claims(
        path,
        scenario,
        claims,
        evidence_cutoff=cutoff,
    )


def _load_latest_claims(
    database_path: Path,
    scenario: Mapping[str, Any],
    evidence_cutoff: datetime,
) -> List[Dict[str, Any]]:
    try:
        connection = sqlite3.connect(
            f"file:{database_path}?mode=ro",
            uri=True,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        rows = connection.execute(
            """
            SELECT
                claim.claim_id,
                claim.source_key,
                claim.claim_type,
                claim.available_at_utc,
                claim.start_status,
                claim.availability_status,
                claim.forecast_probability,
                claim.claim_content_sha256,
                claim.duplicate_cluster_key,
                current_identity.player_id,
                current_identity.web_name,
                current_identity.team_id,
                current_team.name AS team_name,
                current_identity.position
            FROM evidence_claims AS claim
            INNER JOIN official_fpl_players AS claim_identity
                ON claim_identity.capture_id = claim.identity_capture_id
               AND claim_identity.player_id = claim.player_id
            INNER JOIN official_fpl_players AS current_identity
                ON current_identity.capture_id = ?
               AND current_identity.code = claim_identity.code
            INNER JOIN official_fpl_teams AS current_team
                ON current_team.capture_id = current_identity.capture_id
               AND current_team.team_id = current_identity.team_id
            WHERE claim.season_code = ?
              AND claim.gameweek = ?
              AND claim.status = 'quarantined'
              AND claim.source_key IN (
                  'ffscout-predicted-lineups',
                  'premier-league-injuries',
                  'straightred-lineup-consensus'
              )
              AND julianday(claim.available_at_utc)
                    <= julianday(?)
              AND julianday(claim.available_at_utc)
                    <= julianday(claim.deadline_utc)
            ORDER BY claim.available_at_utc, claim.claim_id;
            """,
            (
                int(scenario["officialCaptureId"]),
                str(scenario["seasonCode"]),
                int(scenario["openingGameweek"]),
                _format_utc(evidence_cutoff),
            ),
        ).fetchall()
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "evidence-stress.claim-read",
            "The cutoff-safe external claims could not be read.",
        ) from exception
    finally:
        if "connection" in locals():
            connection.close()

    latest: Dict[Tuple[str, str, int], Dict[str, Any]] = {}
    for row in rows:
        rendered = {
            "claimId": int(row["claim_id"]),
            "sourceKey": str(row["source_key"]),
            "claimType": str(row["claim_type"]),
            "availableAtUtc": str(row["available_at_utc"]),
            "startStatus": (
                None
                if row["start_status"] is None
                else str(row["start_status"])
            ),
            "availabilityStatus": (
                None
                if row["availability_status"] is None
                else str(row["availability_status"])
            ),
            "forecastProbability": (
                None
                if row["forecast_probability"] is None
                else float(row["forecast_probability"])
            ),
            "claimContentSha256": str(row["claim_content_sha256"]),
            "duplicateClusterKey": (
                None
                if row["duplicate_cluster_key"] is None
                else str(row["duplicate_cluster_key"])
            ),
            "playerId": int(row["player_id"]),
            "webName": str(row["web_name"]),
            "teamId": int(row["team_id"]),
            "teamName": str(row["team_name"]),
            "position": str(row["position"]),
        }
        key = (
            rendered["sourceKey"],
            rendered["claimType"],
            rendered["playerId"],
        )
        latest[key] = rendered
    return sorted(
        latest.values(),
        key=lambda row: (
            str(row["sourceKey"]),
            str(row["claimType"]),
            int(row["playerId"]),
        ),
    )


def _build_from_scenario_and_claims(
    database_path: Path,
    scenario: Mapping[str, Any],
    claims: Sequence[Mapping[str, Any]],
    *,
    evidence_cutoff: datetime,
) -> Dict[str, Any]:
    _require_hurdle_scenario(scenario)
    candidates = _build_candidates(Path(database_path), scenario)
    matrices = _week_matrices(scenario)
    incumbent, incumbent_solver = _optimise_horizon(
        candidates,
        matrices,
        HORIZON_GAMEWEEKS,
        CVAR_WEIGHT,
    )
    incumbent_score = _score_horizon(
        incumbent,
        candidates,
        matrices,
        HORIZON_GAMEWEEKS,
    )
    by_id = {
        int(player["playerId"]): dict(player) for player in candidates
    }
    candidate_ids = set(by_id)
    incumbent_ids = {
        int(player_id) for player_id in incumbent["playerIds"]
    }
    adverse = [
        dict(claim)
        for claim in claims
        if int(claim["playerId"]) in candidate_ids
        and _is_adverse(claim)
    ]
    scenarios = _stress_definitions(adverse, incumbent_ids)
    results = [
        _solve_stress(
            definition,
            candidates,
            matrices,
            incumbent,
            incumbent_score,
            incumbent_ids,
            by_id,
            adverse,
        )
        for definition in scenarios
    ]
    decision_relevant = [
        row
        for row in results
        if bool(row["squadChanged"])
        and bool(row["isSourceConsistentAlternative"])
        and float(
            row["exactScenarioMeanDifferenceIfStressTruePoints"]
        )
        > 0.0
    ]
    selected_adverse_ids = sorted(
        incumbent_ids
        & {int(claim["playerId"]) for claim in adverse}
    )
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
        "forecastDecisionCutoffUtc": scenario["decisionCutoffUtc"],
        "evidenceDecisionCutoffUtc": _format_utc(evidence_cutoff),
        "officialCaptureId": scenario["officialCaptureId"],
        "scenarioCount": scenario["scenarioCount"],
        "candidatePoolCount": len(candidates),
        "stressMethod": {
            "method": STRESS_METHOD,
            "affectedGameweeks": [1],
            "laterGameweeksUnchanged": True,
            "assignsSourceProbability": False,
            "interpretation": (
                "An explicit extreme world in which each adverse evidence "
                "player records zero Gameweek 1 minutes and points."
            ),
        },
        "policy": {
            "evaluationPolicyKey": SELECTED_POLICY_KEY,
            "horizonGameweeks": HORIZON_GAMEWEEKS,
            "optimizerPolicyKey": "expected-points",
            "optimizerVersion": OPTIMIZER_VERSION,
            "benchWeight": BENCH_WEIGHT,
            "cvarWeight": CVAR_WEIGHT,
        },
        "incumbent": {
            "playerIds": incumbent["playerIds"],
            "players": [
                _player_identity(by_id[int(player_id)])
                for player_id in incumbent["playerIds"]
            ],
            "budgetTenths": incumbent["budgetTenths"],
            "solverStatus": incumbent_solver["status"],
            "reportedMipGap": incumbent_solver["reportedMipGap"],
            "exactScenarioMeanPoints": _round(
                _mean_score(incumbent_score)
            ),
        },
        "coverage": {
            "latestClaimCount": len(claims),
            "adverseClaimCount": len(adverse),
            "selectedAdversePlayerCount": len(selected_adverse_ids),
            "stressScenarioCount": len(results),
            "squadChangingScenarioCount": sum(
                bool(row["squadChanged"]) for row in results
            ),
            "decisionRelevantScenarioCount": len(decision_relevant),
        },
        "selectedAdversePlayers": [
            _player_identity(by_id[player_id])
            for player_id in selected_adverse_ids
        ],
        "adverseClaims": [_claim_identity(row) for row in adverse],
        "stressScenarios": results,
        "decision": (
            "review-evidence-sensitive-squad"
            if decision_relevant
            else "retain-incumbent-under-current-evidence-stresses"
        ),
        "limitations": [
            (
                "Each stress is an extreme conditional world, not a learned "
                "probability or a replacement central forecast."
            ),
            (
                "A predicted-XI omission is stressed as zero Gameweek 1 "
                "minutes even though the player could appear as a substitute."
            ),
            (
                "Source-wide and joint scenarios preserve dependence by "
                "deduplicating affected players; dependent consensus is not "
                "combined with independent sources."
            ),
            (
                "The result cannot promote evidence or mutate the displayed "
                "opening prediction."
            ),
        ],
        "source": {
            "scenarioArtifactVersion": scenario["artifactVersion"],
            "scenarioContentSha256": scenario["scenarioContentSha256"],
            "scenarioRunIdentitySha256": scenario["runIdentitySha256"],
        },
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "forecastDecisionCutoffUtc": artifact[
                "forecastDecisionCutoffUtc"
            ],
            "evidenceDecisionCutoffUtc": artifact[
                "evidenceDecisionCutoffUtc"
            ],
            "claims": artifact["adverseClaims"],
            "source": artifact["source"],
            "stressMethod": artifact["stressMethod"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _stress_definitions(
    adverse: Sequence[Mapping[str, Any]],
    incumbent_ids: set[int],
) -> List[Dict[str, Any]]:
    by_source: Dict[str, List[Mapping[str, Any]]] = {}
    by_player: Dict[int, List[Mapping[str, Any]]] = {}
    for claim in adverse:
        source_key = str(claim["sourceKey"])
        _require(
            source_key in SUPPORTED_SOURCES,
            "evidence-stress.source",
            "An adverse claim uses an unsupported source.",
        )
        by_source.setdefault(source_key, []).append(claim)
        by_player.setdefault(int(claim["playerId"]), []).append(claim)

    definitions: List[Dict[str, Any]] = []
    for player_id in sorted(incumbent_ids & set(by_player)):
        player_claims = by_player[player_id]
        definitions.append(
            _definition(
                f"selected-player-{player_id}",
                "selected-player-extreme",
                player_claims,
            )
        )

    selected_independent = [
        claim
        for claim in adverse
        if int(claim["playerId"]) in incumbent_ids
        and str(claim["sourceKey"]) not in DEPENDENT_SOURCES
    ]
    if selected_independent:
        definitions.append(
            _definition(
                "selected-independent-joint",
                "selected-independent-joint-extreme",
                selected_independent,
            )
        )

    for source_key in sorted(by_source):
        definitions.append(
            _definition(
                f"source-wide-{source_key}",
                "source-wide-extreme",
                by_source[source_key],
            )
        )

    independent = [
        claim
        for claim in adverse
        if str(claim["sourceKey"]) not in DEPENDENT_SOURCES
    ]
    if independent:
        definitions.append(
            _definition(
                "all-independent-sources",
                "all-independent-sources-extreme",
                independent,
            )
        )

    unique: Dict[Tuple[str, Tuple[int, ...]], Dict[str, Any]] = {}
    for row in definitions:
        key = (
            str(row["scenarioType"]),
            tuple(row["affectedPlayerIds"]),
        )
        unique.setdefault(key, row)
    return list(unique.values())


def _definition(
    scenario_key: str,
    scenario_type: str,
    claims: Iterable[Mapping[str, Any]],
) -> Dict[str, Any]:
    rows = [dict(claim) for claim in claims]
    return {
        "scenarioKey": scenario_key,
        "scenarioType": scenario_type,
        "sourceKeys": sorted(
            {str(claim["sourceKey"]) for claim in rows}
        ),
        "affectedPlayerIds": sorted(
            {int(claim["playerId"]) for claim in rows}
        ),
        "claimIds": sorted({int(claim["claimId"]) for claim in rows}),
    }


def _solve_stress(
    definition: Mapping[str, Any],
    candidates: Sequence[Mapping[str, Any]],
    matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
    incumbent: Mapping[str, Any],
    incumbent_score: Mapping[str, Any],
    incumbent_ids: set[int],
    by_id: Mapping[int, Mapping[str, Any]],
    all_adverse_claims: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    affected_ids = {
        int(player_id) for player_id in definition["affectedPlayerIds"]
    }
    stressed = _zero_gameweek_one(
        matrices,
        candidates,
        affected_ids,
    )
    selection, solver = _optimise_horizon(
        candidates,
        stressed,
        HORIZON_GAMEWEEKS,
        CVAR_WEIGHT,
    )
    optimized_under_stress = _score_horizon(
        selection,
        candidates,
        stressed,
        HORIZON_GAMEWEEKS,
    )
    incumbent_under_stress = _score_horizon(
        incumbent,
        candidates,
        stressed,
        HORIZON_GAMEWEEKS,
    )
    stressed_selection_under_base = _score_horizon(
        selection,
        candidates,
        matrices,
        HORIZON_GAMEWEEKS,
    )
    selected_ids = {
        int(player_id) for player_id in selection["playerIds"]
    }
    removed_ids = incumbent_ids - selected_ids
    added_ids = selected_ids - incumbent_ids
    unapplied_added_claims = [
        claim
        for claim in all_adverse_claims
        if int(claim["playerId"]) in added_ids
        and int(claim["playerId"]) not in affected_ids
    ]
    stress_difference = _round(
        _mean_score(optimized_under_stress)
        - _mean_score(incumbent_under_stress)
    )
    base_difference = _round(
        _mean_score(stressed_selection_under_base)
        - _mean_score(incumbent_score)
    )
    return {
        **definition,
        "affectedPlayerCount": len(affected_ids),
        "affectedSelectedPlayers": [
            _player_identity(by_id[player_id])
            for player_id in sorted(affected_ids & incumbent_ids)
        ],
        "selectionPlayerIds": selection["playerIds"],
        "selectionBudgetTenths": selection["budgetTenths"],
        "solverStatus": solver["status"],
        "reportedMipGap": solver["reportedMipGap"],
        "squadChanged": selected_ids != incumbent_ids,
        "gameweekOneRolesChanged": (
            selection["gameweeks"][0] != incumbent["gameweeks"][0]
        ),
        "removedPlayers": [
            _player_identity(by_id[player_id])
            for player_id in sorted(removed_ids)
        ],
        "addedPlayers": [
            _player_identity(by_id[player_id])
            for player_id in sorted(added_ids)
        ],
        "unappliedAdverseEvidenceForAddedPlayers": [
            _claim_identity(claim) for claim in unapplied_added_claims
        ],
        "isSourceConsistentAlternative": not unapplied_added_claims,
        "exactScenarioMeanDifferenceIfStressTruePoints": (
            stress_difference
        ),
        "exactScenarioMeanDifferenceIfStressFalsePoints": (
            base_difference
        ),
        "stressDecision": (
            "consider-alternative-if-source-trusted"
            if selected_ids != incumbent_ids
            and not unapplied_added_claims
            and stress_difference > 0.0
            else "retain-incumbent"
        ),
    }


def _zero_gameweek_one(
    matrices: Sequence[Tuple[np.ndarray, np.ndarray]],
    candidates: Sequence[Mapping[str, Any]],
    affected_player_ids: set[int],
) -> List[Tuple[np.ndarray, np.ndarray]]:
    result = [(points.copy(), played.copy()) for points, played in matrices]
    index = {
        int(player["playerId"]): column
        for column, player in enumerate(candidates)
    }
    for player_id in affected_player_ids:
        _require(
            player_id in index,
            "evidence-stress.player",
            "A stress player is absent from the candidate matrix.",
        )
        result[0][0][:, index[player_id]] = 0
        result[0][1][:, index[player_id]] = False
    return result


def _is_adverse(claim: Mapping[str, Any]) -> bool:
    claim_type = str(claim["claimType"])
    if claim_type == "start":
        probability = claim.get("forecastProbability")
        if probability is not None:
            return float(probability) < 0.5
        return claim.get("startStatus") == "does-not-start"
    if claim_type == "availability":
        return claim.get("availabilityStatus") in {
            "doubtful",
            "unavailable",
        }
    return False


def _claim_identity(claim: Mapping[str, Any]) -> Dict[str, Any]:
    return {
        "claimId": int(claim["claimId"]),
        "sourceKey": str(claim["sourceKey"]),
        "claimType": str(claim["claimType"]),
        "availableAtUtc": str(claim["availableAtUtc"]),
        "playerId": int(claim["playerId"]),
        "webName": str(claim["webName"]),
        "teamId": int(claim["teamId"]),
        "teamName": str(claim["teamName"]),
        "position": str(claim["position"]),
        "startStatus": claim.get("startStatus"),
        "availabilityStatus": claim.get("availabilityStatus"),
        "forecastProbability": claim.get("forecastProbability"),
        "claimContentSha256": str(claim["claimContentSha256"]),
        "duplicateClusterKey": claim.get("duplicateClusterKey"),
    }


def _mean_score(score: Mapping[str, Any]) -> float:
    return float(score["cumulative"]["meanPoints"])


def _parse_utc(value: str, field: str) -> datetime:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exception:
        raise TemporalRidgeError(
            f"evidence-stress.{field}",
            f"{field} must be an ISO-8601 UTC timestamp.",
        ) from exception
    _require(
        parsed.tzinfo is not None
        and parsed.utcoffset() == timezone.utc.utcoffset(parsed),
        f"evidence-stress.{field}",
        f"{field} must be UTC.",
    )
    return parsed.astimezone(timezone.utc)


def _format_utc(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat().replace(
        "+00:00",
        "Z",
    )


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Globally re-optimise the best-supported opening squad under "
            "explicit cutoff-safe external-evidence stress worlds."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--evidence-cutoff-utc", required=True)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_external_evidence_stress(
            options.database,
            evidence_cutoff_utc=options.evidence_cutoff_utc,
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
