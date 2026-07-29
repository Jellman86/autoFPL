from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

import numpy as np

from .current_multi_horizon_joint_scenarios import _path_permutation
from .historical_joint_scenario_evaluation import (
    MODEL_NAME,
    generate_joint_fold,
)
from .historical_opening_forecast_reconstruction import (
    ARTIFACT_VERSION as FORECAST_ARTIFACT_VERSION,
    FIXTURE_PROXY,
    STATUS as FORECAST_STATUS,
    TARGET_GAMEWEEKS,
    _load_fixture_proxy,
    build_historical_opening_forecast_reconstruction,
)
from .historical_opening_policy_data import (
    REGISTERED_SEASONS,
    _load_opening_fold,
    _required_capture,
)
from .historical_participation_evaluation import (
    _candidate_name,
    _predict_classifier,
)
from .historical_preseason_evaluation import (
    HistoricalGameweek,
    _build_samples,
    _features as participation_features,
    _load_historical_gameweeks,
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
ARTIFACT_TYPE = "historical-opening-joint-scenario-reconstruction"
ARTIFACT_VERSION = "historical-opening-joint-scenario-reconstruction-v1"
STATUS = "retrospective-input-reconstruction-no-outcomes-opened"
APPEARANCE_MODEL = _candidate_name("appearance")
APPEARANCE_POLICY = "raw-prior-season-preseason-classifier-all-gameweeks"


def build_historical_opening_scenario_reconstruction(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    forecast = build_historical_opening_forecast_reconstruction(path)
    _require_forecast(forecast)
    connection = _open_connection(path)
    try:
        captures = tuple(
            _required_capture(connection, season)
            for season in REGISTERED_SEASONS
        )
        forecast_targets = {
            str(target["targetSeasonCode"]): target
            for target in forecast["targets"]
        }
        targets = [
            _reconstruct_target(
                connection,
                _load_opening_fold(
                    connection,
                    captures,
                    target_index,
                    include_outcomes=False,
                ),
                forecast_targets[captures[target_index].season_code],
            )
            for target_index in range(1, len(captures))
        ]
    finally:
        connection.close()

    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "scenarioModel": MODEL_NAME,
        "appearanceModel": APPEARANCE_MODEL,
        "appearancePolicy": APPEARANCE_POLICY,
        "targetGameweeks": list(TARGET_GAMEWEEKS),
        "forecastSource": {
            "artifactVersion": forecast["artifactVersion"],
            "dataIdentitySha256": forecast["dataIdentitySha256"],
            "runIdentitySha256": forecast["runIdentitySha256"],
        },
        "temporalDesign": {
            "pointTrainingRule": "strictly-earlier-season-archives-only",
            "appearanceTrainingRule": "latest-strictly-earlier-season-only",
            "scenarioDonorRule": "latest-strictly-earlier-season-only",
            "targetPerformanceFieldsRead": False,
            "targetFixtureInput": FIXTURE_PROXY,
            "weeklyPathPairing": (
                "fixed-current-engine-independent-weekly-permutation"
            ),
        },
        "targets": targets,
        "limitations": [
            (
                "Historical opening-day official injury/status captures are "
                "unavailable. The same raw preseason appearance estimate is "
                "therefore used in all eight target Gameweeks; no final "
                "archived status is read."
            ),
            (
                "The target fixture structure remains the labelled "
                "final-archive proxy from the point reconstruction."
            ),
            (
                "Whole-Gameweek donor rows preserve within-week player "
                "dependence. Cross-week paths use the same fixed independent "
                "permutation engine as the current prospective artifact."
            ),
            (
                "This phase neither loads target points/minutes nor optimises "
                "or scores a squad."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "scenarioModel": artifact["scenarioModel"],
            "appearanceModel": artifact["appearanceModel"],
            "appearancePolicy": artifact["appearancePolicy"],
            "targetGameweeks": artifact["targetGameweeks"],
            "forecastSource": artifact["forecastSource"],
            "temporalDesign": artifact["temporalDesign"],
            "targets": artifact["targets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _reconstruct_target(
    connection: Any,
    fold: Any,
    forecast: Mapping[str, Any],
) -> Dict[str, Any]:
    if (
        str(forecast["targetSeasonCode"])
        != fold.target_capture.season_code
        or int(forecast["targetCaptureId"])
        != fold.target_capture.capture_id
        or int(forecast["playerCount"]) != len(fold.players)
    ):
        raise TemporalRidgeError(
            "scenario.forecast-target-alignment",
            "The reconstructed forecast and opening cohort differ.",
        )
    latest_prior = fold.training_captures[-1]
    point_training = _build_samples(
        connection,
        latest_prior,
        target_name="total-points",
    )
    appearance_training = _build_samples(
        connection,
        latest_prior,
        target_name="appearance",
    )
    if set(point_training) != set(appearance_training):
        raise TemporalRidgeError(
            "scenario.training-origin-alignment",
            "Point and appearance donor origins differ.",
        )
    appearance_rows = [
        sample
        for gameweek in sorted(appearance_training)
        for sample in appearance_training[gameweek]
    ]
    _, histories = _load_historical_gameweeks(connection, latest_prior)
    fixture_proxy = _load_fixture_proxy(
        connection,
        fold.target_capture.capture_id,
    )
    appearance_target = [
        _appearance_target_sample(
            fold.target_capture.season_code,
            player,
            histories.get(player.player_code, {}),
            fixture_proxy,
        )
        for player in fold.players
    ]
    appearance_predictions, appearance_diagnostics = _predict_classifier(
        appearance_rows,
        appearance_target,
        APPEARANCE_MODEL,
    )
    appearance_by_code = {
        prediction.player_id: prediction.predicted
        for prediction in appearance_predictions
    }
    forecast_players = {
        int(player["playerCode"]): player
        for player in forecast["players"]
    }
    player_codes = tuple(sorted(player.player_code for player in fold.players))
    if set(forecast_players) != set(player_codes):
        raise TemporalRidgeError(
            "scenario.forecast-player-alignment",
            "The reconstructed forecast has a different player cohort.",
        )

    joint_by_gameweek = {}
    for gameweek in TARGET_GAMEWEEKS:
        target = [
            Sample(
                season_code=fold.target_capture.season_code,
                gameweek=gameweek,
                player_id=player.player_code,
                position=player.position,
                features={},
                actual=0,
            )
            for player in sorted(
                fold.players,
                key=lambda row: row.player_code,
            )
        ]
        means = [
            Prediction(
                model=str(forecast.get("modelKey", "multi-season-tree")),
                season_code=sample.season_code,
                gameweek=gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=_forecast_mean(
                    forecast_players[sample.player_id],
                    gameweek,
                ),
                actual=0,
            )
            for sample in target
        ]
        appearances = [
            Prediction(
                model=APPEARANCE_MODEL,
                season_code=sample.season_code,
                gameweek=gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=float(appearance_by_code[sample.player_id]),
                actual=0,
            )
            for sample in target
        ]
        joint = generate_joint_fold(
            point_training,
            appearance_training,
            target,
            means,
            appearances,
        )
        if joint.player_ids != player_codes:
            raise TemporalRidgeError(
                "scenario.player-column-alignment",
                "A reconstructed week uses a different player column order.",
            )
        if np.any((joint.points != 0) & ~joint.played):
            raise TemporalRidgeError(
                "scenario.nonplayer-points",
                "A non-playing scenario player has non-zero points.",
            )
        joint_by_gameweek[gameweek] = joint

    scenario_counts = {
        len(joint.source_gameweeks) for joint in joint_by_gameweek.values()
    }
    if len(scenario_counts) != 1:
        raise TemporalRidgeError(
            "scenario.path-count-alignment",
            "Reconstructed weeks have different path counts.",
        )
    scenario_count = scenario_counts.pop()
    weeks = []
    for gameweek in TARGET_GAMEWEEKS:
        joint = joint_by_gameweek[gameweek]
        permutation = _path_permutation(scenario_count, gameweek)
        points = joint.points[permutation]
        played = joint.played[permutation]
        weeks.append(
            {
                "gameweek": gameweek,
                "sourceGameweeksByPath": [
                    int(joint.source_gameweeks[index])
                    for index in permutation
                ],
                "pointRows": points.tolist(),
                "playedRows": played.tolist(),
                "pointMeanMaximumAbsoluteDelta": _round(
                    float(
                        np.max(
                            np.abs(
                                np.mean(joint.points, axis=0)
                                - np.asarray(
                                    joint.mean_predictions,
                                    dtype=float,
                                )
                            )
                        )
                    )
                ),
                "appearanceMaximumAbsoluteDelta": _round(
                    float(
                        np.max(
                            np.abs(
                                np.mean(joint.played, axis=0)
                                - np.asarray(
                                    joint.appearance_predictions,
                                    dtype=float,
                                )
                            )
                        )
                    )
                ),
            }
        )
    players = [
        {
            "columnIndex": index,
            "playerCode": player.player_code,
            "webName": player.web_name,
            "position": player.position,
            "teamName": player.team_name,
            "teamId": player.team_id,
            "priceTenths": player.price_tenths,
            "appearanceProbability": _round(
                appearance_by_code[player.player_code]
            ),
        }
        for index, player in enumerate(
            sorted(fold.players, key=lambda row: row.player_code)
        )
    ]
    return {
        "targetSeasonCode": fold.target_capture.season_code,
        "targetCaptureId": fold.target_capture.capture_id,
        "latestPriorSeasonCode": latest_prior.season_code,
        "latestPriorCaptureId": latest_prior.capture_id,
        "forecastIdentitySha256": forecast["forecastIdentitySha256"],
        "playerCount": len(players),
        "scenarioCount": scenario_count,
        "appearanceTrainingRowCount": len(appearance_rows),
        "appearanceDiagnostics": appearance_diagnostics,
        "players": players,
        "weeks": weeks,
        "scenarioContentSha256": _sha256(
            {
                "players": players,
                "weeks": weeks,
            }
        ),
    }


def _appearance_target_sample(
    season_code: str,
    player: Any,
    history: Mapping[int, HistoricalGameweek],
    fixture_proxy: Mapping[int, Mapping[str, Any]],
) -> Sample:
    gameweek = TARGET_GAMEWEEKS[0]
    week = fixture_proxy[gameweek]
    fixtures = list(week["teams"].get(player.team_name, ()))
    target = HistoricalGameweek(
        gameweek=gameweek,
        fixture_count=len(fixtures),
        home_fixture_count=sum(bool(row["wasHome"]) for row in fixtures),
        minutes=0,
        starts=0,
        total_points=0,
        expected_goals=0.0,
        expected_assists=0.0,
        expected_goal_involvements=0.0,
        expected_goals_conceded=0.0,
        defensive_contribution=None,
    )
    return Sample(
        season_code=season_code,
        gameweek=gameweek,
        player_id=player.player_code,
        position=player.position,
        features=participation_features(
            target,
            [history[key] for key in sorted(history)],
        ),
        actual=0,
    )


def _forecast_mean(
    player: Mapping[str, Any],
    gameweek: int,
) -> float:
    rows = {
        int(row["gameweek"]): float(row["expectedPoints"])
        for row in player["gameweeks"]
    }
    if set(rows) != set(TARGET_GAMEWEEKS):
        raise TemporalRidgeError(
            "scenario.forecast-gameweek-coverage",
            "A reconstructed player forecast is incomplete.",
        )
    return rows[gameweek]


def _require_forecast(forecast: Mapping[str, Any]) -> None:
    if (
        forecast.get("status") != FORECAST_STATUS
        or forecast.get("artifactVersion") != FORECAST_ARTIFACT_VERSION
        or list(forecast.get("targetGameweeks", ()))
        != list(TARGET_GAMEWEEKS)
        or forecast.get("temporalDesign", {}).get(
            "targetPerformanceFieldsRead"
        )
        is not False
    ):
        raise TemporalRidgeError(
            "scenario.forecast-source",
            "The historical forecast reconstruction is unsupported.",
        )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Reconstruct target-outcome-free historical opening joint paths."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_historical_opening_scenario_reconstruction(
            options.database
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
