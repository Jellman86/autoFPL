# Historical player attacking-component evaluation v1

## Question

Does a coherent marked team-goal process improve individual player goal and
assist forecasts enough to justify rebuilding the historical opening point
distributions?

The component must first beat a direct player model on strictly later events.
A good team scoreline model is not sufficient evidence by itself.

## Fixed factorization

The experiment scores Gameweeks 31–38 in each of the three registered target
seasons, producing 24 expanding-origin folds. Every feature, event model,
attribution fraction and team scoreline fit uses only earlier origins.
Double-fixture player-Gameweeks are excluded from the scored cohort because an
aggregated label cannot identify the fixture that received an event; their raw
fixture events remain in the earlier training attribution totals.

Two conditional Poisson histogram models estimate each appearing player's goal
and assist propensity from the cutoff-safe temporal features. The direct
incumbent multiplies each propensity by appearance probability. The challenger
normalizes those player intensities within each fixture team and reconstructs:

```text
player event intensity =
  Dixon–Coles team goal intensity
  × expanding-fold attributed-event fraction
  × normalized appearance-adjusted player propensity
```

The attributed fraction is one for recorded goals except rare archive
differences, and approximately 0.89–0.91 for assists. It is estimated from all
earlier raw player-fixture rows. Two controls use the same player shares with
league home/away rates, and Dixon–Coles rates with position-only shares.

The fixed gate requires at least 1% combined Poisson-NLL improvement over the
direct player model, no goal or assist NLL/Brier regression, at least 13 of 24
fold wins, no position NLL regression above 5%, and lower NLL than both
controls.

## Result

The team-conditioned player-share model improves every measured slice, but
misses the materiality gate:

| Metric | Direct player Poisson | Team-conditioned allocation | Change |
| --- | ---: | ---: | ---: |
| Combined Poisson NLL | 0.109411 | **0.108831** | 0.53% improvement |
| Combined event Brier | 0.026379 | **0.026277** | improvement |
| Absolute calibration error | 0.000968 | **0.000141** | improvement |
| Mean absolute error | 0.058528 | **0.057520** | improvement |
| Fold wins | — | 15/24 | passes |

Both event types improve:

| Event | Direct NLL | Challenger NLL | NLL improvement | Brier change |
| --- | ---: | ---: | ---: | ---: |
| Goal | 0.109100 | **0.108520** | 0.53% | improves 0.22% |
| Assist | 0.109723 | **0.109143** | 0.53% | improves 0.56% |

The challenger also improves defender, forward, goalkeeper and midfielder NLL,
and beats the league-rate player-share control (0.110044) and the
position-share control (0.113990). The gain is stable but still roughly half
the preregistered 1% threshold. The 2025/26 target is also mixed at fold level,
which argues against spending opened-target degrees of freedom on calibration
or share variants.

## Decision

Do not retain the attacking-event component and do not construct an opening
point distribution from it. The served v2 initial squad and retained hurdle
paths remain unchanged.

This is a useful near-miss rather than evidence that team state is irrelevant:
the coherent allocation improves goals, assists, every position, calibration
and a strict majority of folds. It is preserved as a frozen challenger for
genuinely prospective evidence, but its gate must not be weakened after
observing these outcomes.

The next initial-squad priority is the already registered 3/6/8-Gameweek
decision-horizon comparison on the retained distributions. Choosing the
historically supported horizon can change the globally optimal 15-player squad
directly; another small event-component variation cannot precede it.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_player_attacking_component_evaluation \
  --database /path/to/autofpl.db \
  --scoreline-evaluation \
    docs/research/results/historical-scoreline-replication-evaluation-v1.json \
  --output /path/to/historical-player-attacking-component-evaluation.json
```

The retained result is
[historical-player-attacking-component-evaluation-v1.json](results/historical-player-attacking-component-evaluation-v1.json).
Its data identity is
`c4d64c7fdc48a476d404bac938e1cf0d5931baa6d918a60b133ded95f418ec9a`
and its run identity is
`8bad9f99f2ad521b9999460d47eb42bc44a98d08574b6c8f43c2a7343e5b0bed`.
The retained file SHA-256 is
`7b8860170153735a5d0ec77de531f11c66cb37b6c3de3670f6ff2b9a650f773b`.
