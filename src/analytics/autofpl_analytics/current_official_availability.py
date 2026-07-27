from __future__ import annotations

import math
from typing import Any, Dict, Optional

from .temporal_ridge import TemporalRidgeError

RULE_VERSION = "current-official-appearance-ceiling-v1"
ZERO_CHANCE_STATUSES = frozenset({"i", "n", "s", "u"})


def official_appearance_ceiling(
    status: str,
    chance_next_round: Optional[int],
) -> Dict[str, Any]:
    normalized_status = str(status)
    chance = _chance(chance_next_round)
    if normalized_status == "a":
        if chance not in (None, 100):
            raise TemporalRidgeError(
                "availability.inconsistent-available-status",
                "An available player must have no official chance or 100.",
            )
        ceiling = 1.0
        interpretation = "available-uncapped"
    elif normalized_status == "d":
        if chance is None or chance <= 0 or chance >= 100:
            raise TemporalRidgeError(
                "availability.inconsistent-doubtful-status",
                "A doubtful player requires an official chance between 1 "
                "and 99.",
            )
        ceiling = chance / 100.0
        interpretation = "doubtful-official-upper-bound"
    elif normalized_status in ZERO_CHANCE_STATUSES:
        if chance != 0:
            raise TemporalRidgeError(
                "availability.inconsistent-zero-chance-status",
                "Injured, unavailable, not-available and suspended players "
                "require official chance zero.",
            )
        ceiling = 0.0
        interpretation = "official-zero-upper-bound"
    else:
        raise TemporalRidgeError(
            "availability.unknown-official-status",
            "The official player status is outside the fixed availability "
            "contract.",
        )
    return {
        "ruleVersion": RULE_VERSION,
        "officialStatus": normalized_status,
        "officialChanceOfPlayingNextRound": chance,
        "appearanceProbabilityCeiling": ceiling,
        "interpretation": interpretation,
    }


def constrain_factorized_participation(
    appearance_probability: float,
    start_given_appearance: float,
    played_60_given_appearance: float,
    ceiling: float,
) -> Dict[str, float]:
    appearance = _probability(appearance_probability)
    start_conditional = _probability(start_given_appearance)
    sixty_conditional = _probability(played_60_given_appearance)
    official_ceiling = _probability(ceiling)
    constrained_appearance = min(appearance, official_ceiling)
    return {
        "appearanceProbability": constrained_appearance,
        "startProbability": constrained_appearance * start_conditional,
        "played60Probability": (
            constrained_appearance * sixty_conditional
        ),
    }


def _chance(value: Optional[int]) -> Optional[int]:
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, int):
        raise TemporalRidgeError(
            "availability.invalid-official-chance",
            "Official chance must be a whole percentage or null.",
        )
    if value < 0 or value > 100:
        raise TemporalRidgeError(
            "availability.invalid-official-chance",
            "Official chance must be between zero and 100.",
        )
    return value


def _probability(value: float) -> float:
    number = float(value)
    if not math.isfinite(number) or number < 0.0 or number > 1.0:
        raise TemporalRidgeError(
            "availability.invalid-probability",
            "Availability constraints require probabilities between zero "
            "and one.",
        )
    return number
