from __future__ import annotations

import sqlite3
import sys
import unittest
from datetime import datetime, timezone
from pathlib import Path

ANALYTICS_ROOT = Path(__file__).resolve().parents[2] / "src" / "analytics"
if str(ANALYTICS_ROOT) not in sys.path:
    sys.path.insert(0, str(ANALYTICS_ROOT))

from autofpl_analytics.multi_season_evaluation import (  # noqa: E402
    FEATURES,
)
from autofpl_analytics.team_fixture_strength_features import (  # noqa: E402
    FEATURES_WITH_TEAM_FIXTURE_STRENGTH,
    FixtureContext,
    add_team_fixture_strength_features,
    build_team_rate_state,
    load_fixture_contexts,
    team_fixture_strength,
)
from autofpl_analytics.team_goal_strength_evaluation import (  # noqa: E402
    Match,
)
from autofpl_analytics.temporal_ridge import Sample  # noqa: E402


class TeamFixtureStrengthFeatureTests(unittest.TestCase):
    def test_matchup_uses_prior_venue_attack_and_opponent_defence(self) -> None:
        cutoff = datetime(2025, 8, 20, tzinfo=timezone.utc)
        training = [
            self._match(
                1,
                "2025-08-01T15:00:00+00:00",
                "Alpha",
                "Bravo",
                4.0,
                0.5,
            ),
            self._match(
                2,
                "2025-08-08T15:00:00+00:00",
                "Charlie",
                "Delta",
                1.0,
                2.0,
            ),
        ]
        state = build_team_rate_state(training, cutoff)
        values = team_fixture_strength(
            state,
            [
                FixtureContext(
                    "2025-26",
                    0,
                    3,
                    3,
                    "2025-08-22T15:00:00+00:00",
                    "Alpha",
                    "Bravo",
                )
            ],
            "Alpha",
        )

        self.assertGreater(
            values["fixtureTeamExpectedGoals"],
            values["fixtureOpponentExpectedGoals"],
        )
        unseen = team_fixture_strength(
            state,
            [
                FixtureContext(
                    "2025-26",
                    0,
                    3,
                    4,
                    "2025-08-22T15:00:00+00:00",
                    "Echo",
                    "Foxtrot",
                )
            ],
            "Echo",
        )
        self.assertAlmostEqual(
            state.global_attack[True],
            unseen["fixtureTeamExpectedGoals"],
        )
        self.assertAlmostEqual(
            state.global_attack[False],
            unseen["fixtureOpponentExpectedGoals"],
        )

    def test_future_match_cannot_change_earlier_rate_state(self) -> None:
        cutoff = datetime(2025, 8, 20, tzinfo=timezone.utc)
        prior = self._match(
            1,
            "2025-08-01T15:00:00+00:00",
            "Alpha",
            "Bravo",
            1.5,
            0.8,
        )
        future = self._match(
            2,
            "2025-08-30T15:00:00+00:00",
            "Alpha",
            "Bravo",
            8.0,
            8.0,
        )

        with self.assertRaisesRegex(Exception, "after the target cutoff"):
            build_team_rate_state([prior, future], cutoff)

    def test_blank_and_missing_history_are_distinct(self) -> None:
        self.assertEqual(
            {
                "fixtureTeamExpectedGoals": 0.0,
                "fixtureOpponentExpectedGoals": 0.0,
            },
            team_fixture_strength(None, [], "Alpha"),
        )
        fixture = FixtureContext(
            "2025-26",
            0,
            1,
            1,
            "2025-08-01T15:00:00+00:00",
            "Alpha",
            "Bravo",
        )
        self.assertEqual(
            {
                "fixtureTeamExpectedGoals": None,
                "fixtureOpponentExpectedGoals": None,
            },
            team_fixture_strength(None, [fixture], "Alpha"),
        )

    def test_feature_contract_is_fixed(self) -> None:
        sample = Sample(
            "2025-26",
            1,
            7,
            "MID",
            {feature: 0.0 for feature in FEATURES},
            0,
        )
        enriched = add_team_fixture_strength_features(
            sample,
            {
                "fixtureTeamExpectedGoals": 1.5,
                "fixtureOpponentExpectedGoals": 0.9,
            },
        )

        self.assertEqual(
            FEATURES_WITH_TEAM_FIXTURE_STRENGTH,
            tuple(enriched.features),
        )

    def test_fixture_context_loader_does_not_require_outcome_columns(
        self,
    ) -> None:
        connection = sqlite3.connect(":memory:")
        connection.row_factory = sqlite3.Row
        connection.executescript(
            """
            CREATE TABLE historical_fpl_player_gameweeks (
                capture_id INTEGER NOT NULL,
                player_code INTEGER NOT NULL,
                gameweek INTEGER NOT NULL,
                fixture_id INTEGER NOT NULL,
                kickoff_utc TEXT NOT NULL,
                team_name TEXT NOT NULL,
                was_home INTEGER NOT NULL
            );
            INSERT INTO historical_fpl_player_gameweeks VALUES
                (1, 10, 1, 100, '2025-08-01T15:00:00+00:00',
                 'Alpha', 1),
                (1, 20, 1, 100, '2025-08-01T15:00:00+00:00',
                 'Bravo', 0);
            """
        )
        capture = type(
            "Capture",
            (),
            {"capture_id": 1, "season_code": "2025-26"},
        )()

        contexts = load_fixture_contexts(connection, capture, 0)

        self.assertEqual(1, len(contexts))
        self.assertEqual("Alpha", contexts[0].home_team)
        self.assertEqual("Bravo", contexts[0].away_team)

    @staticmethod
    def _match(
        fixture_id: int,
        kickoff: str,
        home: str,
        away: str,
        home_xg: float,
        away_xg: float,
    ) -> Match:
        return Match(
            "2025-26",
            0,
            fixture_id,
            fixture_id,
            kickoff,
            home,
            away,
            round(home_xg),
            round(away_xg),
            home_xg,
            away_xg,
        )


if __name__ == "__main__":
    unittest.main()
