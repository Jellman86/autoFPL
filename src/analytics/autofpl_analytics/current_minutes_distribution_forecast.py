from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence

from .historical_minutes_distribution_evaluation import (
    CANDIDATE_MODEL,
    WeightedDistribution,
    _hurdle_distribution,
    _quantile,
)
from .historical_preseason_evaluation import _build_samples
from .preseason_participation_forecast import (
    CURRENT_GAMEWEEK,
    CURRENT_SEASON,
    _capture,
    _load_archive,
    _load_target,
    build_preseason_participation_forecast,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-minutes-distribution-shadow"
ARTIFACT_VERSION = "current-minutes-distribution-shadow-v1"
STATUS = "prospective-shadow-unscored"
SCREEN_DATA_IDENTITY = (
    "0ba7c4ff64c2d597b365870764983eeb3fe939b3bec177e159ced3fdda6e6d38"
)
SCREEN_RUN_IDENTITY = (
    "1eddd9fc5d58a29b1e617686d06b5ddd1b7376b365d4f0e6fcae918098ce8294"
)
SCREEN_MEAN_CRPS = 8.666169
SCREEN_PLAYER_EMPIRICAL_IMPROVEMENT = 0.172226


def build_current_minutes_distribution_forecast(
    database_path: Path,
    season_code: str = CURRENT_SEASON,
    gameweek: int = CURRENT_GAMEWEEK,
) -> Dict[str, Any]:
    path = Path(database_path)
    participation = build_preseason_participation_forecast(
        path, season_code, gameweek
    )
    connection = _open_connection(path)
    try:
        target = _load_target(connection, season_code, gameweek)
        if target is None:
            raise TemporalRidgeError(
                "data.current-target-not-found",
                "No cutoff-eligible official current target capture exists.",
            )
        archive = _load_archive(connection, target)
        if archive is None:
            raise TemporalRidgeError(
                "data.prior-season-archive-not-found",
                "The pinned prior-season archive is unavailable.",
            )
        capture = _capture(archive)
        samples = _build_samples(
            connection, capture, target_name="minutes"
        )
    finally:
        connection.close()

    player_support: DefaultDict[int, list[int]] = defaultdict(list)
    position_support: DefaultDict[str, list[int]] = defaultdict(list)
    for historical_gameweek in sorted(samples):
        for sample in samples[historical_gameweek]:
            if sample.actual > 0:
                player_support[sample.player_id].append(sample.actual)
                position_support[sample.position].append(sample.actual)
    players = [
        _player_document(player, player_support, position_support)
        for player in participation["players"]
    ]
    fallback_count = sum(
        player["conditionalSupportIdentity"]
        == "position-positive-minutes-fallback"
        for player in players
    )
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "productImportReady": False,
        "seasonCode": season_code,
        "gameweek": gameweek,
        "deadlineUtc": participation["deadlineUtc"],
        "decisionCutoffUtc": participation["decisionCutoffUtc"],
        "officialCaptureId": participation["officialCaptureId"],
        "sourceParticipationRunIdentitySha256": (
            participation["runIdentitySha256"]
        ),
        "model": {
            "modelKey": CANDIDATE_MODEL,
            "historicalScreenStatus": "passes-retrospective-screen",
            "historicalScreenDataIdentitySha256": SCREEN_DATA_IDENTITY,
            "historicalScreenRunIdentitySha256": SCREEN_RUN_IDENTITY,
            "historicalMeanCrps": SCREEN_MEAN_CRPS,
            "historicalCrpsImprovementOverPlayerEmpiricalFraction": (
                SCREEN_PLAYER_EMPIRICAL_IMPROVEMENT
            ),
            "appearanceVariants": [
                "raw-supported",
                "official-ceiling-prospective",
            ],
        },
        "training": {
            "seasonCode": capture.season_code,
            "historicalCaptureId": capture.capture_id,
            "sourceRevision": capture.source_revision,
            "playersSha256": capture.players_sha256,
            "gameweeksSha256": capture.gameweeks_sha256,
            "trainingGameweekCount": len(samples),
            "trainingRowCount": sum(len(rows) for rows in samples.values()),
        },
        "playerCount": len(players),
        "playerSupportCount": len(players) - fallback_count,
        "positionFallbackCount": fallback_count,
        "players": players,
        "prospectiveEvaluation": {
            "status": "registered-awaiting-2026-27-outcome",
            "primaryMetric": "crps",
            "diagnostics": [
                "central-80-coverage",
                "central-80-width",
                "mean-error",
            ],
            "variantsMustRemainFrozen": True,
        },
        "limitations": [
            "The retained historical screen is retrospective and cannot "
            "promote this current artifact.",
            "Official availability is a separately labelled prospective "
            "variant and was absent from the historical screen.",
            "The empirical support does not model new tactical or "
            "substitution regimes.",
            "This artifact is a minutes component, not a complete FPL-points "
            "distribution or squad recommendation.",
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "decisionCutoffUtc": artifact["decisionCutoffUtc"],
            "sourceParticipationRunIdentitySha256": (
                artifact["sourceParticipationRunIdentitySha256"]
            ),
            "training": artifact["training"],
            "historicalScreenRunIdentitySha256": SCREEN_RUN_IDENTITY,
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _player_document(
    player: Mapping[str, Any],
    player_support: Mapping[int, Sequence[int]],
    position_support: Mapping[str, Sequence[int]],
) -> Dict[str, Any]:
    player_code = int(player["playerCode"])
    position = str(player["position"])
    support = list(player_support.get(player_code, ()))
    if support:
        identity = "stable-code-player-positive-minutes"
    else:
        support = list(position_support.get(position, ()))
        identity = "position-positive-minutes-fallback"
    if not support:
        raise TemporalRidgeError(
            "data.current-minutes-support-unavailable",
            "A current player has no player or position minutes support.",
        )
    raw_probability = float(player["appearanceProbability"])
    official_probability = float(
        player["variants"]["officialCeilingFactorized"][
            "appearanceProbability"
        ]
    )
    return {
        "playerId": int(player["playerId"]),
        "playerCode": player_code,
        "webName": str(player["webName"]),
        "position": position,
        "teamId": int(player["teamId"]),
        "conditionalSupportIdentity": identity,
        "conditionalSupportCount": len(support),
        "variants": {
            "rawSupported": _distribution_document(
                _hurdle_distribution(raw_probability, support),
                "historically-screened-appearance-input",
            ),
            "officialCeilingProspective": _distribution_document(
                _hurdle_distribution(official_probability, support),
                "prospective-official-availability-input",
            ),
        },
    }


def _distribution_document(
    distribution: WeightedDistribution, status: str
) -> Dict[str, Any]:
    expected = sum(
        value * weight
        for value, weight in zip(distribution.values, distribution.weights)
    )
    return {
        "status": status,
        "isPromoted": False,
        "influencesAdvice": False,
        "zeroMinutesProbability": _round(
            next(
                (
                    weight
                    for value, weight in zip(
                        distribution.values, distribution.weights
                    )
                    if value == 0
                ),
                0.0,
            )
        ),
        "expectedMinutes": _round(expected),
        "p10Minutes": _round(_quantile(distribution, 0.10)),
        "medianMinutes": _round(_quantile(distribution, 0.50)),
        "p90Minutes": _round(_quantile(distribution, 0.90)),
        "support": [
            {"minutes": _round(value), "probability": _round(weight)}
            for value, weight in zip(
                distribution.values, distribution.weights
            )
        ],
    }


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="Build the frozen current minutes-distribution shadow."
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=CURRENT_SEASON)
    parser.add_argument("--gameweek", type=int, default=CURRENT_GAMEWEEK)
    parser.add_argument("--output", required=True, type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_minutes_distribution_forecast(
            options.database, options.season, options.gameweek
        )
        _write_report(artifact, options.output)
        return 0
    except (TemporalRidgeError, OSError, ValueError) as exception:
        code = getattr(exception, "code", "forecast.failed")
        sys.stderr.write(
            json.dumps(
                {"error": {"code": code, "message": str(exception)}},
                sort_keys=True,
            )
            + "\n"
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
