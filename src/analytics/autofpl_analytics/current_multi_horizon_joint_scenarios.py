from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

import numpy as np

from .current_joint_scenario_forecast import (
    APPEARANCE_VARIANT,
    POINT_AVAILABILITY_FUSION,
    RAW_APPEARANCE_VARIANT,
    _availability_adjusted_point_mean,
    _require_capture_alignment,
    _require_player_alignment,
    _retained_screen,
    _utc_instant,
)
from .current_multi_horizon_player_forecast import (
    ARTIFACT_VERSION as POINT_ARTIFACT_VERSION,
    DECISION_HORIZONS,
    STATUS as POINT_FORECAST_STATUS,
    TARGET_GAMEWEEKS,
    build_current_multi_horizon_player_forecast,
)
from .historical_joint_scenario_evaluation import (
    MODEL_NAME,
    generate_joint_fold,
)
from .historical_preseason_evaluation import _build_samples, _load_capture
from .multi_season_player_forecast import CURRENT_SEASON
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
ARTIFACT_TYPE = "current-multi-horizon-joint-scenario-shadow"
ARTIFACT_VERSION = "current-multi-horizon-joint-scenario-shadow-v1"
STATUS = "prospective-shadow-unscored"
PATH_RANDOM_SEED = 20_260_729
PATH_ENGINE = "numpy-pcg64-independent-weekly-permutation-v1"
FUTURE_AVAILABILITY_POLICY = (
    "gw1-official-ceiling-then-raw-preseason-appearance"
)


def build_current_multi_horizon_joint_scenarios(
    database_path: Path,
    season_code: str = CURRENT_SEASON,
) -> Dict[str, Any]:
    path = Path(database_path)
    point_forecast = build_current_multi_horizon_player_forecast(
        path,
        season_code,
    )
    participation_forecast = build_preseason_participation_forecast(
        path,
        season_code,
        TARGET_GAMEWEEKS[0],
    )
    return _build_from_artifacts(
        path,
        point_forecast,
        participation_forecast,
        _retained_screen(),
    )


def _build_from_artifacts(
    database_path: Path,
    point_forecast: Mapping[str, Any],
    participation_forecast: Mapping[str, Any],
    screen: Mapping[str, Any],
    *,
    allowed_point_artifact_versions: Sequence[str] = (
        POINT_ARTIFACT_VERSION,
    ),
) -> Dict[str, Any]:
    _require_sources(
        point_forecast,
        participation_forecast,
        screen,
        allowed_point_artifact_versions,
    )
    connection = _open_connection(Path(database_path))
    try:
        capture = _load_capture(
            connection,
            str(participation_forecast["training"]["seasonCode"]),
        )
        if capture is None:
            raise TemporalRidgeError(
                "multi-scenario.source-archive-not-found",
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

    point_players = _unique_players(
        point_forecast,
        "multi-scenario.point-player-duplicate",
    )
    participation_players = _unique_players(
        participation_forecast,
        "multi-scenario.participation-player-duplicate",
    )
    if set(point_players) != set(participation_players):
        raise TemporalRidgeError(
            "multi-scenario.current-player-alignment",
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
                "multi-scenario.current-player-code-duplicate",
                "Current scenario players do not have unique stable codes.",
            )
        point_by_gameweek = {
            int(row["gameweek"]): float(row["expectedPoints"])
            for row in point_player["gameweeks"]
        }
        if set(point_by_gameweek) != set(TARGET_GAMEWEEKS):
            raise TemporalRidgeError(
                "multi-scenario.point-gameweek-coverage",
                "A current player does not have exactly the registered "
                "Gameweek point means.",
            )
        by_code[player_code] = {
            "point": point_player,
            "participation": participation_player,
            "pointByGameweek": point_by_gameweek,
            "pointWeekByGameweek": {
                int(row["gameweek"]): dict(row)
                for row in point_player["gameweeks"]
            },
        }

    player_codes = tuple(sorted(by_code))
    joint_by_gameweek = {}
    week_player_inputs: Dict[int, Dict[int, Dict[str, Any]]] = {}
    for gameweek in TARGET_GAMEWEEKS:
        target = [
            Sample(
                season_code=str(point_forecast["seasonCode"]),
                gameweek=gameweek,
                player_id=player_code,
                position=str(values["point"]["position"]),
                features={},
                actual=0,
            )
            for player_code, values in sorted(by_code.items())
        ]
        inputs = {
            player_code: _week_input(gameweek, values)
            for player_code, values in by_code.items()
        }
        means = [
            Prediction(
                model=str(point_forecast["modelKey"]),
                season_code=sample.season_code,
                gameweek=gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=float(inputs[sample.player_id]["pointMean"]),
                actual=0,
            )
            for sample in target
        ]
        appearances = [
            Prediction(
                model=str(inputs[sample.player_id]["appearanceVariant"]),
                season_code=sample.season_code,
                gameweek=gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=float(
                    inputs[sample.player_id]["appearanceProbability"]
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
        if joint.player_ids != player_codes:
            raise TemporalRidgeError(
                "multi-scenario.player-column-alignment",
                "A generated Gameweek uses a different player column order.",
            )
        if np.any((joint.points != 0) & ~joint.played):
            raise TemporalRidgeError(
                "multi-scenario.nonplayer-points.nonzero",
                "A generated non-playing scenario player has non-zero points.",
            )
        joint_by_gameweek[gameweek] = joint
        week_player_inputs[gameweek] = inputs

    scenario_counts = {
        len(joint.source_gameweeks) for joint in joint_by_gameweek.values()
    }
    if len(scenario_counts) != 1:
        raise TemporalRidgeError(
            "multi-scenario.scenario-count-alignment",
            "Generated Gameweeks do not share one scenario count.",
        )
    scenario_count = scenario_counts.pop()
    weeks = []
    for gameweek in TARGET_GAMEWEEKS:
        joint = joint_by_gameweek[gameweek]
        permutation = _path_permutation(scenario_count, gameweek)
        point_rows = joint.points[permutation].tolist()
        played_rows = joint.played[permutation].tolist()
        mean_deltas = np.mean(joint.points, axis=0) - np.asarray(
            joint.mean_predictions,
            dtype=float,
        )
        appearance_deltas = np.mean(joint.played, axis=0) - np.asarray(
            joint.appearance_predictions,
            dtype=float,
        )
        weeks.append(
            {
                "gameweek": gameweek,
                "appearancePolicy": (
                    APPEARANCE_VARIANT
                    if gameweek == TARGET_GAMEWEEKS[0]
                    else RAW_APPEARANCE_VARIANT
                ),
                "sourceGameweeksByPath": [
                    int(joint.source_gameweeks[index])
                    for index in permutation
                ],
                "pointRows": point_rows,
                "playedRows": played_rows,
                "diagnostics": {
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
                    "selfDonorAssignments": (
                        joint.self_donor_assignments
                    ),
                    "fallbackDonorAssignments": (
                        joint.fallback_donor_assignments
                    ),
                },
            }
        )

    players = []
    for column_index, player_code in enumerate(player_codes):
        values = by_code[player_code]
        point_player = values["point"]
        participation_player = values["participation"]
        players.append(
            {
                "columnIndex": column_index,
                "playerId": int(point_player["playerId"]),
                "playerCode": player_code,
                "webName": str(point_player["webName"]),
                "teamId": int(point_player["teamId"]),
                "teamName": str(point_player["teamName"]),
                "position": str(point_player["position"]),
                "officialStatus": str(
                    participation_player["officialStatus"]
                ),
                "officialChanceOfPlayingNextRound": participation_player[
                    "officialChanceOfPlayingNextRound"
                ],
                "pointHistoryIdentityStatus": str(
                    point_player["historicalIdentityStatus"]
                ),
                "participationHistoryIdentityStatus": str(
                    participation_player["priorSeasonIdentityStatus"]
                ),
                "gameweeks": [
                    {
                        "gameweek": gameweek,
                        **week_player_inputs[gameweek][player_code],
                    }
                    for gameweek in TARGET_GAMEWEEKS
                ],
                "horizons": list(point_player["horizons"]),
            }
        )

    scenario_content_sha256 = _sha256(
        {
            "playerCodes": list(player_codes),
            "weeks": [
                {
                    "gameweek": week["gameweek"],
                    "sourceGameweeksByPath": week[
                        "sourceGameweeksByPath"
                    ],
                    "pointRows": week["pointRows"],
                    "playedRows": week["playedRows"],
                }
                for week in weeks
            ],
        }
    )
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "seasonCode": point_forecast["seasonCode"],
        "openingGameweek": point_forecast["openingGameweek"],
        "targetGameweeks": list(TARGET_GAMEWEEKS),
        "decisionHorizons": list(DECISION_HORIZONS),
        "deadlineUtc": point_forecast["deadlineUtc"],
        "decisionCutoffUtc": point_forecast["decisionCutoffUtc"],
        "officialCaptureId": int(point_forecast["officialCaptureId"]),
        "scenarioModelKey": MODEL_NAME,
        "scenarioCount": scenario_count,
        "playerCount": len(players),
        "pathConstruction": {
            "engine": PATH_ENGINE,
            "randomSeed": PATH_RANDOM_SEED,
            "weeklyMarginalRule": (
                "each-source-gameweek-used-exactly-once-per-target-gameweek"
            ),
            "withinGameweekDependence": (
                "whole-source-gameweek-row-preserved"
            ),
            "crossGameweekDependence": (
                "independent-fixed-seed-permutation-unvalidated"
            ),
        },
        "availabilityPolicy": FUTURE_AVAILABILITY_POLICY,
        "pointAvailabilityFusion": POINT_AVAILABILITY_FUSION,
        "players": players,
        "weeks": weeks,
        "scenarioContentSha256": scenario_content_sha256,
        "training": {
            "sourceSeasonCode": capture.season_code,
            "sourceHistoricalCaptureId": capture.capture_id,
            "sourcePlayersSha256": capture.players_sha256,
            "sourceGameweeksSha256": capture.gameweeks_sha256,
            "pointForecastArtifactVersion": point_forecast[
                "artifactVersion"
            ],
            "pointForecastRunIdentitySha256": point_forecast[
                "runIdentitySha256"
            ],
            "participationForecastRunIdentitySha256": (
                participation_forecast["runIdentitySha256"]
            ),
            "retrospectiveScreenRunIdentitySha256": screen[
                "runIdentitySha256"
            ],
        },
        "distributionStatus": (
            "multi-gameweek-joint-scenario-shadow-prospective-unscored"
        ),
        "limitations": [
            (
                "The weekly whole-row residual candidate passed its "
                "retrospective CRPS screen, but these current paths remain "
                "prospectively unscored."
            ),
            (
                "Whole source-Gameweek rows preserve within-week shared "
                "shocks. Cross-Gameweek paths use a fixed independent "
                "permutation because temporal residual dependence has not "
                "yet been estimated."
            ),
            (
                "Gameweek 1 uses the official appearance ceiling. Later "
                "weeks revert to the raw preseason appearance estimate "
                "rather than assuming an unknown injury duration."
            ),
            (
                "The 38 exact donor paths are a CPU reference support, not "
                "the eventual large batched Monte Carlo workload."
            ),
            (
                "This artifact cannot influence served advice or mutate an "
                "owner squad."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "decisionCutoffUtc": artifact["decisionCutoffUtc"],
            "pathConstruction": artifact["pathConstruction"],
            "availabilityPolicy": artifact["availabilityPolicy"],
            "training": artifact["training"],
            "scenarioContentSha256": scenario_content_sha256,
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _unique_players(
    artifact: Mapping[str, Any],
    error_code: str,
) -> Dict[int, Mapping[str, Any]]:
    players = artifact.get("players")
    if not isinstance(players, list):
        raise TemporalRidgeError(
            error_code,
            "A scenario source does not contain a player list.",
        )
    by_id = {int(player["playerId"]): player for player in players}
    if len(by_id) != len(players):
        raise TemporalRidgeError(
            error_code,
            "A scenario source contains duplicate player identifiers.",
        )
    return by_id


def _week_input(
    gameweek: int,
    values: Mapping[str, Any],
) -> Dict[str, Any]:
    point = values["point"]
    participation = values["participation"]
    raw_point_mean = float(values["pointByGameweek"][gameweek])
    point_week = values["pointWeekByGameweek"][gameweek]
    variants = participation.get("variants", {})
    try:
        raw_appearance = float(
            variants[RAW_APPEARANCE_VARIANT]["appearanceProbability"]
        )
        constrained_appearance = float(
            variants[APPEARANCE_VARIANT]["appearanceProbability"]
        )
    except (KeyError, TypeError, ValueError) as exception:
        raise TemporalRidgeError(
            "multi-scenario.appearance-variant",
            "A current player does not contain the required appearance "
            "probability variants.",
        ) from exception
    participation_raw_appearance = raw_appearance
    if "appearanceProbability" in point_week:
        raw_appearance = float(point_week["appearanceProbability"])
        try:
            appearance_ceiling = float(
                variants[APPEARANCE_VARIANT]["availability"][
                    "appearanceProbabilityCeiling"
                ]
            )
        except (KeyError, TypeError, ValueError):
            appearance_ceiling = (
                constrained_appearance
                if constrained_appearance
                < participation_raw_appearance
                else 1.0
            )
        if (
            not np.isfinite(raw_appearance)
            or not np.isfinite(appearance_ceiling)
            or raw_appearance < 0.0
            or raw_appearance > 1.0
            or appearance_ceiling < 0.0
            or appearance_ceiling > 1.0
        ):
            raise TemporalRidgeError(
                "multi-scenario.point-appearance",
                "The point model appearance probability is invalid.",
            )
        constrained_appearance = min(
            raw_appearance,
            appearance_ceiling,
        )
        if gameweek == TARGET_GAMEWEEKS[0]:
            point_mean = (
                0.0
                if raw_appearance == 0.0
                else raw_point_mean
                * constrained_appearance
                / raw_appearance
            )
            appearance_probability = constrained_appearance
            appearance_variant = (
                "point-model-appearance-with-official-ceiling"
            )
        else:
            point_mean = raw_point_mean
            appearance_probability = raw_appearance
            appearance_variant = "point-model-appearance"
    else:
        point_with_current_mean = {
            **point,
            "expectedPoints": raw_point_mean,
        }
        if gameweek == TARGET_GAMEWEEKS[0]:
            point_mean = _availability_adjusted_point_mean(
                point_with_current_mean,
                participation,
            )
            appearance_probability = constrained_appearance
            appearance_variant = APPEARANCE_VARIANT
        else:
            point_mean = raw_point_mean
            appearance_probability = raw_appearance
            appearance_variant = RAW_APPEARANCE_VARIANT
    return {
        "pointMean": _round(point_mean),
        "pointMeanBeforeAvailability": _round(raw_point_mean),
        "pointAvailabilityMultiplier": _round(
            0.0 if raw_point_mean == 0.0 else point_mean / raw_point_mean
        ),
        "appearanceProbability": _round(appearance_probability),
        "appearanceVariant": appearance_variant,
    }


def _path_permutation(scenario_count: int, gameweek: int) -> np.ndarray:
    if scenario_count <= 0:
        raise TemporalRidgeError(
            "multi-scenario.empty-path-support",
            "A multi-Gameweek scenario requires at least one path.",
        )
    if gameweek == TARGET_GAMEWEEKS[0]:
        return np.arange(scenario_count, dtype=np.int64)
    generator = np.random.Generator(
        np.random.PCG64(PATH_RANDOM_SEED + gameweek)
    )
    return generator.permutation(scenario_count)


def _require_sources(
    point: Mapping[str, Any],
    participation: Mapping[str, Any],
    screen: Mapping[str, Any],
    allowed_point_artifact_versions: Sequence[str] = (
        POINT_ARTIFACT_VERSION,
    ),
) -> None:
    if (
        point.get("status") != POINT_FORECAST_STATUS
        or point.get("artifactVersion")
        not in set(allowed_point_artifact_versions)
        or bool(point.get("influencesAdvice"))
        or list(point.get("targetGameweeks", []))
        != list(TARGET_GAMEWEEKS)
        or list(point.get("decisionHorizons", []))
        != list(DECISION_HORIZONS)
    ):
        raise TemporalRidgeError(
            "multi-scenario.point-source",
            "The point source is not the fixed multi-horizon shadow.",
        )
    if (
        participation.get("status") != PARTICIPATION_FORECAST_STATUS
        or bool(participation.get("influencesAdvice"))
    ):
        raise TemporalRidgeError(
            "multi-scenario.participation-source",
            "The participation source is not the fixed shadow.",
        )
    identities = (
        (
            str(point["seasonCode"]),
            int(point["officialCaptureId"]),
            _utc_instant(point["decisionCutoffUtc"]),
        ),
        (
            str(participation["seasonCode"]),
            int(participation["officialCaptureId"]),
            _utc_instant(participation["decisionCutoffUtc"]),
        ),
    )
    if identities[0] != identities[1]:
        raise TemporalRidgeError(
            "multi-scenario.source-alignment",
            "Point and participation sources do not share one cutoff.",
        )
    if (
        screen.get("retrospectiveScreen", {}).get("status")
        != "passes-retrospective-screen"
        or bool(screen.get("isPromoted"))
        or bool(screen.get("mayInfluenceAdvice"))
    ):
        raise TemporalRidgeError(
            "multi-scenario.retrospective-screen",
            "The weekly scenario candidate screen is not the frozen "
            "research-only result.",
        )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Emit fixed-seed whole-row Gameweek 1-8 scenario paths for the "
            "registered opening-squad horizons."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=CURRENT_SEASON)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_multi_horizon_joint_scenarios(
            options.database,
            options.season,
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
