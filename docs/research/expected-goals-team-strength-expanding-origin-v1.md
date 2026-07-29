# Expected-goals team-strength expanding-origin evaluation v1

## Question

Does estimating latent team attack and defence from expected goals produce
better future score distributions than both a league Poisson baseline and the
validated realized-goal Dixon–Coles model?

This is a separately labelled exploratory candidate screen. The 2025/26 folds
were already opened by the realized-goal experiment, so this result can reject
or retain a specification for prospective shadow testing but cannot promote it.

## Fixed candidate

Player expected goals from the immutable FPL history archive are summed within
each exact team/fixture perspective. Non-finite or negative values fail closed.
This yields team expected goals without a name-based player join or a
third-party xG feed.

The candidate uses the same fixed 180-day decay, attack/defence/home equations,
L2 regularisation, two-stage fit and Dixon–Coles low-score correction as the
realized-goal model. The only changed input is the rate-fitting target: team
expected goals replaces realized goals in a weighted Poisson quasi-likelihood.
The low-score correlation remains fitted against realized score pairs.

Expected goals are used because realized goals are a noisy sample of attacking
opportunity. This is a candidate-generation rationale, not evidence of gain;
the chronological comparison decides whether it helps.

## Identical-fold comparison

Gameweeks 31–38 of 2025/26 compare:

1. weighted league home/away Poisson rates;
2. time-decayed realized-goal Dixon–Coles strengths; and
3. time-decayed expected-goals Dixon–Coles strengths.

All models receive identical prior matches and target fixtures. Metrics remain
joint-score NLL, result NLL/Brier, goal RMSE and clean-sheet Brier.

The xG candidate must independently beat both references by at least 1% joint
NLL, avoid result-NLL and goal-RMSE regression, and win a strict majority of
folds against each. Passing retains it only for player-distribution ablation;
promotion remains prohibited.

## Result

The Quark run at application revision
`0af00788e12ace8c98e5a8ddc8032fd698a5fef2` completed all eight registered
folds and scored 79 matches:

| Model | Joint NLL | Outcome NLL | Goal RMSE | Fold wins by xG |
|---|---:|---:|---:|---:|
| League home/away Poisson | 2.888884 | 1.062630 | 1.083032 | 5/8 |
| Realized-goal Dixon–Coles | 2.877264 | 1.011422 | 1.080398 | 6/8 |
| Expected-goals Dixon–Coles | **2.866422** | 1.016645 | **1.075289** | — |

Expected goals improved joint NLL by 0.7775% against the league baseline and
0.3768% against realized-goal Dixon–Coles. It improved goal RMSE against both,
but missed the fixed 1% joint-NLL threshold against both and regressed outcome
NLL by 0.005223 against realized goals.

The screen therefore failed. The expected-goals rates remain excluded from
player forecasts and initial-squad advice. The ranking is informative candidate
evidence, but these already-opened folds must not be used to relax the gate or
tune a blend.

## Reproduction

```shell
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.xg_team_strength_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/xg-team-strength-report.json
```

The evaluator is read-only and deterministic. Tests verify identical folds,
explicit goals/xG fitting targets, and that changing future expected goals
cannot alter an earlier fold.

## Next boundary

Only a retained match model may be translated to current official fixture
rates. Those rates still have to improve player FPL-point distributions under
CRPS, calibration and squad decision-utility gates before entering any
3/6/8-Gameweek optimizer.
