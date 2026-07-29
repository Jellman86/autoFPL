# Historical transfer-aware opening challenger v1

## Question

Does jointly selecting the opening squad and a conservative sequence of free
transfers improve realised Gameweek 1–8 FPL points over the retained fixed
six-Gameweek expected-points policy?

This is a reused-holdout challenger screen. The three historical target
outcomes were already opened by the fixed-squad policy comparison, so this
experiment can reject an idea or retain it for a new prospective comparison.
It cannot replace the selected policy or influence advice.

## Fixed transfer policy

The 2026/27 game still awards one free transfer per Gameweek, permits up to five
to be rolled and charges four points for each transfer above the free
allowance. See the official
[2026/27 changes](https://www.premierleague.com/en/news/4679873/all-you-need-to-know-about-changes-to-fpl-for-202627)
and [transfer rules](https://www.premierleague.com/en/news/2174907).

The first challenger deliberately models a narrower, cleanly evaluable subset:

- optimise one legal opening squad and Gameweek 1 roles;
- allow at most one free, same-position transfer before each Gameweek from 2
  through 6;
- enforce the £100.0m budget, exact position quotas and three-player club limit
  after every transfer;
- optimise a legal XI and captain every Gameweek;
- use constant opening prices, no hits and no chips; and
- hold the Gameweek 6 squad through Gameweeks 7–8, selecting those two role
  decisions from preseason scenario means.

The mixed-integer model reaches a reported zero gap. The fixed-squad incumbent
is a feasible special case, so the transfer-aware preseason surrogate cannot
have a lower in-model objective. Realised points still use the common exact FPL
captain-fallback and formation-preserving auto-substitution scorer.

## Fixed screen

The challenger is retained only if it satisfies the same conservative
stability shape as the earlier policy screen:

1. mean Gameweek 1–8 improvement of at least two points;
2. wins in at least two of three target seasons; and
3. no target-season regression worse than two points.

Passing would retain a 2026/27 shadow challenger only. It would not promote it.

## Result

The byte-reproducible comparison rejected the challenger:

| Target | Fixed squad | Transfer-aware | Difference | Planned transfers |
| --- | ---: | ---: | ---: | ---: |
| 2023/24 | 424 | 410 | -14 | 5 |
| 2024/25 | 388 | 314 | -74 | 4 |
| 2025/26 | 357 | 381 | +24 | 5 |
| **Mean difference** |  |  | **-21.33** |  |

It won only one target and its worst regression was 74 points. Every gate
failed, so the decision is `do-not-retain`. The result data identity is
`d5edaa0e05c2de7e503a46fd254c1760d684c8347f06ce8a565e39a6bbdabe3b`
and run identity is
`1f96515aff01dba9afc6345f1b5805e25bf94aa8fce3ff71e7a3c81dfb4c08ee`.
An independent second run was byte-identical.

The plans reveal the failure mode directly. In 2024/25 and 2025/26, the
optimiser repeatedly transferred the same pair of players in opposite
directions as the preseason weekly means alternated. Extra recourse amplified
weak fixture variation instead of adding robust flexibility.

## Decision

Do not add planned transfers to the selected opening prediction. The retained
fixed six-Gameweek squad is materially safer under the available forecast.

The next transfer candidate must wait for a per-fixture player distribution
that has demonstrated meaningful opponent, minutes and role discrimination.
It should then add an explicit transfer-persistence or uncertainty-aware
recourse model and be evaluated on new chronological evidence. Merely adding
banked-transfer state or hit variables to the present forecast would make the
optimiser more expressive without making its inputs more accurate.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_transfer_aware_opening_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-transfer-aware-opening.json
```

The command opens SQLite read-only. Tests cover transfer dynamics, budget,
club and positional legality, exact optimality, exact FPL scoring, a synthetic
fixture swing and the fixed stability gate.
