from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

from .historical_appearance_hurdle_opening_evaluation import (
    SCENARIO_ARTIFACT_VERSION,
    build_historical_appearance_hurdle_opening_scenarios,
)
from .historical_opening_policy_data import (
    REGISTERED_SEASONS,
    _load_opening_fold,
    _required_capture,
)
from .historical_opening_policy_evaluation import (
    _evaluate_target,
    _select_policy,
)
from .historical_opening_policy_registration import (
    ARTIFACT_VERSION as REGISTRATION_ARTIFACT_VERSION,
    MAXIMUM_WORST_TARGET_REGRESSION_POINTS,
    MINIMUM_MEAN_IMPROVEMENT_POINTS,
    MINIMUM_TARGET_WINS,
    REFERENCE_POLICY_KEY,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _open_connection,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = (
    "historical-appearance-hurdle-multi-horizon-policy-evaluation"
)
ARTIFACT_VERSION = (
    "historical-appearance-hurdle-multi-horizon-policy-evaluation-v1"
)
STATUS = "retrospective-reused-outcomes-prospective-challenger-only"
REGISTRATION_DATA_IDENTITY = (
    "b9d28cac497af35fc0762b7080db7e369759678872f78d47135e050f1920b675"
)
REGISTRATION_RUN_IDENTITY = (
    "3a571e0813079b30fe721f48f670bc619252966ad949d8d1bfb0134771823b45"
)
HURDLE_SCENARIO_DATA_IDENTITY = (
    "589ca66b5add2d04d2380a9688db58508de1f3b179eeff057b9c427240384b2c"
)
HURDLE_SCENARIO_RUN_IDENTITY = (
    "691f24f902818d54227c2c7f9b69a0436ce36a91bdd46f94b92fdda42bbbce5f"
)
HURDLE_DISTRIBUTION_DATA_IDENTITY = (
    "af3e964f3f936b06f542ab6b19ecb996d2f1c97584298a5aff7877aa8837ada1"
)
HURDLE_DISTRIBUTION_RUN_IDENTITY = (
    "00808a1062b294785ebb5be506a7787198722598720d736436c969e003fb2234"
)


def build_historical_appearance_hurdle_multi_horizon_policy_evaluation(
    database_path: Path,
    registration_path: Path,
) -> Dict[str, Any]:
    registration = _load_registration(Path(registration_path))
    scenario = build_historical_appearance_hurdle_opening_scenarios(
        Path(database_path)
    )
    _require_hurdle_scenario(scenario)

    connection = _open_connection(Path(database_path))
    try:
        captures = tuple(
            _required_capture(connection, season)
            for season in REGISTERED_SEASONS
        )
        folds = {
            fold.target_capture.season_code: fold
            for target_index in range(1, len(captures))
            for fold in (
                _load_opening_fold(
                    connection,
                    captures,
                    target_index,
                    include_outcomes=True,
                ),
            )
        }
    finally:
        connection.close()

    targets = [
        _evaluate_target(
            target,
            folds[str(target["targetSeasonCode"])],
        )
        for target in scenario["targets"]
    ]
    policy_summary, selection = _select_policy(targets)
    selected_key = str(selection["selectedPolicyKey"])
    changed = selected_key != REFERENCE_POLICY_KEY
    artifact: Dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "artifactType": ARTIFACT_TYPE,
        "artifactVersion": ARTIFACT_VERSION,
        "status": STATUS,
        "researchStatus": "reused-opened-targets-no-promotion",
        "targetOutcomesOpened": True,
        "registrationSource": registration,
        "retainedDistributionSource": {
            "evaluationDataIdentitySha256": (
                HURDLE_DISTRIBUTION_DATA_IDENTITY
            ),
            "evaluationRunIdentitySha256": (
                HURDLE_DISTRIBUTION_RUN_IDENTITY
            ),
            "scenarioArtifactVersion": scenario["artifactVersion"],
            "scenarioDataIdentitySha256": scenario[
                "dataIdentitySha256"
            ],
            "scenarioRunIdentitySha256": scenario[
                "runIdentitySha256"
            ],
        },
        "commonOutcomeScoring": registration[
            "commonOutcomeScoring"
        ],
        "selectionRule": registration["selectionRule"],
        "targets": targets,
        "policySummary": policy_summary,
        "retrospectiveSelection": selection,
        "decision": {
            "referencePolicyKey": REFERENCE_POLICY_KEY,
            "prospectiveChallengerPolicyKey": selected_key,
            "prospectiveChallengerDiffersFromReference": changed,
            "decisionReason": (
                "retrospective-leader-passed-original-stability-gates-"
                "but-reused-outcomes-require-prospective-scoring"
                if changed
                else "original-reference-retained-by-stability-gates"
            ),
            "servedPolicyChanged": False,
        },
        "prospectiveBoundary": {
            "isPromoted": False,
            "mayInfluenceAdvice": False,
            "nextRequirement": (
                "generate-the-current-policy-candidate-and-score-it-on-"
                "new-2026-27-outcomes-without-reselection"
                if changed
                else "continue-prospective-scoring-of-the-reference-policy"
            ),
        },
        "limitations": [
            (
                "The same three target seasons were opened by the original "
                "policy experiment and later hurdle-model evaluations. A "
                "passing result can freeze a prospective challenger only."
            ),
            (
                "Three seasons provide weak policy-selection power. The "
                "original materiality, target-win and worst-regression gates "
                "remain unchanged."
            ),
            (
                "All policies hold one opening squad for eight Gameweeks. "
                "The experiment does not select transfers, chips or price "
                "change responses."
            ),
            (
                "Historical opening availability remains unavailable and "
                "target fixtures use the registered final-archive proxy."
            ),
        ],
    }
    artifact["dataIdentitySha256"] = _sha256(
        {
            "registrationSource": artifact["registrationSource"],
            "retainedDistributionSource": artifact[
                "retainedDistributionSource"
            ],
            "targets": artifact["targets"],
            "policySummary": artifact["policySummary"],
            "retrospectiveSelection": artifact[
                "retrospectiveSelection"
            ],
            "decision": artifact["decision"],
        }
    )
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _load_registration(path: Path) -> Dict[str, Any]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise TemporalRidgeError(
            "hurdle-horizon.registration-artifact",
            "The original policy registration artifact is unavailable.",
        ) from exception
    _require(
        document.get("artifactVersion") == REGISTRATION_ARTIFACT_VERSION
        and document.get("dataIdentitySha256")
        == REGISTRATION_DATA_IDENTITY
        and document.get("runIdentitySha256")
        == REGISTRATION_RUN_IDENTITY
        and document.get("targetOutcomesOpened") is False,
        "hurdle-horizon.registration-identity",
        "The original policy registration identity differs.",
    )
    rule = document.get("selectionRule", {})
    _require(
        rule.get("referencePolicyKey") == REFERENCE_POLICY_KEY
        and float(
            rule.get("minimumMeanImprovementOverReferencePoints")
        )
        == MINIMUM_MEAN_IMPROVEMENT_POINTS
        and int(rule.get("minimumTargetWinsOverReference"))
        == MINIMUM_TARGET_WINS
        and float(rule.get("maximumWorstTargetRegressionPoints"))
        == MAXIMUM_WORST_TARGET_REGRESSION_POINTS,
        "hurdle-horizon.registration-gates",
        "The original policy stability gates differ.",
    )
    return {
        "artifactVersion": str(document["artifactVersion"]),
        "dataIdentitySha256": str(document["dataIdentitySha256"]),
        "runIdentitySha256": str(document["runIdentitySha256"]),
        "registeredPolicies": document["registeredPolicies"],
        "commonOutcomeScoring": document["commonOutcomeScoring"],
        "selectionRule": document["selectionRule"],
    }


def _require_hurdle_scenario(scenario: Mapping[str, Any]) -> None:
    _require(
        scenario.get("artifactVersion") == SCENARIO_ARTIFACT_VERSION
        and scenario.get("dataIdentitySha256")
        == HURDLE_SCENARIO_DATA_IDENTITY
        and scenario.get("runIdentitySha256")
        == HURDLE_SCENARIO_RUN_IDENTITY
        and scenario.get("temporalDesign", {}).get(
            "targetPerformanceFieldsRead"
        )
        is False,
        "hurdle-horizon.scenario-identity",
        "The retained hurdle scenario identity differs.",
    )


def _require(condition: bool, code: str, message: str) -> None:
    if not condition:
        raise TemporalRidgeError(code, message)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Apply the originally registered 3/6/8-Gameweek policy matrix "
            "to the retained appearance-hurdle opening distributions."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument(
        "--registration",
        required=True,
        type=Path,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_historical_appearance_hurdle_multi_horizon_policy_evaluation(
            options.database,
            options.registration,
        )
        _write_report(artifact, options.output)
        return 0
    except TemporalRidgeError as exception:
        sys.stderr.write(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "error",
                    "error": str(exception),
                },
                sort_keys=True,
            )
            + "\n"
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
