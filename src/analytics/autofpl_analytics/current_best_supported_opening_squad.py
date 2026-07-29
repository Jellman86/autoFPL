from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Optional, Sequence

from .current_appearance_hurdle_joint_scenarios import (
    build_current_appearance_hurdle_joint_scenarios,
)
from .current_selected_opening_squad import (
    BEST_SUPPORTED_ARTIFACT_VERSION,
    BEST_SUPPORTED_STATUS,
    SCHEMA_VERSION,
    _build_from_scenario,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _write_report,
)


def build_current_best_supported_opening_squad(
    database_path: Path,
) -> Dict[str, Any]:
    path = Path(database_path)
    scenario = build_current_appearance_hurdle_joint_scenarios(path)
    historical = scenario["variant"]["historicalEvaluation"]
    return _build_from_scenario(
        path,
        scenario,
        artifact_version=BEST_SUPPORTED_ARTIFACT_VERSION,
        status=BEST_SUPPORTED_STATUS,
        influences_advice=True,
        model_evaluation_source={
            "artifactVersion": historical["evaluatorVersion"],
            "dataIdentitySha256": historical["dataIdentitySha256"],
            "runIdentitySha256": historical["runIdentitySha256"],
        },
        additional_limitations=(
            (
                "This v2 candidate uses the retained appearance-hurdle "
                "point model. Its historical screen reused the development "
                "holdout and its current outcomes remain unavailable."
            ),
            (
                "The previous v1 selected squad remains immutable as a "
                "registered comparator; v2 is preferred only as the "
                "best-supported current prediction."
            ),
        ),
    )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Freeze the best-supported current appearance-hurdle opening "
            "squad and all eight role decisions."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_best_supported_opening_squad(
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
