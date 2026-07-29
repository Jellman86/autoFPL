from __future__ import annotations

import hashlib
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "analytics"))

from autofpl_analytics.current_multi_horizon_initial_squad import (  # noqa: E402
    _build_candidates,
    _optimise_horizon,
    _week_matrices,
)
from autofpl_analytics.transfer_aware_opening_squad import (  # noqa: E402
    OPTIMIZER_VERSION,
    optimise_transfer_aware_horizon,
    score_transfer_aware_plan,
)
from tests.analytics import (  # noqa: E402
    test_current_multi_horizon_initial_squad as multi_squad_fixture,
)


class TransferAwareOpeningSquadTests(unittest.TestCase):
    def test_exact_plan_is_legal_deterministic_and_dominates_fixed_plan(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            scenario = (
                multi_squad_fixture.CurrentMultiHorizonInitialSquadTests
                ._database_and_scenario(database)
            )
            candidates = _build_candidates(database, scenario)
            matrices = _week_matrices(scenario)
            before = hashlib.sha256(database.read_bytes()).hexdigest()

            first, first_solver = optimise_transfer_aware_horizon(
                candidates,
                matrices,
                3,
            )
            second, second_solver = optimise_transfer_aware_horizon(
                candidates,
                matrices,
                3,
            )
            _, fixed_solver = _optimise_horizon(
                candidates,
                matrices,
                3,
                0.0,
            )
            score = score_transfer_aware_plan(
                first,
                candidates,
                matrices,
            )
            after = hashlib.sha256(database.read_bytes()).hexdigest()

        self.assertEqual(before, after)
        self.assertEqual(first, second)
        self.assertEqual(first_solver, second_solver)
        self.assertEqual(OPTIMIZER_VERSION, first_solver["optimizerVersion"])
        self.assertEqual(0.0, first_solver["mipGap"])
        self.assertGreaterEqual(
            first_solver["objectiveValue"] + 1e-6,
            fixed_solver["objectiveValue"],
        )
        self.assertEqual(3, len(first["gameweeks"]))
        self.assertEqual(3, len(score["weekly"]))
        self.assertEqual(5, len(score["pathTotalPoints"]))
        self.assertLessEqual(first["plannedTransferCount"], 2)
        by_id = {
            int(player["playerId"]): player for player in candidates
        }
        previous = None
        for week in first["gameweeks"]:
            squad = set(week["squadPlayerIds"])
            self.assertEqual(15, len(squad))
            self.assertLessEqual(week["budgetTenths"], 1000)
            self.assertEqual(11, len(week["startingPlayerIds"]))
            self.assertIn(
                week["captainPlayerId"],
                week["startingPlayerIds"],
            )
            self.assertIn(
                week["viceCaptainPlayerId"],
                week["startingPlayerIds"],
            )
            self.assertEqual(
                {
                    "goalkeeper": 2,
                    "defender": 5,
                    "midfielder": 5,
                    "forward": 3,
                },
                {
                    position: sum(
                        by_id[player_id]["position"] == position
                        for player_id in squad
                    )
                    for position in (
                        "goalkeeper",
                        "defender",
                        "midfielder",
                        "forward",
                    )
                },
            )
            self.assertLessEqual(
                max(
                    sum(
                        int(by_id[player_id]["teamId"]) == team_id
                        for player_id in squad
                    )
                    for team_id in {
                        int(by_id[player_id]["teamId"])
                        for player_id in squad
                    }
                ),
                3,
            )
            if previous is not None:
                self.assertLessEqual(len(squad - previous), 1)
                self.assertEqual(
                    len(squad - previous),
                    len(previous - squad),
                )
            previous = squad

    def test_fixture_swing_produces_one_position_matched_transfer(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "autofpl.db"
            scenario = (
                multi_squad_fixture.CurrentMultiHorizonInitialSquadTests
                ._database_and_scenario(database)
            )
            candidates = _build_candidates(database, scenario)
            matrices = [
                (points.copy(), played.copy())
                for points, played in _week_matrices(scenario)[:3]
            ]
            forward_columns = [
                index
                for index, player in enumerate(candidates)
                if player["position"] == "forward"
            ]
            for points, _ in matrices:
                points[:, forward_columns] = 0
            matrices[0][0][:, forward_columns[0]] = 100
            matrices[0][0][:, forward_columns[1:3]] = 50
            matrices[1][0][:, forward_columns[-1]] = 100
            matrices[1][0][:, forward_columns[1:3]] = 50
            matrices[2][0][:, forward_columns[-1]] = 100
            matrices[2][0][:, forward_columns[1:3]] = 50

            plan, _ = optimise_transfer_aware_horizon(
                candidates,
                matrices,
                3,
            )

        transfers = [
            row["plannedTransfer"]
            for row in plan["gameweeks"]
            if row["plannedTransfer"] is not None
        ]
        self.assertGreaterEqual(len(transfers), 1)
        by_id = {
            int(player["playerId"]): player for player in candidates
        }
        for transfer in transfers:
            self.assertEqual(
                by_id[transfer["playerOutId"]]["position"],
                by_id[transfer["playerInId"]]["position"],
            )
            self.assertEqual(0, transfer["pointCost"])


if __name__ == "__main__":
    unittest.main()
