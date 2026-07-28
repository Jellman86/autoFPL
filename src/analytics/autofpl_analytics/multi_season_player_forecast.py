from __future__ import annotations

import argparse
import json
import math
import sqlite3
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence

from .cross_season_player_state import (
    _load_current_players,
    _load_target,
)
from .historical_preseason_evaluation import HistoricalCapture
from .multi_season_evaluation import (
    DEFAULT_SEASONS,
    FEATURES,
    TREE_MODEL,
    Observation,
    _build_feature_table,
    _features,
    _load_observations,
    _open_connection,
)
from .preseason_player_forecast import BASELINE_MODEL_KEY, _load_baseline
from .temporal_ridge import (
    Sample,
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)
from .temporal_tree import TREE_CONFIGURATION, _predict_tree

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "multi-season-preseason-shadow-player-gameweek-forecast"
MODEL_KEY = "multi-season-histogram-tree-v1"
STATUS = "retrospective-screen-shadow-challenger"
CURRENT_SEASON = "2026-27"
CURRENT_GAMEWEEK = 1
EVALUATION_RUN_IDENTITY = (
    "577ff6fc3d4283670bae47deb53acad683111c6ed1155c9715442688f4602cfd"
)
EXPECTED_ARCHIVES = {
    "2024-25": {
        "sourceRevision": (
            "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a"
        ),
        "playersSha256": (
            "75686051b265cbe7755ac71213ecaad21b26ee1cc46a8bafbba19c39ce894b05"
        ),
        "gameweeksSha256": (
            "5bbbcba6353b4c72ad273adcc8e3aa451946a826564679788f45b1cb3325b84e"
        ),
    },
    "2025-26": {
        "sourceRevision": (
            "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a"
        ),
        "playersSha256": (
            "412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b"
        ),
        "gameweeksSha256": (
            "0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d"
        ),
    },
}


def build_multi_season_player_forecast(
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
        captures = _load_evaluated_captures(connection, target)
        historical_samples = _build_feature_table(connection, captures)
        training = [
            sample
            for origin in sorted(historical_samples)
            for sample in historical_samples[origin]
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
        target_fixtures = _load_target_fixtures(
            connection,
            int(target["captureId"]),
            gameweek,
        )
        target_samples = [
            _current_sample(
                season_code,
                gameweek,
                player,
                histories.get(int(player["playerCode"]), []),
                target_fixtures.get(int(player["teamId"])),
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
                histories.get(int(player["playerCode"]), []),
                predicted_by_id[int(player["playerId"])],
                baseline[int(player["playerId"])],
            )
            for player in current_players
        ]
        identity_counts: DefaultDict[str, int] = defaultdict(int)
        for player in players:
            identity_counts[player["historicalIdentityStatus"]] += 1
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
                "seasonCodes": list(DEFAULT_SEASONS),
                "historicalCaptures": [
                    _capture_document(capture) for capture in captures
                ],
                "trainingOriginCount": len(historical_samples),
                "trainingRowCount": len(training),
                "selectedModel": TREE_MODEL,
                "modelConfiguration": dict(TREE_CONFIGURATION),
                "modelDiagnostics": diagnostics,
                "evaluationRunIdentitySha256": EVALUATION_RUN_IDENTITY,
            },
            "comparison": {
                "baselineModelKey": BASELINE_MODEL_KEY,
                "retrospectiveMaeImprovementOverBaselineFraction": 0.091511,
                "matchedCurrentSeasonTreeMaeImprovementFraction": 0.002607,
                "matchedCurrentSeasonTreeFoldWins": 3,
                "matchedCurrentSeasonTreeFoldCount": 8,
                "allPositionMaeNonWorse": False,
                "baselineStillDrivesAdvice": True,
            },
            "distributionStatus": "point-mean-only-no-calibrated-distribution",
            "playerCount": len(players),
            "officialPlayerCount": len(official_players),
            "ineligiblePlayerCount": len(official_players) - len(players),
            "historicalIdentityCounts": dict(sorted(identity_counts.items())),
            "players": players,
            "limitations": [
                "This shadow was selected retrospectively and is not a "
                "promoted current-season model.",
                "The fitted point mean has no calibrated interval, start "
                "probability or expected-minutes distribution.",
                "Current official availability is shown but does not adjust "
                "the raw fitted point mean; Baseline v0 remains authoritative "
                "for served advice.",
                "The older season produced only a small aggregate ablation "
                "gain and did not win a majority of folds.",
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


def _load_evaluated_captures(
    connection: sqlite3.Connection,
    target: Mapping[str, Any],
) -> list[HistoricalCapture]:
    captures: list[HistoricalCapture] = []
    for season_code in DEFAULT_SEASONS:
        row = connection.execute(
            """
            SELECT
                capture_id,
                season_code,
                source_revision,
                available_at_utc,
                players_sha256,
                gameweeks_sha256,
                player_count,
                player_gameweek_count,
                stable_code_count
            FROM historical_fpl_season_captures
            WHERE season_code = :season_code
              AND julianday(available_at_utc) <= julianday(:cutoff)
            ORDER BY julianday(available_at_utc) DESC, capture_id DESC
            LIMIT 1;
            """,
            {
                "season_code": season_code,
                "cutoff": target["availableAtUtc"],
            },
        ).fetchone()
        if row is None:
            raise TemporalRidgeError(
                "data.evaluated-archive-unavailable-at-cutoff",
                f"The evaluated {season_code} archive was not available "
                "by the current official capture cutoff.",
            )
        capture = HistoricalCapture(
            capture_id=int(row["capture_id"]),
            season_code=str(row["season_code"]),
            source_revision=str(row["source_revision"]),
            available_at_utc=str(row["available_at_utc"]),
            players_sha256=str(row["players_sha256"]),
            gameweeks_sha256=str(row["gameweeks_sha256"]),
            player_count=int(row["player_count"]),
            player_gameweek_count=int(row["player_gameweek_count"]),
            stable_code_count=int(row["stable_code_count"]),
        )
        expected = EXPECTED_ARCHIVES[season_code]
        actual = {
            "sourceRevision": capture.source_revision,
            "playersSha256": capture.players_sha256,
            "gameweeksSha256": capture.gameweeks_sha256,
        }
        if actual != expected:
            raise TemporalRidgeError(
                "data.archive-not-evaluated",
                f"The available {season_code} archive is not the exact "
                "capture used by the retained multi-season evaluation.",
            )
        captures.append(capture)
    return captures


def _load_target_fixtures(
    connection: sqlite3.Connection,
    capture_id: int,
    gameweek: int,
) -> Dict[int, Dict[str, Any]]:
    rows = connection.execute(
        """
        SELECT
            fixture_id,
            kickoff_utc,
            home_team_id,
            away_team_id
        FROM official_fpl_fixtures
        WHERE capture_id = :capture_id
          AND event_id = :gameweek
        ORDER BY fixture_id;
        """,
        {"capture_id": capture_id, "gameweek": gameweek},
    ).fetchall()
    if not rows:
        raise TemporalRidgeError(
            "data.target-fixtures-not-found",
            "The current target capture has no fixtures for the target "
            "Gameweek.",
        )
    fixtures: Dict[int, Dict[str, Any]] = {}
    for row in rows:
        kickoff = row["kickoff_utc"]
        if kickoff is None:
            raise TemporalRidgeError(
                "data.target-kickoff-not-found",
                "A current target fixture has no scheduled kickoff.",
            )
        for team_id, is_home in (
            (int(row["home_team_id"]), True),
            (int(row["away_team_id"]), False),
        ):
            fixture = fixtures.setdefault(
                team_id,
                {
                    "kickoffs": [],
                    "fixtureCount": 0,
                    "homeFixtureCount": 0,
                },
            )
            fixture["kickoffs"].append(str(kickoff))
            fixture["fixtureCount"] += 1
            fixture["homeFixtureCount"] += int(is_home)
    return fixtures


def _current_sample(
    season_code: str,
    gameweek: int,
    player: Mapping[str, Any],
    history: Sequence[Observation],
    fixture: Optional[Mapping[str, Any]],
) -> Sample:
    if fixture is None:
        raise TemporalRidgeError(
            "data.incomplete-target-fixture-coverage",
            "A current player team has no target Gameweek fixture.",
        )
    kickoffs = list(fixture["kickoffs"])
    target = Observation(
        season_code=season_code,
        season_index=len(DEFAULT_SEASONS),
        gameweek=gameweek,
        player_code=int(player["playerCode"]),
        position=str(player["position"]),
        earliest_kickoff_utc=min(kickoffs),
        latest_kickoff_utc=max(kickoffs),
        fixture_count=int(fixture["fixtureCount"]),
        home_fixture_count=int(fixture["homeFixtureCount"]),
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
        player_id=int(player["playerId"]),
        position=str(player["position"]),
        features=_features(target, history),
        actual=0,
    )


def _player_document(
    player: Mapping[str, Any],
    history: Sequence[Observation],
    expected_points: float,
    baseline_expected_points: float,
) -> Dict[str, Any]:
    fitted = _round(expected_points)
    baseline = _round(baseline_expected_points)
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
        "playerId": int(player["playerId"]),
        "playerCode": int(player["playerCode"]),
        "webName": str(player["webName"]),
        "position": str(player["position"]),
        "teamId": int(player["teamId"]),
        "teamName": str(player["teamName"]),
        "officialStatus": str(player["status"]),
        "officialChanceOfPlayingNextRound": player["chanceNextRound"],
        "availabilityStatus": "authoritative-current-official-not-modelled",
        "historicalIdentityStatus": identity_status,
        "historicalSeasonCodes": season_codes,
        "historicalGameweekCount": len(history),
        "expectedPoints": fitted,
        "baselineV0ExpectedPoints": baseline,
        "differenceFromBaselineV0": _round(fitted - baseline),
    }


def _capture_document(capture: HistoricalCapture) -> Dict[str, Any]:
    return {
        "captureId": capture.capture_id,
        "seasonCode": capture.season_code,
        "sourceRevision": capture.source_revision,
        "availableAtUtc": capture.available_at_utc,
        "playersSha256": capture.players_sha256,
        "gameweeksSha256": capture.gameweeks_sha256,
        "playerCount": capture.player_count,
        "playerGameweekCount": capture.player_gameweek_count,
        "stableCodeCount": capture.stable_code_count,
    }


def _validate(path: Path, season_code: str, gameweek: int) -> None:
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    if season_code != CURRENT_SEASON or gameweek != CURRENT_GAMEWEEK:
        raise TemporalRidgeError(
            "configuration.target",
            "This fixed shadow supports only 2026-27 Gameweek 1.",
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
            "Fit the retained two-season tree and emit a read-only current "
            "GW1 shadow player forecast."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=CURRENT_SEASON)
    parser.add_argument("--gameweek", type=int, default=CURRENT_GAMEWEEK)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_multi_season_player_forecast(
            options.database,
            options.season,
            options.gameweek,
        )
        for player in artifact["players"]:
            _finite(player["expectedPoints"])
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
