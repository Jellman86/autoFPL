from __future__ import annotations

import argparse
import copy
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence

from .temporal_ridge import TemporalRidgeError, _sha256, _write_report

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "prior-competition-player-history-coverage"
ARTIFACT_VERSION = "prior-competition-player-history-coverage-v1"
SUPPORTED_FORECAST_TYPE = (
    "historical-preseason-participation-player-forecast"
)
SUPPORTED_FORECAST_VERSION = "preseason-participation-player-forecast-v1.2"
POSITIONS = frozenset(
    {"goalkeeper", "defender", "midfielder", "forward"}
)


def build_prior_competition_coverage(
    forecast_path: Path,
    prior_competition_clubs: Sequence[str],
) -> Dict[str, Any]:
    forecast = _load_forecast(Path(forecast_path))
    configured_clubs = _validate_clubs(
        prior_competition_clubs,
        forecast["players"],
    )
    gaps: list[Dict[str, Any]] = []
    team_rows: DefaultDict[str, list[Mapping[str, Any]]] = defaultdict(list)
    for player in forecast["players"]:
        team_rows[str(player["teamName"])].append(player)
        if player["priorSeasonIdentityStatus"] == "stable-code-match":
            continue
        source_scope = (
            "prior-competition-match-history"
            if player["teamName"] in configured_clubs
            else "external-or-new-player-match-history"
        )
        gaps.append(
            {
                "playerId": player["playerId"],
                "playerCode": player["playerCode"],
                "webName": player["webName"],
                "position": player["position"],
                "teamId": player["teamId"],
                "teamName": player["teamName"],
                "coverageRequirement": source_scope,
                "identityResolution": (
                    "explicit-current-official-code-to-source-player-id"
                ),
                "identityStatus": "unresolved",
                "historyStatus": "not-ingested",
            }
        )

    teams = []
    for team_name in sorted(team_rows):
        rows = team_rows[team_name]
        missing = sum(
            row["priorSeasonIdentityStatus"] == "no-prior-season-match"
            for row in rows
        )
        teams.append(
            {
                "teamId": rows[0]["teamId"],
                "teamName": team_name,
                "currentPlayerCount": len(rows),
                "archiveIdentityMatchCount": len(rows) - missing,
                "historyGapCount": missing,
                "priorCompetitionClub": team_name in configured_clubs,
            }
        )

    prior_competition_gap_count = sum(
        gap["coverageRequirement"] == "prior-competition-match-history"
        for gap in gaps
    )
    external_gap_count = len(gaps) - prior_competition_gap_count
    document: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": "exploratory-coverage-audit",
        "isPromoted": False,
        "influencesForecast": False,
        "seasonCode": forecast["seasonCode"],
        "gameweek": forecast["gameweek"],
        "deadlineUtc": forecast["deadlineUtc"],
        "decisionCutoffUtc": forecast["decisionCutoffUtc"],
        "sourceForecast": {
            "artifactType": forecast["artifactType"],
            "artifactVersion": forecast["artifactVersion"],
            "officialCaptureId": forecast["officialCaptureId"],
            "dataIdentitySha256": forecast["dataIdentitySha256"],
            "runIdentitySha256": forecast["runIdentitySha256"],
        },
        "configuration": {
            "priorCompetitionClubs": sorted(configured_clubs),
            "identityRule": (
                "an ingested source player must be explicitly mapped to one "
                "current official player.code; names are review evidence and "
                "never an automatic identity join"
            ),
            "historyUnit": "source-match-player-observation",
        },
        "summary": {
            "currentPlayerCount": forecast["playerCount"],
            "archiveIdentityMatchCount": (
                forecast["priorSeasonIdentityMatchCount"]
            ),
            "historyGapCount": len(gaps),
            "priorCompetitionHistoryRequiredCount": (
                prior_competition_gap_count
            ),
            "externalOrNewPlayerHistoryRequiredCount": external_gap_count,
            "archiveIdentityCoverageFraction": _round(
                forecast["priorSeasonIdentityMatchCount"]
                / forecast["playerCount"]
            ),
        },
        "requiredCaptureContract": {
            "capture": [
                "sourceKey",
                "competitionCode",
                "seasonCode",
                "canonicalUrl",
                "sourceRevision",
                "publishedAtUtc",
                "retrievedAtUtc",
                "availableAtUtc",
                "contentSha256",
                "collectorCodeSha",
            ],
            "playerIdentity": [
                "sourcePlayerId",
                "sourcePlayerName",
                "sourceTeamName",
                "currentOfficialPlayerCode",
                "mappingMethod",
                "mappingReviewedAtUtc",
            ],
            "matchObservation": [
                "sourceMatchId",
                "kickoffUtc",
                "teamName",
                "opponentTeamName",
                "competitionCode",
                "minutes",
                "started",
            ],
            "optionalMatchMetrics": [
                "goals",
                "assists",
                "shots",
                "expectedGoals",
                "expectedAssists",
                "saves",
                "cards",
            ],
            "chronologyRule": (
                "only content available by the target decision cutoff may "
                "enter a target artifact; match observations must precede "
                "that target"
            ),
            "missingnessRule": (
                "unavailable metrics remain null with field-level coverage; "
                "zero is an observed value and never a missing-value default"
            ),
        },
        "teams": teams,
        "playersRequiringHistory": sorted(
            gaps,
            key=lambda item: (
                item["teamName"],
                item["position"],
                item["webName"],
                item["playerCode"],
            ),
        ),
        "promotionBoundary": {
            "status": "blocked",
            "blockers": [
                "no-prior-competition-source-capture-ingested",
                "no-explicit-source-to-official-player-mappings",
                "no-identical-fold-out-of-time-ablation",
            ],
            "nextGate": (
                "ingest a bounded source capture, resolve exact player "
                "coverage, then compare the resulting history features on "
                "identical temporal folds before forecast use"
            ),
        },
    }
    document["dataIdentitySha256"] = _sha256(
        {
            "sourceForecast": document["sourceForecast"],
            "configuration": document["configuration"],
        }
    )
    document["runIdentitySha256"] = _sha256(document)
    return document


def _load_forecast(path: Path) -> Dict[str, Any]:
    if not path.is_file():
        raise TemporalRidgeError(
            "coverage.forecast-not-found",
            "The source forecast artifact does not exist.",
        )
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise TemporalRidgeError(
            "coverage.forecast-invalid",
            "The source forecast artifact is not valid UTF-8 JSON.",
        ) from exception
    if not isinstance(document, dict):
        raise TemporalRidgeError(
            "coverage.forecast-invalid",
            "The source forecast artifact must be a JSON object.",
        )
    if (
        document.get("artifactType") != SUPPORTED_FORECAST_TYPE
        or document.get("artifactVersion") != SUPPORTED_FORECAST_VERSION
    ):
        raise TemporalRidgeError(
            "coverage.forecast-contract",
            "The source forecast is not the frozen supported v1.2 artifact.",
        )
    _validate_forecast(document)
    return document


def _validate_forecast(document: Mapping[str, Any]) -> None:
    players = document.get("players")
    if not isinstance(players, list) or not players:
        _invalid("The source forecast has no player collection.")
    if document.get("playerCount") != len(players):
        _invalid("The source forecast player count is inconsistent.")
    identities: set[int] = set()
    codes: set[int] = set()
    team_ids_by_name: Dict[str, int] = {}
    team_names_by_id: Dict[int, str] = {}
    matched = 0
    for raw_player in players:
        if not isinstance(raw_player, dict):
            _invalid("A source forecast player is not an object.")
        player_id = _positive_int(raw_player.get("playerId"), "playerId")
        player_code = _positive_int(
            raw_player.get("playerCode"),
            "playerCode",
        )
        if player_id in identities or player_code in codes:
            _invalid("Source forecast player identities must be unique.")
        identities.add(player_id)
        codes.add(player_code)
        team_id = _positive_int(raw_player.get("teamId"), "teamId")
        for field in ("webName", "teamName"):
            if (
                not isinstance(raw_player.get(field), str)
                or not raw_player[field].strip()
                or len(raw_player[field]) > 100
            ):
                _invalid(f"Source forecast field '{field}' is invalid.")
        if raw_player.get("position") not in POSITIONS:
            _invalid("A source forecast player position is invalid.")
        team_name = str(raw_player["teamName"])
        if (
            team_name in team_ids_by_name
            and team_ids_by_name[team_name] != team_id
        ) or (
            team_id in team_names_by_id
            and team_names_by_id[team_id] != team_name
        ):
            _invalid("Source forecast team identities are inconsistent.")
        team_ids_by_name[team_name] = team_id
        team_names_by_id[team_id] = team_name
        identity_status = raw_player.get("priorSeasonIdentityStatus")
        if identity_status not in (
            "stable-code-match",
            "no-prior-season-match",
        ):
            _invalid("A prior-season identity status is invalid.")
        matched += identity_status == "stable-code-match"
    if (
        document.get("priorSeasonIdentityMatchCount") != matched
        or document.get("priorSeasonIdentityMissingCount")
        != len(players) - matched
    ):
        _invalid("The source forecast identity counts are inconsistent.")
    run_identity = document.get("runIdentitySha256")
    if not _sha(run_identity):
        _invalid("The source forecast run identity is invalid.")
    unsigned = copy.deepcopy(dict(document))
    unsigned.pop("runIdentitySha256", None)
    if _sha256(unsigned) != run_identity:
        raise TemporalRidgeError(
            "coverage.forecast-identity-mismatch",
            "The source forecast content does not match its run identity.",
        )
    if not _sha(document.get("dataIdentitySha256")):
        _invalid("The source forecast data identity is invalid.")
    for field in ("seasonCode", "deadlineUtc", "decisionCutoffUtc"):
        if not isinstance(document.get(field), str) or not document[field]:
            _invalid(f"The source forecast field '{field}' is invalid.")
    _positive_int(document.get("gameweek"), "gameweek")
    _positive_int(document.get("officialCaptureId"), "officialCaptureId")


def _validate_clubs(
    clubs: Sequence[str],
    players: Sequence[Mapping[str, Any]],
) -> frozenset[str]:
    normalized = []
    for club in clubs:
        if not isinstance(club, str) or not club.strip():
            raise TemporalRidgeError(
                "coverage.prior-competition-club-invalid",
                "Prior-competition club names must be non-empty strings.",
            )
        normalized.append(club.strip())
    if not normalized:
        raise TemporalRidgeError(
            "coverage.prior-competition-club-required",
            "At least one prior-competition club must be configured.",
        )
    if len(normalized) != len(set(normalized)):
        raise TemporalRidgeError(
            "coverage.prior-competition-club-duplicate",
            "Prior-competition club names must be unique.",
        )
    current_teams = {str(player["teamName"]) for player in players}
    unknown = sorted(set(normalized) - current_teams)
    if unknown:
        raise TemporalRidgeError(
            "coverage.prior-competition-club-unknown",
            "Configured prior-competition clubs are absent from the "
            f"official target: {', '.join(unknown)}.",
        )
    return frozenset(normalized)


def _positive_int(value: Any, field: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < 1:
        _invalid(f"Source forecast field '{field}' is invalid.")
    return value


def _sha(value: Any) -> bool:
    return (
        isinstance(value, str)
        and len(value) == 64
        and all(character in "0123456789abcdef" for character in value)
    )


def _invalid(message: str) -> None:
    raise TemporalRidgeError("coverage.forecast-invalid", message)


def _round(value: float) -> float:
    return round(float(value), 6)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Audit prior-season history gaps in one frozen current-player "
            "forecast and define the bounded source intake required to close "
            "them."
        )
    )
    parser.add_argument("--forecast", required=True, type=Path)
    parser.add_argument(
        "--prior-competition-club",
        action="append",
        required=True,
        dest="prior_competition_clubs",
    )
    parser.add_argument("--output", required=True, type=Path)
    options = parser.parse_args(arguments)
    try:
        document = build_prior_competition_coverage(
            options.forecast,
            options.prior_competition_clubs,
        )
        _write_report(document, options.output)
        return 0
    except (TemporalRidgeError, OSError, ValueError) as exception:
        code = getattr(exception, "code", "coverage.failed")
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
