from __future__ import annotations

import argparse
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence

from .cross_season_player_state import _load_current_players, _load_target
from .multi_season_evaluation import (
    DEFAULT_SEASONS,
    FEATURES,
    TREE_MODEL,
    Observation,
    _build_feature_table,
    _load_observations,
    _open_connection,
)
from .multi_season_player_forecast import (
    CURRENT_GAMEWEEK,
    CURRENT_SEASON,
    EVALUATION_RUN_IDENTITY,
    MODEL_KEY,
    _capture_document,
    _current_sample,
    _load_evaluated_captures,
    _load_target_fixtures,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)
from .temporal_tree import TREE_CONFIGURATION, _predict_tree

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-multi-horizon-player-point-shadow"
ARTIFACT_VERSION = "current-multi-horizon-player-point-shadow-v1"
STATUS = "prospective-shadow-unscored"
TARGET_GAMEWEEKS = tuple(range(1, 9))
DECISION_HORIZONS = (3, 6, 8)


def build_current_multi_horizon_player_forecast(
    database_path: Path,
    season_code: str = CURRENT_SEASON,
) -> Dict[str, Any]:
    path = Path(database_path)
    _validate(path, season_code)
    connection = _open_connection(path)
    try:
        target = _load_target(connection, season_code, CURRENT_GAMEWEEK)
        if target is None:
            raise TemporalRidgeError(
                "data.current-target-not-found",
                "No cutoff-eligible official current target capture exists.",
            )
        captures = _load_evaluated_captures(connection, target)
        samples_by_origin = _build_feature_table(connection, captures)
        training = [
            sample
            for origin in sorted(samples_by_origin)
            for sample in samples_by_origin[origin]
        ]
        histories: DefaultDict[int, list[Observation]] = defaultdict(list)
        for season_index, capture in enumerate(captures):
            for observation in _load_observations(
                connection,
                capture,
                season_index,
            ):
                histories[observation.player_code].append(observation)

        official_players = _load_current_players(
            connection,
            int(target["captureId"]),
        )
        if len(official_players) != int(target["playerCount"]):
            raise TemporalRidgeError(
                "data.incomplete-current-player-coverage",
                "The current official capture does not have its declared "
                "player coverage.",
            )
        current_players = [
            player
            for player in official_players
            if str(player["status"]) != "u"
        ]
        team_ids = {int(player["teamId"]) for player in official_players}
        fixture_schedule = []
        target_samples = []
        for gameweek in TARGET_GAMEWEEKS:
            fixtures = _load_target_fixtures(
                connection,
                int(target["captureId"]),
                gameweek,
            )
            if set(fixtures) != team_ids:
                raise TemporalRidgeError(
                    "data.incomplete-target-fixture-coverage",
                    f"Gameweek {gameweek} does not cover every current "
                    "official team.",
                )
            fixture_schedule.append(_fixture_document(gameweek, fixtures))
            target_samples.extend(
                _current_sample(
                    season_code,
                    gameweek,
                    player,
                    histories.get(int(player["playerCode"]), []),
                    fixtures.get(int(player["teamId"])),
                )
                for player in current_players
            )

        predictions, diagnostics = _predict_tree(
            training,
            target_samples,
            continuous_features=FEATURES,
            model_name=TREE_MODEL,
        )
        predicted_by_origin = {
            (prediction.gameweek, prediction.player_id): prediction.predicted
            for prediction in predictions
        }
        expected_prediction_count = (
            len(current_players) * len(TARGET_GAMEWEEKS)
        )
        if len(predicted_by_origin) != expected_prediction_count:
            raise TemporalRidgeError(
                "data.incomplete-multi-horizon-predictions",
                "The fitted tree did not return one prediction for every "
                "eligible player and target Gameweek.",
            )

        players = [
            _player_document(
                player,
                histories.get(int(player["playerCode"]), []),
                predicted_by_origin,
            )
            for player in current_players
        ]
        identity_counts: DefaultDict[str, int] = defaultdict(int)
        for player in players:
            identity_counts[player["historicalIdentityStatus"]] += 1

        artifact: Dict[str, Any] = {
            "schemaVersion": SCHEMA_VERSION,
            "artifactType": ARTIFACT_TYPE,
            "artifactVersion": ARTIFACT_VERSION,
            "status": STATUS,
            "modelKey": MODEL_KEY,
            "isPromoted": False,
            "influencesAdvice": False,
            "seasonCode": season_code,
            "openingGameweek": CURRENT_GAMEWEEK,
            "targetGameweeks": list(TARGET_GAMEWEEKS),
            "decisionHorizons": list(DECISION_HORIZONS),
            "deadlineUtc": target["deadlineUtc"],
            "decisionCutoffUtc": target["availableAtUtc"],
            "officialCaptureId": target["captureId"],
            "fixtureSchedule": fixture_schedule,
            "training": {
                "seasonCodes": list(DEFAULT_SEASONS),
                "historicalCaptures": [
                    _capture_document(capture) for capture in captures
                ],
                "trainingOriginCount": len(samples_by_origin),
                "trainingRowCount": len(training),
                "selectedModel": TREE_MODEL,
                "modelConfiguration": dict(TREE_CONFIGURATION),
                "modelDiagnostics": diagnostics,
                "evaluationRunIdentitySha256": EVALUATION_RUN_IDENTITY,
            },
            "distributionStatus": (
                "per-gameweek-point-means-only-scenarios-not-yet-generated"
            ),
            "availabilityPolicy": (
                "raw-preseason-means-official-gw1-status-not-propagated"
            ),
            "playerCount": len(players),
            "officialPlayerCount": len(official_players),
            "ineligiblePlayerCount": len(official_players) - len(players),
            "historicalIdentityCounts": dict(sorted(identity_counts.items())),
            "players": players,
            "limitations": [
                (
                    "This prospective shadow is unscored and cannot influence "
                    "served advice or mutate an owner selection."
                ),
                (
                    "The retained fitted point mean was selected "
                    "retrospectively; this artifact extends its frozen "
                    "features and parameters without retuning."
                ),
                (
                    "Per-Gameweek means are not a joint multi-Gameweek "
                    "distribution. Correlated scenario matrices remain the "
                    "next required optimiser input."
                ),
                (
                    "Current official Gameweek 1 availability is reported but "
                    "is not assumed to persist for three, six or eight weeks."
                ),
                (
                    "Transfers, future price changes, future news and "
                    "rescheduled fixtures are outside this cutoff snapshot."
                ),
            ],
        }
        artifact["dataIdentitySha256"] = _sha256(
            {
                "officialCaptureId": artifact["officialCaptureId"],
                "decisionCutoffUtc": artifact["decisionCutoffUtc"],
                "fixtureSchedule": artifact["fixtureSchedule"],
                "training": artifact["training"],
                "targetGameweeks": artifact["targetGameweeks"],
                "decisionHorizons": artifact["decisionHorizons"],
            }
        )
        artifact["runIdentitySha256"] = _sha256(artifact)
        return artifact
    finally:
        connection.close()


def _fixture_document(
    gameweek: int,
    fixtures: Mapping[int, Mapping[str, Any]],
) -> Dict[str, Any]:
    kickoffs = [
        str(kickoff)
        for fixture in fixtures.values()
        for kickoff in fixture["kickoffs"]
    ]
    fixture_slots = sum(
        int(fixture["fixtureCount"]) for fixture in fixtures.values()
    )
    if not kickoffs or fixture_slots % 2:
        raise TemporalRidgeError(
            "data.invalid-target-fixture-coverage",
            f"Gameweek {gameweek} has an invalid fixture schedule.",
        )
    return {
        "gameweek": gameweek,
        "fixtureCount": fixture_slots // 2,
        "teamCount": len(fixtures),
        "earliestKickoffUtc": min(kickoffs),
        "latestKickoffUtc": max(kickoffs),
    }


def _player_document(
    player: Mapping[str, Any],
    history: Sequence[Observation],
    predicted_by_origin: Mapping[tuple[int, int], float],
) -> Dict[str, Any]:
    player_id = int(player["playerId"])
    gameweeks = [
        {
            "gameweek": gameweek,
            "expectedPoints": _round(
                predicted_by_origin[(gameweek, player_id)]
            ),
        }
        for gameweek in TARGET_GAMEWEEKS
    ]
    horizons = [
        {
            "gameweekCount": horizon,
            "throughGameweek": horizon,
            "expectedPoints": _round(
                sum(
                    row["expectedPoints"]
                    for row in gameweeks
                    if row["gameweek"] <= horizon
                )
            ),
        }
        for horizon in DECISION_HORIZONS
    ]
    season_codes = sorted({row.season_code for row in history})
    if season_codes == list(DEFAULT_SEASONS):
        identity_status = "both-historical-seasons"
    elif season_codes == ["2025-26"]:
        identity_status = "latest-historical-season-only"
    elif season_codes == ["2024-25"]:
        identity_status = "older-historical-season-only"
    else:
        identity_status = "no-historical-season-match"
    return {
        "playerId": player_id,
        "playerCode": int(player["playerCode"]),
        "webName": str(player["webName"]),
        "position": str(player["position"]),
        "teamId": int(player["teamId"]),
        "teamName": str(player["teamName"]),
        "officialStatus": str(player["status"]),
        "officialChanceOfPlayingNextRound": player["chanceNextRound"],
        "availabilityStatus": (
            "authoritative-gw1-context-not-propagated-across-horizon"
        ),
        "historicalIdentityStatus": identity_status,
        "historicalSeasonCodes": season_codes,
        "historicalGameweekCount": len(history),
        "gameweeks": gameweeks,
        "horizons": horizons,
    }


def _validate(path: Path, season_code: str) -> None:
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    if season_code != CURRENT_SEASON:
        raise TemporalRidgeError(
            "configuration.target",
            "This fixed shadow supports only the 2026-27 opening decision.",
        )
    if TARGET_GAMEWEEKS != tuple(range(1, 9)) or DECISION_HORIZONS != (3, 6, 8):
        raise TemporalRidgeError(
            "configuration.horizons",
            "The registered target Gameweeks or decision horizons changed.",
        )


def _finite(value: Any) -> float:
    number = float(value)
    if not math.isfinite(number):
        raise TemporalRidgeError(
            "data.non-finite-forecast",
            "A generated point forecast is not finite.",
        )
    return number


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Fit the frozen two-season tree once and emit read-only current "
            "Gameweek 1-8 player point means for the registered 3/6/8 "
            "opening-squad horizons."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=CURRENT_SEASON)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_multi_horizon_player_forecast(
            options.database,
            options.season,
        )
        for player in artifact["players"]:
            for gameweek in player["gameweeks"]:
                _finite(gameweek["expectedPoints"])
            for horizon in player["horizons"]:
                _finite(horizon["expectedPoints"])
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
