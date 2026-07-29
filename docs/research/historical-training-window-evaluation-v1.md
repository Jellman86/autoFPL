# Historical training-window evaluation v1

## Question

Does extending the unchanged retained player-point model from two historical
seasons to all four pinned seasons materially improve out-of-time point
accuracy?

More observations can reduce variance and cover more prior player histories,
but older seasons also add concept drift from changing teams, roles, rules and
playing styles. The training window is therefore an empirical model choice,
not an assumption that more data is always better.

## Fixed comparison

The incumbent trains the frozen `multi-season-histogram-tree` on 2024/25 and
the available earlier 2025/26 Gameweeks. The challenger keeps the exact model,
features and hyperparameters but adds 2022/23 and 2023/24.

Both variants are evaluated on the same Gameweeks 31–38 of 2025/26. Every fold:

- uses the same immutable target archive and exact official player codes;
- trains only on chronologically earlier player-Gameweeks;
- receives the same target player cohort and total-point labels;
- fits all preprocessing and tree state inside the fold; and
- reports aggregate, position and per-Gameweek errors.

The challenger is retained only if it satisfies every fixed gate:

1. aggregate MAE improves by at least 1%;
2. aggregate RMSE does not regress;
3. it wins a strict majority of the eight Gameweek folds; and
4. no position's MAE regresses by more than 5%.

These outcomes were already opened by earlier research. Passing can retain an
explicit four-season prospective shadow; it cannot promote a model or change
served advice.

## Result

The four-season challenger improved most diagnostics but missed the fixed
primary threshold:

| Metric | Two seasons | Four seasons | Change |
| --- | ---: | ---: | ---: |
| MAE | 0.963833 | 0.956107 | -0.80% |
| RMSE | 1.909170 | 1.903441 | -0.005729 |
| Mean error | 0.025308 | 0.006994 | -0.018314 |
| Gameweek fold wins | — | 7/8 | — |

Goalkeeper MAE improved by 2.11%, defender MAE by 1.21% and midfielder MAE by
0.56%. Forward MAE regressed by 0.26%, well inside the 5% stability limit.
The RMSE, fold-win and position-stability gates therefore passed.

Aggregate MAE improved by 0.8016%, below the predeclared 1% threshold. The
decision is `do-not-retain-four-season-window`. The threshold is not relaxed
after observing a near miss: equal-weight older seasons remain excluded from
the current point forecast and opening squad.

The result data identity is
`604817000f73e9b6c3471802569bedafc8cc4898235a2028321b69c25f3b4e19`
and run identity is
`61e1d6b932e8649cdb916326868d580115e45ec7a261ffd5759999beb91a99b9`.
An independent second complete run was byte-identical.

## Interpretation

The experiment changes both the number of training rows and the historical
state available for exact-code players. It answers the practical training
window question for the complete frozen pipeline, not a narrower causal
question about why any gain occurred.

The result suggests older exact-code history carries some useful signal, but
does not establish an accepted way to weight it. Any recency weighting or
partial pooling must be specified independently and cannot be tuned to make
this already-open result pass.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_training_window_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-training-window-evaluation.json
```

The command opens SQLite read-only, emits canonical source, data and run
identities and refuses output overwrite.
