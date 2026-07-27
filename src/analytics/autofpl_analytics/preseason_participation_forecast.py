from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence

from .cross_season_player_state import (
    PRIOR_SEASON,
    _load_archive,
    _load_current_players,
    _load_history,
    _load_target,
)
from .historical_participation_evaluation import (
    BINARY_TARGETS,
    MINUTES_TARGET,
    _candidate_name,
    _predict_classifier,
)
from .historical_preseason_evaluation import (
    HistoricalCapture,
    _build_samples,
)
from .preseason_player_forecast import (
    CURRENT_GAMEWEEK,
    CURRENT_SEASON,
    EXPECTED_GAMEWEEKS_SHA256,
    EXPECTED_PLAYERS_SHA256,
    EXPECTED_SOURCE_REVISION,
    _current_sample,
    _load_target_fixtures,
    _require_evaluated_archive,
    _validate,
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
from .temporal_tree import TREE_CONFIGURATION

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-preseason-participation-player-forecast"
ARTIFACT_VERSION = "preseason-participation-player-forecast-v1.1"
STATUS = "provisional-preseason-participation-challenger"
EVALUATION_DATA_IDENTITY = (
    "a6d3c3c2c9a64c123ef69584c46aa7200fc6f467eab7858b4e88dd0e12cb5113"
)
EVALUATION_RUN_IDENTITY = (
    "c39a19a93b0233e0130fe278af63b0278a4170c937db571ff056861c92451063"
)
MINUTES_BASELINE = "minutes-player-last"
TARGET_OUTPUTS = {
    "appearance": "appearanceProbability",
    "start": "startProbability",
    "played-60": "played60Probability",
}
HOLDOUT_IMPROVEMENTS = {
    "appearance": 0.215706,
    "start": 0.217959,
    "played-60": 0.186653,
}


def build_preseason_participation_forecast(
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
        capture = _capture(archive)
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

        predictions: Dict[str, Dict[int, float]] = {}
        model_documents: Dict[str, Any] = {}
        training_gameweeks: Optional[int] = None
        training_rows: Optional[int] = None
        for target_name in BINARY_TARGETS:
            samples_by_gameweek = _build_samples(
                connection,
                capture,
                target_name=target_name,
            )
            training = _flatten(samples_by_gameweek)
            if training_gameweeks is None:
                training_gameweeks = len(samples_by_gameweek)
                training_rows = len(training)
            elif (
                training_gameweeks != len(samples_by_gameweek)
                or training_rows != len(training)
            ):
                raise TemporalRidgeError(
                    "data.inconsistent-participation-training-cohort",
                    "Supported participation targets do not share one "
                    "historical training cohort.",
                )
            target_predictions, diagnostics = _predict_classifier(
                training,
                target_samples,
                _candidate_name(target_name),
            )
            predictions[target_name] = _by_player(target_predictions)
            model_documents[target_name] = {
                "modelKey": _candidate_name(target_name),
                "configuration": {
                    **TREE_CONFIGURATION,
                    "loss": "log_loss",
                    "implementation": (
                        "sklearn.ensemble.HistGradientBoostingClassifier"
                    ),
                },
                "diagnostics": diagnostics,
                "lockedHoldoutBrierImprovementFraction": (
                    HOLDOUT_IMPROVEMENTS[target_name]
                ),
                "evaluationDataIdentitySha256": (
                    EVALUATION_DATA_IDENTITY
                ),
                "evaluationRunIdentitySha256": EVALUATION_RUN_IDENTITY,
            }

        minutes_by_gameweek = _build_samples(
            connection,
            capture,
            target_name=MINUTES_TARGET,
        )
        minutes_training = _flatten(minutes_by_gameweek)
        if (
            training_gameweeks != len(minutes_by_gameweek)
            or training_rows != len(minutes_training)
        ):
            raise TemporalRidgeError(
                "data.inconsistent-minutes-training-cohort",
                "The minutes baseline does not share the supported target "
                "training cohort.",
            )
        minutes_fallback = _position_minutes_means(minutes_training)
        players = [
            _player_document(
                player,
                histories.get(int(player["playerCode"]), {}),
                predictions,
                minutes_fallback,
            )
            for player in current_players
        ]
        matched = sum(
            player["priorSeasonIdentityStatus"] == "stable-code-match"
            for player in players
        )
        coherent = sum(
            player["probabilityCoherenceStatus"] == "coherent"
            for player in players
        )
        import_blockers = [
            "current-official-availability-not-fused",
        ]
        if coherent != len(players):
            import_blockers.append(
                "independent-probabilities-violate-event-nesting"
            )
        if matched != len(players):
            import_blockers.append(
                "missing-prior-identity-requires-evaluated-fallback"
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
            "deadlineUtc": target["deadlineUtc"],
            "decisionCutoffUtc": target["availableAtUtc"],
            "officialCaptureId": target["captureId"],
            "training": {
                "seasonCode": PRIOR_SEASON,
                "historicalCaptureId": capture.capture_id,
                "trainingGameweekCount": training_gameweeks,
                "trainingRowCount": training_rows,
                "sourceRevision": capture.source_revision,
                "playersSha256": capture.players_sha256,
                "gameweeksSha256": capture.gameweeks_sha256,
                "models": model_documents,
                "minutesBaseline": {
                    "modelKey": MINUTES_BASELINE,
                    "status": (
                        "retained-after-histogram-tree-failed-locked-holdout"
                    ),
                    "lockedHoldoutTreeMaeImprovementFraction": -0.096902,
                    "evaluationDataIdentitySha256": (
                        EVALUATION_DATA_IDENTITY
                    ),
                    "evaluationRunIdentitySha256": (
                        EVALUATION_RUN_IDENTITY
                    ),
                },
            },
            "probabilityStatus": (
                "within-season-supported-provisional-preseason-bridge"
            ),
            "minutesStatus": "transparent-baseline-not-challenger",
            "playerCount": len(players),
            "officialPlayerCount": len(official_players),
            "ineligiblePlayerCount": len(official_players) - len(players),
            "priorSeasonIdentityMatchCount": matched,
            "priorSeasonIdentityMissingCount": len(players) - matched,
            "probabilityCoherentPlayerCount": coherent,
            "probabilityIncoherentPlayerCount": len(players) - coherent,
            "productImportReadiness": {
                "status": "blocked",
                "isReady": False,
                "blockers": import_blockers,
            },
            "players": players,
            "limitations": [
                "The three probabilities passed a within-season locked "
                "holdout but are not current-season promoted models.",
                "The independently evaluated classifiers are not silently "
                "projected onto a nested probability space; any current "
                "appearance/start or appearance/60-minute inconsistency is "
                "reported per player.",
                "The historical archive has no decision-time injury state; "
                "current official availability is authoritative and remains "
                "separate from the raw probabilities.",
                "Expected minutes uses the retained player-last baseline, "
                "with a full-archive position mean only when stable prior "
                "identity is missing.",
                "Transfers, promoted clubs and tactical changes can create "
                "cross-season concept drift.",
            ],
        }
        artifact["dataIdentitySha256"] = _sha256(
            {
                "officialCaptureId": artifact["officialCaptureId"],
                "decisionCutoffUtc": artifact["decisionCutoffUtc"],
                "training": artifact["training"],
            }
        )
        artifact["runIdentitySha256"] = _sha256(artifact)
        return artifact
    finally:
        connection.close()


def _capture(archive: Mapping[str, Any]) -> HistoricalCapture:
    return HistoricalCapture(
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


def _flatten(
    samples_by_gameweek: Mapping[int, Sequence[Sample]],
) -> list[Sample]:
    return [
        sample
        for gameweek in sorted(samples_by_gameweek)
        for sample in samples_by_gameweek[gameweek]
    ]


def _by_player(predictions: Sequence[Prediction]) -> Dict[int, float]:
    return {
        prediction.player_id: prediction.predicted
        for prediction in predictions
    }


def _position_minutes_means(
    training: Sequence[Sample],
) -> Dict[str, float]:
    values: DefaultDict[str, list[int]] = defaultdict(list)
    for sample in training:
        values[sample.position].append(sample.actual)
    return {
        position: sum(items) / len(items)
        for position, items in sorted(values.items())
    }


def _player_document(
    player: Mapping[str, Any],
    history: Mapping[int, Mapping[str, float]],
    predictions: Mapping[str, Mapping[int, float]],
    minutes_fallback: Mapping[str, float],
) -> Dict[str, Any]:
    player_id = int(player["playerId"])
    ordered_history = [
        values for _, values in sorted(history.items())
    ]
    if ordered_history:
        expected_minutes = float(ordered_history[-1]["minutes"])
        minutes_identity = "stable-code-player-last"
    else:
        expected_minutes = minutes_fallback[str(player["position"])]
        minutes_identity = "position-mean-missing-prior-identity"
    probability_values = {
        output: _round(predictions[target][player_id])
        for target, output in TARGET_OUTPUTS.items()
    }
    is_coherent = (
        probability_values["startProbability"]
        <= probability_values["appearanceProbability"]
        and probability_values["played60Probability"]
        <= probability_values["appearanceProbability"]
    )
    return {
        "playerId": player_id,
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
        **probability_values,
        "probabilityCoherenceStatus": (
            "coherent"
            if is_coherent
            else "raw-independent-models-violate-event-nesting"
        ),
        "expectedMinutes": _round(expected_minutes),
        "expectedMinutesModelKey": MINUTES_BASELINE,
        "expectedMinutesIdentity": minutes_identity,
    }


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Fit locked-holdout-supported historical participation "
            "classifiers and emit a provisional current GW1 player artifact."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=CURRENT_SEASON)
    parser.add_argument("--gameweek", type=int, default=CURRENT_GAMEWEEK)
    parser.add_argument("--output", required=True, type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_preseason_participation_forecast(
            options.database,
            options.season,
            options.gameweek,
        )
        _write_report(artifact, options.output)
        return 0
    except (TemporalRidgeError, OSError, ValueError) as exception:
        code = getattr(exception, "code", "forecast.failed")
        print(
            json.dumps(
                {"error": {"code": code, "message": str(exception)}},
                sort_keys=True,
            ),
            file=sys.stderr,
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
