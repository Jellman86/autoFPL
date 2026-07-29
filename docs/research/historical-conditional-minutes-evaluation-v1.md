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
