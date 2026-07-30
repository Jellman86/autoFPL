# Historical scoring-component reconstruction audit v1

## Question

Can autoFPL reproduce every archived fixture-level FPL point exactly from
observable scoring components before those components become separate
prediction targets?

An event model is only useful if its simulated goals, assists, minutes, clean
sheets and other events combine under the real game rules. This audit tests
that deterministic boundary; it does not fit or promote a forecast.

## Fixed method

The read-only audit uses the four registered, hash-pinned historical season
captures from 2022/23 through 2025/26. It decompresses each retained raw
fixture payload, verifies its SHA-256 identity, keeps only players in the
persisted official position catalogue and deduplicates only byte-equivalent
player/Gameweek/fixture identities.

It reconstructs total points as the sum of:

- appearance points from fixture minutes;
- position-specific goals, assists and clean sheets;
- goalkeeper saves and penalty saves;
- goalkeeper/defender goals-conceded deductions;
- penalty misses, cards and own goals;
- final archived bonus; and
- the 2025/26 defensive-contribution threshold and two-point cap.

Season rules are explicit. Goalkeeper goals change from six to ten points in
2024/25. Defensive-contribution points begin only in 2025/26. The rule
interpretation follows the Premier League's
[2025/26 scoring table](https://www.premierleague.com/en/news/2174909),
[defensive-contribution explanation](https://www.premierleague.com/en/news/4361991/)
and
[2024/25 goalkeeper change](https://www.premierleague.com/en/news/4058895).

The preregistered structural gate requires all four exact archives and at
least 99.9% exact fixture-row reconstruction. The small tolerance is a
fail-closed diagnostic allowance for a documented provider correction; it is
not permission to absorb residuals into a learned model.

## Result

All 113,260 persisted player-fixture rows reconstruct exactly:

| Season | Rows | Exact | MAE | RMSE |
| --- | ---: | ---: | ---: | ---: |
| 2022/23 | 26,505 | 100% | 0 | 0 |
| 2023/24 | 29,725 | 100% | 0 | 0 |
| 2024/25 | 27,283 | 100% | 0 | 0 |
| 2025/26 | 29,747 | 100% | 0 | 0 |
| **All** | **113,260** | **100%** | **0** | **0** |

The raw 2025/26 payload contains ten exact duplicate player/fixture rows; these
collapse to the already persisted identities and do not change any value.

## Decision

Retain this deterministic reconstruction as the target boundary for the
fixture-event model. The next predictive challenger should estimate component
distributions conditional on participation and shared team/opponent match
state, then pass their simulated components through this scorer.

Start with the largest stable components rather than one monolithic event
model:

1. model player minutes/participation jointly with team lineups;
2. model team scorelines and clean-sheet/goals-conceded state;
3. allocate goal and assist intensities to players conditional on minutes;
4. add saves, cards, defensive contributions and bonus where their
   out-of-time ablations improve proper scores; and
5. compare the reconstructed point distribution and complete 3/6/8-Gameweek
   squad policy with the retained incumbents.

Final bonus and assist awards are outcomes here, not future-known inputs.
Every future component feature remains cutoff-bound and any rule change must
be versioned for the forecast season.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_scoring_component_reconstruction_audit \
  --database /path/to/autofpl.db \
  --output /path/to/historical-scoring-component-reconstruction-audit.json
```

The retained result is
[historical-scoring-component-reconstruction-audit-v1.json](results/historical-scoring-component-reconstruction-audit-v1.json).
Its data identity is
`00fc49d961f07e4d908d5d25f05b5d369105782e95f1f74b09281c534ebf5311`
and its run identity is
`f3bd2e7057039c3cd7d34b76af926328d9c30dd75cf43f63dacb138695d3b663`.
The retained file SHA-256 is
`33c65cfbe9a958b84d2e72c96015de1fd95d201afb7e41bede6835ee16dbcbbe`.
