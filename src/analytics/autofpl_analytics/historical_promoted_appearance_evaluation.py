from __future__ import annotations

import argparse
import brotli
import csv
import io
import json
import math
import re
import sys
import unicodedata
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence

import numpy as np
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler

from .historical_appearance_hurdle_opening_evaluation import (
    build_historical_appearance_hurdle_opening_scenarios,
)
from .historical_opening_policy_data import (
    REGISTERED_SEASONS,
    OpeningFold,
    OpeningPlayer,
    _load_opening_fold,
    _required_capture,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-promoted-appearance-evaluation"
ARTIFACT_VERSION = "historical-promoted-appearance-evaluation-v1"
STATUS = "retrospective-source-challenger-evaluated"
SOURCE_SEASONS = ("2021-22", "2022-23", "2023-24", "2024-25")
TARGET_GAMEWEEKS = tuple(range(1, 9))
SOURCE_MODEL = "regularised-prior-competition-appearance-logistic"
INCUMBENT_MODEL = "multi-season-appearance-histogram-classifier"
CHALLENGER_MODEL = "fixed-equal-weight-incumbent-source-pool"
SOURCE_FEATURES = (
    "priorCompetitionAppearanceRate",
    "priorCompetitionStartRate",
    "priorCompetitionMinuteShare",
    "priorCompetitionMinutesPerAppearance",
    "targetGameweekFraction",
    "isGoalkeeper",
    "isDefender",
    "isMidfielder",
    "isForward",
)
PROMOTION_CLASSES: Mapping[str, Mapping[str, str]] = {
    "2021-22": {
        "Fulham": "Fulham",
        "Bournemouth": "Bournemouth",
        "Nottingham": "Nott'm Forest",
    },
    "2022-23": {
        "Burnley": "Burnley",
        "Sheffield United": "Sheffield Utd",
        "Luton Town": "Luton",
    },
    "2023-24": {
        "Leicester City": "Leicester",
        "Ipswich Town": "Ipswich",
        "Southampton": "Southampton",
    },
    "2024-25": {
        "Leeds United": "Leeds",
        "Burnley": "Burnley",
        "Sunderland": "Sunderland",
    },
}
BLEND_WEIGHT = 0.5
LOGISTIC_PENALTY_INVERSE = 1.0
RANDOM_SEED = 20260730
BOOTSTRAP_REPLICATES = 5000
MINIMUM_BRIER_IMPROVEMENT_FRACTION = 0.01
MAXIMUM_LOG_LOSS_DELTA = 0.0
MAXIMUM_CALIBRATION_DELTA = 0.02
MAXIMUM_POSITION_BRIER_DELTA = 0.02
MINIMUM_TARGET_WINS = 2


@dataclass(frozen=True)
class PriorCompetitionPlayer:
    source_player_id: str
    player_name: str
    appearances: int
    starts: int
    minutes: int


@dataclass(frozen=True)
class BridgedPlayer:
    source: PriorCompetitionPlayer
    target: OpeningPlayer
    target_full_name: str
    bridge_method: str


@dataclass(frozen=True)
class PromotionClass:
    source_season_code: str
    target_season_code: str
    source_key: str
    source_snapshot_id: int
    source_content_sha256: str
    source_row_count: int
    source_player_count: int
    promoted_source_player_count: int
    promoted_target_player_count: int
    bridged_players: tuple[BridgedPlayer, ...]


def build_historical_promoted_appearance_evaluation(
    database_path: Path,
    extraction_paths: Mapping[str, Path],
) -> Dict[str, Any]:
    path = Path(database_path)
    if not path.is_file():
        raise TemporalRidgeError(
            "promoted-appearance.database-not-found",
            "The SQLite database does not exist.",
        )
    if set(extraction_paths) != set(SOURCE_SEASONS):
        raise TemporalRidgeError(
            "promoted-appearance.source-set",
            "Exactly one extraction is required for every registered source season.",
        )

    connection = _open_connection(path)
    try:
        captures = tuple(
            _required_capture(connection, season)
            for season in REGISTERED_SEASONS
        )
        folds = tuple(
            _load_opening_fold(
                connection,
                captures,
                target_index,
                include_outcomes=True,
            )
            for target_index in range(len(captures))
        )
        promotion_classes = tuple(
            _load_promotion_class(
                connection,
                folds[index],
                source_season,
                Path(extraction_paths[source_season]),
            )
            for index, source_season in enumerate(SOURCE_SEASONS)
        )
    finally:
        connection.close()

    incumbent_scenarios = (
        build_historical_appearance_hurdle_opening_scenarios(path)
    )
    incumbent = _incumbent_probabilities(incumbent_scenarios)
    target_documents = []
    all_predictions: list[Dict[str, Any]] = []
    for target_index in range(1, len(promotion_classes)):
        model, diagnostics = _fit_source_model(
            promotion_classes[:target_index]
        )
        target = promotion_classes[target_index]
        predictions = _predict_target(
            model,
            target,
            incumbent[target.target_season_code],
        )
        all_predictions.extend(predictions)
        target_documents.append(
            _target_document(target, predictions, diagnostics)
        )

    screen = _screen(all_predictions, target_documents)
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "researchStatus": "opened-retrospective-targets-no-promotion",
        "targetOutcomesOpened": True,
        "sourceModel": SOURCE_MODEL,
        "incumbentModel": INCUMBENT_MODEL,
        "challengerModel": CHALLENGER_MODEL,
        "sourceFeatures": list(SOURCE_FEATURES),
        "fixedBlend": {
            "incumbentWeight": BLEND_WEIGHT,
            "sourceWeight": 1.0 - BLEND_WEIGHT,
            "selectedBeforeTargetScoring": True,
        },
        "temporalDesign": {
            "method": "expanding-promotion-class-origin",
            "trainingSeed": "2021-22-to-2022-23",
            "scoredTargetSeasonCodes": list(REGISTERED_SEASONS[1:]),
            "trainingRule": (
                "strictly-earlier-promotion-classes-only"
            ),
            "targetOutcomeRole": "scoring-only",
            "identityRule": (
                "same-promoted-team-and-exact-or-token-subset-name-only"
            ),
            "fuzzyIdentityMatching": False,
        },
        "fixedScreen": _fixed_screen_document(),
        "promotionClasses": [
            _promotion_class_document(value)
            for value in promotion_classes
        ],
        "targets": target_documents,
        "screen": screen,
        "decision": (
            "retain-for-full-distribution-and-policy-evaluation"
            if screen["passes"]
            else "reject-prior-competition-appearance-challenger"
        ),
        "isPromoted": False,
        "influencesAdvice": False,
        "limitations": [
            (
                "The FBref pages were captured retrospectively. Their final "
                "prior-season aggregates are suitable for this translation "
                "experiment, but they are not historical point-in-time "
                "snapshots."
            ),
            (
                "Only players present both in a promoted club's prior-season "
                "FBref table and its target FPL Gameweek 1 roster are scored. "
                "New signings correctly receive no source feature."
            ),
            (
                "The fixed identity bridge rejects fuzzy proposals and "
                "unresolved aliases. Coverage is reported by promotion class."
            ),
            (
                "Passing this appearance screen would permit a separate "
                "distribution and constrained-squad policy evaluation; it "
                "would not itself promote the feature into served advice."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "promotionClasses": artifact["promotionClasses"],
            "temporalDesign": artifact["temporalDesign"],
            "fixedScreen": artifact["fixedScreen"],
            "targets": artifact["targets"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _load_promotion_class(
    connection: Any,
    fold: OpeningFold,
    source_season: str,
    extraction_path: Path,
) -> PromotionClass:
    if not extraction_path.is_file():
        raise TemporalRidgeError(
            "promoted-appearance.extraction-not-found",
            f"The {source_season} FBref extraction does not exist.",
        )
    try:
        document = json.loads(extraction_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise TemporalRidgeError(
            "promoted-appearance.extraction-json",
            f"The {source_season} FBref extraction is invalid.",
        ) from exception
    expected_key = (
        f"fbref-championship-playing-time-{source_season}"
    )
    _require(
        document.get("sourceKey") == expected_key
        and document.get("competitionSeason") == source_season
        and document.get("schemaVersion") == SCHEMA_VERSION,
        "promoted-appearance.extraction-identity",
        f"The {source_season} FBref extraction identity differs.",
    )
    rows = document.get("players")
    _require(
        isinstance(rows, list)
        and len(rows) == int(document.get("rowCount", -1)),
        "promoted-appearance.extraction-rows",
        f"The {source_season} FBref extraction rows are incomplete.",
    )
    parsed = [_parse_source_row(source_season, row) for row in rows]
    source_player_ids = {row["sourcePlayerId"] for row in parsed}
    _require(
        len(source_player_ids)
        == int(document.get("sourcePlayerCount", -1)),
        "promoted-appearance.extraction-player-count",
        f"The {source_season} stable source-player count differs.",
    )

    team_map = PROMOTION_CLASSES[source_season]
    target_full_names = _load_target_full_names(connection, fold)
    target_players = [
        player
        for player in fold.players
        if player.team_name in set(team_map.values())
    ]
    target_index: DefaultDict[
        tuple[str, tuple[str, ...]], list[OpeningPlayer]
    ] = defaultdict(list)
    for player in target_players:
        target_index[
            (
                player.team_name,
                _name_tokens(target_full_names[player.season_element_id]),
            )
        ].append(player)

    by_source_player: DefaultDict[str, list[Dict[str, Any]]] = defaultdict(
        list
    )
    for row in parsed:
        by_source_player[str(row["sourcePlayerId"])].append(row)
    promoted_source_players = 0
    bridged: list[BridgedPlayer] = []
    used_target_codes: set[int] = set()
    for source_player_id, player_rows in sorted(
        by_source_player.items()
    ):
        promoted_rows = [
            row for row in player_rows if row["teamName"] in team_map
        ]
        if not promoted_rows:
            continue
        promoted_source_players += 1
        promoted_target_teams = {
            team_map[str(row["teamName"])] for row in promoted_rows
        }
        names = {str(row["playerName"]) for row in player_rows}
        _require(
            len(names) == 1,
            "promoted-appearance.source-name-ambiguity",
            f"{source_season} source player {source_player_id} has "
            "multiple names.",
        )
        player_name = next(iter(names))
        proposals = [
            (candidate, method)
            for target_team in sorted(promoted_target_teams)
            for candidate, method in (
                _match_target_player(
                    target_team,
                    player_name,
                    target_index,
                ),
            )
            if candidate is not None
        ]
        unique_proposals = {
            candidate.player_code: (candidate, method)
            for candidate, method in proposals
        }
        if len(unique_proposals) != 1:
            continue
        candidate, method = next(iter(unique_proposals.values()))
        _require(
            candidate.player_code not in used_target_codes,
            "promoted-appearance.target-identity-duplicate",
            f"{source_season} target player {candidate.player_code} "
            "was bridged more than once.",
        )
        used_target_codes.add(candidate.player_code)
        bridged.append(
            BridgedPlayer(
                source=PriorCompetitionPlayer(
                    source_player_id=source_player_id,
                    player_name=player_name,
                    appearances=sum(
                        int(row["appearances"]) for row in player_rows
                    ),
                    starts=sum(int(row["starts"]) for row in player_rows),
                    minutes=sum(
                        int(row["minutes"]) for row in player_rows
                    ),
                ),
                target=candidate,
                target_full_name=target_full_names[
                    candidate.season_element_id
                ],
                bridge_method=method,
            )
        )
    _require(
        bridged,
        "promoted-appearance.identity-coverage",
        f"The {source_season} promotion class has no bridged players.",
    )
    return PromotionClass(
        source_season_code=source_season,
        target_season_code=fold.target_capture.season_code,
        source_key=expected_key,
        source_snapshot_id=int(document.get("snapshotId", 0)),
        source_content_sha256=str(document.get("contentSha256", "")),
        source_row_count=len(parsed),
        source_player_count=len(source_player_ids),
        promoted_source_player_count=promoted_source_players,
        promoted_target_player_count=len(target_players),
        bridged_players=tuple(
            sorted(
                bridged,
                key=lambda value: value.target.player_code,
            )
        ),
    )


def _parse_source_row(
    source_season: str,
    value: Any,
) -> Dict[str, Any]:
    _require(
        isinstance(value, dict),
        "promoted-appearance.source-row-type",
        f"The {source_season} extraction contains a non-object row.",
    )
    required = (
        "sourcePlayerId",
        "playerName",
        "teamName",
        "appearances",
        "starts",
        "minutes",
    )
    _require(
        all(key in value for key in required),
        "promoted-appearance.source-row-schema",
        f"The {source_season} extraction row schema is incomplete.",
    )
    try:
        appearances = int(value["appearances"])
        starts = int(value["starts"])
        minutes = int(value["minutes"])
    except (TypeError, ValueError) as exception:
        raise TemporalRidgeError(
            "promoted-appearance.source-row-value",
            f"The {source_season} extraction has invalid counts.",
        ) from exception
    _require(
        bool(str(value["sourcePlayerId"]).strip())
        and bool(str(value["playerName"]).strip())
        and bool(str(value["teamName"]).strip())
        and 0 <= starts <= appearances <= 46
        and 0 <= minutes <= 46 * 120,
        "promoted-appearance.source-row-bounds",
        f"The {source_season} extraction has out-of-range values.",
    )
    return {
        "sourcePlayerId": str(value["sourcePlayerId"]).strip(),
        "playerName": str(value["playerName"]).strip(),
        "teamName": str(value["teamName"]).strip(),
        "appearances": appearances,
        "starts": starts,
        "minutes": minutes,
    }


def _load_target_full_names(
    connection: Any,
    fold: OpeningFold,
) -> Dict[int, str]:
    row = connection.execute(
        """
        SELECT players_csv_brotli
        FROM historical_fpl_season_captures
        WHERE capture_id = :capture_id;
        """,
        {"capture_id": fold.target_capture.capture_id},
    ).fetchone()
    _require(
        row is not None,
        "promoted-appearance.target-player-payload",
        f"The {fold.target_capture.season_code} player payload is absent.",
    )
    try:
        payload = brotli.decompress(
            bytes(row["players_csv_brotli"])
        ).decode("utf-8-sig")
        reader = csv.DictReader(io.StringIO(payload, newline=""))
        _require(
            {"id", "first_name", "second_name"} <= set(
                reader.fieldnames or ()
            ),
            "promoted-appearance.target-player-schema",
            f"The {fold.target_capture.season_code} player payload "
            "has no full-name identity.",
        )
        names = {
            int(value["id"]): (
                f"{value['first_name']} {value['second_name']}".strip()
            )
            for value in reader
        }
    except (brotli.error, UnicodeError, ValueError) as exception:
        raise TemporalRidgeError(
            "promoted-appearance.target-player-payload",
            f"The {fold.target_capture.season_code} player payload "
            "is invalid.",
        ) from exception
    _require(
        all(
            player.season_element_id in names
            and names[player.season_element_id]
            for player in fold.players
        ),
        "promoted-appearance.target-player-coverage",
        f"The {fold.target_capture.season_code} full-name identity "
        "coverage is incomplete.",
    )
    return names


def _name_tokens(value: str) -> tuple[str, ...]:
    translated = value.translate(
        str.maketrans(
            {
                "ð": "d",
                "Ð": "D",
                "þ": "th",
                "Þ": "Th",
                "ł": "l",
                "Ł": "L",
                "ø": "o",
                "Ø": "O",
                "æ": "ae",
                "Æ": "Ae",
            }
        )
    )
    ascii_name = (
        unicodedata.normalize("NFKD", translated)
        .encode("ascii", "ignore")
        .decode("ascii")
        .casefold()
    )
    return tuple(re.findall(r"[a-z0-9]+", ascii_name))


def _match_target_player(
    target_team: str,
    source_name: str,
    target_index: Mapping[
        tuple[str, tuple[str, ...]], Sequence[OpeningPlayer]
    ],
) -> tuple[Optional[OpeningPlayer], str]:
    source_tokens = _name_tokens(source_name)
    exact = list(target_index.get((target_team, source_tokens), ()))
    if len(exact) == 1:
        return exact[0], "exact-normalised-full-name"
    if len(exact) > 1 or len(set(source_tokens)) < 2:
        return None, "unresolved"
    source_set = set(source_tokens)
    proposals = [
        player
        for (team, target_tokens), players in target_index.items()
        if team == target_team
        and len(set(target_tokens)) >= 2
        and (
            source_set <= set(target_tokens)
            or set(target_tokens) <= source_set
        )
        for player in players
    ]
    if len(proposals) == 1:
        return proposals[0], "unique-token-subset-full-name"
    return None, "unresolved"


def _fit_source_model(
    promotion_classes: Sequence[PromotionClass],
) -> tuple[Pipeline, Dict[str, Any]]:
    features: list[list[float]] = []
    outcomes: list[int] = []
    for promotion_class in promotion_classes:
        for bridged in promotion_class.bridged_players:
            for gameweek in TARGET_GAMEWEEKS:
                features.append(
                    _source_feature_vector(
                        bridged.source,
                        bridged.target.position,
                        gameweek,
                    )
                )
                outcomes.append(
                    int(bridged.target.minutes[gameweek - 1] > 0)
                )
    _require(
        features and len(set(outcomes)) == 2,
        "promoted-appearance.training-classes",
        "The prior promotion classes cannot fit a binary source model.",
    )
    model = Pipeline(
        steps=[
            ("scale", StandardScaler()),
            (
                "logistic",
                LogisticRegression(
                    C=LOGISTIC_PENALTY_INVERSE,
                    max_iter=1000,
                    random_state=RANDOM_SEED,
                ),
            ),
        ]
    )
    model.fit(features, outcomes)
    logistic = model.named_steps["logistic"]
    return model, {
        "implementation": "sklearn.linear_model.LogisticRegression",
        "penaltyInverse": LOGISTIC_PENALTY_INVERSE,
        "randomSeed": RANDOM_SEED,
        "trainingPromotionClasses": [
            (
                f"{value.source_season_code}-to-"
                f"{value.target_season_code}"
            )
            for value in promotion_classes
        ],
        "trainingPlayerCount": sum(
            len(value.bridged_players) for value in promotion_classes
        ),
        "trainingPlayerGameweekCount": len(features),
        "trainingAppearanceRate": _round(float(np.mean(outcomes))),
        "coefficientByStandardisedFeature": {
            feature: _round(float(coefficient))
            for feature, coefficient in zip(
                SOURCE_FEATURES,
                logistic.coef_[0],
                strict=True,
            )
        },
        "intercept": _round(float(logistic.intercept_[0])),
    }


def _source_feature_vector(
    source: PriorCompetitionPlayer,
    position: str,
    gameweek: int,
) -> list[float]:
    return [
        source.appearances / 46.0,
        source.starts / 46.0,
        source.minutes / float(46 * 90),
        source.minutes / float(max(source.appearances, 1) * 90),
        gameweek / float(TARGET_GAMEWEEKS[-1]),
        float(position == "goalkeeper"),
        float(position == "defender"),
        float(position == "midfielder"),
        float(position == "forward"),
    ]


def _incumbent_probabilities(
    scenarios: Mapping[str, Any],
) -> Dict[str, Dict[int, Dict[int, float]]]:
    targets = scenarios.get("targets")
    _require(
        isinstance(targets, list),
        "promoted-appearance.incumbent-scenarios",
        "The incumbent opening scenarios are unavailable.",
    )
    result: Dict[str, Dict[int, Dict[int, float]]] = {}
    for target in targets:
        season = str(target["targetSeasonCode"])
        result[season] = {
            int(player["playerCode"]): {
                int(gameweek["gameweek"]): float(
                    gameweek["appearanceProbability"]
                )
                for gameweek in player["gameweeks"]
            }
            for player in target["players"]
        }
    _require(
        set(result) == set(REGISTERED_SEASONS[1:]),
        "promoted-appearance.incumbent-targets",
        "The incumbent opening target seasons differ.",
    )
    return result


def _predict_target(
    model: Pipeline,
    promotion_class: PromotionClass,
    incumbent: Mapping[int, Mapping[int, float]],
) -> list[Dict[str, Any]]:
    predictions = []
    for bridged in promotion_class.bridged_players:
        _require(
            bridged.target.player_code in incumbent,
            "promoted-appearance.incumbent-player",
            f"Incumbent probabilities omit player "
            f"{bridged.target.player_code}.",
        )
        for gameweek in TARGET_GAMEWEEKS:
            source_probability = float(
                model.predict_proba(
                    [
                        _source_feature_vector(
                            bridged.source,
                            bridged.target.position,
                            gameweek,
                        )
                    ]
                )[0, 1]
            )
            incumbent_probability = float(
                incumbent[bridged.target.player_code][gameweek]
            )
            predictions.append(
                {
                    "targetSeasonCode": (
                        promotion_class.target_season_code
                    ),
                    "playerCode": bridged.target.player_code,
                    "position": bridged.target.position,
                    "gameweek": gameweek,
                    "actual": int(
                        bridged.target.minutes[gameweek - 1] > 0
                    ),
                    "incumbent": incumbent_probability,
                    "source": source_probability,
                    "challenger": (
                        BLEND_WEIGHT * incumbent_probability
                        + (1.0 - BLEND_WEIGHT) * source_probability
                    ),
                }
            )
    return predictions


def _target_document(
    promotion_class: PromotionClass,
    predictions: Sequence[Mapping[str, Any]],
    diagnostics: Mapping[str, Any],
) -> Dict[str, Any]:
    models = [
        {
            "name": name,
            "metrics": _probability_metrics(predictions, key),
            "positionSlices": _position_slices(predictions, key),
        }
        for name, key in (
            (INCUMBENT_MODEL, "incumbent"),
            (SOURCE_MODEL, "source"),
            (CHALLENGER_MODEL, "challenger"),
        )
    ]
    by_name = {str(value["name"]): value for value in models}
    return {
        "targetSeasonCode": promotion_class.target_season_code,
        "sourceSeasonCode": promotion_class.source_season_code,
        "evaluatedPlayerCount": len(
            {int(value["playerCode"]) for value in predictions}
        ),
        "evaluatedPlayerGameweekCount": len(predictions),
        "appearanceCount": sum(
            int(value["actual"]) for value in predictions
        ),
        "sourceModelDiagnostics": dict(diagnostics),
        "models": models,
        "challengerBrierDelta": _round(
            float(
                by_name[CHALLENGER_MODEL]["metrics"]["brierScore"]
            )
            - float(
                by_name[INCUMBENT_MODEL]["metrics"]["brierScore"]
            )
        ),
        "challengerWins": (
            float(by_name[CHALLENGER_MODEL]["metrics"]["brierScore"])
            < float(by_name[INCUMBENT_MODEL]["metrics"]["brierScore"])
        ),
    }


def _probability_metrics(
    predictions: Sequence[Mapping[str, Any]],
    key: str,
) -> Dict[str, float]:
    _require(
        bool(predictions),
        "promoted-appearance.metrics-empty",
        "Appearance metrics require prediction rows.",
    )
    actual = np.asarray(
        [float(value["actual"]) for value in predictions],
        dtype=float,
    )
    probability = np.asarray(
        [float(value[key]) for value in predictions],
        dtype=float,
    )
    clipped = np.clip(probability, 1e-9, 1.0 - 1e-9)
    return {
        "brierScore": _round(
            float(np.mean(np.square(probability - actual)))
        ),
        "logLoss": _round(
            float(
                np.mean(
                    -actual * np.log(clipped)
                    - (1.0 - actual) * np.log(1.0 - clipped)
                )
            )
        ),
        "calibrationError": _round(
            abs(float(np.mean(probability) - np.mean(actual)))
        ),
        "meanProbability": _round(float(np.mean(probability))),
        "appearanceRate": _round(float(np.mean(actual))),
    }


def _position_slices(
    predictions: Sequence[Mapping[str, Any]],
    key: str,
) -> Dict[str, Dict[str, float]]:
    positions = sorted({str(value["position"]) for value in predictions})
    return {
        position: _probability_metrics(
            [
                value
                for value in predictions
                if value["position"] == position
            ],
            key,
        )
        for position in positions
    }


def _screen(
    predictions: Sequence[Mapping[str, Any]],
    targets: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    incumbent = _probability_metrics(predictions, "incumbent")
    challenger = _probability_metrics(predictions, "challenger")
    incumbent_brier = float(incumbent["brierScore"])
    challenger_brier = float(challenger["brierScore"])
    brier_improvement = (
        (incumbent_brier - challenger_brier) / incumbent_brier
        if incumbent_brier > 0.0
        else 0.0
    )
    position_deltas = {}
    for position in sorted(
        {str(value["position"]) for value in predictions}
    ):
        rows = [
            value
            for value in predictions
            if value["position"] == position
        ]
        position_deltas[position] = _round(
            float(
                _probability_metrics(rows, "challenger")["brierScore"]
            )
            - float(
                _probability_metrics(rows, "incumbent")["brierScore"]
            )
        )
    bootstrap = _cluster_bootstrap_brier_delta(predictions)
    target_wins = sum(bool(value["challengerWins"]) for value in targets)
    gates = {
        "minimumBrierImprovement": (
            brier_improvement >= MINIMUM_BRIER_IMPROVEMENT_FRACTION
        ),
        "logLossNonRegression": (
            float(challenger["logLoss"])
            - float(incumbent["logLoss"])
            <= MAXIMUM_LOG_LOSS_DELTA
        ),
        "calibrationTolerance": (
            float(challenger["calibrationError"])
            - float(incumbent["calibrationError"])
            <= MAXIMUM_CALIBRATION_DELTA
        ),
        "positionBrierTolerance": (
            max(position_deltas.values())
            <= MAXIMUM_POSITION_BRIER_DELTA
        ),
        "minimumTargetWins": target_wins >= MINIMUM_TARGET_WINS,
        "bootstrapUpperBoundNonPositive": (
            float(bootstrap["upper95"]) <= 0.0
        ),
    }
    return {
        "passes": all(gates.values()),
        "gates": gates,
        "models": [
            {"name": INCUMBENT_MODEL, "metrics": incumbent},
            {"name": CHALLENGER_MODEL, "metrics": challenger},
        ],
        "brierImprovementFraction": _round(brier_improvement),
        "brierDelta": _round(challenger_brier - incumbent_brier),
        "logLossDelta": _round(
            float(challenger["logLoss"])
            - float(incumbent["logLoss"])
        ),
        "calibrationDelta": _round(
            float(challenger["calibrationError"])
            - float(incumbent["calibrationError"])
        ),
        "positionBrierDeltas": position_deltas,
        "targetSeasonWins": target_wins,
        "pairedPlayerClusterBootstrap": bootstrap,
    }


def _cluster_bootstrap_brier_delta(
    predictions: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    by_player: DefaultDict[tuple[str, int], list[float]] = defaultdict(
        list
    )
    for value in predictions:
        actual = float(value["actual"])
        by_player[
            (str(value["targetSeasonCode"]), int(value["playerCode"]))
        ].append(
            (float(value["challenger"]) - actual) ** 2
            - (float(value["incumbent"]) - actual) ** 2
        )
    cluster_deltas = np.asarray(
        [float(np.mean(values)) for values in by_player.values()],
        dtype=float,
    )
    _require(
        len(cluster_deltas) >= 2,
        "promoted-appearance.bootstrap-clusters",
        "At least two player clusters are required.",
    )
    generator = np.random.default_rng(RANDOM_SEED)
    samples = generator.choice(
        cluster_deltas,
        size=(BOOTSTRAP_REPLICATES, len(cluster_deltas)),
        replace=True,
    )
    means = np.mean(samples, axis=1)
    return {
        "unit": "target-season-player",
        "clusterCount": len(cluster_deltas),
        "replicateCount": BOOTSTRAP_REPLICATES,
        "randomSeed": RANDOM_SEED,
        "lower95": _round(float(np.quantile(means, 0.025))),
        "upper95": _round(float(np.quantile(means, 0.975))),
    }


def _promotion_class_document(
    value: PromotionClass,
) -> Dict[str, Any]:
    methods: DefaultDict[str, int] = defaultdict(int)
    for bridge in value.bridged_players:
        methods[bridge.bridge_method] += 1
    return {
        "sourceSeasonCode": value.source_season_code,
        "targetSeasonCode": value.target_season_code,
        "sourceKey": value.source_key,
        "sourceSnapshotId": value.source_snapshot_id,
        "sourceContentSha256": value.source_content_sha256,
        "sourceRowCount": value.source_row_count,
        "sourcePlayerCount": value.source_player_count,
        "promotedSourcePlayerCount": value.promoted_source_player_count,
        "promotedTargetPlayerCount": value.promoted_target_player_count,
        "bridgedPlayerCount": len(value.bridged_players),
        "targetCoverageFraction": _round(
            len(value.bridged_players)
            / float(value.promoted_target_player_count)
        ),
        "bridgeMethodCounts": dict(sorted(methods.items())),
        "bridgeIdentitySha256": _sha256(
            [
                {
                    "sourcePlayerId": bridge.source.source_player_id,
                    "targetPlayerCode": bridge.target.player_code,
                    "bridgeMethod": bridge.bridge_method,
                }
                for bridge in value.bridged_players
            ]
        ),
    }


def _fixed_screen_document() -> Dict[str, Any]:
    return {
        "primaryMetric": (
            "aggregate-matched-promoted-player-gameweek-brier-score"
        ),
        "minimumBrierImprovementFraction": (
            MINIMUM_BRIER_IMPROVEMENT_FRACTION
        ),
        "maximumLogLossDelta": MAXIMUM_LOG_LOSS_DELTA,
        "maximumCalibrationDelta": MAXIMUM_CALIBRATION_DELTA,
        "maximumPositionBrierDelta": MAXIMUM_POSITION_BRIER_DELTA,
        "minimumTargetWins": MINIMUM_TARGET_WINS,
        "uncertaintyGate": (
            "paired-target-season-player-cluster-bootstrap-"
            "95-percent-upper-brier-delta-at-most-zero"
        ),
        "bootstrapReplicates": BOOTSTRAP_REPLICATES,
    }


def _parse_extractions(values: Sequence[str]) -> Dict[str, Path]:
    result: Dict[str, Path] = {}
    for value in values:
        season, separator, path = value.partition("=")
        if (
            not separator
            or season not in SOURCE_SEASONS
            or not path
            or season in result
        ):
            raise TemporalRidgeError(
                "promoted-appearance.extraction-argument",
                "Each --fbref-extraction must be a unique "
                "registered-season=path pair.",
            )
        result[season] = Path(path)
    return result


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate prior-Championship playing time as an expanding-origin "
            "appearance challenger for promoted FPL players."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument(
        "--fbref-extraction",
        action="append",
        default=[],
        metavar="SEASON=PATH",
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_historical_promoted_appearance_evaluation(
            options.database,
            _parse_extractions(options.fbref_extraction),
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
