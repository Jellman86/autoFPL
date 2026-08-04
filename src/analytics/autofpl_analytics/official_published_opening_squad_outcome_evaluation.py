from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

from .current_scenario_selection_score import _verified_document
from .initial_squad_outcome_evaluation import _score_actual
from .selected_opening_squad_outcome_evaluation import (
    ENGINE_VERSION,
    OUTCOME_GAMEWEEKS,
    _read_outcome,
    _selected_role_definition,
)
from .temporal_ridge import TemporalRidgeError, _sha256, _write_report

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "official-published-opening-squad-prospective-outcome-evaluation"
ARTIFACT_VERSION = (
    "official-published-opening-squad-prospective-outcome-evaluation-v1"
)
SOURCE_ARTIFACT_VERSION = "current-official-published-opening-squad-shadow-v1"
SOURCE_STATUS = "prospective-official-baseline-unscored"
SOURCE_KEY = "official-fpl-ep-next"


def build_official_published_opening_squad_outcome_evaluation(
    database_path: Path | str,
) -> Dict[str, Any]:
    path = Path(database_path)
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    connection: Optional[sqlite3.Connection] = None
    try:
        connection = sqlite3.connect(
            f"file:{path.resolve()}?mode=ro",
            uri=True,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only = ON;")
        source_row = connection.execute(
            """
            SELECT artifact.artifact_id,
                   artifact.official_capture_id,
                   artifact.source_bootstrap_sha256,
                   artifact.source_fixtures_sha256,
                   artifact.incumbent_run_identity_sha256,
                   artifact.producer_data_identity_sha256,
                   artifact.producer_run_identity_sha256,
                   artifact.document_json,
                   artifact.content_sha256,
                   capture.available_at_utc,
                   capture.next_gameweek_number,
                   capture.bootstrap_sha256,
                   capture.fixtures_sha256,
                   event.deadline_utc
            FROM official_published_opening_squad_artifacts AS artifact
            JOIN official_fpl_captures AS capture
              ON capture.capture_id = artifact.official_capture_id
            JOIN official_fpl_events AS event
              ON event.capture_id = capture.capture_id
             AND event.event_id = capture.next_gameweek_number
            WHERE capture.season_code = '2026-27'
              AND capture.next_gameweek_number = 1
              AND julianday(capture.available_at_utc)
                    <= julianday(event.deadline_utc)
            ORDER BY julianday(capture.available_at_utc) DESC,
                     artifact.official_capture_id DESC,
                     artifact.artifact_id DESC
            LIMIT 1;
            """
        ).fetchone()
        if source_row is None:
            raise TemporalRidgeError(
                "official-published.not-found",
                "No frozen official published opening squad is available.",
            )
        source = _verified_document(
            source_row["document_json"],
            source_row["content_sha256"],
            "official-published.content",
        )
        _require_source(source, source_row)

        outcome_rows = connection.execute(
            """
            SELECT outcome.outcome_capture_id,
                   outcome.gameweek,
                   outcome.reference_capture_id,
                   outcome.available_at_utc,
                   outcome.live_sha256,
                   outcome.live_json,
                   outcome.player_count,
                   event.deadline_utc
            FROM official_fpl_outcome_captures AS outcome
            JOIN official_fpl_events AS event
              ON event.capture_id = outcome.reference_capture_id
             AND event.event_id = outcome.gameweek
            WHERE outcome.season_code = ?
              AND outcome.gameweek BETWEEN 1 AND 8
              AND julianday(outcome.available_at_utc)
                    > julianday(event.deadline_utc)
            ORDER BY outcome.gameweek,
                     julianday(outcome.available_at_utc) DESC,
                     outcome.outcome_capture_id DESC;
            """,
            (str(source["seasonCode"]),),
        ).fetchall()
        latest_outcomes: Dict[int, sqlite3.Row] = {}
        for row in outcome_rows:
            latest_outcomes.setdefault(int(row["gameweek"]), row)
        if not latest_outcomes:
            raise TemporalRidgeError(
                "outcome.not-found",
                "No eligible official opening evaluation outcome is available.",
            )
        outcomes_by_gameweek = {
            gameweek: _read_outcome(connection, row)
            for gameweek, row in latest_outcomes.items()
        }
    except sqlite3.Error as exception:
        raise TemporalRidgeError(
            "database.read",
            "The official-published outcome inputs could not be read.",
        ) from exception
    finally:
        if connection is not None:
            connection.close()

    incumbent_roles = _selection_roles(source["incumbent"]["selection"])
    challenger_roles = _selection_roles(source["challenger"]["selection"])
    weekly = []
    incumbent_total = 0
    challenger_total = 0
    wins = 0
    ties = 0
    losses = 0
    outcome_sources = []
    for gameweek in sorted(outcomes_by_gameweek):
        outcome_source, outcomes = outcomes_by_gameweek[gameweek]
        incumbent_score = _score_actual(incumbent_roles[gameweek], outcomes)
        challenger_score = _score_actual(challenger_roles[gameweek], outcomes)
        incumbent_points = int(incumbent_score["totalPoints"])
        challenger_points = int(challenger_score["totalPoints"])
        delta = challenger_points - incumbent_points
        incumbent_total += incumbent_points
        challenger_total += challenger_points
        wins += delta > 0
        ties += delta == 0
        losses += delta < 0
        weekly.append(
            {
                "gameweek": gameweek,
                "incumbent": incumbent_score,
                "challenger": challenger_score,
                "challengerDelta": delta,
            }
        )
        outcome_sources.append(outcome_source)

    observed_gameweeks = [row["gameweek"] for row in weekly]
    is_complete = observed_gameweeks == list(OUTCOME_GAMEWEEKS)
    cumulative_delta = challenger_total - incumbent_total
    result: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": (
            "prospective-outcome-complete"
            if is_complete
            else "prospective-outcome-partial"
        ),
        "isPromotionDecision": False,
        "seasonCode": str(source["seasonCode"]),
        "openingGameweek": int(source["openingGameweek"]),
        "evaluationGameweeks": list(OUTCOME_GAMEWEEKS),
        "officialPublishedSource": {
            "artifactId": int(source_row["artifact_id"]),
            "artifactVersion": SOURCE_ARTIFACT_VERSION,
            "artifactContentSha256": str(source_row["content_sha256"]),
            "officialCaptureId": int(source_row["official_capture_id"]),
            "bootstrapSha256": str(source_row["source_bootstrap_sha256"]),
            "fixturesSha256": str(source_row["source_fixtures_sha256"]),
            "incumbentRunIdentitySha256": str(
                source_row["incumbent_run_identity_sha256"]
            ),
            "selectionOverlapPlayerCount": int(
                source["selectionChange"]["overlapPlayerCount"]
            ),
        },
        "outcomeSources": outcome_sources,
        "realisedScore": {
            "engineVersion": ENGINE_VERSION,
            "weekly": weekly,
            "cumulative": {
                "observedGameweekCount": len(observed_gameweeks),
                "incumbentPoints": incumbent_total,
                "challengerPoints": challenger_total,
                "challengerDelta": cumulative_delta,
                "challengerWeeklyWins": wins,
                "weeklyTies": ties,
                "challengerWeeklyLosses": losses,
            },
        },
        "evidenceStatus": {
            "status": (
                "complete-single-opening-period"
                if is_complete
                else "waiting-for-eight-outcomes"
            ),
            "requiredOutcomeGameweeks": list(OUTCOME_GAMEWEEKS),
            "observedOutcomeGameweeks": observed_gameweeks,
            "checks": {
                "allEightOutcomesAvailable": is_complete,
                "challengerOutscoredIncumbent": (
                    cumulative_delta > 0 if is_complete else None
                ),
                "challengerWonAtLeastAsManyWeeksAsItLost": (
                    wins >= losses if is_complete else None
                ),
            },
            "supportsPromotionReview": (
                is_complete and cumulative_delta > 0 and wins >= losses
            ),
            "isSufficientForAutomaticPromotion": False,
        },
        "limitations": [
            (
                "This scores one prospectively frozen opening period and "
                "cannot precisely estimate official expected-points value "
                "across seasons."
            ),
            (
                "The comparison holds both squads without transfers, chips "
                "or price changes and uses only their frozen preseason roles."
            ),
            (
                "A favourable complete result supports a promotion review; "
                "this artifact never changes served advice."
            ),
        ],
    }
    result["dataIdentitySha256"] = _sha256(
        {
            "outcomeLiveSha256": [row["liveSha256"] for row in outcome_sources],
            "officialPublishedArtifactContentSha256": result[
                "officialPublishedSource"
            ]["artifactContentSha256"],
        }
    )
    result["runIdentitySha256"] = _sha256(
        {
            "artifactVersion": ARTIFACT_VERSION,
            "dataIdentitySha256": result["dataIdentitySha256"],
        }
    )
    return result


def _selection_roles(
    selection: Mapping[str, Any],
) -> Dict[int, Dict[str, Any]]:
    player_ids = [int(value) for value in selection["playerIds"]]
    positions = {
        int(player["playerId"]): str(player["position"])
        for player in selection["players"]
    }
    roles = {
        int(row["gameweek"]): _selected_role_definition(
            player_ids,
            positions,
            row,
        )
        for row in selection["gameweeks"]
    }
    _require(
        set(roles) == set(OUTCOME_GAMEWEEKS),
        "official-published.selection-roles",
        "A frozen squad does not contain all registered weekly roles.",
    )
    return roles


def _require_source(source: Mapping[str, Any], row: sqlite3.Row) -> None:
    registration = source.get("prospectiveScoreRegistration", {})
    source_lineage = source.get("source", {})
    incumbent = source.get("incumbent", {})
    _require(
        source.get("artifactVersion") == SOURCE_ARTIFACT_VERSION
        and source.get("status") == SOURCE_STATUS
        and source.get("isPromoted") is False
        and source.get("influencesAdvice") is False
        and source.get("seasonCode") == "2026-27"
        and int(source.get("openingGameweek", 0)) == 1
        and int(source.get("officialCaptureId", 0)) == int(row["official_capture_id"])
        and source_lineage.get("sourceKey") == SOURCE_KEY
        and source_lineage.get("bootstrapSha256")
        == str(row["source_bootstrap_sha256"])
        == str(row["bootstrap_sha256"])
        and source_lineage.get("fixturesSha256")
        == str(row["source_fixtures_sha256"])
        == str(row["fixtures_sha256"])
        and source_lineage.get("availableAtUtc") == str(row["available_at_utc"])
        and source_lineage.get("deadlineUtc") == str(row["deadline_utc"])
        and source.get("decisionCutoffUtc") == str(row["available_at_utc"])
        and source.get("deadlineUtc") == str(row["deadline_utc"])
        and incumbent.get("sourceRunIdentitySha256")
        == str(row["incumbent_run_identity_sha256"])
        and source.get("dataIdentitySha256")
        == str(row["producer_data_identity_sha256"])
        and source.get("runIdentitySha256")
        == str(row["producer_run_identity_sha256"])
        and registration.get("outcomeGameweeks") == list(OUTCOME_GAMEWEEKS)
        and registration.get("squadMembership")
        == "fixed-opening-squad-no-transfers-for-all-eight-gameweeks"
        and registration.get("roles")
        == "all-eight-weeks-frozen-from-preseason-scenario-means"
        and registration.get("realisedScorer")
        == "exact-fpl-captain-fallback-and-ordered-auto-substitution"
        and registration.get("outcomeStatus")
        == "waiting-for-official-2026-27-outcomes"
        and source.get("decision")
        == "retain-as-prospective-official-baseline-only",
        "official-published.lineage",
        "The frozen official published squad lineage is inconsistent.",
    )


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Score the frozen official ep_next opening squad against its "
            "same-capture autoFPL incumbent."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        output = build_official_published_opening_squad_outcome_evaluation(
            options.database
        )
        _write_report(output, options.output)
    except TemporalRidgeError as exception:
        status = (
            "waiting-for-official-outcome"
            if exception.code == "outcome.not-found"
            else "failed"
        )
        print(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": status,
                    "errorCode": exception.code,
                },
                sort_keys=True,
            ),
            file=sys.stderr,
        )
        return 0 if status == "waiting-for-official-outcome" else 2
    print(
        json.dumps(
            {
                "schemaVersion": SCHEMA_VERSION,
                "status": output["status"],
                "observedGameweeks": output["evidenceStatus"][
                    "observedOutcomeGameweeks"
                ],
                "challengerDelta": output["realisedScore"]["cumulative"][
                    "challengerDelta"
                ],
                "supportsPromotionReview": output["evidenceStatus"][
                    "supportsPromotionReview"
                ],
                "outputFile": None if options.output is None else str(options.output),
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
