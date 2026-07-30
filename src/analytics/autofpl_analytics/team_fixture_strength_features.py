from __future__ import annotations

import math
import sqlite3
from collections import defaultdict
from dataclasses import dataclass
from datetime import datetime
from typing import Any, DefaultDict, Dict, Mapping, Optional, Sequence, Tuple

from .historical_preseason_evaluation import HistoricalCapture
from .multi_season_evaluation import (
    FEATURES,
    Origin,
    _build_feature_table,
)
from .team_goal_strength_evaluation import (
    MAXIMUM_RATE,
    MINIMUM_RATE,
    Match,
    _instant,
    _load_matches,
    _weights,
)
from .temporal_ridge import Sample, TemporalRidgeError

PRIOR_MATCH_EQUIVALENT = 5.0
TEAM_FIXTURE_STRENGTH_FEATURES = (
    "fixtureTeamExpectedGoals",
    "fixtureOpponentExpectedGoals",
)
FEATURES_WITH_TEAM_FIXTURE_STRENGTH = (
    *FEATURES,
    *TEAM_FIXTURE_STRENGTH_FEATURES,
)


@dataclass(frozen=True)
class FixtureContext:
    season_code: str
    season_index: int
    gameweek: int
    fixture_id: int
    kickoff_utc: str
    home_team: str
    away_team: str

    @property
    def origin(self) -> Origin:
        return Origin(self.season_index, self.gameweek, self.season_code)


@dataclass(frozen=True)
class TeamRateState:
    global_attack: Mapping[bool, float]
    attack: Mapping[Tuple[str, bool], Tuple[float, float]]
    defence: Mapping[Tuple[str, bool], Tuple[float, float]]


def build_team_fixture_strength_feature_table(
    connection: sqlite3.Connection,
    captures: Sequence[HistoricalCapture],
) -> Dict[Origin, list[Sample]]:
    plain = _build_feature_table(connection, captures)
    matches = [
        match
        for season_index, capture in enumerate(captures)
        for match in _load_matches(connection, capture, season_index)
    ]
    fixtures = [
        fixture
        for season_index, capture in enumerate(captures)
        for fixture in load_fixture_contexts(
            connection,
            capture,
            season_index,
        )
    ]
    fixtures_by_origin: DefaultDict[Origin, list[FixtureContext]] = (
        defaultdict(list)
    )
    for fixture in fixtures:
        fixtures_by_origin[fixture.origin].append(fixture)
    teams = _load_player_teams(connection, captures)

    result: Dict[Origin, list[Sample]] = {}
    for origin in sorted(plain):
        target_fixtures = fixtures_by_origin.get(origin, [])
        cutoff = (
            min(_instant(fixture.kickoff_utc) for fixture in target_fixtures)
            if target_fixtures
            else None
        )
        training = [match for match in matches if match.origin < origin]
        state = (
            build_team_rate_state(training, cutoff)
            if cutoff is not None and training
            else None
        )
        enriched = []
        for sample in plain[origin]:
            key = (origin.season_index, origin.gameweek, sample.player_id)
            team_name = teams.get(key)
            if team_name is None:
                raise TemporalRidgeError(
                    "team-fixture.player-team",
                    "A player-Gameweek has no unambiguous team identity.",
                )
            values = team_fixture_strength(
                state,
                target_fixtures,
                team_name,
            )
            enriched.append(
                add_team_fixture_strength_features(sample, values)
            )
        result[origin] = enriched
    return result


def add_team_fixture_strength_features(
    sample: Sample,
    values: Mapping[str, Optional[float]],
) -> Sample:
    if tuple(sample.features) != FEATURES:
        raise TemporalRidgeError(
            "team-fixture.base-feature-contract",
            "The base sample does not match the retained feature contract.",
        )
    if tuple(values) != TEAM_FIXTURE_STRENGTH_FEATURES:
        raise TemporalRidgeError(
            "team-fixture.value-contract",
            "The team fixture-strength values are incomplete or unordered.",
        )
    features = {**sample.features, **values}
    if tuple(features) != FEATURES_WITH_TEAM_FIXTURE_STRENGTH:
        raise TemporalRidgeError(
            "team-fixture.feature-contract",
            "The team fixture-strength feature order is not fixed.",
        )
    return Sample(
        season_code=sample.season_code,
        gameweek=sample.gameweek,
        player_id=sample.player_id,
        position=sample.position,
        features=features,
        actual=sample.actual,
    )


def load_fixture_contexts(
    connection: sqlite3.Connection,
    capture: HistoricalCapture,
    season_index: int,
) -> list[FixtureContext]:
    rows = connection.execute(
        """
        SELECT DISTINCT
            fixture_id,
            gameweek,
            kickoff_utc,
            team_name,
            was_home
        FROM historical_fpl_player_gameweeks
        WHERE capture_id = :capture_id
        ORDER BY gameweek, kickoff_utc, fixture_id, team_name;
        """,
        {"capture_id": capture.capture_id},
    ).fetchall()
    grouped: Dict[int, Dict[str, Any]] = {}
    for row in rows:
        fixture = grouped.setdefault(
            int(row["fixture_id"]),
            {
                "gameweeks": set(),
                "kickoffs": set(),
                "home": set(),
                "away": set(),
            },
        )
        fixture["gameweeks"].add(int(row["gameweek"]))
        fixture["kickoffs"].add(str(row["kickoff_utc"]))
        key = "home" if int(row["was_home"]) else "away"
        fixture[key].add(str(row["team_name"]))
    contexts = []
    for fixture_id, fixture in sorted(grouped.items()):
        if (
            len(fixture["gameweeks"]) != 1
            or len(fixture["kickoffs"]) != 1
            or len(fixture["home"]) != 1
            or len(fixture["away"]) != 1
        ):
            raise TemporalRidgeError(
                "team-fixture.fixture-context",
                "A historical fixture identity is ambiguous.",
            )
        contexts.append(
            FixtureContext(
                season_code=capture.season_code,
                season_index=season_index,
                gameweek=next(iter(fixture["gameweeks"])),
                fixture_id=fixture_id,
                kickoff_utc=next(iter(fixture["kickoffs"])),
                home_team=next(iter(fixture["home"])),
                away_team=next(iter(fixture["away"])),
            )
        )
    if not contexts:
        raise TemporalRidgeError(
            "team-fixture.fixture-coverage",
            "The historical capture has no fixture contexts.",
        )
    return contexts


def build_team_rate_state(
    training: Sequence[Match],
    cutoff: datetime,
) -> TeamRateState:
    if not training:
        raise TemporalRidgeError(
            "team-fixture.empty-training",
            "At least one prior match is required for team strength.",
        )
    weights = _weights(training, cutoff)
    global_sums = {True: 0.0, False: 0.0}
    global_weights = {True: 0.0, False: 0.0}
    attack_sums: DefaultDict[Tuple[str, bool], float] = defaultdict(float)
    attack_weights: DefaultDict[Tuple[str, bool], float] = defaultdict(float)
    defence_sums: DefaultDict[Tuple[str, bool], float] = defaultdict(float)
    defence_weights: DefaultDict[Tuple[str, bool], float] = defaultdict(float)
    for weight, match in zip(weights, training, strict=True):
        perspectives = (
            (
                match.home_team,
                True,
                match.home_expected_goals,
                match.away_expected_goals,
            ),
            (
                match.away_team,
                False,
                match.away_expected_goals,
                match.home_expected_goals,
            ),
        )
        for team, home, expected_goals, expected_goals_against in perspectives:
            _require_rate(expected_goals)
            _require_rate(expected_goals_against)
            key = (team, home)
            global_sums[home] += float(weight) * expected_goals
            global_weights[home] += float(weight)
            attack_sums[key] += float(weight) * expected_goals
            attack_weights[key] += float(weight)
            defence_sums[key] += float(weight) * expected_goals_against
            defence_weights[key] += float(weight)
    if any(global_weights[home] <= 0.0 for home in (True, False)):
        raise TemporalRidgeError(
            "team-fixture.missing-venue-history",
            "Prior matches do not cover both home and away perspectives.",
        )
    global_attack = {
        home: _bounded(global_sums[home] / global_weights[home])
        for home in (True, False)
    }
    return TeamRateState(
        global_attack=global_attack,
        attack={
            key: (attack_sums[key], attack_weights[key])
            for key in attack_sums
        },
        defence={
            key: (defence_sums[key], defence_weights[key])
            for key in defence_sums
        },
    )


def team_fixture_strength(
    state: Optional[TeamRateState],
    fixtures: Sequence[FixtureContext],
    team_name: str,
) -> Dict[str, Optional[float]]:
    owned = [
        fixture
        for fixture in fixtures
        if team_name in {fixture.home_team, fixture.away_team}
    ]
    if not owned:
        return {
            TEAM_FIXTURE_STRENGTH_FEATURES[0]: 0.0,
            TEAM_FIXTURE_STRENGTH_FEATURES[1]: 0.0,
        }
    if state is None:
        return {
            TEAM_FIXTURE_STRENGTH_FEATURES[0]: None,
            TEAM_FIXTURE_STRENGTH_FEATURES[1]: None,
        }
    team_rates = []
    opponent_rates = []
    for fixture in owned:
        is_home = fixture.home_team == team_name
        opponent = (
            fixture.away_team if is_home else fixture.home_team
        )
        team_attack = _shrunk(
            state.attack.get((team_name, is_home)),
            state.global_attack[is_home],
        )
        opponent_defence = _shrunk(
            state.defence.get((opponent, not is_home)),
            state.global_attack[is_home],
        )
        opponent_attack = _shrunk(
            state.attack.get((opponent, not is_home)),
            state.global_attack[not is_home],
        )
        team_defence = _shrunk(
            state.defence.get((team_name, is_home)),
            state.global_attack[not is_home],
        )
        team_rates.append(
            _bounded(math.sqrt(team_attack * opponent_defence))
        )
        opponent_rates.append(
            _bounded(math.sqrt(opponent_attack * team_defence))
        )
    return {
        TEAM_FIXTURE_STRENGTH_FEATURES[0]: (
            sum(team_rates) / len(team_rates)
        ),
        TEAM_FIXTURE_STRENGTH_FEATURES[1]: (
            sum(opponent_rates) / len(opponent_rates)
        ),
    }


def _load_player_teams(
    connection: sqlite3.Connection,
    captures: Sequence[HistoricalCapture],
) -> Dict[Tuple[int, int, int], str]:
    result: Dict[Tuple[int, int, int], str] = {}
    for season_index, capture in enumerate(captures):
        rows = connection.execute(
            """
            SELECT player_code, gameweek, team_name
            FROM historical_fpl_player_gameweeks
            WHERE capture_id = :capture_id
            GROUP BY player_code, gameweek, team_name
            ORDER BY gameweek, player_code, team_name;
            """,
            {"capture_id": capture.capture_id},
        ).fetchall()
        grouped: DefaultDict[Tuple[int, int], set[str]] = defaultdict(set)
        for row in rows:
            grouped[
                (int(row["player_code"]), int(row["gameweek"]))
            ].add(str(row["team_name"]))
        for (player_code, gameweek), teams in grouped.items():
            if len(teams) != 1:
                raise TemporalRidgeError(
                    "team-fixture.ambiguous-player-team",
                    "A player has multiple teams in one Gameweek.",
                )
            result[(season_index, gameweek, player_code)] = next(
                iter(teams)
            )
    return result


def _shrunk(
    values: Optional[Tuple[float, float]],
    prior: float,
) -> float:
    weighted_sum, weight = values if values is not None else (0.0, 0.0)
    return _bounded(
        (
            weighted_sum + PRIOR_MATCH_EQUIVALENT * prior
        )
        / (weight + PRIOR_MATCH_EQUIVALENT)
    )


def _require_rate(value: float) -> None:
    if not math.isfinite(value) or value < 0.0:
        raise TemporalRidgeError(
            "team-fixture.invalid-expected-goals",
            "A historical expected-goals value is invalid.",
        )


def _bounded(value: float) -> float:
    return min(MAXIMUM_RATE, max(MINIMUM_RATE, float(value)))
