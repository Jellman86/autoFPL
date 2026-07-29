from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Optional, Sequence

from .current_appearance_hurdle_player_forecast import (
    ARTIFACT_VERSION as HURDLE_POINT_ARTIFACT_VERSION,
    build_current_appearance_hurdle_player_forecast,
)
from .current_joint_scenario_forecast import _retained_screen
from .current_multi_horizon_joint_scenarios import _build_from_artifacts
from .multi_season_player_forecast import CURRENT_SEASON
from .preseason_participation_forecast import (
    build_preseason_participation_forecast,
)
from .temporal_ridge import (
    TemporalRidgeError,
    _sha256,
    _write_report,
)


def build_current_appearance_hurdle_joint_scenarios(
    database_path: Path,
    season_code: str = CURRENT_SEASON,
) -> Dict[str, Any]:
    path = Path(database_path)
    point_forecast = build_current_appearance_hurdle_player_forecast(
        path,
        season_code,
    )
    participation_forecast = build_preseason_participation_forecast(
        path,
        season_code,
        1,
    )
    scenario = _build_from_artifacts(
        path,
        point_forecast,
        participation_forecast,
        _retained_screen(),
        allowed_point_artifact_versions=(
            HURDLE_POINT_ARTIFACT_VERSION,
        ),
    )
    scenario["variant"] = {
        "variantKey": "appearance-hurdle-points",
        "pointModelKey": point_forecast["modelKey"],
        "pointForecastArtifactVersion": point_forecast[
            "artifactVersion"
        ],
        "historicalEvaluation": point_forecast["training"][
            "historicalEvaluation"
        ],
    }
    scenario["runIdentitySha256"] = _rebuild_run_identity(scenario)
    return scenario


def _rebuild_run_identity(scenario: Dict[str, Any]) -> str:
    return _sha256(
        {
            key: value
            for key, value in scenario.items()
            if key != "runIdentitySha256"
        }
    )


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Build current Gameweek 1-8 joint paths from the retained "
            "appearance-hurdle point shadow."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=CURRENT_SEASON)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        artifact = build_current_appearance_hurdle_joint_scenarios(
            options.database,
            options.season,
        )
        _write_report(artifact, options.output)
        return 0
    except TemporalRidgeError as exception:
        sys.stderr.write(
            json.dumps(
                {
                    "schemaVersion": "1.0",
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
