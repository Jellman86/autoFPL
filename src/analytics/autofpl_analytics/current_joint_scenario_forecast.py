from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

import numpy as np

from .historical_joint_scenario_evaluation import (
    EVALUATOR_VERSION,
    MODEL_NAME,
    evaluate_historical_joint_scenarios,
    generate_joint_fold,
)
from .historical_preseason_evaluation import (
    _build_samples,
    _load_capture,
)
from .multi_season_player_forecast import (
    CURRENT_GAMEWEEK,
    CURRENT_SEASON,
    STATUS as POINT_FORECAST_STATUS,
    build_multi_season_player_forecast,
)
from .preseason_participation_forecast import (
    STATUS as PARTICIPATION_FORECAST_STATUS,
    build_preseason_participation_forecast,
)
from .temporal_ridge import (
    Prediction,
    Sample,
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-joint-player-gameweek-scenario-shadow"
ARTIFACT_VERSION = "current-joint-scenario-shadow-v1"
STATUS = "prospective-shadow-unscored"
APPEARANCE_VARIANT = "officialCeilingFactorized"
RAW_APPEARANCE_VARIANT = "rawIndependent"
POINT_AVAILABILITY_FUSION = "official-appearance-ceiling-ratio-v1"


def build_current_joint_scenario_forecast(
    database_path: Path,
    season_code: str = CURRENT_SEASON,
    gameweek: int = CURRENT_GAMEWEEK,
) -> Dict[str, Any]:
    path = Path(database_path)
    screen = evaluate_historical_joint_scenarios(path)
    if (
        screen["status"] != "complete"
        or screen["retrospectiveScreen"] is None
        or screen["retrospectiveScreen"]["status"]
        != "passes-retrospective-screen"
    ):
        raise TemporalRidgeError(
            "scenario.retrospective-screen-not-passed",
            "The frozen joint scenario candidate has not passed its "
            "retrospective screen.",
        )
    point_forecast = build_multi_season_player_forecast(
        path,
        season_code,
        gameweek,
    )
    participation_forecast = build_preseason_participation_forecast(
        path,
        season_code,
        gameweek,
    )
    return _build_from_artifacts(
        path,
        point_forecast,
        participation_forecast,
        screen,
    )


def _build_from_artifacts(
    database_path: Path,
    point_forecast: Mapping[str, Any],
    participation_forecast: Mapping[str, Any],
    screen: Mapping[str, Any],
) -> Dict[str, Any]:
    _require_artifact_alignment(
        point_forecast,
        participation_forecast,
        screen,
    )
    connection = _open_connection(Path(database_path))
    try:
        capture = _load_capture(
            connection,
            str(participation_forecast["training"]["seasonCode"]),
        )
        if capture is None:
            raise TemporalRidgeError(
                "scenario.source-archive-not-found",
                "The joint scenario source archive is unavailable.",
            )
        _require_capture_alignment(
            capture.capture_id,
            capture.players_sha256,
            capture.gameweeks_sha256,
            point_forecast,
            participation_forecast,
        )
        points_by_gameweek = _build_samples(
            connection,
            capture,
            target_name="total-points",
        )
        appearance_by_gameweek = _build_samples(
            connection,
            capture,
            target_name="appearance",
        )
    finally:
        connection.close()

    point_players = {
        int(player["playerId"]): player
        for player in point_forecast["players"]
    }
    participation_players = {
        int(player["playerId"]): player
        for player in participation_forecast["players"]
    }
    if (
        len(point_players) != len(point_forecast["players"])
        or len(participation_players)
        != len(participation_forecast["players"])
        or set(point_players) != set(participation_players)
    ):
        raise TemporalRidgeError(
            "scenario.current-player-alignment",
            "Current point and participation artifacts use different "
            "player cohorts.",
        )
    by_code: Dict[int, Dict[str, Any]] = {}
    for player_id in sorted(point_players):
        point_player = point_players[player_id]
        participation_player = participation_players[player_id]
        _require_player_alignment(point_player, participation_player)
        player_code = int(point_player["playerCode"])
        if player_code in by_code:
            raise TemporalRidgeError(
                "scenario.current-player-code-duplicate",
                "Current scenario players do not have unique stable codes.",
            )
        by_code[player_code] = {
            "point": point_player,
            "participation": participation_player,
            "availabilityAdjustedPointMean": (
                _availability_adjusted_point_mean(
                    point_player,
                    participation_player,
                )
            ),
        }

    season_code = str(point_forecast["seasonCode"])
    gameweek = int(point_forecast["gameweek"])
    target = [
        Sample(
            season_code=season_code,
            gameweek=gameweek,
            player_id=player_code,
            position=str(values["point"]["position"]),
            features={},
            actual=0,
        )
        for player_code, values in sorted(by_code.items())
    ]
    means = [
        Prediction(
            model=str(point_forecast["modelKey"]),
            season_code=season_code,
            gameweek=gameweek,
            player_id=sample.player_id,
            position=sample.position,
            predicted=float(
                by_code[sample.player_id][
                    "availabilityAdjustedPointMean"
                ]
            ),
            actual=0,
        )
        for sample in target
    ]
    appearances = [
        Prediction(
            model=APPEARANCE_VARIANT,
            season_code=season_code,
            gameweek=gameweek,
            player_id=sample.player_id,
            position=sample.position,
            predicted=float(
                by_code[sample.player_id]["participation"]["variants"][
                    APPEARANCE_VARIANT
                ]["appearanceProbability"]
            ),
            actual=0,
        )
        for sample in target
    ]
    joint = generate_joint_fold(
        points_by_gameweek,
        appearance_by_gameweek,
        target,
        means,
        appearances,
    )
    if np.any((joint.points != 0) & ~joint.played):
        raise TemporalRidgeError(
            "scenario.nonplayer-points.nonzero",
            "A generated non-playing scenario player has non-zero points.",
        )

    players = []
    for index, player_code in enumerate(joint.player_ids):
        point_player = by_code[player_code]["point"]
        participation_player = by_code[player_code]["participation"]
        appearance = participation_player["variants"][
            APPEARANCE_VARIANT
        ]
        raw_point_mean = float(point_player["expectedPoints"])
        adjusted_point_mean = float(
            by_code[player_code]["availabilityAdjustedPointMean"]
        )
        players.append(
            {
                "columnIndex": index,
                "playerId": int(point_player["playerId"]),
                "playerCode": player_code,
                "webName": str(point_player["webName"]),
                "teamId": int(point_player["teamId"]),
                "teamName": str(point_player["teamName"]),
                "position": str(point_player["position"]),
                "pointMean": adjusted_point_mean,
                "pointMeanBeforeAvailability": raw_point_mean,
                "pointAvailabilityMultiplier": _round(
                    0.0
                    if raw_point_mean == 0.0
                    else adjusted_point_mean / raw_point_mean
                ),
                "appearanceProbability": float(
                    appearance["appearanceProbability"]
                ),
                "officialStatus": str(
                    participation_player["officialStatus"]
                ),
                "officialChanceOfPlayingNextRound": (
                    participation_player[
                        "officialChanceOfPlayingNextRound"
                    ]
                ),
                "pointHistoryIdentityStatus": str(
                    point_player["historicalIdentityStatus"]
                ),
                "participationHistoryIdentityStatus": str(
                    participation_player["priorSeasonIdentityStatus"]
                ),
            }
        )

    candidate_screen = next(
        (
            model
            for model in screen["distributionModels"]
            if model["name"] == MODEL_NAME
        ),
        None,
    )
    if candidate_screen is None:
        raise TemporalRidgeError(
            "scenario.retrospective-screen-model",
            "The supplied retrospective screen does not contain the "
            "frozen joint scenario candidate.",
        )
    screen_summary = {
        "evaluatorVersion": screen["evaluatorVersion"],
        "dataIdentitySha256": screen["dataIdentitySha256"],
        "runIdentitySha256": screen["runIdentitySha256"],
        "status": screen["retrospectiveScreen"]["status"],
        "meanCrps": candidate_screen["metrics"]["meanCrps"],
        "aggregateCrpsImprovementFraction": screen[
            "retrospectiveScreen"
        ]["aggregateCrpsImprovementFraction"],
        "foldWins": screen["retrospectiveScreen"]["foldWins"],
        "foldCount": screen["retrospectiveScreen"]["foldCount"],
        "isPromoted": False,
    }
    point_rows = joint.points.tolist()
    played_rows = joint.played.tolist()
    scenario_content_sha256 = _sha256(
        {
            "playerCodes": list(joint.player_ids),
            "sourceGameweeks": list(joint.source_gameweeks),
            "pointRows": point_rows,
            "playedRows": played_rows,
        }
    )
    mean_deltas = np.mean(joint.points, axis=0) - np.asarray(
        joint.mean_predictions,
        dtype=float,
    )
    appearance_deltas = np.mean(joint.played, axis=0) - np.asarray(
        joint.appearance_predictions,
        dtype=float,
    )
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "seasonCode": season_code,
        "gameweek": gameweek,
        "deadlineUtc": point_forecast["deadlineUtc"],
        "decisionCutoffUtc": point_forecast["decisionCutoffUtc"],
        "officialCaptureId": int(point_forecast["officialCaptureId"]),
        "scenarioModelKey": MODEL_NAME,
        "appearanceVariant": APPEARANCE_VARIANT,
        "pointAvailabilityFusion": POINT_AVAILABILITY_FUSION,
        "scenarioCount": len(joint.source_gameweeks),
        "playerCount": len(players),
        "sourceGameweeks": list(joint.source_gameweeks),
        "players": players,
        "pointRows": point_rows,
        "playedRows": played_rows,
        "scenarioContentSha256": scenario_content_sha256,
        "scenarioDiagnostics": {
            "meanAbsolutePointMeanDelta": _round(
                float(np.mean(np.abs(mean_deltas)))
            ),
            "maximumAbsolutePointMeanDelta": _round(
                float(np.max(np.abs(mean_deltas)))
            ),
            "meanAbsoluteAppearanceProbabilityDelta": _round(
                float(np.mean(np.abs(appearance_deltas)))
            ),
            "maximumAbsoluteAppearanceProbabilityDelta": _round(
                float(np.max(np.abs(appearance_deltas)))
            ),
            "nonPlayingNonZeroPointCount": 0,
        },
        "training": {
            "sourceSeasonCode": capture.season_code,
            "sourceHistoricalCaptureId": capture.capture_id,
            "sourcePlayersSha256": capture.players_sha256,
            "sourceGameweeksSha256": capture.gameweeks_sha256,
            "pointForecastRunIdentitySha256": point_forecast[
                "runIdentitySha256"
            ],
            "participationForecastRunIdentitySha256": (
                participation_forecast["runIdentitySha256"]
            ),
            "retrospectiveScreen": screen_summary,
            "selfDonorAssignments": joint.self_donor_assignments,
            "fallbackDonorAssignments": (
                joint.fallback_donor_assignments
            ),
        },
        "distributionStatus": (
            "joint-scenario-shadow-prospective-current-season-unscored"
        ),
        "limitations": [
            "The 2025/26 screen was retrospective because its holdout had "
            "already been opened before this candidate was fixed.",
            "The official availability ceiling and current joint rows have "
            "not yet been scored against a 2026/27 outcome.",
            "The point-mean availability multiplier is a prospectively "
            "unscored coherence rule, not a promoted calibration.",
            "Source Gameweek rows preserve shared temporal shocks but do not "
            "explicitly simulate match scorelines, bonus or substitutions.",
            "Missing stable-code history uses a deterministic same-position "
            "donor and remains separately counted.",
            "This artifact cannot influence Baseline v0 advice or a user "
            "selection.",
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "decisionCutoffUtc": artifact["decisionCutoffUtc"],
            "training": artifact["training"],
            "sourceGameweeks": artifact["sourceGameweeks"],
            "players": artifact["players"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _require_artifact_alignment(
    point: Mapping[str, Any],
    participation: Mapping[str, Any],
    screen: Mapping[str, Any],
) -> None:
    if (
        point.get("status") != POINT_FORECAST_STATUS
        or bool(point.get("influencesAdvice"))
        or participation.get("status")
        != PARTICIPATION_FORECAST_STATUS
        or bool(participation.get("influencesAdvice"))
    ):
        raise TemporalRidgeError(
            "scenario.source-artifact-status",
            "Current scenario inputs are not the expected shadow artifacts.",
        )
    identities = (
        (
            str(point["seasonCode"]),
            int(point["gameweek"]),
            int(point["officialCaptureId"]),
            str(point["decisionCutoffUtc"]),
        ),
        (
            str(participation["seasonCode"]),
            int(participation["gameweek"]),
            int(participation["officialCaptureId"]),
            str(participation["decisionCutoffUtc"]),
        ),
    )
    if identities[0] != identities[1]:
        raise TemporalRidgeError(
            "scenario.source-artifact-alignment",
            "Point and participation artifacts do not share one exact "
            "current target.",
        )
    if (
        screen.get("evaluatorVersion") != EVALUATOR_VERSION
        or screen.get("retrospectiveScreen", {}).get("status")
        != "passes-retrospective-screen"
        or bool(screen.get("isPromoted"))
        or bool(screen.get("mayInfluenceAdvice"))
    ):
        raise TemporalRidgeError(
            "scenario.retrospective-screen-identity",
            "The supplied joint scenario screen is not the frozen "
            "research-only evaluator result.",
        )


def _require_capture_alignment(
    capture_id: int,
    players_sha256: str,
    gameweeks_sha256: str,
    point: Mapping[str, Any],
    participation: Mapping[str, Any],
) -> None:
    point_capture = next(
        (
            capture
            for capture in point["training"]["historicalCaptures"]
            if capture["seasonCode"]
            == participation["training"]["seasonCode"]
        ),
        None,
    )
    expected = {
        "captureId": capture_id,
        "playersSha256": players_sha256,
        "gameweeksSha256": gameweeks_sha256,
    }
    actual_point = (
        None
        if point_capture is None
        else {
            "captureId": int(point_capture["captureId"]),
            "playersSha256": str(point_capture["playersSha256"]),
            "gameweeksSha256": str(
                point_capture["gameweeksSha256"]
            ),
        }
    )
    actual_participation = {
        "captureId": int(
            participation["training"]["historicalCaptureId"]
        ),
        "playersSha256": str(
            participation["training"]["playersSha256"]
        ),
        "gameweeksSha256": str(
            participation["training"]["gameweeksSha256"]
        ),
    }
    if actual_point != expected or actual_participation != expected:
        raise TemporalRidgeError(
            "scenario.source-capture-alignment",
            "Scenario inputs do not share the exact source archive.",
        )


def _require_player_alignment(
    point: Mapping[str, Any],
    participation: Mapping[str, Any],
) -> None:
    point_identity = (
        int(point["playerId"]),
        int(point["playerCode"]),
        str(point["position"]),
        int(point["teamId"]),
    )
    participation_identity = (
        int(participation["playerId"]),
        int(participation["playerCode"]),
        str(participation["position"]),
        int(participation["teamId"]),
    )
    if point_identity != participation_identity:
        raise TemporalRidgeError(
            "scenario.current-player-identity",
            "A current point and participation player identity differs.",
        )


def _availability_adjusted_point_mean(
    point: Mapping[str, Any],
    participation: Mapping[str, Any],
) -> float:
    raw_point_mean = float(point["expectedPoints"])
    variants = participation.get("variants", {})
    try:
        raw_appearance = float(
            variants[RAW_APPEARANCE_VARIANT][
                "appearanceProbability"
            ]
        )
        constrained_appearance = float(
            variants[APPEARANCE_VARIANT]["appearanceProbability"]
        )
    except (KeyError, TypeError, ValueError) as exception:
        raise TemporalRidgeError(
            "scenario.appearance-variant",
            "A current player does not contain the required appearance "
            "probability variants.",
        ) from exception
    if (
        not np.isfinite(raw_point_mean)
        or not np.isfinite(raw_appearance)
        or not np.isfinite(constrained_appearance)
        or raw_appearance < 0.0
        or raw_appearance > 1.0
        or constrained_appearance < 0.0
        or constrained_appearance > raw_appearance
    ):
        raise TemporalRidgeError(
            "scenario.appearance-coherence",
            "Current point and appearance inputs cannot be coherently "
            "fused.",
        )
    if raw_appearance == 0.0:
        return 0.0
    return _round(
        raw_point_mean * constrained_appearance / raw_appearance
    )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Build the frozen current joint scenario shadow without "
            "changing served advice."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=CURRENT_SEASON)
    parser.add_argument("--gameweek", type=int, default=CURRENT_GAMEWEEK)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_joint_scenario_forecast(
            options.database,
            options.season,
            options.gameweek,
        )
        _write_report(artifact, options.output)
        return 0
    except TemporalRidgeError as exception:
        error = {
            "schemaVersion": SCHEMA_VERSION,
            "status": "error",
            "errorCode": exception.code,
            "message": str(exception),
        }
        sys.stderr.write(json.dumps(error, sort_keys=True) + "\n")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
