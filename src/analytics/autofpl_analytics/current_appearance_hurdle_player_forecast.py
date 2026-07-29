from __future__ import annotations

import argparse
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence

from .cross_season_player_state import _load_current_players, _load_target
from .current_multi_horizon_player_forecast import (
    DECISION_HORIZONS,
    TARGET_GAMEWEEKS,
    _fixture_document,
    build_current_multi_horizon_player_forecast,
)
from .historical_appearance_hurdle_points_evaluation import (
    APPEARANCE_MODEL,
    CONDITIONAL_MODEL,
    EVALUATOR_VERSION,
    HURDLE_MODEL,
    _appearance_sample,
    _appeared,
    _observations_by_origin,
)
from .historical_participation_evaluation import _predict_classifier
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
ARTIFACT_TYPE = "current-multi-horizon-appearance-hurdle-point-shadow"
ARTIFACT_VERSION = (
    "current-multi-horizon-appearance-hurdle-point-shadow-v1"
)
STATUS = "prospective-shadow-unscored"
HISTORICAL_EVALUATION_DATA_IDENTITY = (
    "1ef395d722844b1833fb059d606606e0bf1f1d42732377474d671f1a3f17b16e"
)
HISTORICAL_EVALUATION_RUN_IDENTITY = (
    "852b3728150cba9ad3bdb46ba24d66e9ac77262b2cf4dc9aae38f0df5cdc7e71"
)


def build_current_appearance_hurdle_player_forecast(
    database_path: Path,
    season_code: str = CURRENT_SEASON,
) -> Dict[str, Any]:
    path = Path(database_path)
    _validate(path, season_code)
    incumbent = build_current_multi_horizon_player_forecast(
        path,
        season_code,
    )
    connection = _open_connection(path)
    try:
        target = _load_target(connection, season_code, CURRENT_GAMEWEEK)
        if target is None:
            raise TemporalRidgeError(
                "hurdle-current.target-not-found",
                "No cutoff-eligible official current target capture exists.",
            )
        _require(
            int(incumbent["officialCaptureId"])
            == int(target["captureId"])
            and str(incumbent["decisionCutoffUtc"])
            == str(target["availableAtUtc"]),
            "hurdle-current.incumbent-alignment",
            "The incumbent forecast does not share the current cutoff.",
        )
        captures = _load_evaluated_captures(connection, target)
        samples_by_origin = _build_feature_table(connection, captures)
        training = [
            sample
            for origin in sorted(samples_by_origin)
            for sample in samples_by_origin[origin]
        ]
        observations_by_origin = _observations_by_origin(
            connection,
            captures,
        )
        appearance_training = [
            _appearance_sample(
                sample,
                observations_by_origin[origin],
            )
            for origin in sorted(samples_by_origin)
            for sample in samples_by_origin[origin]
        ]
        conditional_training = [
            sample
            for origin in sorted(samples_by_origin)
            for sample in samples_by_origin[origin]
            if _appeared(
                sample,
                observations_by_origin[origin],
            )
        ]
        _require(
            training
            and len(training) == len(appearance_training)
            and conditional_training,
            "hurdle-current.training",
            "The fixed hurdle training cohorts are incomplete.",
        )
        histories: DefaultDict[int, list[Observation]] = defaultdict(
            list
        )
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
        _require(
            len(official_players) == int(target["playerCount"]),
            "hurdle-current.player-coverage",
            "The current official capture is incomplete.",
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
            _require(
                set(fixtures) == team_ids,
                "hurdle-current.fixture-coverage",
                f"Gameweek {gameweek} does not cover every current team.",
            )
            fixture_schedule.append(
                _fixture_document(gameweek, fixtures)
            )
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
    finally:
        connection.close()

    appearance, appearance_diagnostics = _predict_classifier(
        appearance_training,
        target_samples,
        APPEARANCE_MODEL,
        continuous_features=FEATURES,
    )
    conditional, conditional_diagnostics = _predict_tree(
        conditional_training,
        target_samples,
        continuous_features=FEATURES,
        model_name=CONDITIONAL_MODEL,
    )
    _require(
        len(appearance)
        == len(conditional)
        == len(target_samples)
        == len(current_players) * len(TARGET_GAMEWEEKS),
        "hurdle-current.prediction-coverage",
        "The hurdle components do not cover every player and Gameweek.",
    )
    component_by_key = {}
    for sample, probability, conditional_mean in zip(
        target_samples,
        appearance,
        conditional,
    ):
        key = (sample.gameweek, sample.player_id)
        _require(
            key not in component_by_key
            and key
            == (probability.gameweek, probability.player_id)
            == (conditional_mean.gameweek, conditional_mean.player_id),
            "hurdle-current.prediction-alignment",
            "The current hurdle component predictions are not aligned.",
        )
        point_mean = float(probability.predicted) * float(
            conditional_mean.predicted
        )
        _require(
            math.isfinite(point_mean)
            and 0.0 <= float(probability.predicted) <= 1.0,
            "hurdle-current.non-finite",
            "A current hurdle prediction is invalid.",
        )
        component_by_key[key] = {
            "appearanceProbability": _round(probability.predicted),
            "conditionalExpectedPoints": _round(
                conditional_mean.predicted
            ),
            "expectedPoints": _round(point_mean),
        }

    incumbent_by_id = {
        int(player["playerId"]): player
        for player in incumbent["players"]
    }
    _require(
        set(incumbent_by_id)
        == {int(player["playerId"]) for player in current_players},
        "hurdle-current.incumbent-cohort",
        "The current hurdle and incumbent player cohorts differ.",
    )
    players = [
        _player_document(
            player,
            histories.get(int(player["playerCode"]), []),
            component_by_key,
            incumbent_by_id[int(player["playerId"])],
        )
        for player in current_players
    ]
    identity_counts: DefaultDict[str, int] = defaultdict(int)
    for player in players:
        identity_counts[player["historicalIdentityStatus"]] += 1
    deltas = [
        float(row["hurdleExpectedPoints"])
        - float(row["incumbentExpectedPoints"])
        for player in players
        for row in player["gameweeks"]
    ]
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "modelKey": HURDLE_MODEL,
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
        "factorization": (
            "appearance-probability-times-expected-points-"
            "conditional-on-appearance"
        ),
        "training": {
            "seasonCodes": list(DEFAULT_SEASONS),
            "historicalCaptures": [
                _capture_document(capture) for capture in captures
            ],
            "trainingOriginCount": len(samples_by_origin),
            "pointTrainingRowCount": len(training),
            "appearanceTrainingRowCount": len(
                appearance_training
            ),
            "conditionalPointTrainingRowCount": len(
                conditional_training
            ),
            "appearanceModel": APPEARANCE_MODEL,
            "appearanceModelConfiguration": {
                **dict(TREE_CONFIGURATION),
                "loss": "log_loss",
            },
            "appearanceModelDiagnostics": appearance_diagnostics,
            "conditionalPointModel": CONDITIONAL_MODEL,
            "conditionalPointModelConfiguration": dict(
                TREE_CONFIGURATION
            ),
            "conditionalPointModelDiagnostics": (
                conditional_diagnostics
            ),
            "historicalEvaluation": {
                "evaluatorVersion": EVALUATOR_VERSION,
                "dataIdentitySha256": (
                    HISTORICAL_EVALUATION_DATA_IDENTITY
                ),
                "runIdentitySha256": (
                    HISTORICAL_EVALUATION_RUN_IDENTITY
                ),
                "decision": (
                    "retain-appearance-hurdle-prospective-shadow"
                ),
            },
        },
        "incumbentSource": {
            "artifactVersion": incumbent["artifactVersion"],
            "modelKey": incumbent["modelKey"],
            "runIdentitySha256": incumbent["runIdentitySha256"],
        },
        "comparison": {
            "playerGameweekCount": len(deltas),
            "meanExpectedPointDelta": _round(
                sum(deltas) / len(deltas)
            ),
            "minimumExpectedPointDelta": _round(min(deltas)),
            "maximumExpectedPointDelta": _round(max(deltas)),
            "currentOutcomeStatus": "prospective-unscored",
        },
        "distributionStatus": (
            "per-gameweek-hurdle-point-means-only-"
            "scenarios-not-yet-generated"
        ),
        "availabilityPolicy": (
            "raw-hurdle-appearance-probability-"
            "official-gw1-ceiling-not-yet-applied"
        ),
        "playerCount": len(players),
        "officialPlayerCount": len(official_players),
        "ineligiblePlayerCount": len(official_players) - len(players),
        "historicalIdentityCounts": dict(sorted(identity_counts.items())),
        "players": players,
        "limitations": [
            (
                "The hurdle specification passed a reused historical "
                "holdout and is retained only as a prospective shadow."
            ),
            (
                "Current Gameweek 1 official availability has not yet been "
                "applied to these raw appearance probabilities and means."
            ),
            (
                "The current means have not yet been converted into joint "
                "scenario paths or an opening-squad comparison."
            ),
            (
                "Future news, lineup evidence, price changes and fixture "
                "revisions remain outside this exact cutoff."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "decisionCutoffUtc": artifact["decisionCutoffUtc"],
            "fixtureSchedule": artifact["fixtureSchedule"],
            "training": artifact["training"],
            "incumbentSource": artifact["incumbentSource"],
            "targetGameweeks": artifact["targetGameweeks"],
            "decisionHorizons": artifact["decisionHorizons"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _player_document(
    player: Mapping[str, Any],
    history: Sequence[Observation],
    component_by_key: Mapping[tuple[int, int], Mapping[str, Any]],
    incumbent: Mapping[str, Any],
) -> Dict[str, Any]:
    player_id = int(player["playerId"])
    incumbent_weeks = {
        int(row["gameweek"]): float(row["expectedPoints"])
        for row in incumbent["gameweeks"]
    }
    _require(
        set(incumbent_weeks) == set(TARGET_GAMEWEEKS),
        "hurdle-current.incumbent-gameweeks",
        "An incumbent player does not cover every target Gameweek.",
    )
    gameweeks = []
    for gameweek in TARGET_GAMEWEEKS:
        component = component_by_key[(gameweek, player_id)]
        gameweeks.append(
            {
                "gameweek": gameweek,
                "appearanceProbability": component[
                    "appearanceProbability"
                ],
                "conditionalExpectedPoints": component[
                    "conditionalExpectedPoints"
                ],
                "expectedPoints": component["expectedPoints"],
                "hurdleExpectedPoints": component[
                    "expectedPoints"
                ],
                "incumbentExpectedPoints": _round(
                    incumbent_weeks[gameweek]
                ),
                "expectedPointDelta": _round(
                    float(component["expectedPoints"])
                    - incumbent_weeks[gameweek]
                ),
            }
        )
    horizons = [
        {
            "gameweekCount": horizon,
            "throughGameweek": horizon,
            "expectedPoints": _round(
                sum(
                    float(row["expectedPoints"])
                    for row in gameweeks
                    if int(row["gameweek"]) <= horizon
                )
            ),
            "incumbentExpectedPoints": _round(
                sum(
                    float(row["incumbentExpectedPoints"])
                    for row in gameweeks
                    if int(row["gameweek"]) <= horizon
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
            "authoritative-gw1-context-not-yet-applied"
        ),
        "historicalIdentityStatus": identity_status,
        "historicalSeasonCodes": season_codes,
        "historicalGameweekCount": len(history),
        "gameweeks": gameweeks,
        "horizons": horizons,
    }


def _validate(path: Path, season_code: str) -> None:
    _require(
        path.is_file(),
        "database.not-found",
        "The SQLite database does not exist.",
    )
    _require(
        season_code == CURRENT_SEASON,
        "configuration.target",
        "This fixed shadow supports only the 2026-27 opening decision.",
    )
    _require(
        TARGET_GAMEWEEKS == tuple(range(1, 9))
        and DECISION_HORIZONS == (3, 6, 8)
        and TREE_MODEL == "multi-season-histogram-tree",
        "configuration.hurdle-contract",
        "The fixed hurdle target or incumbent contract changed.",
    )


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Fit the retained appearance-hurdle point model to current "
            "Gameweeks 1-8 under a separate prospective shadow identity."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=CURRENT_SEASON)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_appearance_hurdle_player_forecast(
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
