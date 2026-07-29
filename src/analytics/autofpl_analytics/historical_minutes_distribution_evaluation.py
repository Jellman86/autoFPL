from __future__ import annotations

import argparse
import json
import math
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, DefaultDict, Dict, List, Mapping, Optional, Sequence

from .historical_conditional_minutes_evaluation import _aligned_gameweeks
from .historical_participation_evaluation import (
    _build_samples,
    _candidate_name,
    _load_capture,
    _predict_classifier,
    _validate,
)
from .historical_preseason_evaluation import (
    DEFAULT_SEASON,
    HOLDOUT_START_GAMEWEEK,
    MINIMUM_TRAINING_GAMEWEEKS,
    HistoricalCapture,
)
from .temporal_ridge import (
    Sample,
    TemporalRidgeError,
    _open_connection,
    _round,
    _sha256,
    _write_report,
)

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "historical-minutes-distribution-evaluation-v1"
CANDIDATE_MODEL = "appearance-hurdle-conditional-empirical-minutes"
PLAYER_EMPIRICAL_MODEL = "player-empirical-minutes"
PLAYER_LAST_MODEL = "player-last-minutes-degenerate"
POSITION_EMPIRICAL_MODEL = "position-empirical-minutes"
REFERENCE_MODELS = (
    PLAYER_EMPIRICAL_MODEL,
    PLAYER_LAST_MODEL,
    POSITION_EMPIRICAL_MODEL,
)
MINIMUM_CRPS_IMPROVEMENT = 0.01
MAXIMUM_POSITION_CRPS_REGRESSION = 0.05
CENTRAL_INTERVAL_PROBABILITY = 0.80


@dataclass(frozen=True)
class WeightedDistribution:
    values: tuple[float, ...]
    weights: tuple[float, ...]


@dataclass(frozen=True)
class DistributionPrediction:
    model: str
    gameweek: int
    player_id: int
    position: str
    distribution: WeightedDistribution
    actual: int


def evaluate_historical_minutes_distributions(
    database_path: Path,
    season_code: str = DEFAULT_SEASON,
    minimum_training_gameweeks: int = MINIMUM_TRAINING_GAMEWEEKS,
    evaluation_start_gameweek: int = HOLDOUT_START_GAMEWEEK,
) -> Dict[str, Any]:
    path = Path(database_path)
    _validate(
        path,
        season_code,
        minimum_training_gameweeks,
        evaluation_start_gameweek,
    )
    connection = _open_connection(path)
    try:
        capture = _load_capture(connection, season_code)
        base = _base(
            season_code,
            minimum_training_gameweeks,
            evaluation_start_gameweek,
            capture,
        )
        if capture is None:
            return _finish(
                base,
                "insufficient-data",
                "historical-season-archive-not-found",
                [],
                [],
                None,
            )
        samples = {
            "appearance": _build_samples(
                connection, capture, target_name="appearance"
            ),
            "minutes": _build_samples(
                connection, capture, target_name="minutes"
            ),
        }
    finally:
        connection.close()

    gameweeks = _aligned_gameweeks(samples)
    target_gameweeks = [
        gameweek
        for gameweek in gameweeks
        if gameweek >= evaluation_start_gameweek
        and len([prior for prior in gameweeks if prior < gameweek])
        >= minimum_training_gameweeks
    ]
    folds: List[Dict[str, Any]] = []
    predictions: List[DistributionPrediction] = []
    for target_gameweek in target_gameweeks:
        training_gameweeks = [
            gameweek for gameweek in gameweeks if gameweek < target_gameweek
        ]
        fold_predictions, diagnostics = _predict_fold(
            samples,
            target_gameweek,
            training_gameweeks,
        )
        predictions.extend(fold_predictions)
        folds.append(
            {
                "gameweek": target_gameweek,
                "trainingGameweeks": training_gameweeks,
                "targetPlayerCount": len(samples["minutes"][target_gameweek]),
                "models": [
                    _summarise_model(name, fold_predictions)
                    for name in (CANDIDATE_MODEL, *REFERENCE_MODELS)
                ],
                "diagnostics": diagnostics,
            }
        )
    if not folds:
        return _finish(
            base,
            "insufficient-data",
            "no-eligible-minutes-distribution-folds",
            [],
            [],
            None,
        )
    models = [
        _summarise_model(name, predictions)
        for name in (CANDIDATE_MODEL, *REFERENCE_MODELS)
    ]
    models.sort(
        key=lambda item: (item["metrics"]["meanCrps"], item["name"])
    )
    return _finish(
        base,
        "complete",
        None,
        folds,
        models,
        _comparison(models, folds),
    )


def _predict_fold(
    samples: Mapping[str, Mapping[int, Sequence[Sample]]],
    target_gameweek: int,
    training_gameweeks: Sequence[int],
) -> tuple[List[DistributionPrediction], Mapping[str, Any]]:
    appearance_training = [
        sample
        for gameweek in training_gameweeks
        for sample in samples["appearance"][gameweek]
    ]
    minutes_training = [
        sample
        for gameweek in training_gameweeks
        for sample in samples["minutes"][gameweek]
    ]
    appearance_target = samples["appearance"][target_gameweek]
    minutes_target = samples["minutes"][target_gameweek]
    appearance_predictions, diagnostics = _predict_classifier(
        appearance_training,
        appearance_target,
        _candidate_name("appearance"),
    )
    probabilities = {
        prediction.player_id: prediction.predicted
        for prediction in appearance_predictions
    }
    player_history: DefaultDict[int, List[int]] = defaultdict(list)
    player_conditional: DefaultDict[int, List[int]] = defaultdict(list)
    position_history: DefaultDict[str, List[int]] = defaultdict(list)
    position_conditional: DefaultDict[str, List[int]] = defaultdict(list)
    for sample in minutes_training:
        player_history[sample.player_id].append(sample.actual)
        position_history[sample.position].append(sample.actual)
        if sample.actual > 0:
            player_conditional[sample.player_id].append(sample.actual)
            position_conditional[sample.position].append(sample.actual)
    predictions: List[DistributionPrediction] = []
    fallback_count = 0
    for sample in minutes_target:
        history = player_history[sample.player_id]
        conditional = player_conditional[sample.player_id]
        if not history:
            history = position_history[sample.position]
            fallback_count += 1
        if not conditional:
            conditional = position_conditional[sample.position]
        if not history or not conditional:
            raise TemporalRidgeError(
                "evaluation.empty-minutes-distribution-history",
                "No player or position minutes history is available.",
            )
        probability = probabilities.get(sample.player_id)
        if probability is None:
            raise TemporalRidgeError(
                "evaluation.missing-appearance-distribution-probability",
                "A target player has no appearance probability.",
            )
        values = {
            CANDIDATE_MODEL: _hurdle_distribution(
                probability, conditional
            ),
            PLAYER_EMPIRICAL_MODEL: _empirical_distribution(history),
            PLAYER_LAST_MODEL: _empirical_distribution((history[-1],)),
            POSITION_EMPIRICAL_MODEL: _empirical_distribution(
                position_history[sample.position]
            ),
        }
        predictions.extend(
            DistributionPrediction(
                model=name,
                gameweek=target_gameweek,
                player_id=sample.player_id,
                position=sample.position,
                distribution=distribution,
                actual=sample.actual,
            )
            for name, distribution in values.items()
        )
    return predictions, {
        "appearance": diagnostics,
        "trainingRowCount": len(minutes_training),
        "appearancePositiveTrainingRowCount": sum(
            sample.actual > 0 for sample in minutes_training
        ),
        "playerHistoryFallbackCount": fallback_count,
    }


def _hurdle_distribution(
    appearance_probability: float,
    conditional_minutes: Sequence[int],
) -> WeightedDistribution:
    probability = float(appearance_probability)
    if (
        not math.isfinite(probability)
        or probability < 0.0
        or probability > 1.0
    ):
        raise TemporalRidgeError(
            "evaluation.invalid-minutes-appearance-probability",
            "Minutes distribution appearance probability must be bounded.",
        )
    values = tuple(float(value) for value in conditional_minutes)
    if not values or any(
        not math.isfinite(value) or value <= 0 for value in values
    ):
        raise TemporalRidgeError(
            "evaluation.invalid-conditional-minutes-support",
            "Conditional minutes support must contain positive finite values.",
        )
    return _weighted_distribution(
        (0.0, *values),
        (1.0 - probability, *(probability / len(values) for _ in values)),
    )


def _empirical_distribution(values: Sequence[int]) -> WeightedDistribution:
    support = tuple(float(value) for value in values)
    if not support:
        raise TemporalRidgeError(
            "evaluation.empty-minutes-empirical-distribution",
            "An empirical minutes distribution has no support.",
        )
    return _weighted_distribution(
        support, tuple(1.0 / len(support) for _ in support)
    )


def _weighted_distribution(
    values: Sequence[float], weights: Sequence[float]
) -> WeightedDistribution:
    support = tuple(float(value) for value in values)
    probabilities = tuple(float(weight) for weight in weights)
    if (
        not support
        or len(support) != len(probabilities)
        or any(not math.isfinite(value) for value in support)
        or any(
            not math.isfinite(weight) or weight < 0
            for weight in probabilities
        )
        or not math.isclose(sum(probabilities), 1.0, abs_tol=1e-12)
    ):
        raise TemporalRidgeError(
            "evaluation.invalid-weighted-minutes-distribution",
            "Weighted minutes support and probabilities are invalid.",
        )
    combined: DefaultDict[float, float] = defaultdict(float)
    for value, weight in zip(support, probabilities):
        combined[value] += weight
    ordered = sorted(combined.items())
    return WeightedDistribution(
        values=tuple(value for value, _ in ordered),
        weights=tuple(weight for _, weight in ordered),
    )


def _crps(distribution: WeightedDistribution, actual: float) -> float:
    first = sum(
        weight * abs(value - actual)
        for value, weight in zip(distribution.values, distribution.weights)
    )
    second = 0.5 * sum(
        left_weight * right_weight * abs(left - right)
        for left, left_weight in zip(
            distribution.values, distribution.weights
        )
        for right, right_weight in zip(
            distribution.values, distribution.weights
        )
    )
    return first - second


def _quantile(distribution: WeightedDistribution, probability: float) -> float:
    cumulative = 0.0
    for value, weight in zip(distribution.values, distribution.weights):
        cumulative += weight
        if cumulative + 1e-12 >= probability:
            return value
    return distribution.values[-1]


def _summarise_model(
    name: str, predictions: Sequence[DistributionPrediction]
) -> Dict[str, Any]:
    selected = [prediction for prediction in predictions if prediction.model == name]
    if not selected:
        raise TemporalRidgeError(
            "evaluation.missing-minutes-distribution-model",
            f"No predictions were available for {name}.",
        )
    return {
        "name": name,
        "metrics": _metrics(selected),
        "slices": {
            "position": {
                position: _metrics(items)
                for position, items in sorted(_by_position(selected).items())
            }
        },
    }


def _metrics(
    predictions: Sequence[DistributionPrediction],
) -> Dict[str, Any]:
    lower_probability = (1.0 - CENTRAL_INTERVAL_PROBABILITY) / 2.0
    upper_probability = 1.0 - lower_probability
    lowers = [
        _quantile(prediction.distribution, lower_probability)
        for prediction in predictions
    ]
    uppers = [
        _quantile(prediction.distribution, upper_probability)
        for prediction in predictions
    ]
    means = [
        sum(
            value * weight
            for value, weight in zip(
                prediction.distribution.values,
                prediction.distribution.weights,
            )
        )
        for prediction in predictions
    ]
    count = len(predictions)
    return {
        "count": count,
        "meanCrps": _round(
            sum(
                _crps(prediction.distribution, prediction.actual)
                for prediction in predictions
            )
            / count
        ),
        "meanError": _round(
            sum(
                mean - prediction.actual
                for mean, prediction in zip(means, predictions)
            )
            / count
        ),
        "central80Coverage": _round(
            sum(
                lower <= prediction.actual <= upper
                for lower, upper, prediction in zip(
                    lowers, uppers, predictions
                )
            )
            / count
        ),
        "central80MeanWidth": _round(
            sum(upper - lower for lower, upper in zip(lowers, uppers))
            / count
        ),
    }


def _by_position(
    predictions: Sequence[DistributionPrediction],
) -> Dict[str, List[DistributionPrediction]]:
    grouped: DefaultDict[str, List[DistributionPrediction]] = defaultdict(list)
    for prediction in predictions:
        grouped[prediction.position].append(prediction)
    return dict(grouped)


def _comparison(
    models: Sequence[Mapping[str, Any]],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    indexed = {str(model["name"]): model for model in models}
    candidate = indexed[CANDIDATE_MODEL]
    references: Dict[str, Any] = {}
    passes = True
    for reference_name in REFERENCE_MODELS:
        reference = indexed[reference_name]
        candidate_crps = float(candidate["metrics"]["meanCrps"])
        reference_crps = float(reference["metrics"]["meanCrps"])
        improvement = (
            (reference_crps - candidate_crps) / reference_crps
            if reference_crps > 0
            else 0.0
        )
        fold_wins = sum(
            _fold_crps(fold, CANDIDATE_MODEL)
            < _fold_crps(fold, reference_name)
            for fold in folds
        )
        position_regressions = {
            position: (
                float(candidate["slices"]["position"][position]["meanCrps"])
                - float(reference["slices"]["position"][position]["meanCrps"])
            )
            / float(reference["slices"]["position"][position]["meanCrps"])
            for position in candidate["slices"]["position"]
            if position in reference["slices"]["position"]
            and float(
                reference["slices"]["position"][position]["meanCrps"]
            )
            > 0
        }
        checks = {
            "minimumCrpsImprovement": (
                improvement >= MINIMUM_CRPS_IMPROVEMENT
            ),
            "majorityFoldWins": fold_wins > len(folds) / 2,
            "noMaterialPositionCrpsRegression": all(
                regression <= MAXIMUM_POSITION_CRPS_REGRESSION
                for regression in position_regressions.values()
            ),
        }
        reference_passes = all(checks.values())
        passes = passes and reference_passes
        references[reference_name] = {
            "crpsImprovementFraction": _round(improvement),
            "foldWins": fold_wins,
            "foldCount": len(folds),
            "positionCrpsRegressionFractions": {
                position: _round(regression)
                for position, regression in sorted(
                    position_regressions.items()
                )
            },
            "checks": checks,
            "passes": reference_passes,
        }
    return {
        "challenger": CANDIDATE_MODEL,
        "references": references,
        "fixedScreenPassed": passes,
        "decision": (
            "retain-for-prospective-minutes-distribution-shadow"
            if passes
            else "do-not-retain"
        ),
        "isPromoted": False,
    }


def _fold_crps(fold: Mapping[str, Any], name: str) -> float:
    model = next(item for item in fold["models"] if item["name"] == name)
    return float(model["metrics"]["meanCrps"])


def _base(
    season_code: str,
    minimum_training_gameweeks: int,
    evaluation_start_gameweek: int,
    capture: Optional[HistoricalCapture],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": (
            "exploratory-reused-holdout-candidate-screen-not-promotion"
        ),
        "isPromoted": False,
        "productImportReady": False,
        "target": "player-gameweek-minutes-distribution",
        "split": "expanding-gameweek-origin-reused-locked-holdout",
        "configuration": {
            "seasonCode": season_code,
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "evaluationStartGameweek": evaluation_start_gameweek,
            "appearanceModel": _candidate_name("appearance"),
            "conditionalSupport": (
                "player-positive-minutes-with-position-fallback"
            ),
            "references": list(REFERENCE_MODELS),
            "centralIntervalProbability": CENTRAL_INTERVAL_PROBABILITY,
            "retentionGate": {
                "minimumCrpsImprovementFraction": (
                    MINIMUM_CRPS_IMPROVEMENT
                ),
                "minimumFoldWins": "strict-majority-against-each-reference",
                "maximumPositionCrpsRegressionFraction": (
                    MAXIMUM_POSITION_CRPS_REGRESSION
                ),
                "promotionAllowed": False,
            },
        },
        "provenance": (
            None
            if capture is None
            else {
                "historicalCaptureId": capture.capture_id,
                "sourceRevision": capture.source_revision,
                "availableAtUtc": capture.available_at_utc,
                "playersSha256": capture.players_sha256,
                "gameweeksSha256": capture.gameweeks_sha256,
                "playerCount": capture.player_count,
                "playerGameweekRowCount": capture.player_gameweek_count,
            }
        ),
        "limitations": [
            "The evaluation folds were opened by earlier participation work.",
            "The archive lacks historical decision-time injury state.",
            "Conditional empirical support does not model tactical or "
            "substitution regime changes.",
            "Genuinely new 2026/27 folds are required before product import.",
        ],
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
    comparison: Optional[Mapping[str, Any]],
) -> Dict[str, Any]:
    report: Dict[str, Any] = {
        **base,
        "status": status,
        "reason": reason,
        "eligibleFoldCount": len(folds),
        "folds": list(folds),
        "models": list(models),
        "comparison": comparison,
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "provenance": report["provenance"],
            "foldGameweeks": [fold["gameweek"] for fold in folds],
            "target": report["target"],
        }
    )
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare an appearance-hurdle conditional empirical minutes "
            "distribution with fixed empirical references."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--season", default=DEFAULT_SEASON)
    parser.add_argument(
        "--minimum-training-gameweeks",
        type=int,
        default=MINIMUM_TRAINING_GAMEWEEKS,
    )
    parser.add_argument(
        "--evaluation-start-gameweek",
        type=int,
        default=HOLDOUT_START_GAMEWEEK,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_historical_minutes_distributions(
            options.database,
            season_code=options.season,
            minimum_training_gameweeks=options.minimum_training_gameweeks,
            evaluation_start_gameweek=options.evaluation_start_gameweek,
        )
        _write_report(report, options.output)
        return 0 if report["status"] == "complete" else 2
    except (TemporalRidgeError, OSError, ValueError) as exception:
        code = getattr(exception, "code", "evaluation.failed")
        sys.stderr.write(
            json.dumps(
                {
                    "schemaVersion": SCHEMA_VERSION,
                    "status": "error",
                    "errorCode": code,
                    "message": str(exception),
                },
                sort_keys=True,
            )
            + "\n"
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
