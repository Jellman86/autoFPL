from __future__ import annotations

import argparse
import hashlib
import json
import sys
import unicodedata
from collections.abc import Mapping, Sequence
from datetime import datetime
from pathlib import Path
from typing import Any
from urllib.parse import urlsplit

import brotli

from .current_lineup_evidence_boundary_audit import (
    _format_utc,
    _parse_utc,
    build_current_lineup_evidence_boundary_audit,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-official-injury-boundary-audit"
ARTIFACT_VERSION = "current-official-injury-boundary-audit-v1"
STATUS = "prospective-source-identity-audit-non-serving"

SOURCE_KEY = "premier-league-injuries"
SOURCE_SCHEMA_VERSION = "premier-league-injury-dom/v1"
SOURCE_URL = "https://www.premierleague.com/en/latest-player-injuries"
SOURCE_CLASS = "official-availability-aggregation"
SOURCE_DEPENDENCE_GROUP = "official-premier-league-and-club-reporting"
SOURCE_TRANSPORT_KEY = "playwright-mcp"
SOURCE_TRANSPORT_VERSION = "playwright-mcp/v0.0.78"
SOURCE_EXTRACTION_VERSION = "premier-league-injuries/v1"
EXPECTED_CLUB_COUNT = 20
MAXIMUM_ROW_COUNT = 200


def build_current_official_injury_boundary_audit(
    database_path: Path,
    evidence_cutoff_utc: str,
) -> dict[str, Any]:
    path = Path(database_path)
    cutoff = _parse_utc(evidence_cutoff_utc, "evidenceCutoffUtc")
    lineage = build_current_lineup_evidence_boundary_audit(
        path,
        evidence_cutoff_utc,
    )
    snapshot, rows, claims, official_players = _load_source(
        path,
        lineage,
        cutoff,
    )
    return _build_from_documents(
        lineage,
        snapshot,
        rows,
        claims,
        official_players,
        evidence_cutoff=cutoff,
    )


def _load_source(
    database_path: Path,
    lineage: Mapping[str, Any],
    cutoff: datetime,
) -> tuple[
    dict[str, Any],
    list[dict[str, Any]],
    list[dict[str, Any]],
    list[dict[str, Any]],
]:
    connection = _open_connection(database_path)
    try:
        row = connection.execute(
            """
            SELECT snapshot_id, status, source_key, source_class,
                   canonical_url, final_url, dependence_group,
                   transport_key, transport_version, season_code,
                   gameweek, deadline_utc, identity_capture_id,
                   retrieved_at_utc, available_at_utc, source_revision,
                   content_sha256, content_bytes, content_brotli
            FROM research_source_snapshots
            WHERE source_key = :source_key
              AND status = 'shadow-only'
              AND identity_capture_id = :capture_id
              AND julianday(available_at_utc) <= julianday(:cutoff)
            ORDER BY julianday(available_at_utc) DESC, snapshot_id DESC
            LIMIT 1;
            """,
            {
                "source_key": SOURCE_KEY,
                "capture_id": int(lineage["officialCaptureId"]),
                "cutoff": _format_utc(cutoff),
            },
        ).fetchone()
        _require(
            row is not None,
            "official-injury.source-not-found",
            "No cutoff-eligible official injury snapshot exists.",
        )
        assert row is not None
        snapshot = {
            key: row[key]
            for key in (
                "snapshot_id",
                "status",
                "source_key",
                "source_class",
                "canonical_url",
                "final_url",
                "dependence_group",
                "transport_key",
                "transport_version",
                "season_code",
                "gameweek",
                "deadline_utc",
                "identity_capture_id",
                "retrieved_at_utc",
                "available_at_utc",
                "source_revision",
                "content_sha256",
                "content_bytes",
            )
        }
        try:
            content = brotli.decompress(bytes(row["content_brotli"]))
        except brotli.error as exception:
            raise TemporalRidgeError(
                "official-injury.source-brotli",
                "The retained official injury payload is not valid Brotli.",
            ) from exception
        _require(
            len(content) == int(row["content_bytes"])
            and hashlib.sha256(content).hexdigest() == str(row["content_sha256"]),
            "official-injury.source-integrity",
            "The retained official injury payload hash is invalid.",
        )
        rows = _parse_source_payload(content)
        claims = [
            dict(claim)
            for claim in connection.execute(
                """
                SELECT claim_id, status, source_key, available_at_utc,
                       content_sha256, source_revision, season_code,
                       gameweek, deadline_utc, identity_capture_id,
                       player_id, claim_type, availability_status,
                       start_status, forecast_probability, directness,
                       source_span, extraction_method,
                       extraction_version, extraction_confidence,
                       duplicate_cluster_key, claim_content_sha256
                FROM evidence_claims
                WHERE source_key = :source_key
                  AND identity_capture_id = :capture_id
                  AND source_revision = :source_revision
                  AND content_sha256 = :content_sha256
                ORDER BY claim_id;
                """,
                {
                    "source_key": SOURCE_KEY,
                    "capture_id": int(lineage["officialCaptureId"]),
                    "source_revision": int(row["source_revision"]),
                    "content_sha256": str(row["content_sha256"]),
                },
            ).fetchall()
        ]
        official_players = [
            dict(player)
            for player in connection.execute(
                """
                SELECT player.player_id, player.code, player.team_id,
                       team.name AS team_name, player.position,
                       player.first_name, player.second_name,
                       player.web_name, player.status
                FROM official_fpl_players AS player
                INNER JOIN official_fpl_teams AS team
                    ON team.capture_id = player.capture_id
                   AND team.team_id = player.team_id
                WHERE player.capture_id = :capture_id
                ORDER BY player.player_id;
                """,
                {"capture_id": int(lineage["officialCaptureId"])},
            ).fetchall()
        ]
    finally:
        connection.close()

    _validate_snapshot(snapshot, lineage, cutoff)
    return snapshot, rows, claims, official_players


def _parse_source_payload(content: bytes) -> list[dict[str, Any]]:
    try:
        document = json.loads(content, object_pairs_hook=_unique_object)
    except (UnicodeDecodeError, json.JSONDecodeError, ValueError) as exception:
        raise TemporalRidgeError(
            "official-injury.source-json",
            "The retained official injury payload is invalid JSON.",
        ) from exception
    _require(
        isinstance(document, dict)
        and set(document)
        == {
            "schemaVersion",
            "sourceUrl",
            "pageTitle",
            "renderedWidgetSha256",
            "clubs",
        }
        and document["schemaVersion"] == SOURCE_SCHEMA_VERSION
        and document["sourceUrl"] == SOURCE_URL
        and _bounded_text(document["pageTitle"], 1, 200)
        and _lower_sha256(document["renderedWidgetSha256"])
        and isinstance(document["clubs"], list)
        and len(document["clubs"]) == EXPECTED_CLUB_COUNT,
        "official-injury.source-shape",
        "The retained official injury payload has an unsupported shape.",
    )
    rows: list[dict[str, Any]] = []
    team_names = set()
    for club_index, club in enumerate(document["clubs"]):
        _require(
            isinstance(club, dict)
            and set(club) == {"teamName", "rows"}
            and _bounded_text(club["teamName"], 1, 100)
            and isinstance(club["rows"], list),
            "official-injury.club-shape",
            "An official injury club has an unsupported shape.",
        )
        normalized_team = _normalize(club["teamName"])
        _require(
            normalized_team not in team_names,
            "official-injury.club-duplicate",
            "The official injury payload contains a duplicate club.",
        )
        team_names.add(normalized_team)
        player_names = set()
        for row_index, source_row in enumerate(club["rows"]):
            _require(
                isinstance(source_row, dict)
                and set(source_row) == {"playerName", "injury", "updateUrl"}
                and _bounded_text(source_row["playerName"], 1, 120)
                and source_row["playerName"] != "-"
                and _bounded_text(source_row["injury"], 1, 160)
                and _safe_optional_url(source_row["updateUrl"]),
                "official-injury.row-shape",
                "An official injury row has an unsupported shape.",
            )
            normalized_player = _normalize(source_row["playerName"])
            _require(
                normalized_player not in player_names,
                "official-injury.player-duplicate",
                "An official injury club contains a duplicate player.",
            )
            player_names.add(normalized_player)
            source_span = (
                f"{club['teamName']} injury list: "
                f"{source_row['playerName']} — {source_row['injury']}"
            )
            _require(
                len(source_span) <= 500,
                "official-injury.source-span",
                "An official injury source span is too long.",
            )
            rows.append(
                {
                    "clubIndex": club_index,
                    "rowIndex": row_index,
                    "sourceTeamName": club["teamName"],
                    "sourcePlayerName": source_row["playerName"],
                    "injury": source_row["injury"],
                    "sourceSpan": source_span,
                }
            )
    _require(
        1 <= len(rows) <= MAXIMUM_ROW_COUNT,
        "official-injury.row-count",
        "The official injury payload has an unsupported row count.",
    )
    return rows


def _build_from_documents(
    lineage: Mapping[str, Any],
    snapshot: Mapping[str, Any],
    source_rows: Sequence[Mapping[str, Any]],
    claims: Sequence[Mapping[str, Any]],
    official_players: Sequence[Mapping[str, Any]],
    *,
    evidence_cutoff: datetime,
) -> dict[str, Any]:
    selected = [dict(player) for player in lineage["players"]]
    boundary = [dict(player) for player in lineage["boundaryAlternatives"]]
    _require(
        len(selected) == 15,
        "official-injury.selected-count",
        "The injury audit requires a complete selected squad.",
    )
    selected_ids = {int(player["playerId"]) for player in selected}
    boundary_ids = {int(player["playerId"]) for player in boundary}
    _require(
        len(selected_ids) == len(selected)
        and len(boundary_ids) == len(boundary)
        and selected_ids.isdisjoint(boundary_ids),
        "official-injury.scope-identity",
        "The selected and boundary scopes contain invalid identities.",
    )
    official_by_id = {
        int(player["player_id"]): dict(player) for player in official_players
    }
    _require(
        len(official_by_id) == len(official_players),
        "official-injury.official-identity",
        "The exact official capture contains duplicate player identities.",
    )
    _require(
        selected_ids | boundary_ids <= set(official_by_id),
        "official-injury.scope-official-identity",
        "A scoped player is absent from the exact official capture.",
    )

    rows_by_span: dict[str, Mapping[str, Any]] = {}
    for source_row in source_rows:
        source_span = str(source_row["sourceSpan"])
        _require(
            source_span not in rows_by_span,
            "official-injury.source-span-duplicate",
            "The official injury payload contains a duplicate source span.",
        )
        rows_by_span[source_span] = source_row
    claims_by_span: dict[str, Mapping[str, Any]] = {}
    for claim in claims:
        _validate_claim(claim, snapshot, lineage, evidence_cutoff)
        source_span = str(claim["source_span"])
        _require(
            source_span in rows_by_span and source_span not in claims_by_span,
            "official-injury.claim-binding",
            "An extracted injury claim does not bind one unique source row.",
        )
        player_id = int(claim["player_id"])
        _require(
            player_id in official_by_id,
            "official-injury.claim-player",
            "An extracted injury claim has no exact official identity.",
        )
        claims_by_span[source_span] = claim
    _require(
        len({int(claim["player_id"]) for claim in claims}) == len(claims),
        "official-injury.claim-player-duplicate",
        "The injury snapshot resolved multiple rows to one official player.",
    )

    preliminary = []
    source_team_to_official_team: dict[str, int] = {}
    for source_row in source_rows:
        claim = claims_by_span.get(str(source_row["sourceSpan"]))
        official = None if claim is None else official_by_id[int(claim["player_id"])]
        if official is not None:
            source_team = str(source_row["sourceTeamName"])
            official_team_id = int(official["team_id"])
            previous = source_team_to_official_team.get(source_team)
            _require(
                previous is None or previous == official_team_id,
                "official-injury.source-team-binding",
                "One source club resolved to multiple official teams.",
            )
            source_team_to_official_team[source_team] = official_team_id
        preliminary.append((source_row, claim, official))

    scoped_players = [*selected, *boundary]
    rendered_rows = []
    for source_row, claim, official in preliminary:
        official_team_id = (
            int(official["team_id"])
            if official is not None
            else source_team_to_official_team.get(str(source_row["sourceTeamName"]))
        )
        scope_team_players = [
            _scope_identity(player, selected_ids, boundary_ids)
            for player in scoped_players
            if official_team_id is not None
            and int(player["teamId"]) == official_team_id
        ]
        scope_team_players.sort(key=lambda player: int(player["playerId"]))
        player_id = None if official is None else int(official["player_id"])
        if player_id in selected_ids:
            scope = "selected-squad"
        elif player_id in boundary_ids:
            scope = "boundary-alternative"
        elif official is not None:
            scope = "outside-decision-boundary"
        elif scope_team_players:
            scope = "unresolved-in-scope-team"
        else:
            scope = "unresolved-outside-scope-or-team"
        rendered_rows.append(
            {
                "sourceTeamName": str(source_row["sourceTeamName"]),
                "sourcePlayerName": str(source_row["sourcePlayerName"]),
                "injury": str(source_row["injury"]),
                "injuryDetailStatus": (
                    "source-placeholder"
                    if str(source_row["injury"]) == "-"
                    else "reported"
                ),
                "sourceSpan": str(source_row["sourceSpan"]),
                "identityStatus": (
                    "resolved" if official is not None else "unresolved"
                ),
                "scope": scope,
                "resolvedPlayer": (
                    None
                    if official is None
                    else {
                        "playerId": int(official["player_id"]),
                        "code": int(official["code"]),
                        "webName": str(official["web_name"]),
                        "teamId": int(official["team_id"]),
                        "teamName": str(official["team_name"]),
                        "position": str(official["position"]),
                        "officialStatus": str(official["status"]),
                    }
                ),
                "claim": (
                    None
                    if claim is None
                    else {
                        "claimId": int(claim["claim_id"]),
                        "status": str(claim["status"]),
                        "availabilityStatus": str(claim["availability_status"]),
                        "extractionConfidence": float(claim["extraction_confidence"]),
                        "claimContentSha256": str(claim["claim_content_sha256"]),
                    }
                ),
                "sourceTeamOfficialTeamId": official_team_id,
                "scopeTeamPlayers": scope_team_players,
            }
        )
    rendered_rows.sort(
        key=lambda row: (
            row["sourceTeamName"],
            row["sourcePlayerName"],
            row["injury"],
        )
    )
    rows_by_player = {
        int(row["resolvedPlayer"]["playerId"]): row
        for row in rendered_rows
        if row["resolvedPlayer"] is not None
    }
    selected_audit = [
        _render_scoped_player(player, rows_by_player.get(int(player["playerId"])))
        for player in selected
    ]
    boundary_audit = [
        _render_scoped_player(player, rows_by_player.get(int(player["playerId"])))
        for player in boundary
    ]
    selected_audit.sort(key=_player_sort_key)
    boundary_audit.sort(key=_player_sort_key)
    unresolved = [row for row in rendered_rows if row["identityStatus"] == "unresolved"]
    selected_listed = [
        row for row in selected_audit if row["injuryListingStatus"] == "listed-doubtful"
    ]
    boundary_listed = [
        row for row in boundary_audit if row["injuryListingStatus"] == "listed-doubtful"
    ]
    artifact: dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "seasonCode": lineage["seasonCode"],
        "openingGameweek": lineage["openingGameweek"],
        "deadlineUtc": lineage["deadlineUtc"],
        "officialCaptureId": lineage["officialCaptureId"],
        "evidenceDecisionCutoffUtc": _format_utc(evidence_cutoff),
        "lineupBoundarySource": {
            "artifactVersion": lineage["artifactVersion"],
            "dataIdentitySha256": lineage["dataIdentitySha256"],
            "runIdentitySha256": lineage["runIdentitySha256"],
        },
        "source": {
            "sourceKey": SOURCE_KEY,
            "snapshotId": int(snapshot["snapshot_id"]),
            "sourceRevision": int(snapshot["source_revision"]),
            "identityCaptureId": int(snapshot["identity_capture_id"]),
            "availableAtUtc": _format_utc(
                _parse_utc(
                    str(snapshot["available_at_utc"]),
                    "sourceAvailableAtUtc",
                )
            ),
            "contentSha256": str(snapshot["content_sha256"]),
            "contentBytes": int(snapshot["content_bytes"]),
            "transportKey": str(snapshot["transport_key"]),
            "transportVersion": str(snapshot["transport_version"]),
            "selectionRule": (
                "latest-exact-capture-snapshot-at-or-before-explicit-evidence-cutoff"
            ),
        },
        "coverage": {
            "sourceClubCount": EXPECTED_CLUB_COUNT,
            "sourceRowCount": len(rendered_rows),
            "resolvedRowCount": len(claims),
            "unresolvedRowCount": len(unresolved),
            "identityResolutionRate": _round(len(claims) / len(rendered_rows)),
            "selectedPlayerCount": len(selected_audit),
            "selectedListedPlayerCount": len(selected_listed),
            "boundaryAlternativePlayerCount": len(boundary_audit),
            "boundaryAlternativeListedPlayerCount": len(boundary_listed),
            "unresolvedInScopeTeamCount": sum(
                row["scope"] == "unresolved-in-scope-team" for row in unresolved
            ),
            "sourcePlaceholderInjuryRowCount": sum(
                row["injuryDetailStatus"] == "source-placeholder"
                for row in rendered_rows
            ),
        },
        "selectedPlayers": selected_audit,
        "boundaryAlternatives": boundary_audit,
        "unresolvedRows": unresolved,
        "sourceRows": rendered_rows,
        "decision": (
            "retain-selected-squad-and-review-identity-gaps-without-forecast-mutation"
        ),
        "limitations": [
            (
                "A player absent from the injury list is not proven fit or "
                "available; this source contains reported injury rows only."
            ),
            (
                "An unresolved raw row is never guessed onto a selected or "
                "boundary player, even when the source club has scoped players."
            ),
            (
                "Every resolved listing remains a quarantined doubtful flag "
                "without an invented absence probability or return date."
            ),
            (
                "This identity audit is prospective and non-serving; a future "
                "forecast effect still requires cutoff-safe outcome scoring."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "evidenceDecisionCutoffUtc": artifact["evidenceDecisionCutoffUtc"],
            "lineupBoundarySource": artifact["lineupBoundarySource"],
            "source": artifact["source"],
            "claimIdentities": [
                {
                    "claimId": int(claim["claim_id"]),
                    "claimContentSha256": str(claim["claim_content_sha256"]),
                }
                for claim in claims
            ],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _validate_snapshot(
    snapshot: Mapping[str, Any],
    lineage: Mapping[str, Any],
    cutoff: datetime,
) -> None:
    available = _parse_utc(
        str(snapshot["available_at_utc"]),
        "sourceAvailableAtUtc",
    )
    _require(
        str(snapshot["status"]) == "shadow-only"
        and str(snapshot["source_key"]) == SOURCE_KEY
        and str(snapshot["source_class"]) == SOURCE_CLASS
        and str(snapshot["canonical_url"]) == SOURCE_URL
        and str(snapshot["final_url"]) == SOURCE_URL
        and str(snapshot["dependence_group"]) == SOURCE_DEPENDENCE_GROUP
        and str(snapshot["transport_key"]) == SOURCE_TRANSPORT_KEY
        and str(snapshot["transport_version"]) == SOURCE_TRANSPORT_VERSION
        and str(snapshot["season_code"]) == str(lineage["seasonCode"])
        and int(snapshot["gameweek"]) == int(lineage["openingGameweek"])
        and int(snapshot["identity_capture_id"]) == int(lineage["officialCaptureId"])
        and _parse_utc(str(snapshot["deadline_utc"]), "sourceDeadlineUtc")
        == _parse_utc(str(lineage["deadlineUtc"]), "deadlineUtc")
        and available <= cutoff,
        "official-injury.source-lineage",
        "The retained official injury source lineage is invalid.",
    )


def _validate_claim(
    claim: Mapping[str, Any],
    snapshot: Mapping[str, Any],
    lineage: Mapping[str, Any],
    cutoff: datetime,
) -> None:
    claim_available = _parse_utc(
        str(claim["available_at_utc"]),
        "claimAvailableAtUtc",
    )
    snapshot_available = _parse_utc(
        str(snapshot["available_at_utc"]),
        "sourceAvailableAtUtc",
    )
    _require(
        str(claim["status"]) == "quarantined"
        and str(claim["source_key"]) == SOURCE_KEY
        and str(claim["content_sha256"]) == str(snapshot["content_sha256"])
        and int(claim["source_revision"]) == int(snapshot["source_revision"])
        and str(claim["season_code"]) == str(lineage["seasonCode"])
        and int(claim["gameweek"]) == int(lineage["openingGameweek"])
        and int(claim["identity_capture_id"]) == int(lineage["officialCaptureId"])
        and _parse_utc(str(claim["deadline_utc"]), "claimDeadlineUtc")
        == _parse_utc(str(lineage["deadlineUtc"]), "deadlineUtc")
        and claim_available == snapshot_available
        and claim_available <= cutoff
        and str(claim["claim_type"]) == "availability"
        and str(claim["availability_status"]) == "doubtful"
        and claim["start_status"] is None
        and claim["forecast_probability"] is None
        and str(claim["directness"]) == "reported"
        and str(claim["extraction_method"]) == "deterministic"
        and str(claim["extraction_version"]) == SOURCE_EXTRACTION_VERSION,
        "official-injury.claim-lineage",
        "An extracted official injury claim has invalid lineage.",
    )


def _scope_identity(
    player: Mapping[str, Any],
    selected_ids: set[int],
    boundary_ids: set[int],
) -> dict[str, Any]:
    player_id = int(player["playerId"])
    return {
        "playerId": player_id,
        "webName": str(player["webName"]),
        "teamId": int(player["teamId"]),
        "teamName": str(player["teamName"]),
        "position": str(player["position"]),
        "scope": (
            "selected-squad"
            if player_id in selected_ids
            else "boundary-alternative"
            if player_id in boundary_ids
            else "unknown"
        ),
    }


def _render_scoped_player(
    player: Mapping[str, Any],
    source_row: Mapping[str, Any] | None,
) -> dict[str, Any]:
    result = {
        "playerId": int(player["playerId"]),
        "webName": str(player["webName"]),
        "teamId": int(player["teamId"]),
        "teamName": str(player["teamName"]),
        "position": str(player["position"]),
        "injuryListingStatus": (
            "listed-doubtful" if source_row is not None else "not-listed-in-source"
        ),
        "sourceRow": source_row,
    }
    if "boundaryForSelectedPlayerIds" in player:
        result["boundaryForSelectedPlayerIds"] = [
            int(value) for value in player["boundaryForSelectedPlayerIds"]
        ]
    return result


def _player_sort_key(player: Mapping[str, Any]) -> tuple[str, str, str, int]:
    return (
        str(player["position"]),
        str(player["teamName"]),
        str(player["webName"]),
        int(player["playerId"]),
    )


def _unique_object(pairs: Sequence[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON property: {key}")
        result[key] = value
    return result


def _bounded_text(value: Any, minimum: int, maximum: int) -> bool:
    return (
        isinstance(value, str)
        and minimum <= len(value) <= maximum
        and not any(unicodedata.category(character) == "Cc" for character in value)
    )


def _lower_sha256(value: Any) -> bool:
    return (
        isinstance(value, str)
        and len(value) == 64
        and all(character in "0123456789abcdef" for character in value)
    )


def _safe_optional_url(value: Any) -> bool:
    if value is None:
        return True
    if not _bounded_text(value, 1, 2048):
        return False
    try:
        parsed = urlsplit(value)
        hostname = parsed.hostname
    except ValueError:
        return False
    return (
        parsed.scheme.lower() == "https"
        and bool(hostname)
        and parsed.username is None
        and parsed.password is None
        and not parsed.fragment
    )


def _normalize(value: str) -> str:
    decomposed = unicodedata.normalize("NFD", value)
    output = []
    last_was_space = True
    for character in decomposed:
        if unicodedata.category(character) == "Mn":
            continue
        if character.isalnum():
            output.append(character.lower())
            last_was_space = False
        elif not last_was_space:
            output.append(" ")
            last_was_space = True
    return "".join(output).strip()


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Audit resolved and unresolved official Premier League injury "
            "rows against the selected opening squad and its exact forecast "
            "boundary without mutating a forecast."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--evidence-cutoff-utc", required=True)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_official_injury_boundary_audit(
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
