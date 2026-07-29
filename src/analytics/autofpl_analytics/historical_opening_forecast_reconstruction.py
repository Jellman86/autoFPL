from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence

from .historical_opening_policy_data import (
    OUTCOME_GAMEWEEKS,
    REGISTERED_SEASONS,
    OpeningFold,
    OpeningPlayer,
    _load_opening_fold,
    _required_capture,
)
from .multi_season_evaluation import (
    FEATURES,
    TREE_MODEL,
    Observation,
    _build_feature_table,
    _features,
    _load_observations,
)
from .temporal_ridge import (
    Sample,
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)
from .temporal_tree import TREE_CONFIGURATION, _predict_tree

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-opening-player-forecast-reconstruction"
ARTIFACT_VERSION = "historical-opening-player-forecast-reconstruction-v1"
STATUS = "retrospective-input-reconstruction-no-outcomes-opened"
TARGET_GAMEWEEKS = OUTCOME_GAMEWEEKS
DECISION_HORIZONS = (3, 6, 8)
FIXTURE_PROXY = (
    "target-archive-realised-gameweek-fixture-structure-no-result-fields"
)


def build_historical_opening_forecast_reconstruction(
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
        targets = [
            _reconstruct_target(
                connection,
                _load_opening_fold(
                    connection,
                    captures,
                    target_index,
                    include_outcomes=False,
                ),
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
        "modelKey": TREE_MODEL,
        "modelConfiguration": dict(TREE_CONFIGURATION),
        "targetGameweeks": list(TARGET_GAMEWEEKS),
        "decisionHorizons": list(DECISION_HORIZONS),
        "temporalDesign": {
            "method": "expanding-season-origin",
            "trainingRule": "strictly-earlier-season-archives-only",
            "targetPerformanceFieldsRead": False,
            "targetOutcomeFieldsRead": [],
            "targetConstraintFields": [
                "gameweek-1-element",
                "gameweek-1-position",
                "gameweek-1-team",
                "gameweek-1-value",
            ],
            "fixtureInput": FIXTURE_PROXY,
        },
        "targets": targets,
        "limitations": [
            (
                "Historical fixture structure comes from the final pinned "
                "archive because an immutable opening-day fixture capture is "
                "not available. Result, point, minute and event fields from "
                "the target season are not selected."
            ),
            (
                "A postponement or rearrangement learned after the original "
                "opening deadline can therefore affect this fixed fixture "
                "proxy. This must remain explicit in policy interpretation."
            ),
            (
                "This artifact reconstructs point means only. It neither "
                "generates scenarios nor reads target outcomes."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "modelKey": artifact["modelKey"],
            "modelConfiguration": artifact["modelConfiguration"],
            "targetGameweeks": artifact["targetGameweeks"],
            "temporalDesign": artifact["temporalDesign"],
            "targets": artifact["targets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _reconstruct_target(
    connection: Any,
    fold: OpeningFold,
) -> Dict[str, Any]:
    training_samples = _build_feature_table(
        connection,
        fold.training_captures,
    )
    training = [
        sample
        for origin in sorted(training_samples)
        for sample in training_samples[origin]
    ]
    if not training:
        raise TemporalRidgeError(
            "forecast.empty-training",
            f"{fold.target_capture.season_code} has no earlier training rows.",
        )
    histories: DefaultDict[int, list[Observation]] = defaultdict(list)
    for season_index, capture in enumerate(fold.training_captures):
        for observation in _load_observations(
            connection,
            capture,
            season_index,
        ):
            histories[observation.player_code].append(observation)
    fixtures = _load_fixture_proxy(
        connection,
        fold.target_capture.capture_id,
    )
    means_by_player: DefaultDict[int, Dict[int, float]] = defaultdict(dict)
    diagnostics = []
    for gameweek in TARGET_GAMEWEEKS:
        target = [
            _target_sample(
                fold.target_capture.season_code,
                len(fold.training_captures),
                gameweek,
                player,
                histories.get(player.player_code, ()),
                fixtures,
            )
            for player in fold.players
        ]
        predictions, model_diagnostics = _predict_tree(
            training,
            target,
            continuous_features=FEATURES,
            model_name=TREE_MODEL,
        )
        for prediction in predictions:
            means_by_player[prediction.player_id][gameweek] = _round(
                prediction.predicted
            )
        diagnostics.append(
            {
                "gameweek": gameweek,
                **model_diagnostics,
            }
        )
    players = [
        {
            "playerCode": player.player_code,
            "webName": player.web_name,
            "position": player.position,
            "teamName": player.team_name,
            "teamId": player.team_id,
            "priceTenths": player.price_tenths,
            "priorSeasonCount": len(
                {
                    row.season_code
                    for row in histories.get(player.player_code, ())
                }
            ),
            "priorGameweekCount": len(
                histories.get(player.player_code, ())
            ),
            "gameweeks": [
                {
                    "gameweek": gameweek,
                    "expectedPoints": means_by_player[player.player_code][
                        gameweek
                    ],
                }
                for gameweek in TARGET_GAMEWEEKS
            ],
            "horizonExpectedPoints": {
                str(horizon): _round(
                    sum(
                        means_by_player[player.player_code][gameweek]
                        for gameweek in TARGET_GAMEWEEKS[:horizon]
                    )
                )
                for horizon in DECISION_HORIZONS
            },
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
        "trainingOriginCount": len(training_samples),
        "trainingRowCount": len(training),
        "playerCount": len(players),
        "fixtureProxy": {
            "source": FIXTURE_PROXY,
            "gameweekTeamCounts": {
                str(gameweek): len(fixtures[gameweek]["teams"])
                for gameweek in TARGET_GAMEWEEKS
            },
            "gameweekFixtureCounts": {
                str(gameweek): len(fixtures[gameweek]["fixtureIds"])
                for gameweek in TARGET_GAMEWEEKS
            },
        },
        "modelDiagnostics": diagnostics,
        "players": players,
        "forecastIdentitySha256": _sha256(players),
    }


def _load_fixture_proxy(
    connection: Any,
    capture_id: int,
) -> Dict[int, Dict[str, Any]]:
    rows = connection.execute(
        """
        SELECT DISTINCT
            gameweek,
            team_name,
            fixture_id,
            kickoff_utc,
            was_home
        FROM historical_fpl_player_gameweeks
        WHERE capture_id = :capture_id
          AND gameweek BETWEEN :first_gameweek AND :last_gameweek
        ORDER BY gameweek, team_name, kickoff_utc, fixture_id;
        """,
        {
            "capture_id": capture_id,
            "first_gameweek": TARGET_GAMEWEEKS[0],
            "last_gameweek": TARGET_GAMEWEEKS[-1],
        },
    ).fetchall()
    by_gameweek: Dict[int, Dict[str, Any]] = {
        gameweek: {
            "teams": defaultdict(list),
            "fixtureIds": set(),
            "allKickoffs": [],
        }
        for gameweek in TARGET_GAMEWEEKS
    }
    for row in rows:
        gameweek = int(row["gameweek"])
        fixture = {
            "fixtureId": int(row["fixture_id"]),
            "kickoffUtc": str(row["kickoff_utc"]),
            "wasHome": bool(row["was_home"]),
        }
        by_gameweek[gameweek]["teams"][str(row["team_name"])].append(
            fixture
        )
        by_gameweek[gameweek]["fixtureIds"].add(fixture["fixtureId"])
        by_gameweek[gameweek]["allKickoffs"].append(fixture["kickoffUtc"])
    for gameweek, values in by_gameweek.items():
        if not values["allKickoffs"]:
            raise TemporalRidgeError(
                "forecast.fixture-proxy-empty",
                f"Gameweek {gameweek} has no historical fixture proxy.",
            )
    return by_gameweek


def _target_sample(
    season_code: str,
    season_index: int,
    gameweek: int,
    player: OpeningPlayer,
    history: Sequence[Observation],
    fixture_proxy: Mapping[int, Mapping[str, Any]],
) -> Sample:
    week = fixture_proxy[gameweek]
    fixtures = list(week["teams"].get(player.team_name, ()))
    kickoffs = [str(row["kickoffUtc"]) for row in fixtures]
    if not kickoffs:
        representative = min(str(value) for value in week["allKickoffs"])
        kickoffs = [representative]
    target = Observation(
        season_code=season_code,
        season_index=season_index,
        gameweek=gameweek,
        player_code=player.player_code,
        position=player.position,
        earliest_kickoff_utc=min(kickoffs),
        latest_kickoff_utc=max(kickoffs),
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
        features=_features(target, history),
        actual=0,
    )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Reconstruct strictly expanding-season GW1-8 opening point "
            "forecasts without reading target performance outcomes."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_historical_opening_forecast_reconstruction(
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
