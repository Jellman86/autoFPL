# Historical start-state hurdle points evaluation v1

## Question

Does separating a player's Gameweek into starter, substitute appearance and
zero-minutes states improve the retained appearance-hurdle point mean?

This is the direct follow-up to the selected-squad lineup audit. It tests
whether the already available historical start target can make external
predicted-XI evidence useful without treating a non-start as a non-appearance.

## Fixed method

The challenger preserves the retained appearance classifier and adds the fixed
historical start classifier on the same expanding origins. Start probability is
made coherent without changing appearance:

```text
coherent P(start) = min(raw P(start), P(appearance))
P(substitute appearance) = P(appearance) - coherent P(start)

E(points) =
  P(start) × E(points | at least one start)
  + P(substitute appearance) × E(points | appearance and zero starts)
```

The two conditional point trees use only strictly earlier origins. A player
with at least one start in a double Gameweek belongs to the starter state for
that aggregated row. The incumbent is rebuilt on the identical target players
as `P(appearance) × E(points | appearance)`.

The preregistered screen requires at least 1% aggregate MAE improvement, RMSE
non-regression, a strict majority of fold wins, no position MAE regression
above 5%, and no Brier or log-loss regression from the start-coherence rule.

## Result

The start coherence rule works as intended. It adjusts 42 of 6,252
player-Gameweeks, improves start Brier score from `0.081505` to `0.081392`,
and improves start log loss from `0.259305` to `0.259005`.

The point mixture does not pass:

| Metric | Appearance hurdle | Start-state hurdle | Change |
| --- | ---: | ---: | ---: |
| MAE | 0.945900 | 0.945133 | +0.0811% improvement |
| RMSE | 1.903293 | 1.910812 | +0.007519 regression |
| Fold wins | — | 4/8 | no strict majority |

Forwards, goalkeepers and midfielders improve slightly on MAE, while defenders
regress by 0.368%. All position changes remain inside the stability limit, but
the aggregate materiality, RMSE and fold-win gates fail.

## Decision

Reject this start-state point mean and leave selected opening-squad v2
unchanged. Do not tune the clipping rule, conditional trees or gate on these
opened folds.

The useful conclusion is narrower: start probabilities can be made coherent
and lineup evidence can target the correct state, but another partition of the
same aggregate point tree is not the missing source of material accuracy. The
next point challenger should model fixture-level scoring components—minutes,
goals, assists, clean sheets, saves, cards, bonus and the relevant
team/opponent state—conditional on participation, then reconstruct the exact
FPL point distribution. Predicted-lineup evidence remains an input to the
participation component, not a direct point multiplier.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_start_state_hurdle_points_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-start-state-hurdle-points-evaluation.json
```

The retained result is
[historical-start-state-hurdle-points-evaluation-v1.json](results/historical-start-state-hurdle-points-evaluation-v1.json).
Its data identity is
`1f090566ec308b0225e27401652a8cbcfe6b3712c4d3004abc80e2c5afa5abd9`
and its run identity is
`d584042ae896cfe6e6fc10afc0b07b70127e2d9e55683619ccaf2935d2e97b65`.
An independent rerun was byte-identical with file SHA-256
`20407e49572204889e4362ddb468b5cc52cc2a4abe75306d8e5bbd8db6159765`.
