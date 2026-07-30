from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import math
import sqlite3
import sys
from collections import Counter
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

import brotli

from .historical_opening_policy_data import (
    EXPECTED_CAPTURE_IDENTITIES,
    REGISTERED_SEASONS,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-scoring-component-reconstruction-audit"
AUDITOR_VERSION = "historical-scoring-component-reconstruction-audit-v1"
REQUIRED_COLUMNS = frozenset(
    {
        "element",
        "GW",
        "fixture",
        "minutes",
        "total_points",
        "goals_scored",
        "assists",
        "clean_sheets",
        "goals_conceded",
        "saves",
        "bonus",
        "yellow_cards",
        "red_cards",
        "own_goals",
        "penalties_missed",
        "penalties_saved",
    }
)
POSITION_CODES = {
    "goalkeeper": "GKP",
    "defender": "DEF",
    "midfielder": "MID",
    "forward": "FWD",
}
GOAL_POINTS = {
    "GKP": 6,
    "DEF": 6,
    "MID": 5,
    "FWD": 4,
}
GOALKEEPER_TEN_POINT_SEASONS = frozenset({"2024-25", "2025-26"})
DEFENSIVE_CONTRIBUTION_SEASONS = frozenset({"2025-26"})
MINIMUM_EXACT_RECONSTRUCTION_FRACTION = 0.999


def audit_historical_scoring_components(database_path: Path) -> Dict[str, Any]:
    path = Path(database_path)
    if not path.is_file():
        raise TemporalRidgeError(
            "scoring-reconstruction.database-not-found",
            "The SQLite database does not exist.",
        )

    connection = _open_read_only(path)
    try:
        season_reports = [
            _audit_season(connection, season)
            for season in REGISTERED_SEASONS
        ]
    finally:
        connection.close()

    row_count = sum(report["rowCount"] for report in season_reports)
    exact_count = sum(
        report["exactReconstructionCount"] for report in season_reports
    )
    absolute_error = sum(
        report["absoluteErrorPoints"] for report in season_reports
    )
    squared_error = sum(
        report["squaredErrorPoints"] for report in season_reports
    )
    exact_fraction = exact_count / row_count if row_count else 0.0
    passed = (
        row_count > 0
        and exact_fraction >= MINIMUM_EXACT_RECONSTRUCTION_FRACTION
        and all(report["passed"] for report in season_reports)
    )
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "auditorVersion": AUDITOR_VERSION,
        "status": "complete",
        "registeredSeasonCodes": list(REGISTERED_SEASONS),
        "scoringRules": {
            "appearance": {
                "oneToFiftyNineMinutes": 1,
                "sixtyOrMoreMinutes": 2,
            },
            "goalPointsByPosition": {
                "goalkeeperBefore2024-25": 6,
                "goalkeeperFrom2024-25": 10,
                "defender": 6,
                "midfielder": 5,
                "forward": 4,
            },
            "assistPoints": 3,
            "cleanSheetPointsByPosition": {
                "goalkeeper": 4,
                "defender": 4,
                "midfielder": 1,
                "forward": 0,
            },
            "goalConcededDeduction": {
                "positions": ["goalkeeper", "defender"],
                "pointsPerTwoGoals": -1,
            },
            "savePoints": {"pointsPerThreeSaves": 1},
            "penaltySavePoints": 5,
            "penaltyMissPoints": -2,
            "yellowCardPoints": -1,
            "redCardPoints": -3,
            "ownGoalPoints": -2,
            "bonusPoints": "archived-final-fixture-value",
            "defensiveContributionFrom2025-26": {
                "points": 2,
                "defenderThreshold": 10,
                "midfielderAndForwardThreshold": 12,
                "capPerFixture": 2,
            },
        },
        "aggregate": {
            "rowCount": row_count,
            "exactReconstructionCount": exact_count,
            "exactReconstructionFraction": _round(exact_fraction),
            "mae": _round(absolute_error / row_count),
            "rmse": _round(math.sqrt(squared_error / row_count)),
        },
        "seasonAudits": season_reports,
        "acceptanceGate": {
            "minimumExactReconstructionFraction": (
                MINIMUM_EXACT_RECONSTRUCTION_FRACTION
            ),
            "allRegisteredSeasonsRequired": True,
            "passed": passed,
        },
        "decision": (
            "retain-scoring-reconstruction-as-event-model-target"
            if passed
            else "repair-scoring-reconstruction-before-event-model"
        ),
        "isPromoted": False,
        "influencesAdvice": False,
        "limitations": [
            (
                "This is a deterministic outcome reconstruction audit, not "
                "a predictive model and not evidence that any event component "
                "can yet be forecast accurately."
            ),
            (
                "Final archived bonus and assist awards are treated as "
                "observed scoring components; future models must predict their "
                "uncertainty without using post-kickoff information."
            ),
            (
                "Season-specific rule changes are explicit. A future-season "
                "forecast must bind to the rules applicable at its decision "
                "cutoff instead of silently inheriting this audit."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "registeredSeasonCodes": artifact["registeredSeasonCodes"],
            "seasonAudits": [
                {
                    "seasonCode": report["seasonCode"],
                    "sourceRevision": report["sourceRevision"],
                    "playersSha256": report["playersSha256"],
                    "gameweeksSha256": report["gameweeksSha256"],
                    "rowCount": report["rowCount"],
                    "residualCounts": report["residualCounts"],
                }
                for report in season_reports
            ],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _audit_season(
    connection: sqlite3.Connection,
    season_code: str,
) -> Dict[str, Any]:
    capture = connection.execute(
        """
        SELECT
            capture_id,
            source_revision,
            players_sha256,
            gameweeks_sha256,
            player_count,
            player_gameweek_count,
            gameweeks_csv_brotli
        FROM historical_fpl_season_captures
        WHERE season_code = :season_code;
        """,
        {"season_code": season_code},
    ).fetchall()
    _require(
        len(capture) == 1,
        "scoring-reconstruction.capture-cardinality",
        f"The registered {season_code} capture is not unique.",
    )
    row = capture[0]
    actual_identity = {
        "sourceRevision": row["source_revision"],
        "playersSha256": row["players_sha256"],
        "gameweeksSha256": row["gameweeks_sha256"],
        "playerCount": row["player_count"],
        "playerGameweekCount": row["player_gameweek_count"],
    }
    _require(
        actual_identity == EXPECTED_CAPTURE_IDENTITIES[season_code],
        "scoring-reconstruction.capture-identity",
        f"The registered {season_code} capture identity differs.",
    )
    try:
        payload = brotli.decompress(bytes(row["gameweeks_csv_brotli"]))
    except brotli.error as exception:
        raise TemporalRidgeError(
            "scoring-reconstruction.raw-brotli",
            f"The {season_code} raw Gameweek payload is invalid.",
        ) from exception
    _require(
        hashlib.sha256(payload).hexdigest() == row["gameweeks_sha256"],
        "scoring-reconstruction.raw-sha256",
        f"The {season_code} raw Gameweek payload hash differs.",
    )

    identities = {
        int(identity["season_element_id"]): str(identity["position"])
        for identity in connection.execute(
            """
            SELECT season_element_id, position
            FROM historical_fpl_players
            WHERE capture_id = :capture_id;
            """,
            {"capture_id": row["capture_id"]},
        )
    }
    reader = csv.DictReader(io.StringIO(payload.decode("utf-8-sig")))
    fieldnames = frozenset(reader.fieldnames or ())
    missing = REQUIRED_COLUMNS - fieldnames
    _require(
        not missing,
        "scoring-reconstruction.raw-columns",
        f"The {season_code} payload is missing: {', '.join(sorted(missing))}.",
    )
    if season_code in DEFENSIVE_CONTRIBUTION_SEASONS:
        _require(
            "defensive_contribution" in fieldnames,
            "scoring-reconstruction.defensive-contribution-column",
            f"The {season_code} payload lacks defensive contributions.",
        )

    residuals: Counter[int] = Counter()
    component_totals: Counter[str] = Counter()
    position_counts: Counter[str] = Counter()
    seen_rows: Dict[tuple[int, int, int], tuple[int, Dict[str, int]]] = {}
    duplicate_count = 0
    row_count = 0
    exact_count = 0
    absolute_error = 0
    squared_error = 0
    for raw in reader:
        element_id = _integer(raw, "element")
        position = identities.get(element_id)
        if position is None:
            continue
        position_code = POSITION_CODES[position]
        component_points = _component_points(
            season_code,
            position_code,
            raw,
        )
        actual = _integer(raw, "total_points")
        identity = (
            element_id,
            _integer(raw, "GW"),
            _integer(raw, "fixture"),
        )
        existing = seen_rows.get(identity)
        if existing is not None:
            _require(
                existing == (actual, component_points),
                "scoring-reconstruction.duplicate-conflict",
                f"The {season_code} payload has a conflicting duplicate row.",
            )
            duplicate_count += 1
            continue
        seen_rows[identity] = (actual, component_points)
        reconstructed = sum(component_points.values())
        residual = actual - reconstructed
        residuals[residual] += 1
        component_totals.update(component_points)
        position_counts[position] += 1
        row_count += 1
        exact_count += int(residual == 0)
        absolute_error += abs(residual)
        squared_error += residual * residual

    _require(
        row_count == row["player_gameweek_count"],
        "scoring-reconstruction.row-count",
        f"The {season_code} raw and persisted player-fixture counts differ.",
    )
    exact_fraction = exact_count / row_count
    return {
        "seasonCode": season_code,
        "sourceRevision": row["source_revision"],
        "playersSha256": row["players_sha256"],
        "gameweeksSha256": row["gameweeks_sha256"],
        "rowCount": row_count,
        "exactDuplicateRawRowCount": duplicate_count,
        "positionRowCounts": dict(sorted(position_counts.items())),
        "exactReconstructionCount": exact_count,
        "exactReconstructionFraction": _round(exact_fraction),
        "absoluteErrorPoints": absolute_error,
        "squaredErrorPoints": squared_error,
        "mae": _round(absolute_error / row_count),
        "rmse": _round(math.sqrt(squared_error / row_count)),
        "residualCounts": {
            str(residual): count
            for residual, count in sorted(residuals.items())
        },
        "componentPointTotals": dict(sorted(component_totals.items())),
        "passed": (
            exact_fraction >= MINIMUM_EXACT_RECONSTRUCTION_FRACTION
        ),
    }


def _component_points(
    season_code: str,
    position: str,
    row: Mapping[str, str],
) -> Dict[str, int]:
    minutes = _integer(row, "minutes")
    goal_points = dict(GOAL_POINTS)
    if season_code in GOALKEEPER_TEN_POINT_SEASONS:
        goal_points["GKP"] = 10
    clean_sheet_points = {
        "GKP": 4,
        "DEF": 4,
        "MID": 1,
        "FWD": 0,
    }
    result = {
        "appearance": 2 if minutes >= 60 else 1 if minutes > 0 else 0,
        "goals": _integer(row, "goals_scored") * goal_points[position],
        "assists": _integer(row, "assists") * 3,
        "cleanSheets": (
            _integer(row, "clean_sheets")
            * clean_sheet_points[position]
        ),
        "goalsConceded": (
            -(_integer(row, "goals_conceded") // 2)
            if position in {"GKP", "DEF"}
            else 0
        ),
        "saves": (
            _integer(row, "saves") // 3 if position == "GKP" else 0
        ),
        "penaltySaves": _integer(row, "penalties_saved") * 5,
        "penaltyMisses": -_integer(row, "penalties_missed") * 2,
        "yellowCards": -_integer(row, "yellow_cards"),
        "redCards": -_integer(row, "red_cards") * 3,
        "ownGoals": -_integer(row, "own_goals") * 2,
        "bonus": _integer(row, "bonus"),
        "defensiveContributions": 0,
    }
    if season_code in DEFENSIVE_CONTRIBUTION_SEASONS:
        contributions = _integer(row, "defensive_contribution")
        threshold = 10 if position == "DEF" else 12
        if position != "GKP" and contributions >= threshold:
            result["defensiveContributions"] = 2
    return result


def _integer(row: Mapping[str, str], name: str) -> int:
    try:
        return int(row[name])
    except (KeyError, TypeError, ValueError) as exception:
        raise TemporalRidgeError(
            "scoring-reconstruction.raw-integer",
            f"The raw {name} value is not an integer.",
        ) from exception


def _open_read_only(path: Path) -> sqlite3.Connection:
    connection = sqlite3.connect(
        f"file:{path.resolve()}?mode=ro",
        uri=True,
    )
    connection.row_factory = sqlite3.Row
    return connection


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Audit exact historical FPL point reconstruction from archived "
            "fixture-level scoring components."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = audit_historical_scoring_components(options.database)
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
