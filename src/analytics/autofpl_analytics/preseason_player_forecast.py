from __future__ import annotations

import argparse
import json
import math
import sqlite3
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

from .cross_season_player_state import (
    PRIOR_SEASON,
    _load_archive,
    _load_current_players,
    _load_history,
    _load_target,
)
from .historical_preseason_evaluation import (
    FEATURES,
    HistoricalCapture,
    HistoricalGameweek,
    TREE_MODEL,
    _build_samples,
    _features,
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
ARTIFACT_TYPE = "historical-preseason-player-gameweek-forecast"
MODEL_KEY = "historical-preseason-histogram-tree-v1"
STATUS = "provisional-preseason-challenger"
CURRENT_SEASON = "2026-27"
CURRENT_GAMEWEEK = 1
BASELINE_MODEL_KEY = "official-market-baseline-v0-player-table"
EVALUATION_DATA_IDENTITY = (
    "628bd4aae195dc26cfaaba1d692d2e91cb486a0c8f8ad8f737eb7ceb42a52503"
)
EVALUATION_RUN_IDENTITY = (
    "5ed600cf5ad5ebf830d814d615a1bd6648ba74db25d8939831bdb40116f2ca2b"
)
EXPECTED_SOURCE_REVISION = (
    "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a"
)
EXPECTED_PLAYERS_SHA256 = (
    "412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b"
)
EXPECTED_GAMEWEEKS_SHA256 = (
    "0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d"
)


def build_preseason_player_forecast(
    database_path: Path,
    season_code: str = CURRENT_SEASON,
    gameweek: int = CURRENT_GAMEWEEK,
) -> Dict[str, Any]:
    path = Path(database_path)
    _validate(path, season_code, gameweek)
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
                "The pinned prior-season archive was unavailable by the "
                "current target cutoff.",
            )
        _require_evaluated_archive(archive)
        capture = HistoricalCapture(
            capture_id=int(archive["captureId"]),
            season_code=PRIOR_SEASON,
            source_revision=str(archive["sourceRevision"]),
            available_at_utc=str(archive["availableAtUtc"]),
            players_sha256=str(archive["playersSha256"]),
            gameweeks_sha256=str(archive["gameweeksSha256"]),
            player_count=int(archive["playerCount"]),
            player_gameweek_count=int(archive["playerGameweekCount"]),
            stable_code_count=int(archive["stableCodeCount"]),
        )
        historical_samples = _build_samples(connection, capture)
        training = [
            sample
            for historical_gameweek in sorted(historical_samples)
            for sample in historical_samples[historical_gameweek]
        ]
        histories, historical_players, historical_row_count = _load_history(
            connection,
            capture.capture_id,
        )
        if (
            len(historical_players) != capture.player_count
            or historical_row_count != capture.player_gameweek_count
        ):
            raise TemporalRidgeError(
                "data.incomplete-prior-season-coverage",
                "The evaluated prior-season archive no longer has its "
                "declared coverage.",
            )
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
        fixtures = _load_target_fixtures(
            connection,
            int(target["captureId"]),
            gameweek,
        )
        target_samples = [
            _current_sample(
                season_code,
                gameweek,
                player,
                histories.get(int(player["playerCode"]), {}),
                fixtures.get(int(player["teamId"]), (0, 0)),
            )
            for player in current_players
        ]
        predictions, diagnostics = _predict_tree(
            training,
            target_samples,
            continuous_features=FEATURES,
            model_name=TREE_MODEL,
        )
        predicted_by_id = {
            prediction.player_id: prediction.predicted
            for prediction in predictions
        }
        baseline = _load_baseline(
            connection,
            int(target["captureId"]),
        )
        current_ids = {int(player["playerId"]) for player in current_players}
        if set(baseline) != current_ids:
            raise TemporalRidgeError(
                "data.incomplete-baseline-coverage",
                "Baseline v0 and the official current target do not have "
                "identical player coverage.",
            )
        players = [
            _player_document(
                player,
                histories.get(int(player["playerCode"]), {}),
                predicted_by_id[int(player["playerId"])],
                baseline[int(player["playerId"])],
            )
            for player in current_players
        ]
        matched = sum(
            player["priorSeasonIdentityStatus"] == "stable-code-match"
            for player in players
        )
        artifact: Dict[str, Any] = {
            "schemaVersion": SCHEMA_VERSION,
            "artifactType": ARTIFACT_TYPE,
            "status": STATUS,
            "modelKey": MODEL_KEY,
            "isPromoted": False,
            "influencesAdvice": False,
            "seasonCode": season_code,
            "gameweek": gameweek,
            "deadlineUtc": target["deadlineUtc"],
            "decisionCutoffUtc": target["availableAtUtc"],
            "officialCaptureId": target["captureId"],
            "training": {
                "seasonCode": PRIOR_SEASON,
                "historicalCaptureId": capture.capture_id,
                "trainingGameweekCount": len(historical_samples),
                "trainingRowCount": len(training),
                "sourceRevision": capture.source_revision,
                "playersSha256": capture.players_sha256,
                "gameweeksSha256": capture.gameweeks_sha256,
                "selectedModel": TREE_MODEL,
                "modelConfiguration": dict(TREE_CONFIGURATION),
                "modelDiagnostics": diagnostics,
                "evaluationDataIdentitySha256": EVALUATION_DATA_IDENTITY,
                "evaluationRunIdentitySha256": EVALUATION_RUN_IDENTITY,
            },
            "comparison": {
                "baselineModelKey": BASELINE_MODEL_KEY,
                "selectionMetric": "locked-holdout-mae",
                "lockedHoldoutMaeImprovementFraction": 0.088702,
                "baselineStillDrivesAdvice": True,
            },
            "distributionStatus": "point-mean-only-no-calibrated-distribution",
            "playerCount": len(players),
            "officialPlayerCount": len(official_players),
            "ineligiblePlayerCount": len(official_players) - len(players),
            "priorSeasonIdentityMatchCount": matched,
            "priorSeasonIdentityMissingCount": len(players) - matched,
            "players": players,
            "limitations": [
                "This is a within-season-validated preseason bridge, not a "
                "current-season promoted model.",
                "The fitted point mean has no calibrated interval, start "
                "probability or expected-minutes distribution.",
                "Current official availability is shown but does not adjust "
                "the raw fitted point mean; Baseline v0 remains authoritative "
                "for served advice.",
                "Transfers, promoted clubs and tactical changes can create "
                "cross-season concept drift.",
            ],
        }
        artifact["dataIdentitySha256"] = _sha256(
            {
                "officialCaptureId": artifact["officialCaptureId"],
                "decisionCutoffUtc": artifact["decisionCutoffUtc"],
                "training": artifact["training"],
                "baselineModelKey": BASELINE_MODEL_KEY,
            }
        )
        artifact["runIdentitySha256"] = _sha256(artifact)
        return artifact
    finally:
        connection.close()


def _validate(path: Path, season_code: str, gameweek: int) -> None:
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    if season_code != CURRENT_SEASON or gameweek != CURRENT_GAMEWEEK:
        raise TemporalRidgeError(
            "configuration.target",
            "This fixed bridge supports only 2026-27 Gameweek 1.",
        )


def _require_evaluated_archive(archive: Mapping[str, Any]) -> None:
    expected = {
        "sourceRevision": EXPECTED_SOURCE_REVISION,
        "playersSha256": EXPECTED_PLAYERS_SHA256,
        "gameweeksSha256": EXPECTED_GAMEWEEKS_SHA256,
    }
    actual = {key: str(archive[key]) for key in expected}
    if actual != expected:
        raise TemporalRidgeError(
            "data.archive-not-evaluated",
            "The available prior-season archive is not the exact archive "
            "that passed the locked preseason holdout.",
        )


def _load_target_fixtures(
    connection: sqlite3.Connection,
    capture_id: int,
    gameweek: int,
) -> Dict[int, tuple[int, int]]:
    rows = connection.execute(
        """
        SELECT home_team_id, away_team_id
        FROM official_fpl_fixtures
        WHERE capture_id = :capture_id
          AND event_id = :gameweek
        ORDER BY fixture_id;
        """,
        {"capture_id": capture_id, "gameweek": gameweek},
    ).fetchall()
    fixtures: Dict[int, tuple[int, int]] = {}
    for row in rows:
        home_id = int(row["home_team_id"])
        away_id = int(row["away_team_id"])
        home_total, home_count = fixtures.get(home_id, (0, 0))
        away_total, away_count = fixtures.get(away_id, (0, 0))
        fixtures[home_id] = (home_total + 1, home_count + 1)
        fixtures[away_id] = (away_total + 1, away_count)
    if not rows:
        raise TemporalRidgeError(
            "data.target-fixtures-not-found",
            "The current target capture has no fixtures for the target "
            "Gameweek.",
        )
    return fixtures


def _current_sample(
    season_code: str,
    gameweek: int,
    player: Mapping[str, Any],
    history: Mapping[int, Mapping[str, float]],
    fixture_counts: tuple[int, int],
) -> Sample:
    fixture_count, home_fixture_count = fixture_counts
    target = HistoricalGameweek(
        gameweek=gameweek,
        fixture_count=fixture_count,
        home_fixture_count=home_fixture_count,
        minutes=0,
        starts=0,
        total_points=0,
        expected_goals=0.0,
        expected_assists=0.0,
        expected_goal_involvements=0.0,
        expected_goals_conceded=0.0,
        defensive_contribution=0,
    )
    prior = [
        HistoricalGameweek(
            gameweek=historical_gameweek,
            fixture_count=0,
            home_fixture_count=0,
            minutes=int(values["minutes"]),
            starts=int(values["starts"]),
            total_points=int(values["totalPoints"]),
            expected_goals=float(values["expectedGoals"]),
            expected_assists=float(values["expectedAssists"]),
            expected_goal_involvements=float(
                values["expectedGoalInvolvements"]
            ),
            expected_goals_conceded=float(
                values["expectedGoalsConceded"]
            ),
            defensive_contribution=int(
                values["defensiveContribution"]
            ),
        )
        for historical_gameweek, values in sorted(history.items())
    ]
    return Sample(
        season_code=season_code,
        gameweek=gameweek,
        player_id=int(player["playerId"]),
        position=str(player["position"]),
        features=_features(target, prior),
        actual=0,
    )


def _load_baseline(
    connection: sqlite3.Connection,
    official_capture_id: int,
) -> Dict[int, float]:
    row = connection.execute(
        """
        SELECT document_json
        FROM player_gameweek_forecast_artifacts
        WHERE official_capture_id = :capture_id
          AND model_key = :model_key
        LIMIT 1;
        """,
        {
            "capture_id": official_capture_id,
            "model_key": BASELINE_MODEL_KEY,
        },
    ).fetchone()
    if row is None:
        raise TemporalRidgeError(
            "data.baseline-artifact-not-found",
            "The exact current capture has no Baseline v0 player artifact.",
        )
    try:
        document = json.loads(str(row["document_json"]))
        players = document["players"]
        return {
            int(player["playerId"]): _finite(player["expectedPoints"])
            for player in players
        }
    except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exception:
        raise TemporalRidgeError(
            "data.baseline-artifact-invalid",
            "The Baseline v0 player artifact is invalid.",
        ) from exception


def _player_document(
    player: Mapping[str, Any],
    history: Mapping[int, Mapping[str, float]],
    expected_points: float,
    baseline_expected_points: float,
) -> Dict[str, Any]:
    fitted = _round(expected_points)
    baseline = _round(baseline_expected_points)
    return {
        "playerId": int(player["playerId"]),
        "playerCode": int(player["playerCode"]),
        "webName": str(player["webName"]),
        "position": str(player["position"]),
        "teamId": int(player["teamId"]),
        "teamName": str(player["teamName"]),
        "officialStatus": str(player["status"]),
        "officialChanceOfPlayingNextRound": player["chanceNextRound"],
        "availabilityStatus": "authoritative-current-official-not-modelled",
        "priorSeasonIdentityStatus": (
            "stable-code-match" if history else "no-prior-season-match"
        ),
        "priorSeasonGameweekCount": len(history),
        "expectedPoints": fitted,
        "baselineV0ExpectedPoints": baseline,
        "differenceFromBaselineV0": _round(fitted - baseline),
    }


def _finite(value: Any) -> float:
    number = float(value)
    if not math.isfinite(number):
        raise ValueError("Expected a finite number.")
    return number


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Fit the holdout-supported historical point challenger and emit "
            "a provisional current GW1 player forecast artifact."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=CURRENT_SEASON)
    parser.add_argument("--gameweek", type=int, default=CURRENT_GAMEWEEK)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_preseason_player_forecast(
            options.database,
            options.season,
            options.gameweek,
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
