from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

from .current_multi_horizon_initial_squad import (
    ARTIFACT_VERSION as MULTI_SQUAD_ARTIFACT_VERSION,
    SELECTED_POLICY_KEY,
    STATUS as MULTI_SQUAD_STATUS,
    _build_candidates,
    _build_from_scenario as build_multi_squad_from_scenario,
    _score_horizon,
    _week_matrices,
)
from .current_multi_horizon_joint_scenarios import (
    build_current_multi_horizon_joint_scenarios,
)
from .historical_opening_policy_evaluation import _complete_roles
from .temporal_ridge import (
    TemporalRidgeError,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-selected-opening-squad-shadow"
ARTIFACT_VERSION = "current-selected-opening-squad-shadow-v1"
STATUS = "prospective-shadow-unscored"
OUTCOME_GAMEWEEKS = tuple(range(1, 9))


def build_current_selected_opening_squad(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    scenario = build_current_multi_horizon_joint_scenarios(path)
    return _build_from_scenario(path, scenario)


def _build_from_scenario(
    database_path: Path,
    scenario: Mapping[str, Any],
) -> Dict[str, Any]:
    multi = build_multi_squad_from_scenario(
        Path(database_path),
        scenario,
    )
    _require_multi_squad(multi)
    selected = [
        policy
        for policy in multi["policies"]
        if bool(policy["isSelectedForProspectiveScoring"])
    ]
    if len(selected) != 1:
        raise TemporalRidgeError(
            "selected-opening.policy-count",
            "The current artifact does not select exactly one policy.",
        )
    policy = selected[0]
    if str(policy["evaluationPolicyKey"]) != SELECTED_POLICY_KEY:
        raise TemporalRidgeError(
            "selected-opening.policy-identity",
            "The current artifact selected an unsupported policy.",
        )
    candidates = _build_candidates(Path(database_path), scenario)
    week_matrices = _week_matrices(scenario)
    completed = _complete_roles(
        policy["selection"],
        candidates,
        week_matrices,
        int(policy["horizonGameweeks"]),
    )
    score = _score_horizon(
        completed,
        candidates,
        week_matrices,
        len(OUTCOME_GAMEWEEKS),
    )
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "isPromoted": False,
        "influencesAdvice": False,
        "seasonCode": multi["seasonCode"],
        "openingGameweek": multi["openingGameweek"],
        "deadlineUtc": multi["deadlineUtc"],
        "decisionCutoffUtc": multi["decisionCutoffUtc"],
        "officialCaptureId": multi["officialCaptureId"],
        "candidatePoolCount": multi["candidatePoolCount"],
        "scenarioCount": multi["scenarioCount"],
        "selectedPolicy": {
            "evaluationPolicyKey": policy["evaluationPolicyKey"],
            "horizonGameweeks": policy["horizonGameweeks"],
            "optimizerPolicyKey": policy["policyKey"],
            "objective": policy["objective"],
            "solver": policy["solver"],
            "retrospectiveEvaluationSource": multi[
                "selectedProspectivePolicy"
            ]["retrospectiveEvaluationSource"],
        },
        "selection": {
            "playerIds": completed["playerIds"],
            "budgetTenths": completed["budgetTenths"],
            "players": completed["players"],
            "gameweeks": completed["gameweeks"],
        },
        "prospectiveScoreRegistration": {
            "outcomeGameweeks": list(OUTCOME_GAMEWEEKS),
            "squadMembership": (
                "fixed-opening-squad-no-transfers-for-all-eight-gameweeks"
            ),
            "roles": (
                "all-eight-weeks-frozen-from-preseason-scenario-means"
            ),
            "realisedScorer": (
                "exact-fpl-captain-fallback-and-ordered-auto-substitution"
            ),
            "outcomeStatus": "waiting-for-official-2026-27-outcomes",
        },
        "preseasonScenarioScore": score,
        "source": {
            "artifactVersion": multi["artifactVersion"],
            "dataIdentitySha256": multi["dataIdentitySha256"],
            "runIdentitySha256": multi["runIdentitySha256"],
            "scenarioSource": multi["scenarioSource"],
        },
        "limitations": [
            (
                "This is the retrospectively selected policy applied to "
                "current preseason evidence. It is unscored prospectively "
                "and cannot influence served advice."
            ),
            (
                "All eight weekly role decisions are frozen now so later "
                "outcomes cannot change the evaluated policy."
            ),
            (
                "The squad is held without transfers, chips or price changes "
                "for the registered eight-Gameweek evaluation."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "selectedPolicy": artifact["selectedPolicy"],
            "selection": artifact["selection"],
            "prospectiveScoreRegistration": artifact[
                "prospectiveScoreRegistration"
            ],
            "source": artifact["source"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _require_multi_squad(multi: Mapping[str, Any]) -> None:
    if (
        multi.get("artifactVersion") != MULTI_SQUAD_ARTIFACT_VERSION
        or multi.get("status") != MULTI_SQUAD_STATUS
        or bool(multi.get("isPromoted"))
        or bool(multi.get("influencesAdvice"))
        or multi.get("recommendedPolicyKey") != SELECTED_POLICY_KEY
    ):
        raise TemporalRidgeError(
            "selected-opening.multi-squad-source",
            "The current multi-horizon source is unsupported.",
        )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Freeze the retrospectively selected current opening squad and "
            "all eight preseason role decisions for prospective scoring."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_selected_opening_squad(options.database)
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
