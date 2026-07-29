# Historical conditional-minutes evaluation v1

## Question and status

Can a two-stage appearance hurdle improve unconditional player-Gameweek
minutes over both the retained player-last baseline and the previously rejected
unconditional histogram tree?

The original minutes holdout is already open. This is an exploratory
reused-holdout candidate screen that may freeze a prospective 2026/27
challenger but cannot promote a model or alter the current artifact.

## Fixed candidate

The existing supported histogram classifier estimates appearance probability
from cutoff-safe prior performance and target fixture facts. A histogram
regressor with the unchanged feature and hyperparameter contract is fitted
only on training rows where the player appeared:

```text
E[minutes] =
    P(appearance)
    × bounded E[minutes | appearance]
```

Conditional minutes are bounded between zero and 90 times the known target
fixture count before multiplication. This supports double Gameweeks without
allowing an unconstrained regressor to manufacture impossible minutes.
Appearance outcomes select only training rows; no target appearance or minutes
outcome enters its own prediction.

## Identical-fold comparison

Gameweeks 31–38 of the immutable 2025/26 archive use expanding origins. The
candidate is compared with:

1. the retained last-observed player minutes baseline; and
2. the original unconditional histogram tree trained on the zero-heavy
   mixture.

Against each reference independently, the candidate must improve aggregate MAE
by at least 1%, avoid RMSE regression, win a strict majority of folds and avoid
position MAE regression above 5%. Passing retains the exact specification only
for a prospective player-distribution shadow. Promotion and product import
remain prohibited.

## Result

The Quark run at application revision
`7621b735c9a3635552830840771c570dae3679c3` completed all eight folds and
scored 6,252 player-Gameweeks:

| Model | MAE | RMSE |
|---|---:|---:|
| Player-last minutes | **12.280442** | 28.954555 |
| Appearance-hurdle conditional tree | 13.286008 | **23.164577** |
| Unconditional histogram tree | 13.470439 | 23.369100 |

The hurdle improved MAE by 1.3692% over the unconditional tree, reduced RMSE
by 0.204523 minutes and won seven of eight folds. It nevertheless regressed MAE
by 8.1884% against player-last, won only one of eight folds and crossed the 5%
position-MAE tolerance for goalkeeper, defender and midfielder. It therefore
failed the dual-reference gate and remains excluded from current minutes and
player-point forecasts.

The much lower RMSE alongside worse MAE is useful diagnostic evidence: a
deterministic last-value comparator and a smooth conditional mean make
different errors on the zero-heavy minutes target. The gate must not be relaxed
after seeing this result. A subsequent candidate should model and score the
complete minutes distribution with a proper distributional score such as CRPS,
rather than tune another mean estimator on these opened folds.

## Reproduction

```shell
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_conditional_minutes_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-conditional-minutes.json
```

The evaluator opens SQLite read-only, is deterministic and refuses output
overwrite. Tests verify exact bounded factorization, aligned targets and that a
future minutes outcome cannot alter an earlier fold.

## Next boundary

If the fixed screen passes, the model may be emitted beside the current raw
minutes baseline as a prospectively scored shadow. It still supplies only an
expected-minutes component; a full player-points distribution must combine
minutes uncertainty with conditional event rates and shared match state before
the 3-, 6- and 8-Gameweek opening-squad policies are compared.
