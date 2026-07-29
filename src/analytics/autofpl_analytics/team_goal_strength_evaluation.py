from __future__ import annotations

import argparse
import json
import math
import sqlite3
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

import numpy as np
from scipy.optimize import minimize, minimize_scalar

from .historical_preseason_evaluation import HistoricalCapture, _load_capture
from .multi_season_evaluation import (
    DEFAULT_EVALUATION_START_GAMEWEEK,
    DEFAULT_SEASONS,
    MINIMUM_TRAINING_ORIGINS,
    Origin,
    _open_connection,
)
from .temporal_ridge import TemporalRidgeError, _round, _sha256, _write_report

SCHEMA_VERSION = "1.0"
EVALUATOR_VERSION = "time-decayed-dixon-coles-expanding-origin-v1"
BASELINE_MODEL = "league-home-away-independent-poisson"
CHALLENGER_MODEL = "time-decayed-dixon-coles"
MODEL_NAMES = (BASELINE_MODEL, CHALLENGER_MODEL)
HALF_LIFE_DAYS = 180.0
L2_PENALTY = 0.10
IDENTIFIABILITY_PENALTY = 100.0
MAXIMUM_GOALS = 12
MINIMUM_RATE = 0.02
MAXIMUM_RATE = 8.0
RHO_BOUNDS = (-0.20, 0.20)


@dataclass(frozen=True)
class Match:
    season_code: str
    season_index: int
    gameweek: int
    fixture_id: int
    kickoff_utc: str
    home_team: str
    away_team: str
    home_goals: int
    away_goals: int

    @property
    def origin(self) -> Origin:
        return Origin(self.season_index, self.gameweek, self.season_code)


@dataclass(frozen=True)
class Rates:
    home: float
    away: float
    rho: float


def evaluate_team_goal_strength(
    database_path: Path,
    season_codes: Sequence[str] = DEFAULT_SEASONS,
    evaluation_start_gameweek: int = DEFAULT_EVALUATION_START_GAMEWEEK,
    minimum_training_origins: int = MINIMUM_TRAINING_ORIGINS,
) -> Dict[str, Any]:
    path = Path(database_path)
    _validate(
        path,
        season_codes,
        evaluation_start_gameweek,
        minimum_training_origins,
    )
    seasons = tuple(season_codes)
    connection = _open_connection(path)
    try:
        captures = [_load_capture(connection, season) for season in seasons]
        missing = [
            season
            for season, capture in zip(seasons, captures)
            if capture is None
        ]
        exact = [capture for capture in captures if capture is not None]
        base = _base(
            seasons,
            evaluation_start_gameweek,
            minimum_training_origins,
            exact,
        )
        if missing:
            return _finish(base, "insufficient-data", missing, [], [], None)
        matches = [
            match
            for season_index, capture in enumerate(exact)
            for match in _load_matches(connection, capture, season_index)
        ]
    finally:
        connection.close()

    origins = sorted({match.origin for match in matches})
    targets = [
        origin
        for origin in origins
        if origin.season_code == seasons[-1]
        and origin.gameweek >= evaluation_start_gameweek
    ]
    all_predictions: Dict[str, List[Dict[str, Any]]] = {
        model: [] for model in MODEL_NAMES
    }
    folds: List[Dict[str, Any]] = []
    for target_origin in targets:
        training_origins = [
            origin for origin in origins if origin < target_origin
        ]
        if len(training_origins) < minimum_training_origins:
            continue
        training = [
            match for match in matches if match.origin < target_origin
        ]
        target = [
            match for match in matches if match.origin == target_origin
        ]
        if not training or not target:
            continue
        cutoff = min(_instant(match.kickoff_utc) for match in target)
        baseline_rates = _fit_baseline(training, cutoff)
        model, diagnostics = _fit_dixon_coles(training, cutoff)
        fold_predictions: Dict[str, List[Dict[str, Any]]] = {
            BASELINE_MODEL: [
                _prediction(match, baseline_rates) for match in target
            ],
            CHALLENGER_MODEL: [
                _prediction(match, _rates(model, match)) for match in target
            ],
        }
        for name in MODEL_NAMES:
            all_predictions[name].extend(fold_predictions[name])
        fold_models = [
            _summarise(name, fold_predictions[name]) for name in MODEL_NAMES
        ]
        folds.append(
            {
                "seasonCode": target_origin.season_code,
                "gameweek": target_origin.gameweek,
                "trainingOriginCount": len(training_origins),
                "trainingMatchCount": len(training),
                "targetMatchCount": len(target),
                "cutoffUtc": cutoff.isoformat(),
                "models": fold_models,
                "challengerDiagnostics": diagnostics,
            }
        )
    if not folds:
        return _finish(base, "insufficient-data", [], [], [], None)
    models = [
        _summarise(name, all_predictions[name]) for name in MODEL_NAMES
    ]
    models.sort(
        key=lambda model: (
            model["metrics"]["jointNegativeLogLikelihood"],
            model["name"],
        )
    )
    return _finish(
        base,
        "complete",
        [],
        folds,
        models,
        _comparison(models, folds),
    )


def _load_matches(
    connection: sqlite3.Connection,
    capture: HistoricalCapture,
    season_index: int,
) -> List[Match]:
    rows = connection.execute(
        """
        SELECT fixture_id, gameweek, kickoff_utc, team_name, was_home,
               goals_scored
        FROM historical_fpl_player_gameweeks
        WHERE capture_id = :capture_id
        ORDER BY gameweek, kickoff_utc, fixture_id, team_name, player_code;
        """,
        {"capture_id": capture.capture_id},
    ).fetchall()
    grouped: Dict[int, Dict[str, Any]] = {}
    for row in rows:
        fixture_id = int(row["fixture_id"])
        fixture = grouped.setdefault(
            fixture_id,
            {
                "gameweeks": set(),
                "kickoffs": set(),
                "teams": {},
            },
        )
        fixture["gameweeks"].add(int(row["gameweek"]))
        fixture["kickoffs"].add(str(row["kickoff_utc"]))
        team_name = str(row["team_name"])
        team = fixture["teams"].setdefault(
            team_name, {"venues": set(), "goals": 0}
        )
        team["venues"].add(int(row["was_home"]))
        team["goals"] += int(row["goals_scored"])
    matches: List[Match] = []
    for fixture_id, fixture in sorted(grouped.items()):
        if (
            len(fixture["gameweeks"]) != 1
            or len(fixture["kickoffs"]) != 1
            or len(fixture["teams"]) != 2
        ):
            raise TemporalRidgeError(
                "data.ambiguous-historical-fixture",
                "A historical fixture cannot be reconstructed exactly.",
            )
        home = [
            (name, values)
            for name, values in fixture["teams"].items()
            if values["venues"] == {1}
        ]
        away = [
            (name, values)
            for name, values in fixture["teams"].items()
            if values["venues"] == {0}
        ]
        if len(home) != 1 or len(away) != 1:
            raise TemporalRidgeError(
                "data.ambiguous-historical-venue",
                "A historical fixture has inconsistent home/away identity.",
            )
        matches.append(
            Match(
                capture.season_code,
                season_index,
                next(iter(fixture["gameweeks"])),
                fixture_id,
                next(iter(fixture["kickoffs"])),
                home[0][0],
                away[0][0],
                int(home[0][1]["goals"]),
                int(away[0][1]["goals"]),
            )
        )
    if not matches:
        raise TemporalRidgeError(
            "data.historical-fixtures-not-found",
            "The historical capture contains no reconstructable fixtures.",
        )
    return matches


def _fit_baseline(training: Sequence[Match], cutoff: datetime) -> Rates:
    weights = _weights(training, cutoff)
    total = float(weights.sum())
    return Rates(
        _bounded_rate(
            sum(weight * match.home_goals for weight, match in zip(weights, training))
            / total
        ),
        _bounded_rate(
            sum(weight * match.away_goals for weight, match in zip(weights, training))
            / total
        ),
        0.0,
    )


def _fit_dixon_coles(
    training: Sequence[Match], cutoff: datetime
) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    teams = sorted(
        {match.home_team for match in training}
        | {match.away_team for match in training}
    )
    indices = {team: index for index, team in enumerate(teams)}
    count = len(teams)
    weights = _weights(training, cutoff)
    initial = np.zeros(2 + 2 * count, dtype=float)
    weighted_home = sum(
        weight * match.home_goals for weight, match in zip(weights, training)
    ) / float(weights.sum())
    weighted_away = sum(
        weight * match.away_goals for weight, match in zip(weights, training)
    ) / float(weights.sum())
    initial[0] = math.log(max(MINIMUM_RATE, weighted_away))
    initial[1] = math.log(
        max(MINIMUM_RATE, weighted_home)
        / max(MINIMUM_RATE, weighted_away)
    )

    def objective(values: np.ndarray) -> Tuple[float, np.ndarray]:
        intercept, home_advantage = values[:2]
        attacks = values[2 : 2 + count]
        defences = values[2 + count :]
        loss = 0.0
        gradient = np.zeros_like(values)
        for weight, match in zip(weights, training):
            home_index = indices[match.home_team]
            away_index = indices[match.away_team]
            home_rate, home_derivative = _rate_and_derivative(
                intercept
                + home_advantage
                + attacks[home_index]
                + defences[away_index]
            )
            away_rate, away_derivative = _rate_and_derivative(
                intercept + attacks[away_index] + defences[home_index]
            )
            loss -= weight * (
                _poisson_log_probability(match.home_goals, home_rate)
                + _poisson_log_probability(match.away_goals, away_rate)
            )
            home_gradient = home_derivative * (
                home_rate - match.home_goals
            )
            away_gradient = away_derivative * (
                away_rate - match.away_goals
            )
            gradient[0] += weight * (home_gradient + away_gradient)
            gradient[1] += weight * home_gradient
            gradient[2 + home_index] += weight * home_gradient
            gradient[2 + away_index] += weight * away_gradient
            gradient[2 + count + away_index] += weight * home_gradient
            gradient[2 + count + home_index] += weight * away_gradient
        loss += L2_PENALTY * float(
            np.dot(attacks, attacks) + np.dot(defences, defences)
        )
        loss += IDENTIFIABILITY_PENALTY * float(attacks.mean() ** 2)
        gradient[2 : 2 + count] += 2.0 * L2_PENALTY * attacks
        gradient[2 + count :] += 2.0 * L2_PENALTY * defences
        gradient[2 : 2 + count] += (
            2.0 * IDENTIFIABILITY_PENALTY * attacks.mean() / count
        )
        return float(loss), gradient

    bounds = [
        (math.log(MINIMUM_RATE), math.log(MAXIMUM_RATE)),
        (-1.5, 1.5),
        *[(-2.5, 2.5)] * (2 * count),
    ]
    fitted = minimize(
        objective,
        initial,
        method="L-BFGS-B",
        jac=True,
        bounds=bounds,
        options={"maxiter": 500, "ftol": 1e-10, "gtol": 1e-7},
    )
    if not fitted.success or not np.isfinite(fitted.x).all():
        raise TemporalRidgeError(
            "model.dixon-coles-fit",
            "The time-decayed Dixon-Coles likelihood did not converge.",
        )
    values = fitted.x
    provisional = {
        "indices": indices,
        "intercept": float(values[0]),
        "homeAdvantage": float(values[1]),
        "attacks": values[2 : 2 + count],
        "defences": values[2 + count :],
        "rho": 0.0,
    }
    fitted_rates = []
    for match in training:
        rates = _rates(provisional, match)
        fitted_rates.append((match, rates.home, rates.away))

    def rho_objective(rho: float) -> float:
        loss = 0.0
        for weight, (match, home_rate, away_rate) in zip(
            weights, fitted_rates
        ):
            tau = _tau(
                match.home_goals,
                match.away_goals,
                home_rate,
                away_rate,
                rho,
            )
            if tau <= 0 or not math.isfinite(tau):
                return 1e100
            loss -= weight * math.log(tau)
        return loss

    fitted_rho = minimize_scalar(
        rho_objective,
        bounds=RHO_BOUNDS,
        method="bounded",
        options={"xatol": 1e-10, "maxiter": 500},
    )
    if not fitted_rho.success or not math.isfinite(float(fitted_rho.x)):
        raise TemporalRidgeError(
            "model.dixon-coles-rho-fit",
            "The Dixon-Coles low-score correction did not converge.",
        )
    model = {
        "teams": teams,
        "indices": indices,
        "intercept": float(values[0]),
        "homeAdvantage": float(values[1]),
        "rho": float(fitted_rho.x),
        "attacks": values[2 : 2 + count].copy(),
        "defences": values[2 + count :].copy(),
    }
    return model, {
        "converged": True,
        "iterations": int(fitted.nit),
        "poissonObjective": _round(float(fitted.fun)),
        "rhoObjective": _round(float(fitted_rho.fun)),
        "teamCount": count,
        "effectiveMatchWeight": _round(float(weights.sum())),
        "homeAdvantageLogRate": _round(float(values[1])),
        "rho": _round(float(fitted_rho.x)),
        "gradientInfinityNorm": _round(
            float(np.linalg.norm(fitted.jac, ord=np.inf))
        ),
        "poissonOptimizerMessage": str(fitted.message),
        "rhoOptimizerIterations": int(fitted_rho.nfev),
    }


def _rates(model: Mapping[str, Any], match: Match) -> Rates:
    indices = model["indices"]
    home_attack = (
        float(model["attacks"][indices[match.home_team]])
        if match.home_team in indices
        else 0.0
    )
    home_defence = (
        float(model["defences"][indices[match.home_team]])
        if match.home_team in indices
        else 0.0
    )
    away_attack = (
        float(model["attacks"][indices[match.away_team]])
        if match.away_team in indices
        else 0.0
    )
    away_defence = (
        float(model["defences"][indices[match.away_team]])
        if match.away_team in indices
        else 0.0
    )
    return Rates(
        _bounded_rate(
            math.exp(
                float(model["intercept"])
                + float(model["homeAdvantage"])
                + home_attack
                + away_defence
            )
        ),
        _bounded_rate(
            math.exp(
                float(model["intercept"]) + away_attack + home_defence
            )
        ),
        float(model["rho"]),
    )


def _prediction(match: Match, rates: Rates) -> Dict[str, Any]:
    matrix = _score_matrix(rates)
    home_win = float(np.tril(matrix, -1).sum())
    draw = float(np.trace(matrix))
    away_win = float(np.triu(matrix, 1).sum())
    actual_outcome = (
        0
        if match.home_goals > match.away_goals
        else 1
        if match.home_goals == match.away_goals
        else 2
    )
    outcomes = (home_win, draw, away_win)
    joint_probability = math.exp(
        _poisson_log_probability(match.home_goals, rates.home)
        + _poisson_log_probability(match.away_goals, rates.away)
    ) * _tau(
        match.home_goals,
        match.away_goals,
        rates.home,
        rates.away,
        rates.rho,
    )
    return {
        "seasonCode": match.season_code,
        "gameweek": match.gameweek,
        "fixtureId": match.fixture_id,
        "homeTeam": match.home_team,
        "awayTeam": match.away_team,
        "homeGoals": match.home_goals,
        "awayGoals": match.away_goals,
        "homeRate": rates.home,
        "awayRate": rates.away,
        "jointNegativeLogLikelihood": -math.log(
            max(1e-15, joint_probability)
        ),
        "outcomeNegativeLogLikelihood": -math.log(
            max(1e-15, outcomes[actual_outcome])
        ),
        "outcomeBrier": sum(
            (probability - float(index == actual_outcome)) ** 2
            for index, probability in enumerate(outcomes)
        ),
        "homeCleanSheetProbability": float(matrix[:, 0].sum()),
        "awayCleanSheetProbability": float(matrix[0, :].sum()),
    }


def _score_matrix(rates: Rates) -> np.ndarray:
    home = np.asarray(
        [math.exp(_poisson_log_probability(i, rates.home)) for i in range(MAXIMUM_GOALS + 1)]
    )
    away = np.asarray(
        [math.exp(_poisson_log_probability(i, rates.away)) for i in range(MAXIMUM_GOALS + 1)]
    )
    matrix = np.outer(home, away)
    for home_goals in (0, 1):
        for away_goals in (0, 1):
            matrix[home_goals, away_goals] *= _tau(
                home_goals,
                away_goals,
                rates.home,
                rates.away,
                rates.rho,
            )
    total = float(matrix.sum())
    if total <= 0 or not math.isfinite(total):
        raise TemporalRidgeError(
            "model.invalid-score-distribution",
            "A fitted score distribution is invalid.",
        )
    return matrix / total


def _summarise(
    name: str, predictions: Sequence[Mapping[str, Any]]
) -> Dict[str, Any]:
    count = len(predictions)
    squared_goal_errors = [
        (float(row["homeRate"]) - int(row["homeGoals"])) ** 2
        for row in predictions
    ] + [
        (float(row["awayRate"]) - int(row["awayGoals"])) ** 2
        for row in predictions
    ]
    clean_sheet_errors = [
        (
            float(row["homeCleanSheetProbability"])
            - float(int(row["awayGoals"]) == 0)
        )
        ** 2
        for row in predictions
    ] + [
        (
            float(row["awayCleanSheetProbability"])
            - float(int(row["homeGoals"]) == 0)
        )
        ** 2
        for row in predictions
    ]
    return {
        "name": name,
        "metrics": {
            "matchCount": count,
            "jointNegativeLogLikelihood": _round(
                sum(float(row["jointNegativeLogLikelihood"]) for row in predictions)
                / count
            ),
            "outcomeNegativeLogLikelihood": _round(
                sum(float(row["outcomeNegativeLogLikelihood"]) for row in predictions)
                / count
            ),
            "outcomeBrier": _round(
                sum(float(row["outcomeBrier"]) for row in predictions) / count
            ),
            "goalRmse": _round(
                math.sqrt(sum(squared_goal_errors) / len(squared_goal_errors))
            ),
            "cleanSheetBrier": _round(
                sum(clean_sheet_errors) / len(clean_sheet_errors)
            ),
        },
    }


def _comparison(
    models: Sequence[Mapping[str, Any]],
    folds: Sequence[Mapping[str, Any]],
) -> Dict[str, Any]:
    indexed = {str(model["name"]): model["metrics"] for model in models}
    baseline = indexed[BASELINE_MODEL]
    challenger = indexed[CHALLENGER_MODEL]
    improvement = (
        (
            baseline["jointNegativeLogLikelihood"]
            - challenger["jointNegativeLogLikelihood"]
        )
        / baseline["jointNegativeLogLikelihood"]
    )
    wins = 0
    for fold in folds:
        fold_models = {
            str(model["name"]): model["metrics"] for model in fold["models"]
        }
        if (
            fold_models[CHALLENGER_MODEL]["jointNegativeLogLikelihood"]
            < fold_models[BASELINE_MODEL]["jointNegativeLogLikelihood"]
        ):
            wins += 1
    passes = (
        improvement >= 0.01
        and challenger["outcomeNegativeLogLikelihood"]
        <= baseline["outcomeNegativeLogLikelihood"]
        and challenger["goalRmse"] <= baseline["goalRmse"]
        and wins > len(folds) / 2
    )
    return {
        "baseline": BASELINE_MODEL,
        "challenger": CHALLENGER_MODEL,
        "jointNllImprovementFraction": _round(improvement),
        "outcomeNllDelta": _round(
            challenger["outcomeNegativeLogLikelihood"]
            - baseline["outcomeNegativeLogLikelihood"]
        ),
        "goalRmseDelta": _round(
            challenger["goalRmse"] - baseline["goalRmse"]
        ),
        "foldWins": wins,
        "foldCount": len(folds),
        "fixedScreenPassed": passes,
        "decision": (
            "retain-for-player-distribution-ablation"
            if passes
            else "do-not-use-as-player-feature"
        ),
        "isPromoted": False,
    }


def _weights(training: Sequence[Match], cutoff: datetime) -> np.ndarray:
    values = []
    for match in training:
        age = (cutoff - _instant(match.kickoff_utc)).total_seconds() / 86400
        if age < 0:
            raise TemporalRidgeError(
                "data.future-training-match",
                "A training match occurs after the target cutoff.",
            )
        values.append(0.5 ** (age / HALF_LIFE_DAYS))
    return np.asarray(values, dtype=float)


def _tau(
    home_goals: int,
    away_goals: int,
    home_rate: float,
    away_rate: float,
    rho: float,
) -> float:
    if home_goals == 0 and away_goals == 0:
        return 1.0 - home_rate * away_rate * rho
    if home_goals == 0 and away_goals == 1:
        return 1.0 + home_rate * rho
    if home_goals == 1 and away_goals == 0:
        return 1.0 + away_rate * rho
    if home_goals == 1 and away_goals == 1:
        return 1.0 - rho
    return 1.0


def _rate_and_derivative(log_rate: float) -> Tuple[float, float]:
    minimum = math.log(MINIMUM_RATE)
    maximum = math.log(MAXIMUM_RATE)
    if log_rate <= minimum:
        return MINIMUM_RATE, 0.0
    if log_rate >= maximum:
        return MAXIMUM_RATE, 0.0
    return math.exp(log_rate), 1.0


def _poisson_log_probability(value: int, rate: float) -> float:
    return value * math.log(rate) - rate - math.lgamma(value + 1)


def _bounded_rate(value: float) -> float:
    return min(MAXIMUM_RATE, max(MINIMUM_RATE, float(value)))


def _instant(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise TemporalRidgeError(
            "data.non-utc-kickoff", "A fixture kickoff is not timezone-aware."
        )
    return parsed


def _validate(
    path: Path,
    season_codes: Sequence[str],
    evaluation_start_gameweek: int,
    minimum_training_origins: int,
) -> None:
    if not path.is_file():
        raise TemporalRidgeError(
            "database.not-found", "The SQLite database does not exist."
        )
    seasons = tuple(season_codes)
    if len(seasons) < 2 or len(set(seasons)) != len(seasons):
        raise TemporalRidgeError(
            "configuration.seasons",
            "At least two distinct chronological seasons are required.",
        )
    if not 1 <= evaluation_start_gameweek <= 38:
        raise TemporalRidgeError(
            "configuration.evaluation-start",
            "The evaluation start Gameweek must be between 1 and 38.",
        )
    if minimum_training_origins < 1:
        raise TemporalRidgeError(
            "configuration.minimum-training-origins",
            "At least one training origin is required.",
        )


def _base(
    seasons: Sequence[str],
    evaluation_start_gameweek: int,
    minimum_training_origins: int,
    captures: Sequence[HistoricalCapture],
) -> Dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "evaluatorVersion": EVALUATOR_VERSION,
        "researchStatus": "registered-retrospective-screen-not-promotion",
        "isPromoted": False,
        "target": "joint-home-away-match-score-distribution",
        "split": "cross-season-expanding-origin",
        "configuration": {
            "seasonCodes": list(seasons),
            "evaluationSeasonCode": seasons[-1],
            "evaluationStartGameweek": evaluation_start_gameweek,
            "minimumTrainingOrigins": minimum_training_origins,
            "halfLifeDays": HALF_LIFE_DAYS,
            "l2Penalty": L2_PENALTY,
            "identifiabilityPenalty": IDENTIFIABILITY_PENALTY,
            "rhoBounds": list(RHO_BOUNDS),
            "maximumScoredGoalsForOutcomeMatrix": MAXIMUM_GOALS,
            "models": list(MODEL_NAMES),
            "promotionGate": {
                "minimumJointNllImprovementFraction": 0.01,
                "maximumOutcomeNllDelta": 0.0,
                "maximumGoalRmseDelta": 0.0,
                "minimumFoldWins": "strict-majority",
                "promotionAllowed": False,
            },
        },
        "captures": [
            {
                "captureId": capture.capture_id,
                "seasonCode": capture.season_code,
                "sourceRevision": capture.source_revision,
                "playersSha256": capture.players_sha256,
                "gameweeksSha256": capture.gameweeks_sha256,
            }
            for capture in captures
        ],
    }


def _finish(
    base: Mapping[str, Any],
    status: str,
    missing_seasons: Sequence[str],
    folds: Sequence[Mapping[str, Any]],
    models: Sequence[Mapping[str, Any]],
    comparison: Optional[Mapping[str, Any]],
) -> Dict[str, Any]:
    reason = None
    if status != "complete":
        reason = (
            "historical-season-archive-not-found"
            if missing_seasons
            else "no-eligible-team-strength-folds"
        )
    report = {
        **base,
        "status": status,
        "reason": reason,
        "missingSeasonCodes": list(missing_seasons),
        "eligibleFoldCount": len(folds),
        "folds": list(folds),
        "models": list(models),
        "comparison": comparison,
    }
    report["runIdentitySha256"] = _sha256(report)
    return report


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate a fixed time-decayed Dixon-Coles score model on "
            "expanding historical origins."
        )
    )
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument(
        "--evaluation-start-gameweek",
        type=int,
        default=DEFAULT_EVALUATION_START_GAMEWEEK,
    )
    parser.add_argument(
        "--minimum-training-origins",
        type=int,
        default=MINIMUM_TRAINING_ORIGINS,
    )
    parser.add_argument("--output", type=Path)
    options = parser.parse_args(arguments)
    try:
        report = evaluate_team_goal_strength(
            options.database,
            evaluation_start_gameweek=options.evaluation_start_gameweek,
            minimum_training_origins=options.minimum_training_origins,
        )
        _write_report(report, options.output)
        return 0 if report["status"] == "complete" else 2
    except (TemporalRidgeError, sqlite3.Error) as exception:
        code = (
            exception.code
            if isinstance(exception, TemporalRidgeError)
            else "data.team-strength-schema"
        )
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
