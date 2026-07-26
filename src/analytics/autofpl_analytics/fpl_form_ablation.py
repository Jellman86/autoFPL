from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import replace
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

from .fpl_form_feature_table import (
    FplFormFeatureError,
    build_fpl_form_feature_table,
)
from .temporal_ridge import (
    BASELINE_NAMES,
    CONTINUOUS_FEATURES,
    MODEL_NAME as RIDGE_MODEL_NAME,
    RIDGE_PENALTY,
    Prediction,
    Sample,
    TemporalRidgeError,
    _build_table,
    _open_connection,
    _predict_fold,
    _samples_for_table,
    _sha256,
    _summarise_model,
    _write_report,
)
from .temporal_tree import (
    MODEL_NAME as TREE_MODEL_NAME,
    TREE_CONFIGURATION,
    _predict_tree,
    evaluate_temporal_tree,
)

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "fpl-form-feature-ablation-v1"
CONDITIONAL_RIDGE = "temporal-ridge+fpl-form-conditional"
CONDITIONAL_TREE = "hist-gradient-boosting+fpl-form-conditional"
ADJUSTED_RIDGE = "temporal-ridge+fpl-form-appearance-adjusted"
ADJUSTED_TREE = "hist-gradient-boosting+fpl-form-appearance-adjusted"
CONDITIONAL_FEATURES = (
    "fplFormPublishedConditionalPoints",
    "fplFormHasForecast",
)
ADJUSTED_FEATURES = CONDITIONAL_FEATURES + (
    "fplFormAppearanceAdjustedPoints",
    "fplFormHasCompleteAppearanceProbabilities",
)
MODEL_NAMES = (
    RIDGE_MODEL_NAME,
    TREE_MODEL_NAME,
    CONDITIONAL_RIDGE,
    CONDITIONAL_TREE,
    ADJUSTED_RIDGE,
    ADJUSTED_TREE,
    *BASELINE_NAMES,
)


def evaluate_fpl_form_ablation(
    database_path: Path,
    season_code: Optional[str] = None,
    minimum_training_gameweeks: int = 3,
    ridge_penalty: float = RIDGE_PENALTY,
) -> Dict[str, Any]:
    """Evaluate FPL Form features only on source-complete temporal folds."""
    if not math.isfinite(ridge_penalty) or ridge_penalty <= 0:
        raise TemporalRidgeError(
            "configuration.ridge-penalty",
            "The ridge penalty must be a positive finite number.",
        )
    path = Path(database_path)
    official_report = evaluate_temporal_tree(
        path,
        season_code=season_code,
        minimum_training_gameweeks=minimum_training_gameweeks,
    )
    base = _base_report(
        season_code,
        minimum_training_gameweeks,
        ridge_penalty,
        official_report,
    )
    if official_report["status"] != "complete":
        return _finish(
            base,
            "insufficient-data",
            "no-official-eligible-folds",
            [],
            [],
            [],
        )

    connection = _open_connection(path)
    predictions: List[Prediction] = []
    folds: List[Dict[str, Any]] = []
    exclusions: List[Dict[str, Any]] = []
    try:
        for official_fold in official_report["folds"]:
            prepared = _prepare_fold(
                path,
                connection,
                official_fold,
            )
            if prepared["status"] != "complete":
                exclusions.append(prepared)
                continue

            training = prepared["trainingSamples"]
            target = prepared["targetSamples"]
            fold_predictions, diagnostics = _predict_variants(
                training,
                target,
                ridge_penalty,
            )
            predictions.extend(fold_predictions)
            fold_models = [
                _summarise_model(name, fold_predictions)
                for name in MODEL_NAMES
            ]
            fold_models.sort(
                key=lambda model: (model["metrics"]["mae"], model["name"])
            )
            folds.append(
                {
                    "seasonCode": official_fold["seasonCode"],
                    "gameweek": official_fold["gameweek"],
                    "deadlineUtc": official_fold["deadlineUtc"],
                    "decisionCutoffUtc": official_fold[
                        "decisionCutoffUtc"
                    ],
                    "target": prepared["targetIdentity"],
                    "training": prepared["trainingIdentity"],
                    "trainingGameweeks": len(prepared["trainingIdentity"]),
                    "trainingRows": len(training),
                    "models": fold_models,
                    "diagnostics": diagnostics,
                }
            )
    finally:
        connection.close()

    if not folds:
        return _finish(
            base,
            "insufficient-data",
            "no-source-complete-expanding-origin-folds",
            [],
            [],
            exclusions,
        )
    models = [
        _summarise_model(name, predictions)
        for name in MODEL_NAMES
    ]
    models.sort(key=lambda model: (model["metrics"]["mae"], model["name"]))
    return _finish(
        base,
        "complete",
        None,
        folds,
        models,
        exclusions,
    )


def _prepare_fold(
    database_path: Path,
    connection: Any,
    official_fold: Mapping[str, Any],
) -> Dict[str, Any]:
    season_code = str(official_fold["seasonCode"])
    training_samples: List[Sample] = []
    training_identity: List[Dict[str, Any]] = []
    for training in official_fold["training"]:
        gameweek = int(training["gameweek"])
        source = build_fpl_form_feature_table(
            database_path,
            season_code,
            gameweek,
        )
        if source["status"] != "complete":
            return _excluded_fold(
                official_fold,
                "training-source-unavailable",
                gameweek,
                source,
            )
        table = _build_table(database_path, season_code, gameweek)
        official_samples = _samples_for_table(
            connection,
            table,
            int(training["outcomeCaptureId"]),
        )
        augmented = _augment_samples(official_samples, source)
        training_samples.extend(augmented)
        training_identity.append(
            {
                **training,
                "fplFormFeatureRunIdentitySha256": source[
                    "runIdentitySha256"
                ],
                "fplFormForecast": source["forecast"],
                "fplFormForecastPlayerCount": source[
                    "forecastPlayerCount"
                ],
            }
        )

    target_gameweek = int(official_fold["gameweek"])
    target_source = build_fpl_form_feature_table(
        database_path,
        season_code,
        target_gameweek,
    )
    if target_source["status"] != "complete":
        return _excluded_fold(
            official_fold,
            "target-source-unavailable",
            target_gameweek,
            target_source,
        )
    target_table = _build_table(
        database_path,
        season_code,
        target_gameweek,
    )
    target_official_samples = _samples_for_table(
        connection,
        target_table,
        int(official_fold["target"]["outcomeCaptureId"]),
    )
    target_samples = _augment_samples(
        target_official_samples,
        target_source,
    )
    target_identity = {
        **official_fold["target"],
        "fplFormFeatureRunIdentitySha256": target_source[
            "runIdentitySha256"
        ],
        "fplFormForecast": target_source["forecast"],
        "fplFormForecastPlayerCount": target_source[
            "forecastPlayerCount"
        ],
    }
    return {
        "status": "complete",
        "trainingSamples": training_samples,
        "targetSamples": target_samples,
        "trainingIdentity": training_identity,
        "targetIdentity": target_identity,
    }


def _excluded_fold(
    official_fold: Mapping[str, Any],
    reason: str,
    unavailable_gameweek: int,
    source: Mapping[str, Any],
) -> Dict[str, Any]:
    return {
        "status": "excluded",
        "seasonCode": official_fold["seasonCode"],
        "gameweek": official_fold["gameweek"],
        "reason": reason,
        "unavailableGameweek": unavailable_gameweek,
        "sourceReason": source["reason"],
        "officialDecisionCutoffUtc": source["decisionCutoffUtc"],
        "officialFeatureRunIdentitySha256": source[
            "officialFeatureRunIdentitySha256"
        ],
    }


def _augment_samples(
    samples: Sequence[Sample],
    source: Mapping[str, Any],
) -> List[Sample]:
    features_by_player = {
        int(player["playerId"]): player
        for player in source["players"]
    }
    if len(features_by_player) != int(source["officialPlayerCount"]):
        raise TemporalRidgeError(
            "data.fpl-form-player-coverage",
            "The source feature table does not cover the official population.",
        )
    augmented: List[Sample] = []
    for sample in samples:
        row = features_by_player.get(sample.player_id)
        if row is None:
            raise TemporalRidgeError(
                "data.fpl-form-player-coverage",
                "A labelled official player is absent from the source table.",
            )
        values = {
            **sample.features,
            "fplFormPublishedConditionalPoints": _optional_float(
                row["publishedConditionalPoints"]
            ),
            "fplFormHasForecast": float(bool(row["hasForecast"])),
            "fplFormAppearanceAdjustedPoints": _optional_float(
                row["appearanceAdjustedPoints"]
            ),
            "fplFormHasCompleteAppearanceProbabilities": float(
                bool(row["hasCompleteAppearanceProbabilities"])
            ),
        }
        augmented.append(replace(sample, features=values))
    return augmented


def _optional_float(value: Any) -> Optional[float]:
    return None if value is None else float(value)


def _predict_variants(
    training: Sequence[Sample],
    target: Sequence[Sample],
    ridge_penalty: float,
) -> Tuple[List[Prediction], Dict[str, Any]]:
    official_ridge, official_ridge_diagnostic = _predict_fold(
        training,
        target,
        ridge_penalty,
    )
    official_tree, official_tree_diagnostic = _predict_tree(
        training,
        target,
    )
    conditional_features = CONTINUOUS_FEATURES + CONDITIONAL_FEATURES
    conditional_ridge, conditional_ridge_diagnostic = _predict_fold(
        training,
        target,
        ridge_penalty,
        continuous_features=conditional_features,
        model_name=CONDITIONAL_RIDGE,
        include_baselines=False,
    )
    conditional_tree, conditional_tree_diagnostic = _predict_tree(
        training,
        target,
        continuous_features=conditional_features,
        model_name=CONDITIONAL_TREE,
    )
    adjusted_features = CONTINUOUS_FEATURES + ADJUSTED_FEATURES
    adjusted_ridge, adjusted_ridge_diagnostic = _predict_fold(
        training,
        target,
        ridge_penalty,
        continuous_features=adjusted_features,
        model_name=ADJUSTED_RIDGE,
        include_baselines=False,
    )
    adjusted_tree, adjusted_tree_diagnostic = _predict_tree(
        training,
        target,
        continuous_features=adjusted_features,
        model_name=ADJUSTED_TREE,
    )
    predictions = [
        *official_ridge,
        *official_tree,
        *conditional_ridge,
        *conditional_tree,
        *adjusted_ridge,
        *adjusted_tree,
    ]
    return predictions, {
        RIDGE_MODEL_NAME: official_ridge_diagnostic,
        TREE_MODEL_NAME: official_tree_diagnostic,
        CONDITIONAL_RIDGE: conditional_ridge_diagnostic,
        CONDITIONAL_TREE: conditional_tree_diagnostic,
        ADJUSTED_RIDGE: adjusted_ridge_diagnostic,
        ADJUSTED_TREE: adjusted_tree_diagnostic,
    }


def _base_report(
    season_code: Optional[str],
    minimum_training_gameweeks: int,
    ridge_penalty: float,
    official_report: Mapping[str, Any],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": "exploratory-source-ablation-not-promoted",
        "isPromoted": False,
        "target": "official-gameweek-total-points",
        "split": "source-complete-expanding-gameweek-origin",
        "candidateOfficialFoldCount": len(official_report["folds"]),
        "configuration": {
            "seasonCode": season_code,
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "ridgePenalty": ridge_penalty,
            "officialContinuousFeatures": list(CONTINUOUS_FEATURES),
            "conditionalSourceFeatures": list(CONDITIONAL_FEATURES),
            "appearanceAdjustedSourceFeatures": list(ADJUSTED_FEATURES),
            "ridgePreprocessing": (
                "training-fold median, missing indicators and z-score"
            ),
            "tree": dict(TREE_CONFIGURATION),
            "cohortRule": (
                "all variants use identical folds and labelled players; "
                "every training and target Gameweek requires a cutoff-safe "
                "source capture"
            ),
        },
        "availabilityRule": (
            "each source forecast must be captured no later than that "
            "Gameweek's official feature decision cutoff"
        ),
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
    exclusions: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    report = {
        **base,
        "status": status,
        "reason": reason,
        "eligibleFoldCount": len(folds),
        "excludedFoldCount": len(exclusions),
        "excludedFolds": list(exclusions),
        "folds": list(folds),
        "models": list(models),
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "candidateOfficialFoldCount": report[
                "candidateOfficialFoldCount"
            ],
            "excludedFolds": report["excludedFolds"],
            "folds": [
                {
                    "seasonCode": fold["seasonCode"],
                    "gameweek": fold["gameweek"],
                    "target": fold["target"],
                    "training": fold["training"],
                }
                for fold in folds
            ],
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Ablate cutoff-safe FPL Form forecasts on official model folds."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season")
    parser.add_argument(
        "--minimum-training-gameweeks",
        type=int,
        default=3,
    )
    parser.add_argument(
        "--ridge-penalty",
        type=float,
        default=RIDGE_PENALTY,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_fpl_form_ablation(
            options.database,
            season_code=options.season,
            minimum_training_gameweeks=options.minimum_training_gameweeks,
            ridge_penalty=options.ridge_penalty,
        )
        _write_report(report, options.output)
        return 0 if report["status"] == "complete" else 2
    except (FplFormFeatureError, TemporalRidgeError) as exception:
        error = {
            "schemaVersion": SCHEMA_VERSION,
            "status": "error",
            "errorCode": exception.code,
            "message": str(exception),
        }
        sys.stderr.write(json.dumps(error, sort_keys=True) + "\n")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
