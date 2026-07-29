# Time-decayed Dixon–Coles expanding-origin evaluation v1

## Question

Can a dynamic latent team attack/defence model forecast Premier League score
distributions better than a cutoff-correct league-rate Poisson baseline,
strongly enough to supply fixture context to player distributions?

This is a match-model screen, not a player-points or squad-policy promotion.
It follows the rejected rolling FPL-points proxy with a model of the underlying
football process.

## Fixed model

Historical matches are reconstructed from the immutable FPL player archive.
For each fixture, the two team perspectives must agree on one fixture,
Gameweek, kickoff and home/away assignment. Team goals are the sum of player
goals for that team and fixture. Ambiguous reconstruction fails closed.

For home team `i` and away team `j`, the challenger estimates

```text
log(lambda_ij) = intercept + home_advantage + attack_i + defence_j
log(mu_ij)     = intercept + attack_j + defence_i
```

and applies the Dixon–Coles low-score correction to the two Poisson
distributions. Earlier matches receive an exponentially decaying weight with a
fixed 180-day half-life. Attack and defence effects receive fixed L2
regularisation; a mean-attack penalty resolves model identifiability. The
correlation parameter is bounded to `[-0.20, 0.20]`. For numerical stability,
the fixed two-stage likelihood first fits the weighted Poisson attack, defence
and home effects with an analytic gradient, then fits the one-dimensional
low-score correlation conditional on those rates. No parameter is selected
using the evaluation folds.

The design is based on the dynamic team-strength and low-score likelihood in
[Dixon and Coles (1997)](https://doi.org/10.1111/1467-9876.00065).
Distributional comparison follows the proper-scoring principle in
[Gneiting and Raftery (2007)](https://doi.org/10.1198/016214506000001437):
the primary metric is mean negative log likelihood of the realized joint
score, rather than accuracy of a single predicted result.

## Chronology and baselines

Gameweeks 31–38 of the latest archived season are expanding-origin folds.
Every fold fits from scratch using only matches in earlier season/Gameweek
origins. Recency weights are measured from the first target kickoff. Team names
are the cross-season identity; a previously unseen team receives league-average
attack and defence effects.

The fixed baseline estimates independent home- and away-goal Poisson rates
from the same weighted training matches. Both models receive identical
training and target fixtures.

Metrics are:

- mean joint-score negative log likelihood;
- three-way result negative log likelihood and Brier score;
- goal-rate RMSE; and
- clean-sheet Brier score.

The challenger passes only with at least 1% joint-score NLL improvement, no
result-NLL regression, no goal-RMSE regression and a strict majority of fold
wins. Passing retains its rates for a player-distribution ablation. It cannot
directly influence advice.

## Reproduction

```shell
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.team_goal_strength_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/team-goal-strength-report.json
```

The command opens SQLite read-only, emits a deterministic run identity and
refuses to overwrite an existing report. Tests verify score-matrix
normalisation, deterministic fitting, exact match reconstruction and that a
later result cannot change an earlier fold.

## Next boundary

If retained, the frozen model will produce expected team goals, opponent goals
and clean-sheet probabilities for every current official fixture over the
registered 3/6/8-Gameweek horizons. Those values must then improve player
point distributions under CRPS/calibration and decision-utility gates before
they may enter the opening-squad optimizer.
