from __future__ import annotations

import argparse
import hashlib
import json
import math
import sqlite3
import statistics
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, DefaultDict, Dict, List, Mapping, Optional, Sequence, Tuple

from .baseline import (
    EvaluationError,
    PairedGameweek,
    _load_target_pairs,
    _open_read_only,
    _require_schema,
)
from .feature_table import FeatureTableError, build_feature_table

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "temporal-ridge-v1"
MODEL_NAME = "temporal-ridge"
RIDGE_PENALTY = 10.0
POSITIONS = ("goalkeeper", "defender", "midfielder", "forward")
BASELINE_NAMES = (
    "zero-points",
    "position-expanding-mean",
    "player-last-points",
    "official-running-mean",
)
CONTINUOUS_FEATURES = (
    "priceTenths",
    "selectedByPercent",
    "chanceNextRound",
    "targetFixtureCount",
    "homeFixtureRate",
    "restDaysBeforeFirstKickoff",
    "minimumRestDaysWithinGameweek",
    "cumulativePointsPerPriorGameweek",
    "cumulativeMinutesPerPriorGameweek",
    "cumulativeStartsPerPriorGameweek",
    "historySampleCount",
    "historyRolling3TotalPointsMean",
    "historyRolling3MinutesMean",
    "historyRolling3StartsMean",
    "historyRolling3PlayedRate",
    "historyRolling3Played60Rate",
    "historyRolling3GoalsScoredMean",
    "historyRolling3AssistsMean",
    "historyRolling3CleanSheetsMean",
    "historyRolling3BonusMean",
    "historyEwmaTotalPointsMean",
    "historyEwmaMinutesMean",
    "historyEwmaStartsMean",
    "historyEwmaPlayedRate",
    "historyEwmaPlayed60Rate",
    "historyEwmaGoalsScoredMean",
    "historyEwmaAssistsMean",
    "historyEwmaCleanSheetsMean",
    "historyEwmaBonusMean",
    "teamRolling3GoalsForMean",
    "teamRolling3GoalsAgainstMean",
    "teamRolling3PointsPerMatch",
    "teamRolling3CleanSheetRate",
    "teamRolling3ScoredRate",
    "opponentRolling3GoalsForMean",
    "opponentRolling3GoalsAgainstMean",
    "opponentRolling3PointsPerMatch",
    "opponentRolling3CleanSheetRate",
    "opponentRolling3ScoredRate",
)


class TemporalRidgeError(Exception):
    """Raised when an honest temporal challenger cannot be evaluated."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


@dataclass(frozen=True)
class Sample:
    season_code: str
    gameweek: int
    player_id: int
    position: str
    features: Mapping[str, Optional[float]]
    actual: int


@dataclass(frozen=True)
class Prediction:
    model: str
    season_code: str
    gameweek: int
    player_id: int
    position: str
    predicted: float
    actual: int


@dataclass(frozen=True)
class FittedTransform:
    medians: Tuple[float, ...]
    means: Tuple[float, ...]
    scales: Tuple[float, ...]
    feature_names: Tuple[str, ...]
    missing_counts: Tuple[int, ...]
    zero_variance_features: Tuple[str, ...]


def evaluate_temporal_ridge(
    database_path: Path,
    season_code: Optional[str] = None,
    minimum_training_gameweeks: int = 3,
    ridge_penalty: float = RIDGE_PENALTY,
) -> Dict[str, Any]:
    """Evaluate a fixed ridge challenger with expanding temporal origins."""
    path = Path(database_path)
    _validate_configuration(
        path,
        season_code,
        minimum_training_gameweeks,
        ridge_penalty,
    )
    connection = _open_connection(path)
    try:
        target_pairs = _load_target_pairs(connection, season_code)
        base = _base_report(
            season_code,
            minimum_training_gameweeks,
            ridge_penalty,
            len(target_pairs),
        )
        if not target_pairs:
            return _finish_report(
                base,
                "insufficient-data",
                "no-complete-replay-outcome-pairs",
                [],
                [],
            )

        predictions: List[Prediction] = []
        folds: List[Dict[str, Any]] = []
        for target_pair in target_pairs:
            target_table = _build_table(
                path,
                target_pair.season_code,
                target_pair.gameweek,
            )
            history_captures = target_table["provenance"][
                "historyOutcomeCaptures"
            ]
            training_samples: List[Sample] = []
            training_identity: List[Dict[str, Any]] = []
            for capture in history_captures:
                try:
                    training_table = _build_table(
                        path,
                        target_pair.season_code,
                        int(capture["gameweek"]),
                    )
                except TemporalRidgeError as exception:
                    if exception.code == "data.target-replay-not-found":
                        continue
                    raise
                samples = _samples_for_table(
                    connection,
                    training_table,
                    int(capture["outcomeCaptureId"]),
                )
                training_samples.extend(samples)
                training_identity.append(
                    {
                        "seasonCode": target_pair.season_code,
                        "gameweek": capture["gameweek"],
                        "featureReplayCaptureId": training_table[
                            "provenance"
                        ]["replayCaptureId"],
                        "featureRunIdentitySha256": training_table[
                            "runIdentitySha256"
                        ],
                        "outcomeCaptureId": capture["outcomeCaptureId"],
                        "outcomeAvailableAtUtc": capture["availableAtUtc"],
                        "playerCount": len(samples),
                    }
                )
            if len(training_identity) < minimum_training_gameweeks:
                continue

            target_samples = _samples_for_table(
                connection,
                target_table,
                target_pair.outcome_capture_id,
            )
            fold_predictions, diagnostic = _predict_fold(
                training_samples,
                target_samples,
                ridge_penalty,
            )
            predictions.extend(fold_predictions)
            fold_models = [
                _summarise_model(name, fold_predictions)
                for name in (MODEL_NAME, *BASELINE_NAMES)
            ]
            fold_models.sort(
                key=lambda model: (model["metrics"]["mae"], model["name"])
            )
            folds.append(
                {
                    "seasonCode": target_pair.season_code,
                    "gameweek": target_pair.gameweek,
                    "deadlineUtc": target_pair.deadline_utc,
                    "decisionCutoffUtc": target_table["decisionCutoffUtc"],
                    "target": {
                        "featureReplayCaptureId": target_table["provenance"][
                            "replayCaptureId"
                        ],
                        "featureRunIdentitySha256": target_table[
                            "runIdentitySha256"
                        ],
                        "outcomeCaptureId": target_pair.outcome_capture_id,
                        "outcomeAvailableAtUtc": (
                            target_pair.outcome_available_at_utc
                        ),
                        "playerCount": len(target_samples),
                    },
                    "training": training_identity,
                    "trainingGameweeks": len(training_identity),
                    "trainingRows": len(training_samples),
                    "models": fold_models,
                    "diagnostics": diagnostic,
                }
            )

        if not folds:
            return _finish_report(
                base,
                "insufficient-data",
                "no-eligible-expanding-origin-folds",
                [],
                [],
            )
        models = [
            _summarise_model(name, predictions)
            for name in (MODEL_NAME, *BASELINE_NAMES)
        ]
        models.sort(key=lambda model: (model["metrics"]["mae"], model["name"]))
        return _finish_report(base, "complete", None, folds, models)
    finally:
        connection.close()


def _validate_configuration(
    path: Path,
    season_code: Optional[str],
    minimum_training_gameweeks: int,
    ridge_penalty: float,
) -> None:
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found",
            "The SQLite database does not exist.",
        )
    if season_code is not None and (
        not season_code.strip() or len(season_code) > 16
    ):
        raise TemporalRidgeError(
            "configuration.season-code",
            "season_code must be a non-empty value of at most 16 characters.",
        )
    if minimum_training_gameweeks < 1:
        raise TemporalRidgeError(
            "configuration.minimum-training-gameweeks",
            "minimum_training_gameweeks must be at least one.",
        )
    if not math.isfinite(ridge_penalty) or ridge_penalty <= 0:
        raise TemporalRidgeError(
            "configuration.ridge-penalty",
            "ridge_penalty must be a finite positive number.",
        )


def _open_connection(path: Path) -> sqlite3.Connection:
    try:
        connection = _open_read_only(path)
        _require_schema(connection)
        return connection
    except EvaluationError as exception:
        raise TemporalRidgeError(exception.code, str(exception)) from exception


def _build_table(path: Path, season_code: str, gameweek: int) -> Dict[str, Any]:
    try:
        return build_feature_table(path, season_code, gameweek)
    except FeatureTableError as exception:
        raise TemporalRidgeError(exception.code, str(exception)) from exception


def _samples_for_table(
    connection: sqlite3.Connection,
    table: Mapping[str, Any],
    outcome_capture_id: int,
) -> List[Sample]:
    rows = connection.execute(
        """
        SELECT player_id, total_points
        FROM official_fpl_player_outcomes
        WHERE outcome_capture_id = :outcome_capture_id
        ORDER BY player_id;
        """,
        {"outcome_capture_id": outcome_capture_id},
    ).fetchall()
    outcomes = {int(row["player_id"]): int(row["total_points"]) for row in rows}
    players = table["players"]
    expected_ids = {int(player["playerId"]) for player in players}
    if len(outcomes) < len(expected_ids) or not expected_ids.issubset(outcomes):
        raise TemporalRidgeError(
            "data.incomplete-player-coverage",
            "Feature/outcome player coverage is incomplete for "
            f"{table['seasonCode']} Gameweek {table['gameweek']}.",
        )
    return [
        Sample(
            season_code=str(table["seasonCode"]),
            gameweek=int(table["gameweek"]),
            player_id=int(player["playerId"]),
            position=str(player["position"]),
            features=_extract_features(table, player),
            actual=outcomes[int(player["playerId"])],
        )
        for player in players
    ]


def _extract_features(
    table: Mapping[str, Any],
    player: Mapping[str, Any],
) -> Dict[str, Optional[float]]:
    history = player["history"]
    rolling = history["rolling"]["3"]
    ewma = history["exponentiallyWeighted"] or {}
    prior_gameweeks = max(1, int(table["gameweek"]) - 1)
    fixtures = player["targetFixtures"]
    home_rate = (
        sum(int(fixture["isHome"]) for fixture in fixtures) / len(fixtures)
        if fixtures
        else None
    )
    teams = {int(team["teamId"]): team for team in table["teams"]}
    team_rolling = teams[int(player["teamId"])]["history"]["rolling"]["3"]
    opponent_segments = [
        teams[int(fixture["opponentTeamId"])]["history"]["rolling"]["3"]
        for fixture in fixtures
    ]

    values: Dict[str, Optional[float]] = {
        "priceTenths": _number(player["priceTenths"]),
        "selectedByPercent": _number(player["selectedByPercent"]),
        "chanceNextRound": _number(player["chanceNextRound"]),
        "targetFixtureCount": _number(player["targetFixtureCount"]),
        "homeFixtureRate": home_rate,
        "restDaysBeforeFirstKickoff": _number(
            player["restDaysBeforeFirstKickoff"]
        ),
        "minimumRestDaysWithinGameweek": _number(
            player["minimumRestDaysWithinGameweek"]
        ),
        "cumulativePointsPerPriorGameweek": (
            float(player["cumulativeTotalPoints"]) / prior_gameweeks
        ),
        "cumulativeMinutesPerPriorGameweek": (
            float(player["cumulativeMinutes"]) / prior_gameweeks
        ),
        "cumulativeStartsPerPriorGameweek": (
            float(player["cumulativeStarts"]) / prior_gameweeks
        ),
        "historySampleCount": _number(history["sampleCount"]),
    }
    _copy_metrics(values, "historyRolling3", rolling)
    _copy_metrics(values, "historyEwma", ewma)
    _copy_team_metrics(values, "teamRolling3", team_rolling)
    for metric in _team_metric_names():
        values[f"opponentRolling3{metric}"] = _optional_mean(
            [
                _number(segment.get(_lower_first(metric)))
                for segment in opponent_segments
            ]
        )
    if tuple(values) != CONTINUOUS_FEATURES:
        raise TemporalRidgeError(
            "evaluation.feature-contract",
            "The extracted temporal feature contract is inconsistent.",
        )
    return values


def _copy_metrics(
    target: Dict[str, Optional[float]],
    prefix: str,
    source: Mapping[str, Any],
) -> None:
    for metric in (
        "TotalPointsMean",
        "MinutesMean",
        "StartsMean",
        "PlayedRate",
        "Played60Rate",
        "GoalsScoredMean",
        "AssistsMean",
        "CleanSheetsMean",
        "BonusMean",
    ):
        target[f"{prefix}{metric}"] = _number(
            source.get(_lower_first(metric))
        )


def _team_metric_names() -> Tuple[str, ...]:
    return (
        "GoalsForMean",
        "GoalsAgainstMean",
        "PointsPerMatch",
        "CleanSheetRate",
        "ScoredRate",
    )


def _copy_team_metrics(
    target: Dict[str, Optional[float]],
    prefix: str,
    source: Mapping[str, Any],
) -> None:
    for metric in _team_metric_names():
        target[f"{prefix}{metric}"] = _number(
            source.get(_lower_first(metric))
        )


def _lower_first(value: str) -> str:
    return value[0].lower() + value[1:]


def _number(value: Any) -> Optional[float]:
    if value is None:
        return None
    number = float(value)
    if not math.isfinite(number):
        raise TemporalRidgeError(
            "data.non-finite-feature",
            "A temporal feature is not finite.",
        )
    return number


def _optional_mean(values: Sequence[Optional[float]]) -> Optional[float]:
    present = [value for value in values if value is not None]
    return sum(present) / len(present) if present else None


def _predict_fold(
    training: Sequence[Sample],
    target: Sequence[Sample],
    ridge_penalty: float,
    continuous_features: Sequence[str] = CONTINUOUS_FEATURES,
    model_name: str = MODEL_NAME,
    include_baselines: bool = True,
) -> Tuple[List[Prediction], Dict[str, Any]]:
    if not training or not target:
        raise TemporalRidgeError(
            "evaluation.empty-fold",
            "An eligible fold has no training or target rows.",
        )
    feature_names = tuple(continuous_features)
    transform = _fit_transform(training, feature_names)
    train_x = [
        _apply_transform(sample, transform, feature_names)
        for sample in training
    ]
    target_x = [
        _apply_transform(sample, transform, feature_names)
        for sample in target
    ]
    train_y = [float(sample.actual) for sample in training]
    intercept, coefficients = _fit_ridge(train_x, train_y, ridge_penalty)
    ridge_values = [
        intercept + sum(coef * value for coef, value in zip(coefficients, row))
        for row in target_x
    ]

    position_points: DefaultDict[str, List[int]] = defaultdict(list)
    player_points: DefaultDict[int, List[Tuple[int, int]]] = defaultdict(list)
    for sample in training:
        position_points[sample.position].append(sample.actual)
        player_points[sample.player_id].append(
            (sample.gameweek, sample.actual)
        )
    global_mean = sum(train_y) / len(train_y)
    predictions: List[Prediction] = []
    for sample, ridge_value in zip(target, ridge_values):
        position_values = position_points[sample.position]
        position_mean = (
            sum(position_values) / len(position_values)
            if position_values
            else global_mean
        )
        history = sorted(player_points[sample.player_id])
        last_points = float(history[-1][1]) if history else position_mean
        official_mean = (
            sample.features["cumulativePointsPerPriorGameweek"]
        )
        assert official_mean is not None
        values = {model_name: ridge_value}
        if include_baselines:
            values.update(
                {
                    "zero-points": 0.0,
                    "position-expanding-mean": position_mean,
                    "player-last-points": last_points,
                    "official-running-mean": float(official_mean),
                }
            )
        predictions.extend(
            Prediction(
                model=name,
                season_code=sample.season_code,
                gameweek=sample.gameweek,
                player_id=sample.player_id,
                position=sample.position,
                predicted=value,
                actual=sample.actual,
            )
            for name, value in values.items()
        )
    ranked_coefficients = sorted(
        zip(transform.feature_names, coefficients),
        key=lambda item: (-abs(item[1]), item[0]),
    )
    diagnostic = {
        "continuousFeatureCount": len(feature_names),
        "modelFeatureCount": len(transform.feature_names),
        "ridgePenalty": ridge_penalty,
        "trainingTargetMean": _round(sum(train_y) / len(train_y)),
        "imputation": {
            name: count
            for name, count in zip(feature_names, transform.missing_counts)
            if count
        },
        "zeroVarianceFeatures": list(transform.zero_variance_features),
        "coefficientL2Norm": _round(
            math.sqrt(sum(value * value for value in coefficients))
        ),
        "largestStandardisedCoefficients": [
            {"feature": name, "coefficient": _round(value)}
            for name, value in ranked_coefficients[:10]
        ],
    }
    return predictions, diagnostic


def _fit_transform(
    samples: Sequence[Sample],
    continuous_features: Sequence[str] = CONTINUOUS_FEATURES,
) -> FittedTransform:
    continuous_names = tuple(continuous_features)
    columns = [
        [sample.features[name] for sample in samples]
        for name in continuous_names
    ]
    medians = tuple(
        float(statistics.median([value for value in column if value is not None]))
        if any(value is not None for value in column)
        else 0.0
        for column in columns
    )
    missing_counts = tuple(
        sum(value is None for value in column) for column in columns
    )
    unscaled = [
        _expand_sample(sample, medians, continuous_names)
        for sample in samples
    ]
    feature_names = tuple(
        name
        for continuous in continuous_names
        for name in (continuous, f"{continuous}.missing")
    ) + tuple(f"position.{position}" for position in POSITIONS)
    means = tuple(
        sum(row[index] for row in unscaled) / len(unscaled)
        for index in range(len(feature_names))
    )
    variances = tuple(
        sum((row[index] - means[index]) ** 2 for row in unscaled)
        / len(unscaled)
        for index in range(len(feature_names))
    )
    scales = tuple(
        math.sqrt(variance) if variance > 1e-12 else 1.0
        for variance in variances
    )
    zero_variance = tuple(
        feature_names[index]
        for index, variance in enumerate(variances)
        if variance <= 1e-12
    )
    return FittedTransform(
        medians=medians,
        means=means,
        scales=scales,
        feature_names=feature_names,
        missing_counts=missing_counts,
        zero_variance_features=zero_variance,
    )


def _expand_sample(
    sample: Sample,
    medians: Sequence[float],
    continuous_features: Sequence[str] = CONTINUOUS_FEATURES,
) -> List[float]:
    values: List[float] = []
    for index, name in enumerate(continuous_features):
        value = sample.features[name]
        values.extend(
            (
                medians[index] if value is None else value,
                float(value is None),
            )
        )
    values.extend(float(sample.position == position) for position in POSITIONS)
    return values


def _apply_transform(
    sample: Sample,
    transform: FittedTransform,
    continuous_features: Sequence[str] = CONTINUOUS_FEATURES,
) -> List[float]:
    values = _expand_sample(
        sample,
        transform.medians,
        continuous_features,
    )
    return [
        (value - mean) / scale
        for value, mean, scale in zip(
            values,
            transform.means,
            transform.scales,
        )
    ]


def _fit_ridge(
    rows: Sequence[Sequence[float]],
    targets: Sequence[float],
    penalty: float,
) -> Tuple[float, List[float]]:
    target_mean = sum(targets) / len(targets)
    dimension = len(rows[0])
    gram = [[0.0 for _ in range(dimension)] for _ in range(dimension)]
    rhs = [0.0 for _ in range(dimension)]
    for row, target in zip(rows, targets):
        centered_target = target - target_mean
        for left in range(dimension):
            rhs[left] += row[left] * centered_target
            for right in range(left, dimension):
                gram[left][right] += row[left] * row[right]
    for left in range(dimension):
        gram[left][left] += penalty
        for right in range(left):
            gram[left][right] = gram[right][left]
    return target_mean, _solve_linear_system(gram, rhs)


def _solve_linear_system(
    matrix: Sequence[Sequence[float]],
    vector: Sequence[float],
) -> List[float]:
    size = len(vector)
    augmented = [
        list(matrix[row]) + [float(vector[row])]
        for row in range(size)
    ]
    for column in range(size):
        pivot = max(
            range(column, size),
            key=lambda row: abs(augmented[row][column]),
        )
        if abs(augmented[pivot][column]) <= 1e-12:
            raise TemporalRidgeError(
                "evaluation.ridge-solve-failed",
                "The regularised linear system could not be solved.",
            )
        augmented[column], augmented[pivot] = (
            augmented[pivot],
            augmented[column],
        )
        pivot_value = augmented[column][column]
        for index in range(column, size + 1):
            augmented[column][index] /= pivot_value
        for row in range(size):
            if row == column:
                continue
            factor = augmented[row][column]
            if factor == 0:
                continue
            for index in range(column, size + 1):
                augmented[row][index] -= factor * augmented[column][index]
    return [augmented[row][size] for row in range(size)]


def _summarise_model(
    name: str,
    predictions: Sequence[Prediction],
) -> Dict[str, Any]:
    selected = [item for item in predictions if item.model == name]
    positions: DefaultDict[str, List[Prediction]] = defaultdict(list)
    for item in selected:
        positions[item.position].append(item)
    return {
        "name": name,
        "metrics": _metrics(selected),
        "slices": {
            "position": {
                position: _metrics(items)
                for position, items in sorted(positions.items())
            }
        },
    }


def _metrics(predictions: Sequence[Prediction]) -> Dict[str, Any]:
    if not predictions:
        raise TemporalRidgeError(
            "evaluation.empty-predictions",
            "A challenger produced no predictions.",
        )
    errors = [item.predicted - item.actual for item in predictions]
    return {
        "count": len(errors),
        "mae": _round(sum(abs(error) for error in errors) / len(errors)),
        "rmse": _round(
            math.sqrt(sum(error * error for error in errors) / len(errors))
        ),
        "meanError": _round(sum(errors) / len(errors)),
    }


def _base_report(
    season_code: Optional[str],
    minimum_training_gameweeks: int,
    ridge_penalty: float,
    complete_pair_count: int,
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": "exploratory-challenger-not-promoted",
        "isPromoted": False,
        "target": "official-gameweek-total-points",
        "split": "expanding-gameweek-origin",
        "completePairCount": complete_pair_count,
        "configuration": {
            "seasonCode": season_code,
            "minimumTrainingGameweeks": minimum_training_gameweeks,
            "crossSeasonPlayerMatching": False,
            "ridgePenalty": ridge_penalty,
            "penaltySelection": "fixed-before-evaluation",
            "imputation": "training-fold-median-with-missing-indicators",
            "scaling": "training-fold-z-score",
            "continuousFeatures": list(CONTINUOUS_FEATURES),
            "positionEncoding": list(POSITIONS),
        },
        "availabilityRule": (
            "target features use latest pre-deadline replay; training labels "
            "use latest correction available at target replay capture time; "
            "training features use each historical Gameweek's own latest "
            "pre-deadline replay"
        ),
    }


def _finish_report(
    base: Mapping[str, Any],
    status: str,
    reason: Optional[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    report = {
        **base,
        "status": status,
        "reason": reason,
        "eligibleFoldCount": len(folds),
        "folds": list(folds),
        "models": list(models),
    }
    report["dataIdentitySha256"] = _sha256(
        {
            "completePairCount": report["completePairCount"],
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


def _round(value: float) -> float:
    return round(value, 6)


def _sha256(value: Any) -> str:
    encoded = json.dumps(
        value,
        ensure_ascii=True,
        allow_nan=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _write_report(
    report: Mapping[str, Any],
    output_path: Optional[Path],
) -> None:
    rendered = json.dumps(
        report,
        ensure_ascii=True,
        allow_nan=False,
        indent=2,
        sort_keys=True,
    )
    if output_path is None:
        sys.stdout.write(rendered + "\n")
        return
    try:
        with Path(output_path).open(
            "x",
            encoding="utf-8",
            newline="\n",
        ) as stream:
            stream.write(rendered + "\n")
    except FileExistsError as exception:
        raise TemporalRidgeError(
            "output.already-exists",
            "The output path already exists; refusing to overwrite it.",
        ) from exception
    except OSError as exception:
        raise TemporalRidgeError(
            "output.write-failed",
            "The challenger report could not be written.",
        ) from exception


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate the leakage-safe temporal ridge total-points challenger."
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
        report = evaluate_temporal_ridge(
            options.database,
            season_code=options.season,
            minimum_training_gameweeks=options.minimum_training_gameweeks,
            ridge_penalty=options.ridge_penalty,
        )
        _write_report(report, options.output)
        return 0 if report["status"] == "complete" else 2
    except TemporalRidgeError as exception:
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
