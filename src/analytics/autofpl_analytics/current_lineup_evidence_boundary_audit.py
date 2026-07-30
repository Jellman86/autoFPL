from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence, Tuple

from .current_best_supported_opening_squad import (
    build_current_best_supported_opening_squad,
)
from .current_appearance_hurdle_opening_forecast_sensitivity import (
    build_current_appearance_hurdle_opening_forecast_sensitivity,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-lineup-evidence-boundary-audit"
ARTIFACT_VERSION = "current-lineup-evidence-boundary-audit-v1.1"
STATUS = "prospective-evidence-audit-non-serving"

SOURCE_CLASS = {
    "ffscout-predicted-lineups": "specialist-predicted-lineup",
    "premier-league-injuries": "official-availability-aggregation",
    "straightred-lineup-consensus": "dependent-lineup-consensus",
}
DEPENDENT_SOURCES = frozenset({"straightred-lineup-consensus"})
SUPPORTED_CLAIM_TYPES = frozenset({"availability", "start"})


def build_current_lineup_evidence_boundary_audit(
    database_path: Path,
    evidence_cutoff_utc: str,
) -> Dict[str, Any]:
    path = Path(database_path)
    incumbent = build_current_best_supported_opening_squad(path)
    sensitivity = (
        build_current_appearance_hurdle_opening_forecast_sensitivity(
            path
        )
    )
    _require(
        int(sensitivity["officialCaptureId"])
        == int(incumbent["officialCaptureId"]),
        "lineup-evidence.sensitivity-capture",
        "The opening-squad sensitivity must use the incumbent capture.",
    )
    boundary_alternatives = _boundary_alternatives(sensitivity)
    cutoff = _parse_utc(evidence_cutoff_utc, "evidenceCutoffUtc")
    deadline = _parse_utc(
        str(incumbent["deadlineUtc"]),
        "deadlineUtc",
    )
    _require(
        cutoff < deadline,
        "lineup-evidence.post-deadline",
        "The evidence audit cutoff must be before the Gameweek deadline.",
    )
    claims = _load_claims(
        path,
        season_code=str(incumbent["seasonCode"]),
        gameweek=int(incumbent["openingGameweek"]),
        official_capture_id=int(incumbent["officialCaptureId"]),
        cutoff=cutoff,
    )
    return _build_from_documents(
        incumbent,
        claims,
        evidence_cutoff=cutoff,
        boundary_alternatives=boundary_alternatives,
        sensitivity_source={
            "artifactVersion": sensitivity["artifactVersion"],
            "dataIdentitySha256": sensitivity["dataIdentitySha256"],
            "runIdentitySha256": sensitivity["runIdentitySha256"],
        },
    )


def _boundary_alternatives(
    sensitivity: Mapping[str, Any],
) -> List[Dict[str, Any]]:
    by_id: Dict[int, Dict[str, Any]] = {}
    for threshold in sensitivity["selectedPlayerThresholds"]:
        selected_player_id = int(threshold["player"]["playerId"])
        for candidate in threshold["boundaryAddedPlayers"]:
            player_id = int(candidate["playerId"])
            identity = {
                "playerId": player_id,
                "webName": str(candidate["webName"]),
                "teamId": int(candidate["teamId"]),
                "teamName": str(candidate["teamName"]),
                "position": str(candidate["position"]),
                "modelAppearanceProbability": None,
            }
            existing = by_id.get(player_id)
            if existing is None:
                identity["boundaryForSelectedPlayerIds"] = [
                    selected_player_id
                ]
                by_id[player_id] = identity
                continue
            _require(
                {
                    key: existing[key] for key in identity
                }
                == identity,
                "lineup-evidence.boundary-identity",
                "A forecast-boundary alternative changed identity.",
            )
            existing["boundaryForSelectedPlayerIds"].append(
                selected_player_id
            )

    result = list(by_id.values())
    for player in result:
        player["boundaryForSelectedPlayerIds"] = sorted(
            set(player["boundaryForSelectedPlayerIds"])
        )
    result.sort(
        key=lambda player: (
            str(player["position"]),
            str(player["teamName"]),
            str(player["webName"]),
            int(player["playerId"]),
        )
    )
    return result


def _load_claims(
    database_path: Path,
    *,
    season_code: str,
    gameweek: int,
    official_capture_id: int,
    cutoff: datetime,
) -> List[Dict[str, Any]]:
    connection = _open_connection(database_path)
    try:
        rows = connection.execute(
            """
            SELECT
                claim_id,
                status,
                source_key,
                available_at_utc,
                content_sha256,
                source_revision,
                player_id,
                claim_type,
                availability_status,
                start_status,
                forecast_probability,
                directness,
                source_span,
                extraction_version,
                duplicate_cluster_key,
                claim_content_sha256
            FROM evidence_claims
            WHERE season_code = ?
              AND gameweek = ?
              AND identity_capture_id = ?
              AND claim_type IN ('availability', 'start')
            ORDER BY available_at_utc, claim_id;
            """,
            (season_code, gameweek, official_capture_id),
        ).fetchall()
    finally:
        connection.close()

    claims = []
    for row in rows:
        available = _parse_utc(
            str(row["available_at_utc"]),
            "availableAtUtc",
        )
        if available > cutoff:
            continue
        claims.append(
            {
                "claimId": int(row["claim_id"]),
                "status": str(row["status"]),
                "sourceKey": str(row["source_key"]),
                "availableAtUtc": _format_utc(available),
                "contentSha256": str(row["content_sha256"]),
                "sourceRevision": int(row["source_revision"]),
                "playerId": int(row["player_id"]),
                "claimType": str(row["claim_type"]),
                "availabilityStatus": row["availability_status"],
                "startStatus": row["start_status"],
                "forecastProbability": (
                    None
                    if row["forecast_probability"] is None
                    else float(row["forecast_probability"])
                ),
                "directness": str(row["directness"]),
                "sourceSpan": str(row["source_span"]),
                "extractionVersion": str(row["extraction_version"]),
                "duplicateClusterKey": row["duplicate_cluster_key"],
                "claimContentSha256": str(
                    row["claim_content_sha256"]
                ),
            }
        )
    return claims


def _build_from_documents(
    incumbent: Mapping[str, Any],
    claims: Sequence[Mapping[str, Any]],
    *,
    evidence_cutoff: datetime,
    boundary_alternatives: Sequence[Mapping[str, Any]] = (),
    sensitivity_source: Optional[Mapping[str, Any]] = None,
) -> Dict[str, Any]:
    selected_players = list(incumbent["selection"]["players"])
    _require(
        len(selected_players) == 15,
        "lineup-evidence.squad-size",
        "The selected opening squad must contain exactly 15 players.",
    )
    selected_ids = {
        int(player["playerId"]) for player in selected_players
    }
    _require(
        len(selected_ids) == 15,
        "lineup-evidence.squad-duplicates",
        "The selected opening squad contains duplicate players.",
    )
    boundary_players = [dict(player) for player in boundary_alternatives]
    boundary_ids = {
        int(player["playerId"]) for player in boundary_players
    }
    _require(
        len(boundary_ids) == len(boundary_players),
        "lineup-evidence.boundary-duplicates",
        "The forecast boundary contains duplicate alternative players.",
    )
    _require(
        selected_ids.isdisjoint(boundary_ids),
        "lineup-evidence.boundary-selected-overlap",
        "A forecast-boundary alternative is already selected.",
    )
    latest = _latest_claims(
        claim
        for claim in claims
        if int(claim["playerId"]) in selected_ids
    )
    latest_boundary = _latest_claims(
        claim
        for claim in claims
        if int(claim["playerId"]) in boundary_ids
    )
    claims_by_player: Dict[int, List[Mapping[str, Any]]] = {
        player_id: [] for player_id in selected_ids
    }
    for claim in latest:
        claims_by_player[int(claim["playerId"])].append(claim)

    players = [
        _player_audit(
            player,
            claims_by_player[int(player["playerId"])],
        )
        for player in selected_players
    ]
    players.sort(
        key=lambda row: (
            str(row["position"]),
            str(row["teamName"]),
            str(row["webName"]),
            int(row["playerId"]),
        )
    )
    boundary_claims_by_player: Dict[
        int,
        List[Mapping[str, Any]],
    ] = {player_id: [] for player_id in boundary_ids}
    for claim in latest_boundary:
        boundary_claims_by_player[int(claim["playerId"])].append(claim)
    audited_boundary_players = [
        _player_audit(
            player,
            boundary_claims_by_player[int(player["playerId"])],
        )
        for player in boundary_players
    ]
    audited_boundary_players.sort(
        key=lambda row: (
            str(row["position"]),
            str(row["teamName"]),
            str(row["webName"]),
            int(row["playerId"]),
        )
    )
    predicted_starters = [
        row
        for row in players
        if row["categoricalStartSignal"] == "predicted-starter"
    ]
    predicted_non_starters = [
        row
        for row in players
        if row["categoricalStartSignal"]
        == "predicted-non-starter"
    ]
    conflicts = [
        row
        for row in players
        if row["categoricalStartSignal"] == "conflicting"
    ]
    risk_players = [
        row for row in players if bool(row["selectionRiskFlag"])
    ]
    boundary_risk_players = [
        row
        for row in audited_boundary_players
        if bool(row["selectionRiskFlag"])
    ]
    boundary_covered = [
        row
        for row in audited_boundary_players
        if len(row["latestClaims"]) > 0
    ]
    covered = [
        row
        for row in players
        if row["categoricalStartSignal"] != "uncovered"
    ]
    source_summary = _source_summary(latest)
    forecast_cutoff = _parse_utc(
        str(incumbent["decisionCutoffUtc"]),
        "decisionCutoffUtc",
    )
    available_times = [
        _parse_utc(str(claim["availableAtUtc"]), "availableAtUtc")
        for claim in latest
    ]
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "seasonCode": incumbent["seasonCode"],
        "openingGameweek": incumbent["openingGameweek"],
        "deadlineUtc": incumbent["deadlineUtc"],
        "officialCaptureId": incumbent["officialCaptureId"],
        "forecastDecisionCutoffUtc": _format_utc(forecast_cutoff),
        "evidenceDecisionCutoffUtc": _format_utc(evidence_cutoff),
        "evidenceBoundary": {
            "latestSelectedClaimAvailableAtUtc": (
                _format_utc(max(available_times))
                if available_times
                else None
            ),
            "latestEvidenceSecondsAfterForecastCutoff": (
                _round(
                    (
                        max(available_times) - forecast_cutoff
                    ).total_seconds()
                )
                if available_times
                else None
            ),
            "claimSelectionRule": (
                "latest-by-player-source-target-at-or-before-explicit-"
                "evidence-cutoff-and-exact-official-capture"
            ),
        },
        "selectedSquadSource": {
            "artifactVersion": incumbent["artifactVersion"],
            "runIdentitySha256": incumbent["runIdentitySha256"],
        },
        "forecastSensitivitySource": (
            None
            if sensitivity_source is None
            else dict(sensitivity_source)
        ),
        "coverage": {
            "selectedPlayerCount": len(players),
            "startClaimCoveredPlayerCount": len(covered),
            "predictedStarterCount": len(predicted_starters),
            "predictedNonStarterCount": len(predicted_non_starters),
            "categoricalConflictCount": len(conflicts),
            "selectionRiskPlayerCount": len(risk_players),
            "boundaryAlternativePlayerCount": len(
                audited_boundary_players
            ),
            "boundaryAlternativeEvidenceCoveredPlayerCount": len(
                boundary_covered
            ),
            "boundaryAlternativeRiskPlayerCount": len(
                boundary_risk_players
            ),
        },
        "selectionRiskPlayers": [
            {
                "playerId": row["playerId"],
                "webName": row["webName"],
                "teamName": row["teamName"],
                "position": row["position"],
                "modelAppearanceProbability": row[
                    "modelAppearanceProbability"
                ],
                "categoricalStartSignal": row[
                    "categoricalStartSignal"
                ],
                "reason": row["selectionRiskReason"],
            }
            for row in risk_players
        ],
        "boundaryAlternativeRiskPlayers": [
            {
                "playerId": row["playerId"],
                "webName": row["webName"],
                "teamName": row["teamName"],
                "position": row["position"],
                "categoricalStartSignal": row[
                    "categoricalStartSignal"
                ],
                "reason": row["selectionRiskReason"],
            }
            for row in boundary_risk_players
        ],
        "players": players,
        "boundaryAlternatives": audited_boundary_players,
        "sources": source_summary,
        "boundaryAlternativeSources": _source_summary(
            latest_boundary,
            scope="boundary-alternative",
        ),
        "decision": (
            "retain-v2-without-uncalibrated-lineup-mutation-and-"
            "prioritise-start-substitute-zero-mixture"
        ),
        "nextRegisteredChallenger": {
            "target": (
                "fixture-level-start-substitute-appearance-zero-minutes-"
                "mixture"
            ),
            "requirements": [
                (
                    "Score source start probabilities against exact "
                    "decision-time starts by lead-time bucket."
                ),
                (
                    "Estimate substitute-appearance probability separately "
                    "from start probability."
                ),
                (
                    "Estimate points and minutes conditional on starting "
                    "and appearing as a substitute."
                ),
                (
                    "Propagate the complete mixture through the unchanged "
                    "3/6/8-Gameweek scenario and globally constrained "
                    "policy screen."
                ),
            ],
        },
        "limitations": [
            (
                "Every external claim remains quarantined and has no learned "
                "reliability weight for this season and lead time."
            ),
            (
                "A predicted-XI omission forecasts a start, not an "
                "appearance; it cannot be substituted directly for the "
                "incumbent appearance probability."
            ),
            (
                "strAIghtred is a dependent consensus source and is never "
                "counted as an additional independent vote."
            ),
            (
                "A Premier League injury-page listing is retained only as "
                "a doubtful availability flag; it does not establish an "
                "absence probability or return date."
            ),
            (
                "The evidence arrived after the official capture used by "
                "the selected forecast. This audit binds both cutoffs and "
                "does not rewrite the earlier artifact."
            ),
        ],
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
            "selectedSquadSource": artifact["selectedSquadSource"],
            "forecastSensitivitySource": artifact[
                "forecastSensitivitySource"
            ],
            "claims": [
                {
                    "claimId": claim["claimId"],
                    "claimContentSha256": claim[
                        "claimContentSha256"
                    ],
                }
                for claim in [*latest, *latest_boundary]
            ],
            "claimSelectionRule": artifact["evidenceBoundary"][
                "claimSelectionRule"
            ],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _latest_claims(
    claims: Iterable[Mapping[str, Any]],
) -> List[Mapping[str, Any]]:
    latest: Dict[Tuple[int, str, str], Mapping[str, Any]] = {}
    for claim in claims:
        claim_type = str(claim["claimType"])
        _require(
            claim_type in SUPPORTED_CLAIM_TYPES,
            "lineup-evidence.claim-type",
            "The lineup evidence audit received an unsupported claim type.",
        )
        key = (
            int(claim["playerId"]),
            str(claim["sourceKey"]),
            claim_type,
        )
        previous = latest.get(key)
        if previous is None or _claim_order(claim) > _claim_order(
            previous
        ):
            latest[key] = claim
    result = list(latest.values())
    result.sort(
        key=lambda claim: (
            int(claim["playerId"]),
            str(claim["sourceKey"]),
            str(claim["claimType"]),
        )
    )
    return result


def _claim_order(claim: Mapping[str, Any]) -> Tuple[datetime, int]:
    return (
        _parse_utc(str(claim["availableAtUtc"]), "availableAtUtc"),
        int(claim["claimId"]),
    )


def _player_audit(
    player: Mapping[str, Any],
    claims: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    start_claims = [
        claim for claim in claims if claim["claimType"] == "start"
    ]
    availability_claims = [
        claim
        for claim in claims
        if claim["claimType"] == "availability"
    ]
    start_values = {
        str(claim["startStatus"]) for claim in start_claims
    }
    if not start_values:
        signal = "uncovered"
    elif len(start_values) > 1:
        signal = "conflicting"
    elif start_values == {"starts"}:
        signal = "predicted-starter"
    elif start_values == {"does-not-start"}:
        signal = "predicted-non-starter"
    else:
        signal = "uncertain"
    unavailable_values = {
        str(claim["availabilityStatus"])
        for claim in availability_claims
        if claim["availabilityStatus"] is not None
    }
    risk_reason = None
    if signal == "predicted-non-starter":
        risk_reason = "latest-lineup-source-omits-selected-player"
    elif signal == "conflicting":
        risk_reason = "latest-lineup-sources-conflict"
    elif unavailable_values & {"doubtful", "unavailable"}:
        risk_reason = "latest-availability-source-flags-risk"
    rendered_claims = [
        {
            "claimId": int(claim["claimId"]),
            "status": str(claim["status"]),
            "sourceKey": str(claim["sourceKey"]),
            "sourceClass": SOURCE_CLASS.get(
                str(claim["sourceKey"]),
                "other",
            ),
            "isDependentConsensus": (
                str(claim["sourceKey"]) in DEPENDENT_SOURCES
            ),
            "claimType": str(claim["claimType"]),
            "startStatus": claim["startStatus"],
            "availabilityStatus": claim["availabilityStatus"],
            "forecastProbability": claim["forecastProbability"],
            "availableAtUtc": str(claim["availableAtUtc"]),
            "sourceRevision": int(claim["sourceRevision"]),
            "sourceSpan": str(claim["sourceSpan"]),
            "hasDuplicateCluster": (
                claim["duplicateClusterKey"] is not None
            ),
            "claimContentSha256": str(
                claim["claimContentSha256"]
            ),
        }
        for claim in claims
    ]
    rendered_claims.sort(
        key=lambda claim: (
            claim["sourceKey"],
            claim["claimType"],
            claim["claimId"],
        )
    )
    model_appearance = player.get("modelAppearanceProbability")
    result = {
        "playerId": int(player["playerId"]),
        "webName": str(player["webName"]),
        "teamId": int(player["teamId"]),
        "teamName": str(player["teamName"]),
        "position": str(player["position"]),
        "modelAppearanceProbability": (
            None
            if model_appearance is None
            else float(model_appearance)
        ),
        "modelStartProbability": None,
        "categoricalStartSignal": signal,
        "selectionRiskFlag": risk_reason is not None,
        "selectionRiskReason": risk_reason,
        "latestClaims": rendered_claims,
    }
    if "boundaryForSelectedPlayerIds" in player:
        result["boundaryForSelectedPlayerIds"] = [
            int(value)
            for value in player["boundaryForSelectedPlayerIds"]
        ]
    return result


def _source_summary(
    claims: Sequence[Mapping[str, Any]],
    *,
    scope: str = "selected",
) -> List[Dict[str, Any]]:
    _require(
        scope in {"selected", "boundary-alternative"},
        "lineup-evidence.source-summary-scope",
        "The evidence source-summary scope is unsupported.",
    )
    claim_count_key = (
        "selectedPlayerClaimCount"
        if scope == "selected"
        else "boundaryAlternativeClaimCount"
    )
    player_count_key = (
        "selectedPlayerCount"
        if scope == "selected"
        else "boundaryAlternativePlayerCount"
    )
    by_source: Dict[str, List[Mapping[str, Any]]] = {}
    for claim in claims:
        by_source.setdefault(str(claim["sourceKey"]), []).append(claim)
    result = []
    for source_key, rows in sorted(by_source.items()):
        result.append(
            {
                "sourceKey": source_key,
                "sourceClass": SOURCE_CLASS.get(source_key, "other"),
                "isDependentConsensus": source_key in DEPENDENT_SOURCES,
                claim_count_key: len(rows),
                player_count_key: len(
                    {int(row["playerId"]) for row in rows}
                ),
                "statuses": sorted(
                    {str(row["status"]) for row in rows}
                ),
                "latestAvailableAtUtc": max(
                    str(row["availableAtUtc"]) for row in rows
                ),
                "sourceRevisions": sorted(
                    {int(row["sourceRevision"]) for row in rows}
                ),
                "contentSha256s": sorted(
                    {str(row["contentSha256"]) for row in rows}
                ),
            }
        )
    return result


def _parse_utc(value: str, field: str) -> datetime:
    text = str(value).strip()
    try:
        parsed = datetime.fromisoformat(
            text[:-1] + "+00:00" if text.endswith("Z") else text
        )
    except ValueError as exception:
        raise TemporalRidgeError(
            "lineup-evidence.timestamp",
            f"{field} must be an ISO-8601 timestamp.",
        ) from exception
    if parsed.tzinfo is None:
        raise TemporalRidgeError(
            "lineup-evidence.timestamp",
            f"{field} must include a UTC offset.",
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
            "Audit the latest cutoff-bound lineup and availability evidence "
            "around the selected opening squad without applying an "
            "uncalibrated forecast mutation."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--evidence-cutoff-utc", required=True)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_lineup_evidence_boundary_audit(
            options.database,
            options.evidence_cutoff_utc,
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
