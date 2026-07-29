from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence

from .current_appearance_hurdle_joint_scenarios import (
    build_current_appearance_hurdle_joint_scenarios,
)
from .current_appearance_hurdle_player_forecast import (
    HISTORICAL_EVALUATION_DATA_IDENTITY,
    HISTORICAL_EVALUATION_RUN_IDENTITY,
)
from .current_opening_squad_optimality_audit import (
    BOOTSTRAP_METHOD,
    BOOTSTRAP_REPLICATES,
    BOOTSTRAP_SEED,
    _build_from_scenario as _build_base_audit,
)
from .historical_appearance_hurdle_opening_evaluation import (
    ARTIFACT_VERSION as OPENING_EVALUATION_ARTIFACT_VERSION,
    DECISION_RETAIN as OPENING_EVALUATION_DECISION,
    RETAINED_DATA_IDENTITY as OPENING_EVALUATION_DATA_IDENTITY,
    RETAINED_RUN_IDENTITY as OPENING_EVALUATION_RUN_IDENTITY,
)
from .historical_appearance_hurdle_opening_distribution_evaluation import (
    ARTIFACT_VERSION as DISTRIBUTION_EVALUATION_ARTIFACT_VERSION,
    DECISION_RETAIN as DISTRIBUTION_EVALUATION_DECISION,
    RETAINED_DATA_IDENTITY as DISTRIBUTION_EVALUATION_DATA_IDENTITY,
    RETAINED_RUN_IDENTITY as DISTRIBUTION_EVALUATION_RUN_IDENTITY,
)
from .historical_appearance_hurdle_points_evaluation import (
    EVALUATOR_VERSION as POINT_EVALUATION_ARTIFACT_VERSION,
    HURDLE_MODEL,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
ARTIFACT_TYPE = "current-opening-squad-optimality-audit"
ARTIFACT_VERSION = "current-opening-squad-optimality-audit-v2"
VARIANT_KEY = "appearance-hurdle-points"


def build_current_appearance_hurdle_opening_optimality_audit(
    database_path: Path,
    *,
    bootstrap_replicates: int = BOOTSTRAP_REPLICATES,
) -> Dict[str, Any]:
    path = Path(database_path)
    scenario = build_current_appearance_hurdle_joint_scenarios(path)
    return _build_from_scenario(
        path,
        scenario,
        bootstrap_replicates=bootstrap_replicates,
    )


def _build_from_scenario(
    database_path: Path,
    scenario: Mapping[str, Any],
    *,
    bootstrap_replicates: int = BOOTSTRAP_REPLICATES,
) -> Dict[str, Any]:
    _require_hurdle_scenario(scenario)
    artifact = _build_base_audit(
        Path(database_path),
        scenario,
        bootstrap_replicates=bootstrap_replicates,
    )
    artifact["artifactVersion"] = ARTIFACT_VERSION
    artifact["modelVariant"] = {
        "variantKey": VARIANT_KEY,
        "pointModelKey": HURDLE_MODEL,
        "pointEvaluationSource": {
            "artifactVersion": POINT_EVALUATION_ARTIFACT_VERSION,
            "dataIdentitySha256": (
                HISTORICAL_EVALUATION_DATA_IDENTITY
            ),
            "runIdentitySha256": (
                HISTORICAL_EVALUATION_RUN_IDENTITY
            ),
        },
        "openingPolicyEvaluationSource": {
            "artifactVersion": OPENING_EVALUATION_ARTIFACT_VERSION,
            "dataIdentitySha256": OPENING_EVALUATION_DATA_IDENTITY,
            "runIdentitySha256": OPENING_EVALUATION_RUN_IDENTITY,
            "decision": OPENING_EVALUATION_DECISION,
        },
        "distributionEvaluationSource": {
            "artifactVersion": (
                DISTRIBUTION_EVALUATION_ARTIFACT_VERSION
            ),
            "dataIdentitySha256": (
                DISTRIBUTION_EVALUATION_DATA_IDENTITY
            ),
            "runIdentitySha256": (
                DISTRIBUTION_EVALUATION_RUN_IDENTITY
            ),
            "decision": DISTRIBUTION_EVALUATION_DECISION,
        },
    }
    artifact["source"]["modelVariant"] = artifact["modelVariant"]
    artifact["limitations"] = [
        (
            "The incumbent is globally optimal only for the declared "
            "six-Gameweek linear expected-points surrogate, the retained "
            "appearance-hurdle distributions and the current official "
            "constraints."
        ),
        (
            "The hurdle policy passed its complete historical opening "
            "screen, but those target outcomes were already opened. This "
            "audit supplies conditional current robustness, not prospective "
            "2026/27 promotion evidence."
        ),
        *artifact["limitations"][1:],
    ]
    artifact["dataIdentitySha256"] = _sha256(
        {
            "officialCaptureId": artifact["officialCaptureId"],
            "policy": artifact["policy"],
            "source": artifact["source"],
            "bootstrapMethod": BOOTSTRAP_METHOD,
            "bootstrapSeed": BOOTSTRAP_SEED,
            "bootstrapReplicates": bootstrap_replicates,
        }
    )
    artifact.pop("runIdentitySha256", None)
    artifact["runIdentitySha256"] = _sha256(artifact)
    return artifact


def _require_hurdle_scenario(scenario: Mapping[str, Any]) -> None:
    variant = scenario.get("variant", {})
    historical = variant.get("historicalEvaluation", {})
    if (
        variant.get("variantKey") != VARIANT_KEY
        or variant.get("pointModelKey") != HURDLE_MODEL
        or historical.get("evaluatorVersion")
        != POINT_EVALUATION_ARTIFACT_VERSION
        or historical.get("dataIdentitySha256")
        != HISTORICAL_EVALUATION_DATA_IDENTITY
        or historical.get("runIdentitySha256")
        != HISTORICAL_EVALUATION_RUN_IDENTITY
        or historical.get("decision")
        != "retain-appearance-hurdle-prospective-shadow"
    ):
        raise TemporalRidgeError(
            "hurdle-opening-audit.scenario",
            "The optimality audit requires the exact retained "
            "appearance-hurdle scenario lineage.",
        )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Audit conditional optimality and paired-path stability for "
            "the current appearance-hurdle opening squad."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = (
            build_current_appearance_hurdle_opening_optimality_audit(
                options.database
            )
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
