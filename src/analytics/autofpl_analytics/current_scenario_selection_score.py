from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence, Tuple

import numpy as np

from .scenario_reference import (
    ENGINE_VERSION,
    SelectionDefinition,
    compare_paired_scores,
    score_selection_scenarios,
    summarise_scores,
)
from .temporal_ridge import TemporalRidgeError

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-selection-joint-scenario-score-shadow"
ARTIFACT_VERSION = "current-selection-scenario-score-v1"
STATUS = "prospective-shadow-unscored"
MAXIMUM_ARTIFACT_BYTES = 2 * 1024 * 1024


def build_current_scenario_selection_score(
    database_path: Path | str,
) -> Dict[str, Any]:
    path = Path(database_path)
    connection: Optional[sqlite3.Connection] = None
    try:
        connection = sqlite3.connect(
            f"file:{path}?mode=ro",
            uri=True,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        scenario_row = connection.execute(
            """
            SELECT scenario_artifact_id, official_capture_id,
                   document_json, content_sha256
            FROM joint_scenario_shadow_artifacts
            ORDER BY
                julianday(decision_cutoff_utc) DESC,
                scenario_artifact_id DESC
            LIMIT 1;
            """
        ).fetchone()
        if scenario_row is None:
            raise TemporalRidgeError(
                "scenario-score.scenario-not-found",
                "The current joint scenario artifact is unavailable.",
            )
        scenario = _verified_document(
            scenario_row["document_json"],
            scenario_row["content_sha256"],
            "scenario-score.scenario-content",
        )
        scenario_artifact_id = int(
            scenario_row["scenario_artifact_id"]
        )
        capture_id = int(scenario_row["official_capture_id"])
        _require(
            scenario.get("scenarioArtifactId")
            in (None, scenario_artifact_id)
            and scenario.get("officialCaptureId") == capture_id
            and scenario.get("scenarioArtifactContentSha256")
            in (None, str(scenario_row["content_sha256"])),
            "scenario-score.scenario-lineage",
            "The stored scenario identity does not match its document.",
        )

        forecast_row = connection.execute(
            """
            SELECT artifact_id, capture_id, document_json, content_sha256
            FROM baseline_forecast_artifacts
            WHERE capture_id = ?
            ORDER BY artifact_id DESC
            LIMIT 1;
            """,
            (capture_id,),
        ).fetchone()
        if forecast_row is None:
            raise TemporalRidgeError(
                "scenario-score.forecast-not-found",
                "The exact model forecast artifact is unavailable.",
            )
        forecast = _verified_document(
            forecast_row["document_json"],
            forecast_row["content_sha256"],
            "scenario-score.forecast-content",
        )
        forecast_artifact_id = int(forecast_row["artifact_id"])
        _require(
            forecast.get("forecastArtifactId")
            in (None, forecast_artifact_id)
            and forecast.get("forecastArtifactContentHash")
            in (None, str(forecast_row["content_sha256"]))
            and forecast.get("isSynthetic") is False,
            "scenario-score.forecast-lineage",
            "The model forecast does not match the current scenario.",
        )
        _require(
            forecast.get("gameweek") == scenario.get("gameweek")
            and _utc_instant(forecast.get("decisionCutoffUtc"))
            == _utc_instant(scenario.get("decisionCutoffUtc")),
            "scenario-score.forecast-lineage",
            "The model forecast does not match the scenario cutoff.",
        )

        selection_row = connection.execute(
            """
            SELECT selection_revision_id, revision,
                   forecast_artifact_id, selection_json,
                   selection_content_sha256, locked_at_utc
            FROM selection_revisions
            WHERE forecast_artifact_id = ?
            ORDER BY revision DESC, selection_revision_id DESC
            LIMIT 1;
            """,
            (forecast_artifact_id,),
        ).fetchone()
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "scenario-score.database-read",
            "The scenario score inputs could not be read.",
        ) from exception
    finally:
        if connection is not None:
            connection.close()

    player_columns, positions = _scenario_player_index(scenario)
    point_rows = np.asarray(scenario.get("pointRows"))
    played_rows = np.asarray(scenario.get("playedRows"))
    _require(
        point_rows.ndim == 2
        and played_rows.ndim == 2
        and point_rows.shape == played_rows.shape
        and point_rows.shape[0] == scenario.get("scenarioCount")
        and point_rows.shape[1] == scenario.get("playerCount"),
        "scenario-score.matrix-shape",
        "The stored scenario matrices do not match their declared shape.",
    )

    model_selection = _model_selection(forecast)
    model_result = _score_selection(
        model_selection,
        player_columns,
        positions,
        point_rows,
        played_rows,
    )
    model_selection_hash = _sha256(
        model_result["selection"]
    )

    user_source: Optional[Dict[str, Any]] = None
    user_result: Optional[Dict[str, Any]] = None
    comparison: Optional[Dict[str, Any]] = None
    user_selection_hash: Optional[str] = None
    if selection_row is not None:
        selection_document = _verified_document(
            selection_row["selection_json"],
            selection_row["selection_content_sha256"],
            "scenario-score.selection-content",
        )
        _require(
            int(selection_row["forecast_artifact_id"])
            == forecast_artifact_id,
            "scenario-score.selection-lineage",
            "The user selection does not match the model forecast.",
        )
        user_selection = _user_selection(selection_document)
        user_result = _score_selection(
            user_selection,
            player_columns,
            positions,
            point_rows,
            played_rows,
        )
        user_selection_hash = str(
            selection_row["selection_content_sha256"]
        )
        user_source = {
            "status": "available",
            "selectionRevisionId": int(
                selection_row["selection_revision_id"]
            ),
            "revision": int(selection_row["revision"]),
            "selectionContentSha256": user_selection_hash,
            "lockedAtUtc": selection_row["locked_at_utc"],
        }
        comparison = compare_paired_scores(
            model_result["_scores"],
            user_result["_scores"],
        )
    else:
        user_source = {
            "status": "missing",
            "selectionRevisionId": None,
            "revision": None,
            "selectionContentSha256": None,
            "lockedAtUtc": None,
        }

    data_identity = _sha256(
        {
            "scenarioArtifactId": scenario_artifact_id,
            "scenarioArtifactContentSha256": str(
                scenario_row["content_sha256"]
            ),
            "scenarioContentSha256": scenario[
                "scenarioContentSha256"
            ],
            "forecastArtifactId": forecast_artifact_id,
            "forecastArtifactContentSha256": str(
                forecast_row["content_sha256"]
            ),
            "modelSelectionContentSha256": model_selection_hash,
            "userSelectionContentSha256": user_selection_hash,
        }
    )
    run_identity = _sha256(
        {
            "artifactVersion": ARTIFACT_VERSION,
            "engineVersion": ENGINE_VERSION,
            "dataIdentitySha256": data_identity,
        }
    )
    return {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "seasonCode": scenario["seasonCode"],
        "gameweek": scenario["gameweek"],
        "deadlineUtc": scenario["deadlineUtc"],
        "decisionCutoffUtc": scenario["decisionCutoffUtc"],
        "officialCaptureId": capture_id,
        "engineVersion": ENGINE_VERSION,
        "scenarioCount": int(point_rows.shape[0]),
        "scenarioSource": {
            "scenarioArtifactId": scenario_artifact_id,
            "scenarioArtifactContentSha256": str(
                scenario_row["content_sha256"]
            ),
            "scenarioContentSha256": scenario[
                "scenarioContentSha256"
            ],
            "scenarioRunIdentitySha256": scenario[
                "runIdentitySha256"
            ],
        },
        "modelSource": {
            "forecastArtifactId": forecast_artifact_id,
            "forecastArtifactContentSha256": str(
                forecast_row["content_sha256"]
            ),
            "modelLabel": forecast["modelLabel"],
            "selectionContentSha256": model_selection_hash,
        },
        "userSource": user_source,
        "model": _public_result(model_result),
        "user": (
            _public_result(user_result)
            if user_result is not None
            else None
        ),
        "userVsModel": comparison,
        "limitations": [
            (
                "This is a prospective shadow comparison and does not "
                "influence squad advice."
            ),
            (
                "The empirical support contains 38 retained joint rows; "
                "tail estimates remain coarse until more prospective "
                "seasons are available."
            ),
            (
                "Safer and higher-ceiling alternative generation is not "
                "part of this artifact version."
            ),
        ],
        "dataIdentitySha256": data_identity,
        "runIdentitySha256": run_identity,
    }


def _verified_document(
    raw: Any,
    expected_hash: Any,
    code: str,
) -> Dict[str, Any]:
    encoded = str(raw).encode("utf-8")
    if (
        not encoded
        or len(encoded) > MAXIMUM_ARTIFACT_BYTES
        or hashlib.sha256(encoded).hexdigest() != str(expected_hash)
    ):
        raise TemporalRidgeError(
            code,
            "A persisted source artifact failed its content boundary.",
        )
    try:
        document = json.loads(encoded)
    except (json.JSONDecodeError, UnicodeDecodeError) as exception:
        raise TemporalRidgeError(
            code,
            "A persisted source artifact is not valid JSON.",
        ) from exception
    _require(
        isinstance(document, dict),
        code,
        "A persisted source artifact must be a JSON object.",
    )
    return document


def _scenario_player_index(
    scenario: Mapping[str, Any],
) -> Tuple[Dict[int, int], Dict[int, str]]:
    players = scenario.get("players")
    _require(
        isinstance(players, list)
        and len(players) == scenario.get("playerCount"),
        "scenario-score.player-index",
        "The scenario player index is incomplete.",
    )
    columns: Dict[int, int] = {}
    positions: Dict[int, str] = {}
    for expected_column, player in enumerate(players):
        _require(
            isinstance(player, dict)
            and player.get("columnIndex") == expected_column,
            "scenario-score.player-index",
            "The scenario player columns are not contiguous.",
        )
        player_id = int(player["playerId"])
        _require(
            player_id > 0 and player_id not in columns,
            "scenario-score.player-index",
            "The scenario player identities are not unique.",
        )
        columns[player_id] = expected_column
        positions[player_id] = str(player["position"])
    return columns, positions


def _model_selection(
    forecast: Mapping[str, Any],
) -> Dict[str, Any]:
    selection = forecast.get("selection")
    players = selection.get("players") if isinstance(selection, dict) else None
    _require(
        isinstance(players, list) and len(players) == 15,
        "scenario-score.model-selection",
        "The model forecast does not contain a complete selection.",
    )
    starting = [
        int(player["playerId"])
        for player in players
        if player.get("lineupPlace") == "starting"
    ]
    bench = [
        player
        for player in players
        if player.get("lineupPlace") == "bench"
    ]
    goalkeepers = [
        int(player["playerId"])
        for player in bench
        if player.get("position") == "goalkeeper"
    ]
    substitutes = [
        int(player["playerId"])
        for player in sorted(
            (
                player
                for player in bench
                if player.get("position") != "goalkeeper"
            ),
            key=lambda player: int(player["benchOrder"]),
        )
    ]
    captains = [
        int(player["playerId"])
        for player in players
        if player.get("captaincy") == "captain"
    ]
    vice_captains = [
        int(player["playerId"])
        for player in players
        if player.get("captaincy") == "vice-captain"
    ]
    _require(
        len(starting) == 11
        and len(goalkeepers) == 1
        and len(substitutes) == 3
        and len(captains) == 1
        and len(vice_captains) == 1,
        "scenario-score.model-selection",
        "The model selection roles are incomplete.",
    )
    return {
        "startingPlayerIds": starting,
        "captainPlayerId": captains[0],
        "viceCaptainPlayerId": vice_captains[0],
        "replacementGoalkeeperPlayerId": goalkeepers[0],
        "outfieldSubstitutePlayerIds": substitutes,
    }


def _user_selection(
    selection: Mapping[str, Any],
) -> Dict[str, Any]:
    required = (
        "startingPlayerIds",
        "captainPlayerId",
        "viceCaptainPlayerId",
        "replacementGoalkeeperPlayerId",
        "outfieldSubstitutePlayerIds",
    )
    _require(
        all(key in selection for key in required),
        "scenario-score.user-selection",
        "The user selection document is incomplete.",
    )
    return dict(selection)


def _score_selection(
    selection: Mapping[str, Any],
    player_columns: Mapping[int, int],
    positions: Mapping[int, str],
    point_rows: np.ndarray,
    played_rows: np.ndarray,
) -> Dict[str, Any]:
    starting = tuple(int(value) for value in selection["startingPlayerIds"])
    goalkeeper = int(selection["replacementGoalkeeperPlayerId"])
    substitutes = tuple(
        int(value)
        for value in selection["outfieldSubstitutePlayerIds"]
    )
    player_ids = starting + (goalkeeper,) + substitutes
    _require(
        len(player_ids) == 15
        and len(set(player_ids)) == 15
        and all(player_id in player_columns for player_id in player_ids),
        "scenario-score.selection-player",
        "A selection player is unavailable from the joint scenario.",
    )
    definition = SelectionDefinition.create(
        player_ids=player_ids,
        positions=tuple(positions[player_id] for player_id in player_ids),
        starting_player_ids=starting,
        replacement_goalkeeper_player_id=goalkeeper,
        outfield_substitute_player_ids=substitutes,
        captain_player_id=int(selection["captainPlayerId"]),
        vice_captain_player_id=int(selection["viceCaptainPlayerId"]),
    )
    columns = [
        player_columns[player_id]
        for player_id in definition.player_ids
    ]
    scores = score_selection_scenarios(
        definition,
        point_rows[:, columns],
        played_rows[:, columns],
    )
    return {
        "selection": {
            "playerIds": list(definition.player_ids),
            "positions": list(definition.positions),
            "startingPlayerIds": list(definition.starting_player_ids),
            "replacementGoalkeeperPlayerId": (
                definition.replacement_goalkeeper_player_id
            ),
            "outfieldSubstitutePlayerIds": list(
                definition.outfield_substitute_player_ids
            ),
            "captainPlayerId": definition.captain_player_id,
            "viceCaptainPlayerId": definition.vice_captain_player_id,
        },
        "summary": summarise_scores(scores),
        "totalPointRows": [
            int(value)
            for value in scores.total_points.tolist()
        ],
        "captainBonusPointRows": [
            int(value)
            for value in scores.captain_bonus_points.tolist()
        ],
        "_scores": scores,
    }


def _public_result(result: Dict[str, Any]) -> Dict[str, Any]:
    return {
        key: value
        for key, value in result.items()
        if key != "_scores"
    }


def _sha256(value: Any) -> str:
    encoded = json.dumps(
        value,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _utc_instant(value: Any) -> datetime:
    try:
        parsed = datetime.fromisoformat(
            str(value).replace("Z", "+00:00")
        )
    except ValueError as exception:
        raise TemporalRidgeError(
            "scenario-score.source-time",
            "A scenario score source has an invalid cutoff.",
        ) from exception
    if parsed.tzinfo is None:
        raise TemporalRidgeError(
            "scenario-score.source-time",
            "A scenario score source cutoff must include UTC.",
        )
    return parsed.astimezone(timezone.utc)


def _require(
    condition: bool,
    code: str,
    message: str,
) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Score the exact model and latest user selection against the "
            "current persisted joint scenario shadow."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    options = parser.parse_args(arguments)
    if options.output.exists():
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "failed",
                    "errorCode": "output.already-exists",
                },
                sort_keys=True,
            ),
            file=sys.stderr,
        )
        return 1
    try:
        artifact = build_current_scenario_selection_score(
            options.database
        )
        options.output.write_text(
            json.dumps(
                artifact,
                indent=2,
                sort_keys=True,
            )
            + "\n",
            encoding="utf-8",
        )
    except (OSError, TemporalRidgeError, ValueError) as exception:
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "failed",
                    "errorCode": getattr(
                        exception,
                        "code",
                        "scenario-score.failed",
                    ),
                },
                sort_keys=True,
            ),
            file=sys.stderr,
        )
        return 1
    print(
        json.dumps(
            {
                "schemaVersion": SCHEMA_VERSION,
                "status": artifact["status"],
                "officialCaptureId": artifact[
                    "officialCaptureId"
                ],
                "scenarioCount": artifact["scenarioCount"],
                "outputFile": options.output.name,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
