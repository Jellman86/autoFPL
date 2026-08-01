from __future__ import annotations

import argparse
import brotli
import hashlib
import json
import math
import sqlite3
import sys
import unicodedata
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
    SELECTED_POLICY_KEY,
    _build_candidates,
    _optimise_horizon,
    _score_horizon,
    _week_matrices,
)
from .historical_opening_policy_evaluation import _complete_roles
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-public-projection-opening-squad-shadow"
ARTIFACT_VERSION = "current-public-projection-opening-squad-shadow-v1"
STATUS = "prospective-external-challenger-unscored"
SOURCE_KEY = "solio-public-projections"
SOURCE_URL = "https://fpl.solioanalytics.com/api/data/latest"
SOURCE_CANONICAL_URL = f"{SOURCE_URL}.json"
SOURCE_CLASS = "public-quantitative-market-projection"
SOURCE_DEPENDENCE_GROUP = "solio-sports-market-model"
SOURCE_TRANSPORT_KEY = "spider-mcp"
HORIZON_GAMEWEEKS = 6
OUTCOME_GAMEWEEKS = tuple(range(1, 9))
MINIMUM_SOURCE_PLAYERS = 20
MINIMUM_MATCH_FRACTION = 0.80
POSITION_MAP = {
    "GK": "goalkeeper",
    "GKP": "goalkeeper",
    "DEF": "defender",
    "MID": "midfielder",
    "FWD": "forward",
}


def build_current_public_projection_opening_squad(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    scenario = build_current_appearance_hurdle_joint_scenarios(path)
    incumbent = build_current_best_supported_opening_squad(path)
    _require(
        int(scenario["officialCaptureId"])
        == int(incumbent["officialCaptureId"])
        and incumbent["selectedPolicy"]["evaluationPolicyKey"]
        == SELECTED_POLICY_KEY,
        "public-projection.incumbent",
        "The best-supported squad does not share the scenario cutoff.",
    )
    snapshot, source_document, official = _load_source(
        path,
        scenario,
    )
    projections = _parse_source_document(
        source_document,
        scenario,
        snapshot,
    )
    matches, unmatched = _match_projections(projections, official)
    candidates = _build_candidates(path, scenario)
    base_matrices = _week_matrices(scenario)
    overlay_matrices = _projection_overlay_matrices(
        base_matrices,
        candidates,
        matches,
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
    candidate_by_id = {
        int(player["playerId"]): player for player in candidates
    }
    incumbent_ids = {
        int(value) for value in incumbent["selection"]["playerIds"]
    }
    challenger_ids = {int(value) for value in challenger["playerIds"]}
    source_ids = set(matches)
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
        "evidenceDecisionCutoffUtc": snapshot["availableAtUtc"],
        "officialCaptureId": scenario["officialCaptureId"],
        "scenarioCount": scenario["scenarioCount"],
        "candidatePoolCount": len(candidates),
        "source": {
            "sourceKey": SOURCE_KEY,
            "snapshotId": snapshot["snapshotId"],
            "sourceRevision": snapshot["sourceRevision"],
            "availableAtUtc": snapshot["availableAtUtc"],
            "contentSha256": snapshot["contentSha256"],
            "generatedAtUtc": source_document["generatedAt"],
            "deadlineUtc": source_document["deadlineIso"],
            "declaredSource": source_document["source"],
            "publishedPlayerCount": len(projections),
            "matchedPlayerCount": len(matches),
            "unmatchedPlayerCount": len(unmatched),
            "matchFraction": _round(len(matches) / len(projections)),
        },
        "method": {
            "methodKey": (
                "solio-gw1-mean-overlay-on-retained-six-week-policy-v1"
            ),
            "horizonGameweeks": HORIZON_GAMEWEEKS,
            "laterGameweeksUnchanged": True,
            "unpublishedPlayersUnchanged": True,
            "distributionPolicy": (
                "external-values-affect-expected-value-surrogate-only"
            ),
            "evaluationPolicyKey": SELECTED_POLICY_KEY,
            "optimizerVersion": solver["optimizerVersion"],
        },
        "prospectiveScoreRegistration": {
            "outcomeGameweeks": list(OUTCOME_GAMEWEEKS),
            "squadMembership": (
                "fixed-opening-squad-no-transfers-for-all-eight-gameweeks"
            ),
            "roles": (
                "all-eight-weeks-frozen-from-preseason-scenario-means"
            ),
            "realisedScorer": (
                "exact-fpl-captain-fallback-and-ordered-auto-substitution"
            ),
            "outcomeStatus": "waiting-for-official-2026-27-outcomes",
        },
        "projections": [
            {
                **matches[player_id]["source"],
                "playerId": player_id,
                "webName": official[player_id]["webName"],
                "teamId": official[player_id]["teamId"],
                "teamShortName": official[player_id]["teamShortName"],
                "position": official[player_id]["position"],
                "autoFplGameweekOneMean": _round(
                    _base_mean(base_matrices, candidates, player_id)
                ),
                "publicProjectionDelta": _round(
                    float(matches[player_id]["projectedPoints"])
                    - _base_mean(base_matrices, candidates, player_id)
                ),
            }
            for player_id in sorted(matches)
        ],
        "unmatchedProjections": unmatched,
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
        "selectionChange": {
            "overlapPlayerCount": len(incumbent_ids & challenger_ids),
            "removedPlayers": [
                _player_identity(candidate_by_id[player_id])
                for player_id in sorted(incumbent_ids - challenger_ids)
            ],
            "addedPlayers": [
                _player_identity(candidate_by_id[player_id])
                for player_id in sorted(challenger_ids - incumbent_ids)
            ],
            "incumbentPublishedProjectionCount": len(
                incumbent_ids & source_ids
            ),
            "challengerPublishedProjectionCount": len(
                challenger_ids & source_ids
            ),
        },
        "decision": "retain-as-prospective-external-challenger-only",
        "limitations": [
            (
                "The external source publishes only a ranked player subset; "
                "all unpublished players retain the autoFPL mean."
            ),
            (
                "Only Gameweek 1 receives the external point mean. "
                "Gameweeks 2 through 8 remain unchanged."
            ),
            (
                "The public values affect the linear expected-value "
                "surrogate only. Exact scenario comparisons remain on the "
                "retained autoFPL paths and describe the cost if the overlay "
                "adds no information."
            ),
            (
                "This source has no retained outcome fold and cannot replace "
                "the serving v2 forecast before prospective scoring."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "source": artifact["source"],
            "method": artifact["method"],
            "prospectiveScoreRegistration": artifact[
                "prospectiveScoreRegistration"
            ],
            "scenarioRunIdentitySha256": scenario["runIdentitySha256"],
            "incumbentRunIdentitySha256": incumbent[
                "runIdentitySha256"
            ],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _load_source(
    database_path: Path,
    scenario: Mapping[str, Any],
) -> tuple[Dict[str, Any], Dict[str, Any], Dict[int, Dict[str, Any]]]:
    connection = _open_connection(database_path)
    try:
        row = connection.execute(
            """
            SELECT snapshot_id, source_revision, available_at_utc,
                   content_sha256, content_bytes, content_brotli,
                   source_class, canonical_url, final_url,
                   dependence_group, transport_key, season_code,
                   gameweek, deadline_utc
            FROM research_source_snapshots
            WHERE source_key = :source_key
              AND status = 'shadow-only'
              AND identity_capture_id = :capture_id
              AND julianday(available_at_utc) <= julianday(:deadline)
            ORDER BY julianday(available_at_utc) DESC, snapshot_id DESC
            LIMIT 1;
            """,
            {
                "source_key": SOURCE_KEY,
                "capture_id": int(scenario["officialCaptureId"]),
                "deadline": str(scenario["deadlineUtc"]),
            },
        ).fetchone()
        if row is None:
            raise TemporalRidgeError(
                "public-projection.source-not-found",
                "No cutoff-eligible public projection snapshot exists.",
            )
        try:
            content = brotli.decompress(bytes(row["content_brotli"]))
            document = json.loads(content)
        except (brotli.error, UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise TemporalRidgeError(
                "public-projection.source-content",
                "The retained public projection content is invalid.",
            ) from exc
        _require(
            len(content) == int(row["content_bytes"])
            and hashlib.sha256(content).hexdigest()
            == str(row["content_sha256"]),
            "public-projection.source-integrity",
            "The retained public projection content hash is invalid.",
        )
        _require(
            str(row["source_class"]) == SOURCE_CLASS
            and str(row["canonical_url"]) == SOURCE_CANONICAL_URL
            and str(row["final_url"]) == SOURCE_CANONICAL_URL
            and str(row["dependence_group"])
            == SOURCE_DEPENDENCE_GROUP
            and str(row["transport_key"]) == SOURCE_TRANSPORT_KEY
            and str(row["season_code"]) == str(scenario["seasonCode"])
            and int(row["gameweek"])
            == int(scenario["openingGameweek"])
            and _instant(row["deadline_utc"])
            == _instant(scenario["deadlineUtc"]),
            "public-projection.source-lineage",
            "The retained public projection source lineage is invalid.",
        )
        official_rows = connection.execute(
            """
            SELECT player.player_id, player.web_name, player.team_id,
                   team.short_name, player.position, player.price_tenths
            FROM official_fpl_players AS player
            INNER JOIN official_fpl_teams AS team
                ON team.capture_id = player.capture_id
               AND team.team_id = player.team_id
            WHERE player.capture_id = :capture_id
              AND player.status != 'u'
            ORDER BY player.player_id;
            """,
            {"capture_id": int(scenario["officialCaptureId"])},
        ).fetchall()
    except sqlite3.Error as exc:
        raise TemporalRidgeError(
            "public-projection.database-read",
            "The public projection inputs could not be read.",
        ) from exc
    finally:
        connection.close()
    official = {
        int(value["player_id"]): {
            "playerId": int(value["player_id"]),
            "webName": str(value["web_name"]),
            "teamId": int(value["team_id"]),
            "teamShortName": str(value["short_name"]),
            "position": str(value["position"]),
            "priceTenths": int(value["price_tenths"]),
        }
        for value in official_rows
    }
    _require(
        official,
        "public-projection.official-player-coverage",
        "The exact official player cohort is unavailable.",
    )
    return (
        {
            "snapshotId": int(row["snapshot_id"]),
            "sourceRevision": int(row["source_revision"]),
            "availableAtUtc": str(row["available_at_utc"]),
            "contentSha256": str(row["content_sha256"]),
        },
        document,
        official,
    )


def _parse_source_document(
    document: Mapping[str, Any],
    scenario: Mapping[str, Any],
    snapshot: Mapping[str, Any],
) -> list[Dict[str, Any]]:
    try:
        gameweek = int(document["gameweek"])
        deadline = _instant(document["deadlineIso"])
        generated = _instant(document["generatedAt"])
        available = _instant(snapshot["availableAtUtc"])
        declared_source = str(document["source"])
        rows = list(document["topProjected"])
    except (KeyError, TypeError, ValueError) as exc:
        raise TemporalRidgeError(
            "public-projection.schema",
            "The public projection document has an invalid envelope.",
        ) from exc
    _require(
        gameweek == int(scenario["openingGameweek"])
        and deadline == _instant(scenario["deadlineUtc"])
        and generated <= available <= deadline
        and declared_source == SOURCE_URL,
        "public-projection.target",
        "The public projection does not match the exact opening target.",
    )
    _require(
        len(rows) >= MINIMUM_SOURCE_PLAYERS,
        "public-projection.coverage",
        "The public projection does not contain the minimum ranked cohort.",
    )
    projections = []
    identities = set()
    for row in rows:
        try:
            name = str(row["name"]).strip()
            team = str(row["team"]).strip()
            position = POSITION_MAP[str(row["position"]).strip()]
            price = int(row["price"])
            points = float(row["prPoints"])
        except (KeyError, TypeError, ValueError) as exc:
            raise TemporalRidgeError(
                "public-projection.player",
                "A public projection player row is invalid.",
            ) from exc
        identity = (_normalise(name), team, position, price)
        _require(
            name
            and team
            and 35 <= price <= 200
            and math.isfinite(points)
            and 0.0 <= points <= 30.0
            and identity not in identities,
            "public-projection.player",
            "A public projection player row is invalid or duplicated.",
        )
        identities.add(identity)
        projections.append(
            {
                "name": name,
                "team": team,
                "position": position,
                "priceTenths": price,
                "projectedPoints": points,
                "source": {
                    "sourceName": name,
                    "sourceTeam": team,
                    "sourcePosition": position,
                    "sourcePriceTenths": price,
                    "projectedPoints": _round(points),
                },
            }
        )
    return projections


def _match_projections(
    projections: Sequence[Mapping[str, Any]],
    official: Mapping[int, Mapping[str, Any]],
) -> tuple[Dict[int, Dict[str, Any]], list[Dict[str, Any]]]:
    index: Dict[tuple[str, str, str, int], list[int]] = {}
    for player_id, player in official.items():
        key = (
            _normalise(player["webName"]),
            str(player["teamShortName"]),
            str(player["position"]),
            int(player["priceTenths"]),
        )
        index.setdefault(key, []).append(int(player_id))
    matches: Dict[int, Dict[str, Any]] = {}
    unmatched = []
    for projection in projections:
        key = (
            _normalise(projection["name"]),
            str(projection["team"]),
            str(projection["position"]),
            int(projection["priceTenths"]),
        )
        candidates = index.get(key, [])
        if len(candidates) != 1 or candidates[0] in matches:
            unmatched.append(dict(projection["source"]))
            continue
        matches[candidates[0]] = dict(projection)
    _require(
        len(matches) / len(projections) >= MINIMUM_MATCH_FRACTION
        and set(POSITION_MAP.values())
        <= {str(official[player_id]["position"]) for player_id in matches},
        "public-projection.identity-coverage",
        "The public projection identity match is incomplete.",
    )
    return matches, unmatched


def _projection_overlay_matrices(
    matrices: Sequence[tuple[np.ndarray, np.ndarray]],
    candidates: Sequence[Mapping[str, Any]],
    matches: Mapping[int, Mapping[str, Any]],
) -> list[tuple[np.ndarray, np.ndarray]]:
    _require(
        len(matrices) >= HORIZON_GAMEWEEKS,
        "public-projection.scenario-horizon",
        "The retained scenario does not cover the selected horizon.",
    )
    adjusted = [
        (np.asarray(points, dtype=float).copy(), np.asarray(played).copy())
        for points, played in matrices
    ]
    first_points = adjusted[0][0]
    _require(
        first_points.ndim == 2
        and first_points.shape[1] == len(candidates),
        "public-projection.scenario-columns",
        "The retained scenario columns are misaligned.",
    )
    index_by_id = {
        int(player["playerId"]): index
        for index, player in enumerate(candidates)
    }
    _require(
        set(matches) <= set(index_by_id),
        "public-projection.scenario-player",
        "A matched projection player is outside the scenario cohort.",
    )
    for player_id, projection in matches.items():
        first_points[:, index_by_id[player_id]] = float(
            projection["projectedPoints"]
        )
    return adjusted


def _base_mean(
    matrices: Sequence[tuple[np.ndarray, np.ndarray]],
    candidates: Sequence[Mapping[str, Any]],
    player_id: int,
) -> float:
    column = next(
        index
        for index, player in enumerate(candidates)
        if int(player["playerId"]) == player_id
    )
    return float(np.mean(matrices[0][0][:, column]))


def _player_identity(player: Mapping[str, Any]) -> Dict[str, Any]:
    return {
        "playerId": int(player["playerId"]),
        "webName": str(player["webName"]),
        "teamId": int(player["teamId"]),
        "teamName": str(player["teamName"]),
        "position": str(player["position"]),
        "priceTenths": int(player["priceTenths"]),
    }


def _normalise(value: Any) -> str:
    folded = unicodedata.normalize("NFKD", str(value))
    return "".join(character for character in folded if character.isalnum()).casefold()


def _instant(value: Any) -> datetime:
    text = str(value)
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    try:
        parsed = datetime.fromisoformat(text)
    except ValueError as exc:
        raise TemporalRidgeError(
            "public-projection.instant",
            "A public projection timestamp is invalid.",
        ) from exc
    if parsed.tzinfo is None:
        raise TemporalRidgeError(
            "public-projection.instant",
            "A public projection timestamp must include a timezone.",
        )
    return parsed.astimezone(timezone.utc)


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Build a quarantined six-Gameweek opening-squad challenger "
            "from the latest cutoff-safe Solio public projection snapshot."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_public_projection_opening_squad(
            options.database
        )
        _write_report(artifact, options.output)
        return 0
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


if __name__ == "__main__":
    raise SystemExit(main())
