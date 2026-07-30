from __future__ import annotations

import sqlite3
from collections import defaultdict
from dataclasses import dataclass
from typing import Any, DefaultDict, Dict, Mapping, Sequence

from .historical_preseason_evaluation import HistoricalCapture
from .multi_season_evaluation import (
    EWMA_ALPHA,
    FEATURES,
    Origin,
    _build_feature_table,
    _finite,
)
from .temporal_ridge import Sample, TemporalRidgeError

CREATIVE_METRICS = (
    "Bps",
    "Influence",
    "Creativity",
    "Threat",
)
CREATIVE_WINDOWS = (
    "prior",
    "rolling3",
    "rolling5",
    "ewma",
)
OFFICIAL_CREATIVE_FEATURES = tuple(
    f"{window}{metric}Mean"
    for window in CREATIVE_WINDOWS
    for metric in CREATIVE_METRICS
)
FEATURES_WITH_OFFICIAL_CREATIVE = (
    *FEATURES,
    *OFFICIAL_CREATIVE_FEATURES,
)


@dataclass(frozen=True)
class OfficialCreativeObservation:
    season_code: str
    season_index: int
    gameweek: int
    player_code: int
    bps: float
    influence: float
    creativity: float
    threat: float


def build_official_creative_feature_table(
    connection: sqlite3.Connection,
    captures: Sequence[HistoricalCapture],
) -> Dict[Origin, list[Sample]]:
    base = _build_feature_table(connection, captures)
    observations = _creative_observations_by_origin(
        connection,
        captures,
    )
    if set(base) != set(observations):
        raise TemporalRidgeError(
            "official-creative.origin-alignment",
            "Base and official creative feature origins differ.",
        )

    histories: DefaultDict[int, list[OfficialCreativeObservation]] = (
        defaultdict(list)
    )
    result: Dict[Origin, list[Sample]] = {}
    for origin in sorted(base):
        current = observations[origin]
        expected_ids = {sample.player_id for sample in base[origin]}
        if set(current) != expected_ids:
            raise TemporalRidgeError(
                "official-creative.player-alignment",
                "Base and official creative player cohorts differ.",
            )
        result[origin] = [
            add_official_creative_features(
                sample,
                histories.get(sample.player_id, ()),
            )
            for sample in base[origin]
        ]
        for player_code in sorted(current):
            histories[player_code].append(current[player_code])
    return result


def load_official_creative_histories(
    connection: sqlite3.Connection,
    captures: Sequence[HistoricalCapture],
) -> DefaultDict[int, list[OfficialCreativeObservation]]:
    by_origin = _creative_observations_by_origin(connection, captures)
    histories: DefaultDict[int, list[OfficialCreativeObservation]] = (
        defaultdict(list)
    )
    for origin in sorted(by_origin):
        for player_code in sorted(by_origin[origin]):
            histories[player_code].append(by_origin[origin][player_code])
    return histories


def add_official_creative_features(
    sample: Sample,
    history: Sequence[OfficialCreativeObservation],
) -> Sample:
    if tuple(sample.features) != FEATURES:
        raise TemporalRidgeError(
            "official-creative.base-feature-contract",
            "The base sample does not match the retained feature contract.",
        )
    features = dict(sample.features)
    summaries = {
        "prior": _summary(history),
        "rolling3": _summary(history[-3:]),
        "rolling5": _summary(history[-5:]),
        "ewma": _ewma(history),
    }
    for window in CREATIVE_WINDOWS:
        for metric in CREATIVE_METRICS:
            features[f"{window}{metric}Mean"] = summaries[window][
                metric.lower()
            ]
    if tuple(features) != FEATURES_WITH_OFFICIAL_CREATIVE:
        raise TemporalRidgeError(
            "official-creative.feature-contract",
            "The official creative feature order is not fixed.",
        )
    return Sample(
        season_code=sample.season_code,
        gameweek=sample.gameweek,
        player_id=sample.player_id,
        position=sample.position,
        features=features,
        actual=sample.actual,
    )


def _creative_observations_by_origin(
    connection: sqlite3.Connection,
    captures: Sequence[HistoricalCapture],
) -> Dict[Origin, Dict[int, OfficialCreativeObservation]]:
    by_origin: Dict[Origin, Dict[int, OfficialCreativeObservation]] = {}
    for season_index, capture in enumerate(captures):
        rows = connection.execute(
            """
            SELECT
                player_code,
                gameweek,
                SUM(bps) AS bps,
                SUM(CAST(influence AS REAL)) AS influence,
                SUM(CAST(creativity AS REAL)) AS creativity,
                SUM(CAST(threat AS REAL)) AS threat
            FROM historical_fpl_player_gameweeks
            WHERE capture_id = :capture_id
            GROUP BY player_code, gameweek
            ORDER BY gameweek, player_code;
            """,
            {"capture_id": capture.capture_id},
        ).fetchall()
        if len(rows) != _expected_origin_rows(connection, capture.capture_id):
            raise TemporalRidgeError(
                "official-creative.row-coverage",
                "Official creative aggregates have incomplete coverage.",
            )
        for row in rows:
            origin = Origin(
                season_index,
                int(row["gameweek"]),
                capture.season_code,
            )
            player_code = int(row["player_code"])
            players = by_origin.setdefault(origin, {})
            if player_code in players:
                raise TemporalRidgeError(
                    "official-creative.duplicate-player",
                    "An origin contains a duplicate creative player row.",
                )
            players[player_code] = OfficialCreativeObservation(
                season_code=capture.season_code,
                season_index=season_index,
                gameweek=origin.gameweek,
                player_code=player_code,
                bps=_finite(row["bps"]),
                influence=_finite(row["influence"]),
                creativity=_finite(row["creativity"]),
                threat=_finite(row["threat"]),
            )
    return by_origin


def _expected_origin_rows(
    connection: sqlite3.Connection,
    capture_id: int,
) -> int:
    row = connection.execute(
        """
        SELECT COUNT(*) AS count
        FROM (
            SELECT player_code, gameweek
            FROM historical_fpl_player_gameweeks
            WHERE capture_id = :capture_id
            GROUP BY player_code, gameweek
        );
        """,
        {"capture_id": capture_id},
    ).fetchone()
    return 0 if row is None else int(row["count"])


def _summary(
    rows: Sequence[OfficialCreativeObservation],
) -> Mapping[str, float | None]:
    if not rows:
        return {
            "bps": None,
            "influence": None,
            "creativity": None,
            "threat": None,
        }
    count = len(rows)
    return {
        "bps": sum(row.bps for row in rows) / count,
        "influence": sum(row.influence for row in rows) / count,
        "creativity": sum(row.creativity for row in rows) / count,
        "threat": sum(row.threat for row in rows) / count,
    }


def _ewma(
    rows: Sequence[OfficialCreativeObservation],
) -> Mapping[str, float | None]:
    if not rows:
        return _summary(rows)
    values: Dict[str, float] = {
        "bps": rows[0].bps,
        "influence": rows[0].influence,
        "creativity": rows[0].creativity,
        "threat": rows[0].threat,
    }
    for row in rows[1:]:
        for metric in values:
            values[metric] = (
                EWMA_ALPHA * float(getattr(row, metric))
                + (1.0 - EWMA_ALPHA) * values[metric]
            )
    return values
