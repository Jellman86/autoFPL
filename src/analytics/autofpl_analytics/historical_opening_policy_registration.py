from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

from .current_multi_horizon_initial_squad import HORIZONS, POLICIES
from .historical_opening_scenario_reconstruction import (
    ARTIFACT_VERSION as SCENARIO_ARTIFACT_VERSION,
    STATUS as SCENARIO_STATUS,
    build_historical_opening_scenario_reconstruction,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "historical-opening-policy-evaluation-registration"
ARTIFACT_VERSION = "historical-opening-policy-evaluation-registration-v1"
STATUS = "registered-before-target-outcomes-opened"
COMMON_SCORING_GAMEWEEKS = tuple(range(1, 9))
REFERENCE_POLICY_KEY = "6-expected-points"
MINIMUM_MEAN_IMPROVEMENT_POINTS = 2.0
MINIMUM_TARGET_WINS = 2
MAXIMUM_WORST_TARGET_REGRESSION_POINTS = 2.0
POLICY_TIE_BREAK = (
    "higher-mean-then-higher-worst-target-then-expected-points-"
    "then-shorter-horizon-then-policy-key"
)


def build_historical_opening_policy_registration(
    database_path: Path,
) -> Dict[str, Any]:
    scenario = build_historical_opening_scenario_reconstruction(
        Path(database_path)
    )
    _require_scenario(scenario)
    policies = [
        {
            "evaluationPolicyKey": _policy_key(
                int(horizon),
                str(policy["policyKey"]),
            ),
            "horizonGameweeks": int(horizon),
            "optimizerPolicyKey": str(policy["policyKey"]),
            "cvarWeight": float(policy["cvarWeight"]),
        }
        for horizon in HORIZONS
        for policy in POLICIES
    ]
    targets = [
        {
            "targetSeasonCode": target["targetSeasonCode"],
            "targetCaptureId": target["targetCaptureId"],
            "latestPriorSeasonCode": target["latestPriorSeasonCode"],
            "playerCount": target["playerCount"],
            "scenarioCount": target["scenarioCount"],
            "forecastIdentitySha256": target[
                "forecastIdentitySha256"
            ],
            "scenarioContentSha256": target[
                "scenarioContentSha256"
            ],
        }
        for target in scenario["targets"]
    ]
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "targetOutcomesOpened": False,
        "scenarioSource": {
            "artifactVersion": scenario["artifactVersion"],
            "dataIdentitySha256": scenario["dataIdentitySha256"],
            "runIdentitySha256": scenario["runIdentitySha256"],
        },
        "evaluationTargets": targets,
        "registeredPolicies": policies,
        "commonOutcomeScoring": {
            "gameweeks": list(COMMON_SCORING_GAMEWEEKS),
            "squadMembership": (
                "fixed-opening-squad-no-transfers-for-all-eight-gameweeks"
            ),
            "rolesWithinOptimisationHorizon": (
                "use-policy-optimised-weekly-xi-captain-vice-and-bench"
            ),
            "rolesAfterOptimisationHorizon": (
                "choose-legal-maximum-preseason-scenario-mean-xi-and-"
                "captain-with-deterministic-bench-order"
            ),
            "realisedScorer": (
                "exact-fpl-captain-fallback-and-ordered-auto-substitution"
            ),
            "targetWeighting": "equal-weight-per-season",
            "primaryMetric": (
                "mean-realised-eight-gameweek-fpl-points-across-targets"
            ),
        },
        "selectionRule": {
            "referencePolicyKey": REFERENCE_POLICY_KEY,
            "leaderRanking": POLICY_TIE_BREAK,
            "minimumMeanImprovementOverReferencePoints": (
                MINIMUM_MEAN_IMPROVEMENT_POINTS
            ),
            "minimumTargetWinsOverReference": MINIMUM_TARGET_WINS,
            "maximumWorstTargetRegressionPoints": (
                MAXIMUM_WORST_TARGET_REGRESSION_POINTS
            ),
            "decision": (
                "select-the-ranked-leader-only-if-all-stability-gates-pass;"
                "otherwise-retain-the-predeclared-reference"
            ),
        },
        "prospectiveBoundary": {
            "isPromoted": False,
            "mayInfluenceAdvice": False,
            "nextRequirement": (
                "freeze-the-selected-policy-and-score-new-2026-27-outcomes"
            ),
        },
        "limitations": [
            (
                "Three target seasons provide limited policy-selection "
                "power. The stability gate deliberately resists choosing a "
                "six-way retrospective winner on mean alone."
            ),
            (
                "All policies are compared on the same realised eight-week "
                "period. Horizon changes opening squad construction, not the "
                "amount of outcome data awarded to a policy."
            ),
            (
                "The scenario source retains its documented historical "
                "fixture-proxy and missing opening-status limitations."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "scenarioSource": artifact["scenarioSource"],
            "evaluationTargets": artifact["evaluationTargets"],
            "registeredPolicies": artifact["registeredPolicies"],
            "commonOutcomeScoring": artifact["commonOutcomeScoring"],
            "selectionRule": artifact["selectionRule"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _policy_key(horizon: int, policy_key: str) -> str:
    return f"{horizon}-{policy_key}"


def _require_scenario(scenario: Mapping[str, Any]) -> None:
    if (
        scenario.get("status") != SCENARIO_STATUS
        or scenario.get("artifactVersion") != SCENARIO_ARTIFACT_VERSION
        or scenario.get("temporalDesign", {}).get(
            "targetPerformanceFieldsRead"
        )
        is not False
        or len(scenario.get("targets", ())) != 3
    ):
        raise TemporalRidgeError(
            "registration.scenario-source",
            "The historical scenario reconstruction is unsupported.",
        )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Freeze the historical opening-policy comparison before target "
            "outcomes are opened."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_historical_opening_policy_registration(
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
