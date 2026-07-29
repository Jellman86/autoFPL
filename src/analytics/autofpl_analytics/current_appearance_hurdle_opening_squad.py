from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

import numpy as np

from .current_appearance_hurdle_joint_scenarios import (
    build_current_appearance_hurdle_joint_scenarios,
)
from .current_multi_horizon_initial_squad import (
    SELECTED_POLICY_KEY,
    _build_candidates,
    _build_from_scenario as build_multi_squad_from_scenario,
    _score_horizon,
    _week_matrices,
)
from .current_selected_opening_squad import (
    build_current_selected_opening_squad,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-appearance-hurdle-opening-squad-shadow"
ARTIFACT_VERSION = "current-appearance-hurdle-opening-squad-shadow-v1"
STATUS = "retained-challenger-prospective-shadow-unscored"
HORIZON_GAMEWEEKS = 6


def build_current_appearance_hurdle_opening_squad(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    scenario = build_current_appearance_hurdle_joint_scenarios(path)
    multi = build_multi_squad_from_scenario(path, scenario)
    selected = [
        policy
        for policy in multi["policies"]
        if bool(policy["isSelectedForProspectiveScoring"])
    ]
    _require(
        len(selected) == 1
        and selected[0]["evaluationPolicyKey"]
        == SELECTED_POLICY_KEY
        and int(selected[0]["horizonGameweeks"])
        == HORIZON_GAMEWEEKS,
        "hurdle-opening.selected-policy",
        "The hurdle comparison did not reproduce the selected policy.",
    )
    challenger = selected[0]
    incumbent = build_current_selected_opening_squad(path)
    _require(
        int(incumbent["officialCaptureId"])
        == int(scenario["officialCaptureId"])
        and incumbent["selectedPolicy"]["evaluationPolicyKey"]
        == SELECTED_POLICY_KEY,
        "hurdle-opening.incumbent",
        "The incumbent selected squad does not share the comparison cutoff.",
    )
    candidates = _build_candidates(path, scenario)
    week_matrices = _week_matrices(scenario)
    challenger_score = challenger["exactScenarioScore"]
    incumbent_score = _score_horizon(
        incumbent["selection"],
        candidates,
        week_matrices,
        HORIZON_GAMEWEEKS,
    )
    challenger_ids = {
        int(value) for value in challenger["selection"]["playerIds"]
    }
    incumbent_ids = {
        int(value) for value in incumbent["selection"]["playerIds"]
    }
    by_id = {
        int(player["playerId"]): player for player in candidates
    }
    challenger_values = np.asarray(
        challenger_score["pathTotalPoints"],
        dtype=float,
    )
    incumbent_values = np.asarray(
        incumbent_score["pathTotalPoints"],
        dtype=float,
    )
    _require(
        challenger_values.shape == incumbent_values.shape,
        "hurdle-opening.path-alignment",
        "The hurdle and incumbent scores are not paired.",
    )
    differences = challenger_values - incumbent_values
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
        "candidatePoolCount": len(candidates),
        "policy": {
            "evaluationPolicyKey": SELECTED_POLICY_KEY,
            "horizonGameweeks": HORIZON_GAMEWEEKS,
            "optimizerPolicyKey": challenger["policyKey"],
            "objective": challenger["objective"],
        },
        "incumbent": {
            "selection": incumbent["selection"],
            "exactScoreOnHurdleScenarios": incumbent_score,
            "sourceRunIdentitySha256": incumbent[
                "runIdentitySha256"
            ],
        },
        "challenger": {
            "selection": challenger["selection"],
            "solver": challenger["solver"],
            "exactScoreOnHurdleScenarios": challenger_score,
            "sourceMultiHorizonRunIdentitySha256": multi[
                "runIdentitySha256"
            ],
        },
        "selectionChange": {
            "overlapPlayerCount": len(
                incumbent_ids & challenger_ids
            ),
            "removedPlayers": [
                _player_identity(by_id[value])
                for value in sorted(incumbent_ids - challenger_ids)
            ],
            "addedPlayers": [
                _player_identity(by_id[value])
                for value in sorted(challenger_ids - incumbent_ids)
            ],
            "incumbentBudgetTenths": incumbent["selection"][
                "budgetTenths"
            ],
            "challengerBudgetTenths": challenger["selection"][
                "budgetTenths"
            ],
        },
        "pairedScenarioComparison": {
            "meanPointDifference": _round(
                float(np.mean(differences))
            ),
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
        "source": {
            "scenarioArtifactVersion": scenario["artifactVersion"],
            "scenarioRunIdentitySha256": scenario[
                "runIdentitySha256"
            ],
            "scenarioVariant": scenario["variant"],
            "historicalEvaluation": scenario["variant"][
                "historicalEvaluation"
            ],
        },
        "decision": (
            "retain-current-hurdle-opening-squad-for-prospective-comparison"
        ),
        "limitations": [
            (
                "The hurdle point model passed a reused historical holdout; "
                "the current squad remains prospectively unscored."
            ),
            (
                "The paired comparison is in-sample on hurdle-derived "
                "scenario paths and cannot promote the challenger."
            ),
            (
                "Gameweek 1 official availability is applied; later news, "
                "lineups, price changes and fixture revisions are not."
            ),
            (
                "The incumbent remains the selected served shadow until an "
                "explicit versioned handoff and outcome gate replaces it."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "policy": artifact["policy"],
            "incumbentSourceRunIdentitySha256": artifact[
                "incumbent"
            ]["sourceRunIdentitySha256"],
            "challengerSourceMultiHorizonRunIdentitySha256": artifact[
                "challenger"
            ]["sourceMultiHorizonRunIdentitySha256"],
            "source": artifact["source"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _player_identity(player: Mapping[str, Any]) -> Dict[str, Any]:
    return {
        "playerId": int(player["playerId"]),
        "webName": str(player["webName"]),
        "teamId": int(player["teamId"]),
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
            "Generate the exact six-Gameweek opening squad under the "
            "retained appearance-hurdle point shadow and compare it with "
            "the incumbent selected squad."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_appearance_hurdle_opening_squad(
            options.database
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
