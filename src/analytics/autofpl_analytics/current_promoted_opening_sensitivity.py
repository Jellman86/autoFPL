from __future__ import annotations

import argparse
import copy
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence

import numpy as np

from .current_appearance_hurdle_joint_scenarios import (
    _rebuild_run_identity,
)
from .current_appearance_hurdle_player_forecast import (
    STATUS as POINT_FORECAST_STATUS,
    build_current_appearance_hurdle_player_forecast,
)
from .current_best_supported_opening_squad import (
    build_current_best_supported_opening_squad,
)
from .current_joint_scenario_forecast import _retained_screen
from .current_multi_horizon_initial_squad import (
    SELECTED_POLICY_KEY,
    _build_candidates,
    _build_from_scenario as build_multi_squad_from_scenario,
    _score_horizon,
    _week_matrices,
)
from .current_multi_horizon_joint_scenarios import _build_from_artifacts
from .historical_opening_policy_data import (
    REGISTERED_SEASONS,
    _load_opening_fold,
    _required_capture,
)
from .historical_promoted_appearance_evaluation import (
    BLEND_WEIGHT,
    SOURCE_SEASONS,
    PriorCompetitionPlayer,
    _fit_source_model,
    _load_promotion_class,
    _parse_extractions,
    _parse_source_row,
    _source_feature_vector,
)
from .historical_promoted_opening_evaluation import (
    RETAINED_APPEARANCE_DATA_IDENTITY,
    RETAINED_APPEARANCE_RUN_IDENTITY,
)
from .multi_season_player_forecast import CURRENT_SEASON
from .preseason_participation_forecast import (
    build_preseason_participation_forecast,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-promoted-opening-sensitivity"
ARTIFACT_VERSION = "current-promoted-opening-sensitivity-v1"
POINT_ARTIFACT_VERSION = (
    "current-promoted-appearance-hurdle-point-sensitivity-v1"
)
STATUS = "prospective-sensitivity-unscored-non-serving"
HORIZON_GAMEWEEKS = 6
HISTORICAL_POLICY_DATA_IDENTITY = (
    "f35f876a39d7d8766513d83a8a63596b03003dcdc0d83b7889324d451eef707c"
)
HISTORICAL_POLICY_RUN_IDENTITY = (
    "9e57578c3666310e48a8c96efe196a549a849ded590ee0af2ca98a5c60081149"
)


def build_current_promoted_opening_sensitivity(
    database_path: Path,
    historical_extraction_paths: Mapping[str, Path],
    current_extraction_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    point_forecast = _build_adjusted_point_forecast(
        path,
        historical_extraction_paths,
        Path(current_extraction_path),
    )
    participation = build_preseason_participation_forecast(
        path,
        CURRENT_SEASON,
        1,
    )
    scenario = _build_from_artifacts(
        path,
        point_forecast,
        participation,
        _retained_screen(),
        allowed_point_artifact_versions=(POINT_ARTIFACT_VERSION,),
    )
    scenario["variant"] = {
        "variantKey": "promoted-appearance-sensitivity",
        "pointForecastArtifactVersion": POINT_ARTIFACT_VERSION,
        "retainedAppearanceDataIdentitySha256": (
            RETAINED_APPEARANCE_DATA_IDENTITY
        ),
        "historicalPolicyDataIdentitySha256": (
            HISTORICAL_POLICY_DATA_IDENTITY
        ),
        "servingEligible": False,
    }
    scenario["runIdentitySha256"] = _rebuild_run_identity(scenario)
    multi = build_multi_squad_from_scenario(path, scenario)
    selected = [
        value
        for value in multi["policies"]
        if value["evaluationPolicyKey"] == SELECTED_POLICY_KEY
        and int(value["horizonGameweeks"]) == HORIZON_GAMEWEEKS
    ]
    _require(
        len(selected) == 1,
        "current-promoted.selected-policy",
        "The current sensitivity has no unique six-Gameweek policy.",
    )
    challenger = selected[0]
    incumbent = build_current_best_supported_opening_squad(path)
    _require(
        int(incumbent["officialCaptureId"])
        == int(scenario["officialCaptureId"]),
        "current-promoted.incumbent-cutoff",
        "The current v2 squad and sensitivity use different captures.",
    )
    candidates = _build_candidates(path, scenario)
    weeks = _week_matrices(scenario)
    incumbent_score = _score_horizon(
        incumbent["selection"],
        candidates,
        weeks,
        HORIZON_GAMEWEEKS,
    )
    challenger_score = challenger["exactScenarioScore"]
    incumbent_values = np.asarray(
        incumbent_score["pathTotalPoints"],
        dtype=float,
    )
    challenger_values = np.asarray(
        challenger_score["pathTotalPoints"],
        dtype=float,
    )
    _require(
        incumbent_values.shape == challenger_values.shape,
        "current-promoted.path-alignment",
        "The current sensitivity path scores are not paired.",
    )
    differences = challenger_values - incumbent_values
    by_id = {
        int(value["playerId"]): value for value in candidates
    }
    incumbent_ids = {
        int(value) for value in incumbent["selection"]["playerIds"]
    }
    challenger_ids = {
        int(value) for value in challenger["selection"]["playerIds"]
    }
    affected_codes = {
        int(value["playerCode"])
        for value in point_forecast["players"]
        if bool(value.get("priorCompetitionSensitivity"))
    }
    affected_by_id = {
        int(value["playerId"])
        for value in point_forecast["players"]
        if int(value["playerCode"]) in affected_codes
    }
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "seasonCode": scenario["seasonCode"],
        "openingGameweek": scenario["openingGameweek"],
        "decisionCutoffUtc": scenario["decisionCutoffUtc"],
        "officialCaptureId": scenario["officialCaptureId"],
        "scenarioCount": scenario["scenarioCount"],
        "affectedPlayerCount": len(affected_by_id),
        "incumbentAffectedPlayerCount": len(
            incumbent_ids & affected_by_id
        ),
        "challengerAffectedPlayerCount": len(
            challenger_ids & affected_by_id
        ),
        "incumbent": {
            "selection": incumbent["selection"],
            "scoreOnSensitivityScenarios": incumbent_score,
            "sourceRunIdentitySha256": incumbent[
                "runIdentitySha256"
            ],
        },
        "challenger": {
            "selection": challenger["selection"],
            "solver": challenger["solver"],
            "scoreOnSensitivityScenarios": challenger_score,
            "sourceMultiHorizonRunIdentitySha256": multi[
                "runIdentitySha256"
            ],
        },
        "selectionChange": {
            "overlapPlayerCount": len(incumbent_ids & challenger_ids),
            "removedPlayers": [
                _player_identity(by_id[value])
                for value in sorted(incumbent_ids - challenger_ids)
            ],
            "addedPlayers": [
                _player_identity(by_id[value])
                for value in sorted(challenger_ids - incumbent_ids)
            ],
        },
        "pairedScenarioComparison": {
            "meanPointDifference": _round(float(np.mean(differences))),
            "minimumPointDifference": int(np.min(differences)),
            "medianPointDifference": _round(
                float(np.median(differences))
            ),
            "maximumPointDifference": int(np.max(differences)),
            "challengerWinProbability": _round(
                float(np.mean(differences > 0))
            ),
            "tieProbability": _round(
                float(np.mean(differences == 0))
            ),
            "incumbentWinProbability": _round(
                float(np.mean(differences < 0))
            ),
        },
        "largestAppearanceChanges": _largest_changes(point_forecast),
        "source": {
            "pointForecastArtifactVersion": POINT_ARTIFACT_VERSION,
            "pointForecastRunIdentitySha256": point_forecast[
                "runIdentitySha256"
            ],
            "scenarioRunIdentitySha256": scenario[
                "runIdentitySha256"
            ],
            "retainedAppearanceDataIdentitySha256": (
                RETAINED_APPEARANCE_DATA_IDENTITY
            ),
            "retainedAppearanceRunIdentitySha256": (
                RETAINED_APPEARANCE_RUN_IDENTITY
            ),
            "historicalPolicyDataIdentitySha256": (
                HISTORICAL_POLICY_DATA_IDENTITY
            ),
            "historicalPolicyRunIdentitySha256": (
                HISTORICAL_POLICY_RUN_IDENTITY
            ),
        },
        "decision": (
            "diagnostic-only-retain-served-v2-opening-squad"
        ),
        "limitations": [
            (
                "The historical full-squad policy gate failed. This current "
                "artifact is sensitivity analysis only and cannot influence "
                "served advice."
            ),
            (
                "Only reviewed current promoted-player identities receive "
                "the fixed prior-competition appearance pool."
            ),
            (
                "Current 2026/27 outcomes are unavailable; scenario score "
                "differences are model-implied, not realised evidence."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "source": artifact["source"],
            "incumbentSelection": artifact["incumbent"]["selection"],
            "challengerSelection": artifact["challenger"]["selection"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _build_adjusted_point_forecast(
    database_path: Path,
    extraction_paths: Mapping[str, Path],
    current_extraction_path: Path,
) -> Dict[str, Any]:
    _require(
        set(extraction_paths) == set(SOURCE_SEASONS),
        "current-promoted.historical-sources",
        "All four historical source extractions are required.",
    )
    base = build_current_appearance_hurdle_player_forecast(
        database_path
    )
    connection = _open_connection(database_path)
    try:
        captures = tuple(
            _required_capture(connection, season)
            for season in REGISTERED_SEASONS
        )
        classes = tuple(
            _load_promotion_class(
                connection,
                _load_opening_fold(
                    connection,
                    captures,
                    index,
                    include_outcomes=True,
                ),
                source_season,
                Path(extraction_paths[source_season]),
            )
            for index, source_season in enumerate(SOURCE_SEASONS)
        )
    finally:
        connection.close()
    model, diagnostics = _fit_source_model(classes)
    current_sources, current_identity = _load_current_sources(
        current_extraction_path,
        int(base["officialCaptureId"]),
        {
            int(player["playerCode"])
            for player in base["players"]
        },
    )
    forecast = copy.deepcopy(base)
    forecast["artifactType"] = (
        "current-promoted-appearance-hurdle-point-sensitivity"
    )
    forecast["artifactVersion"] = POINT_ARTIFACT_VERSION
    forecast["status"] = POINT_FORECAST_STATUS
    forecast["modelKey"] = (
        "multi-season-appearance-hurdle-points-promoted-sensitivity"
    )
    forecast["isPromoted"] = False
    forecast["influencesAdvice"] = False
    affected = 0
    for player in forecast["players"]:
        source = current_sources.get(int(player["playerCode"]))
        player["priorCompetitionSensitivity"] = source is not None
        if source is None:
            continue
        affected += 1
        for row in player["gameweeks"]:
            gameweek = int(row["gameweek"])
            incumbent_probability = float(
                row["appearanceProbability"]
            )
            source_probability = float(
                model.predict_proba(
                    [
                        _source_feature_vector(
                            source,
                            str(player["position"]),
                            gameweek,
                        )
                    ]
                )[0, 1]
            )
            probability = (
                BLEND_WEIGHT * incumbent_probability
                + (1.0 - BLEND_WEIGHT) * source_probability
            )
            expected = probability * float(
                row["conditionalExpectedPoints"]
            )
            row["incumbentAppearanceProbability"] = _round(
                incumbent_probability
            )
            row["sourceAppearanceProbability"] = _round(
                source_probability
            )
            row["appearanceProbability"] = _round(probability)
            row["expectedPoints"] = _round(expected)
            row["hurdleExpectedPoints"] = _round(expected)
            row["expectedPointDelta"] = _round(
                expected - float(row["incumbentExpectedPoints"])
            )
        for horizon in player["horizons"]:
            gameweek_count = int(horizon["gameweekCount"])
            horizon["expectedPoints"] = _round(
                sum(
                    float(row["expectedPoints"])
                    for row in player["gameweeks"]
                    if int(row["gameweek"]) <= gameweek_count
                )
            )
    _require(
        affected == int(current_identity["reviewedIdentityCount"]),
        "current-promoted.current-coverage",
        "The reviewed current source does not cover the forecast cohort.",
    )
    forecast["priorCompetitionSensitivity"] = {
        **current_identity,
        "affectedPlayerCount": affected,
        "blendWeight": BLEND_WEIGHT,
        "sourceModelDiagnostics": diagnostics,
        "retainedHistoricalAppearanceDataIdentitySha256": (
            RETAINED_APPEARANCE_DATA_IDENTITY
        ),
        "retainedHistoricalPolicyDataIdentitySha256": (
            HISTORICAL_POLICY_DATA_IDENTITY
        ),
        "servingEligible": False,
    }
    forecast["dataIdentitySha256"] = _sha256(
        {
            "baseDataIdentitySha256": base["dataIdentitySha256"],
            "priorCompetitionSensitivity": forecast[
                "priorCompetitionSensitivity"
            ],
            "players": forecast["players"],
        }
    )
    forecast["runIdentitySha256"] = _sha256(
        {
            key: value
            for key, value in forecast.items()
            if key != "runIdentitySha256"
        }
    )
    return forecast


def _load_current_sources(
    extraction_path: Path,
    official_capture_id: int,
    current_player_codes: set[int],
) -> tuple[Dict[int, PriorCompetitionPlayer], Dict[str, Any]]:
    try:
        document = json.loads(extraction_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise TemporalRidgeError(
            "current-promoted.current-extraction",
            "The current FBref extraction is invalid.",
        ) from exception
    rows = document.get("players")
    _require(
        document.get("sourceKey")
        == "fbref-championship-playing-time-2025-26"
        and document.get("competitionSeason") == "2025-26"
        and int(document.get("snapshotId", -1)) == 27
        and document.get("contentSha256")
        == "0c80ce784b02cbcb21692859effc969109c6e54376e9f511ad3b6b49b32fa55b"
        and isinstance(rows, list)
        and len(rows) == int(document.get("rowCount", -1)),
        "current-promoted.current-identity",
        "The current FBref extraction identity differs.",
    )
    parsed = [
        {
            **_parse_source_row("2025-26", row),
            "officialPlayerCode": row.get("officialPlayerCode"),
            "identityStatus": row.get("identityStatus"),
        }
        for row in rows
    ]
    by_source: DefaultDict[str, list[Dict[str, Any]]] = defaultdict(
        list
    )
    for row in parsed:
        by_source[str(row["sourcePlayerId"])].append(row)
    by_code: Dict[int, PriorCompetitionPlayer] = {}
    for source_player_id, player_rows in by_source.items():
        reviewed = [
            row
            for row in player_rows
            if row["identityStatus"] == "reviewed-v1"
            and row["officialPlayerCode"] is not None
        ]
        if not reviewed:
            continue
        codes = {int(row["officialPlayerCode"]) for row in reviewed}
        _require(
            len(codes) == 1,
            "current-promoted.current-code-ambiguity",
            f"Source player {source_player_id} has multiple official codes.",
        )
        code = next(iter(codes))
        _require(
            code not in by_code,
            "current-promoted.current-code-duplicate",
            f"Official code {code} has multiple current source players.",
        )
        by_code[code] = PriorCompetitionPlayer(
            source_player_id=source_player_id,
            player_name=str(reviewed[0]["playerName"]),
            appearances=sum(
                int(row["appearances"]) for row in player_rows
            ),
            starts=sum(int(row["starts"]) for row in player_rows),
            minutes=sum(int(row["minutes"]) for row in player_rows),
        )
    _require(
        len(by_code) == int(document.get("reviewedIdentityCount", -1)),
        "current-promoted.current-reviewed-count",
        "The current reviewed identity count differs.",
    )
    _require(
        set(by_code) <= current_player_codes,
        "current-promoted.current-roster-membership",
        "A reviewed prior-competition player is absent from the current roster.",
    )
    return by_code, {
        "sourceKey": document["sourceKey"],
        "snapshotId": int(document["snapshotId"]),
        "contentSha256": str(document["contentSha256"]),
        "sourceIdentityCaptureId": int(document["identityCaptureId"]),
        "forecastOfficialCaptureId": official_capture_id,
        "reviewedIdentityCount": len(by_code),
    }


def _largest_changes(
    point_forecast: Mapping[str, Any],
) -> list[Dict[str, Any]]:
    changes = []
    for player in point_forecast["players"]:
        if not bool(player.get("priorCompetitionSensitivity")):
            continue
        for row in player["gameweeks"]:
            changes.append(
                {
                    "playerId": int(player["playerId"]),
                    "playerCode": int(player["playerCode"]),
                    "webName": str(player["webName"]),
                    "teamName": str(player["teamName"]),
                    "position": str(player["position"]),
                    "gameweek": int(row["gameweek"]),
                    "incumbentAppearanceProbability": float(
                        row["incumbentAppearanceProbability"]
                    ),
                    "sensitivityAppearanceProbability": float(
                        row["appearanceProbability"]
                    ),
                    "appearanceProbabilityDelta": _round(
                        float(row["appearanceProbability"])
                        - float(row["incumbentAppearanceProbability"])
                    ),
                }
            )
    return sorted(
        changes,
        key=lambda value: (
            -abs(float(value["appearanceProbabilityDelta"])),
            int(value["gameweek"]),
            int(value["playerCode"]),
        ),
    )[:20]


def _player_identity(player: Mapping[str, Any]) -> Dict[str, Any]:
    return {
        "playerId": int(player["playerId"]),
        "webName": str(player["webName"]),
        "teamName": str(player["teamName"]),
        "position": str(player["position"]),
        "priceTenths": int(player["priceTenths"]),
    }


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Measure current opening-squad sensitivity to the frozen "
            "promoted-player appearance translation."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument(
        "--fbref-extraction",
        action="append",
        default=[],
        metavar="SEASON=PATH",
    )
    parser.add_argument(
        "--current-fbref-extraction",
        required=True,
        type=Path,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_promoted_opening_sensitivity(
            options.database,
            _parse_extractions(options.fbref_extraction),
            options.current_fbref_extraction,
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
