from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import sys
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence, Tuple

import brotli

from .historical_preseason_evaluation import HistoricalCapture, _load_capture
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-opening-policy-evaluation-data"
ARTIFACT_VERSION = "historical-opening-policy-evaluation-data-v1"
STATUS = "complete"
REGISTERED_SEASONS = ("2022-23", "2023-24", "2024-25", "2025-26")
TARGET_SEASONS = REGISTERED_SEASONS[1:]
OUTCOME_GAMEWEEKS = tuple(range(1, 9))
RAW_REQUIRED_COLUMNS = frozenset(
    {"element", "position", "team", "value", "GW"}
)
RAW_EXCLUDED_PREDICTIVE_COLUMNS = ("xP",)
POSITION_MAP = {
    "GK": "goalkeeper",
    "GKP": "goalkeeper",
    "DEF": "defender",
    "MID": "midfielder",
    "FWD": "forward",
}
POSITION_QUOTAS = {
    "goalkeeper": 2,
    "defender": 5,
    "midfielder": 5,
    "forward": 3,
}
EXPECTED_CAPTURE_IDENTITIES = {
    "2022-23": {
        "sourceRevision": "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a",
        "playersSha256": "a874c12817bbf4d454e60a5765629742b528d4ad0c2f39f56e4fc89640605f7f",
        "gameweeksSha256": "b78a6d0456141f9033c32fc4122931baae780ac4a4938683451b1fce7a4fdd15",
        "playerCount": 778,
        "playerGameweekCount": 26_505,
    },
    "2023-24": {
        "sourceRevision": "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a",
        "playersSha256": "43d8cf5efb3d901f2499558c40b392b7f0ee22afe28030f36266ad29024c63d9",
        "gameweeksSha256": "e9c09c8856f1c86b4f920f46ddd5033af83409439dfda53be925df2a3e7c8a9e",
        "playerCount": 865,
        "playerGameweekCount": 29_725,
    },
    "2024-25": {
        "sourceRevision": "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a",
        "playersSha256": "75686051b265cbe7755ac71213ecaad21b26ee1cc46a8bafbba19c39ce894b05",
        "gameweeksSha256": "5bbbcba6353b4c72ad273adcc8e3aa451946a826564679788f45b1cb3325b84e",
        "playerCount": 784,
        "playerGameweekCount": 27_283,
    },
    "2025-26": {
        "sourceRevision": "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a",
        "playersSha256": "412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b",
        "gameweeksSha256": "0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d",
        "playerCount": 841,
        "playerGameweekCount": 29_747,
    },
}


@dataclass(frozen=True)
class OpeningPlayer:
    season_element_id: int
    player_code: int
    web_name: str
    position: str
    team_name: str
    team_id: int
    price_tenths: int
    points: Tuple[int, ...]
    minutes: Tuple[int, ...]
    observed_gameweeks: Tuple[int, ...]


@dataclass(frozen=True)
class OpeningFold:
    target_capture: HistoricalCapture
    training_captures: Tuple[HistoricalCapture, ...]
    players: Tuple[OpeningPlayer, ...]
    raw_gameweeks_sha256: str


def build_historical_opening_policy_data(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    connection = _open_connection(path)
    try:
        captures = tuple(
            _required_capture(connection, season)
            for season in REGISTERED_SEASONS
        )
        folds = tuple(
            _load_opening_fold(connection, captures, target_index)
            for target_index in range(1, len(captures))
        )
    finally:
        connection.close()

    targets = [_fold_document(fold) for fold in folds]
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "registeredSeasonCodes": list(REGISTERED_SEASONS),
        "evaluationTargetSeasonCodes": list(TARGET_SEASONS),
        "evaluationTargetCount": len(targets),
        "outcomeGameweeks": list(OUTCOME_GAMEWEEKS),
        "decisionConstraint": {
            "priceSource": "target-season-raw-merged-gw-gameweek-1-value",
            "priceUnit": "tenths-of-a-million",
            "rawPayloadHashVerified": True,
            "sameGameweekXpExcluded": True,
            "excludedPredictiveColumns": list(
                RAW_EXCLUDED_PREDICTIVE_COLUMNS
            ),
        },
        "temporalDesign": {
            "method": "expanding-season-origin",
            "firstArchiveRole": "training-seed-not-evaluation-target",
            "trainingSeasonRule": (
                "strictly-earlier-registered-seasons-only"
            ),
            "targetOutcomeRole": "scoring-only",
        },
        "evaluationTargets": targets,
        "limitations": [
            (
                "The archive is a post-season immutable snapshot. Historical "
                "Gameweek 1 value is used only as the opening budget "
                "constraint and must not become a predictive feature."
            ),
            (
                "Only three target seasons have a strictly earlier registered "
                "training season; 2022/23 is the training seed, not a fourth "
                "evaluation target."
            ),
            (
                "This artifact proves cohort, price and outcome readiness. It "
                "does not select a horizon or risk policy."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "registeredSeasonCodes": artifact["registeredSeasonCodes"],
            "outcomeGameweeks": artifact["outcomeGameweeks"],
            "decisionConstraint": artifact["decisionConstraint"],
            "evaluationTargets": artifact["evaluationTargets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _required_capture(
    connection: Any,
    season_code: str,
) -> HistoricalCapture:
    capture = _load_capture(connection, season_code)
    if capture is None:
        raise TemporalRidgeError(
            "data.historical-season-archive-not-found",
            f"The registered {season_code} archive is unavailable.",
        )
    actual = {
        "sourceRevision": capture.source_revision,
        "playersSha256": capture.players_sha256,
        "gameweeksSha256": capture.gameweeks_sha256,
        "playerCount": capture.player_count,
        "playerGameweekCount": capture.player_gameweek_count,
    }
    if actual != EXPECTED_CAPTURE_IDENTITIES[season_code]:
        raise TemporalRidgeError(
            "data.historical-season-archive-identity",
            f"The {season_code} archive does not match its frozen identity.",
        )
    return capture


def _load_opening_fold(
    connection: Any,
    captures: Sequence[HistoricalCapture],
    target_index: int,
    include_outcomes: bool = True,
) -> OpeningFold:
    target = captures[target_index]
    raw = connection.execute(
        """
        SELECT gameweeks_csv_brotli
        FROM historical_fpl_season_captures
        WHERE capture_id = :capture_id;
        """,
        {"capture_id": target.capture_id},
    ).fetchone()
    if raw is None:
        raise TemporalRidgeError(
            "data.raw-gameweeks-not-found",
            f"The {target.season_code} raw Gameweek payload is unavailable.",
        )
    try:
        gameweeks_csv = brotli.decompress(bytes(raw["gameweeks_csv_brotli"]))
    except brotli.error as exception:
        raise TemporalRidgeError(
            "data.raw-gameweeks-brotli",
            f"The {target.season_code} raw Gameweek payload is invalid.",
        ) from exception
    payload_sha256 = hashlib.sha256(gameweeks_csv).hexdigest()
    if payload_sha256 != target.gameweeks_sha256:
        raise TemporalRidgeError(
            "data.raw-gameweeks-sha256",
            f"The {target.season_code} raw Gameweek payload hash differs.",
        )

    identities = _load_player_identities(connection, target)
    raw_players = _parse_opening_players(
        target.season_code,
        gameweeks_csv,
        identities,
    )
    outcomes = _load_outcomes(connection, target) if include_outcomes else {}
    team_ids = {
        name: index
        for index, name in enumerate(
            sorted({row["teamName"] for row in raw_players.values()}),
            start=1,
        )
    }
    players = tuple(
        OpeningPlayer(
            season_element_id=element_id,
            player_code=int(row["playerCode"]),
            web_name=str(row["webName"]),
            position=str(row["position"]),
            team_name=str(row["teamName"]),
            team_id=team_ids[str(row["teamName"])],
            price_tenths=int(row["priceTenths"]),
            points=(
                tuple(
                    outcomes.get(
                        (int(row["playerCode"]), gameweek),
                        (0, 0),
                    )[0]
                    for gameweek in OUTCOME_GAMEWEEKS
                )
                if include_outcomes
                else ()
            ),
            minutes=(
                tuple(
                    outcomes.get(
                        (int(row["playerCode"]), gameweek),
                        (0, 0),
                    )[1]
                    for gameweek in OUTCOME_GAMEWEEKS
                )
                if include_outcomes
                else ()
            ),
            observed_gameweeks=(
                tuple(
                    gameweek
                    for gameweek in OUTCOME_GAMEWEEKS
                    if (int(row["playerCode"]), gameweek) in outcomes
                )
                if include_outcomes
                else ()
            ),
        )
        for element_id, row in sorted(raw_players.items())
    )
    _require_legal_pool(target.season_code, players)
    return OpeningFold(
        target_capture=target,
        training_captures=tuple(captures[:target_index]),
        players=players,
        raw_gameweeks_sha256=payload_sha256,
    )


def _load_player_identities(
    connection: Any,
    capture: HistoricalCapture,
) -> Dict[int, Dict[str, Any]]:
    rows = connection.execute(
        """
        SELECT season_element_id, player_code, web_name
        FROM historical_fpl_players
        WHERE capture_id = :capture_id
        ORDER BY season_element_id;
        """,
        {"capture_id": capture.capture_id},
    ).fetchall()
    if len(rows) != capture.player_count:
        raise TemporalRidgeError(
            "data.historical-player-coverage",
            f"The {capture.season_code} player identity table is incomplete.",
        )
    return {
        int(row["season_element_id"]): {
            "playerCode": int(row["player_code"]),
            "webName": str(row["web_name"]),
        }
        for row in rows
    }


def _parse_opening_players(
    season_code: str,
    payload: bytes,
    identities: Mapping[int, Mapping[str, Any]],
) -> Dict[int, Dict[str, Any]]:
    try:
        text = payload.decode("utf-8-sig")
    except UnicodeDecodeError as exception:
        raise TemporalRidgeError(
            "data.raw-gameweeks-encoding",
            f"The {season_code} raw Gameweek payload is not UTF-8.",
        ) from exception
    reader = csv.DictReader(io.StringIO(text, newline=""))
    columns = frozenset(reader.fieldnames or ())
    missing = sorted(RAW_REQUIRED_COLUMNS - columns)
    if missing:
        raise TemporalRidgeError(
            "data.raw-gameweeks-schema",
            f"The {season_code} payload is missing: {', '.join(missing)}.",
        )
    opening: Dict[int, Dict[str, Any]] = {}
    try:
        for row in reader:
            if int(row["GW"]) != 1:
                continue
            element_id = int(row["element"])
            identity = identities.get(element_id)
            if identity is None:
                raise TemporalRidgeError(
                    "data.opening-player-identity",
                    f"{season_code} element {element_id} has no stable code.",
                )
            position = POSITION_MAP.get(str(row["position"]))
            if position is None:
                raise TemporalRidgeError(
                    "data.opening-player-position",
                    f"{season_code} element {element_id} has an invalid position.",
                )
            candidate = {
                "playerCode": int(identity["playerCode"]),
                "webName": str(identity["webName"]),
                "position": position,
                "teamName": str(row["team"]).strip(),
                "priceTenths": int(row["value"]),
            }
            if not candidate["teamName"] or candidate["priceTenths"] <= 0:
                raise TemporalRidgeError(
                    "data.opening-player-constraint",
                    f"{season_code} element {element_id} has invalid constraints.",
                )
            existing = opening.get(element_id)
            if existing is not None and existing != candidate:
                raise TemporalRidgeError(
                    "data.opening-player-duplicate",
                    f"{season_code} element {element_id} has inconsistent GW1 rows.",
                )
            opening[element_id] = candidate
    except ValueError as exception:
        raise TemporalRidgeError(
            "data.raw-gameweeks-value",
            f"The {season_code} GW1 constraint fields are invalid.",
        ) from exception
    if not opening:
        raise TemporalRidgeError(
            "data.opening-player-empty",
            f"The {season_code} payload has no Gameweek 1 players.",
        )
    return opening


def _load_outcomes(
    connection: Any,
    capture: HistoricalCapture,
) -> Dict[Tuple[int, int], Tuple[int, int]]:
    rows = connection.execute(
        """
        SELECT
            player_code,
            gameweek,
            SUM(total_points) AS total_points,
            SUM(minutes) AS minutes
        FROM historical_fpl_player_gameweeks
        WHERE capture_id = :capture_id
          AND gameweek BETWEEN :first_gameweek AND :last_gameweek
        GROUP BY player_code, gameweek
        ORDER BY player_code, gameweek;
        """,
        {
            "capture_id": capture.capture_id,
            "first_gameweek": OUTCOME_GAMEWEEKS[0],
            "last_gameweek": OUTCOME_GAMEWEEKS[-1],
        },
    ).fetchall()
    return {
        (int(row["player_code"]), int(row["gameweek"])): (
            int(row["total_points"]),
            int(row["minutes"]),
        )
        for row in rows
    }


def _require_legal_pool(
    season_code: str,
    players: Sequence[OpeningPlayer],
) -> None:
    positions = Counter(player.position for player in players)
    missing = {
        position: quota - positions[position]
        for position, quota in POSITION_QUOTAS.items()
        if positions[position] < quota
    }
    if missing:
        raise TemporalRidgeError(
            "data.opening-player-position-coverage",
            f"The {season_code} opening cohort cannot form a legal squad.",
        )
    cheapest_quota_price = sum(
        sum(
            sorted(
                player.price_tenths
                for player in players
                if player.position == position
            )[:quota]
        )
        for position, quota in POSITION_QUOTAS.items()
    )
    if cheapest_quota_price > 1000:
        raise TemporalRidgeError(
            "data.opening-player-budget",
            f"The {season_code} opening cohort exceeds the squad budget floor.",
        )


def _fold_document(fold: OpeningFold) -> Dict[str, Any]:
    if any(
        len(player.points) != len(OUTCOME_GAMEWEEKS)
        or len(player.minutes) != len(OUTCOME_GAMEWEEKS)
        for player in fold.players
    ):
        raise TemporalRidgeError(
            "data.opening-outcome-coverage",
            "The policy data artifact requires loaded target outcomes.",
        )
    positions = Counter(player.position for player in fold.players)
    teams = Counter(player.team_name for player in fold.players)
    observed = Counter(
        gameweek
        for player in fold.players
        for gameweek in player.observed_gameweeks
    )
    cohort_identity = [
        {
            "seasonElementId": player.season_element_id,
            "playerCode": player.player_code,
            "position": player.position,
            "teamName": player.team_name,
            "priceTenths": player.price_tenths,
        }
        for player in fold.players
    ]
    outcomes = [
        {
            "playerCode": player.player_code,
            "points": list(player.points),
            "minutes": list(player.minutes),
            "observedGameweeks": list(player.observed_gameweeks),
        }
        for player in fold.players
    ]
    return {
        "targetSeasonCode": fold.target_capture.season_code,
        "targetCaptureId": fold.target_capture.capture_id,
        "trainingSeasonCodes": [
            capture.season_code for capture in fold.training_captures
        ],
        "trainingCaptureIds": [
            capture.capture_id for capture in fold.training_captures
        ],
        "openingPlayerCount": len(fold.players),
        "teamCount": len(teams),
        "positionCounts": dict(sorted(positions.items())),
        "openingPriceMinimumTenths": min(
            player.price_tenths for player in fold.players
        ),
        "openingPriceMaximumTenths": max(
            player.price_tenths for player in fold.players
        ),
        "outcomeObservedPlayerCounts": {
            str(gameweek): observed[gameweek]
            for gameweek in OUTCOME_GAMEWEEKS
        },
        "missingOutcomeRowsScoreAsZero": True,
        "rawGameweeksSha256": fold.raw_gameweeks_sha256,
        "openingCohortIdentitySha256": _sha256(cohort_identity),
        "outcomeIdentitySha256": _sha256(outcomes),
    }


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Audit leakage-safe historical opening cohorts, GW1 prices and "
            "GW1-8 outcomes for expanding-season policy evaluation."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_historical_opening_policy_data(options.database)
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
