# Historical promoted-player opening evaluation v1

## Decision

Reject the translated Championship appearance pool from the served initial
squad. Retain it as a strong player-forecast result and as a frozen
prospective challenger, but do not change the current 2026/27 opening policy
from this retrospective evidence.

The feature passed every affected-player distribution gate and every
all-player non-regression gate. It did not pass the already registered
complete-squad decision gate:

- affected-player CRPS improved by `8.9397%`;
- affected-player appearance Brier improved by `0.057079`;
- all-player CRPS improved by `0.006474`;
- all three target seasons and all four affected positions improved;
- realised squad points changed by `0`, `+3` and `0`;
- the mean squad gain was `+1.0`, below the required `+2.0`; and
- only one target won, below the required two.

The squad membership was identical in every target season. The three-point
gain came from a role decision, not a different 15-player squad.

Lowering the policy gate after observing this result would be post-hoc
selection. The correct conclusion is that prior-Championship participation
improves promoted-player distributions but has not yet shown enough complete
initial-squad utility.

## Isolation

The incumbent is the retained appearance-hurdle opening scenario family. The
challenger changes only appearance probabilities for deterministically
bridged promoted players:

```text
0.5 × retained opening appearance probability
+ 0.5 × translated Championship appearance probability
```

Everything else is identical:

- earlier-FPL-season feature training;
- conditional expected points given appearance;
- fixture proxy;
- latest-prior-season joint donor rows;
- weekly path permutation;
- six-Gameweek expected-points objective;
- prices, budget, club and position constraints;
- starting formation, bench and captaincy rules; and
- exact eight-Gameweek FPL outcome scorer.

Unbridged and non-promoted players retain the incumbent appearance
probability exactly.

## Temporal design

For each target:

1. the source logistic model is fit only on strictly earlier promotion
   classes;
2. the target Gameweek 1 roster and prior-season Championship aggregate are
   bridged without target outcomes;
3. incumbent and challenger joint point/appearance paths are reconstructed
   with target performance fields unread;
4. both scenario families are frozen and identity-hashed;
5. the same global policy solver selects the complete squad and roles; and
6. only then are Gameweek 1–8 outcomes opened for proper scores and exact FPL
   policy scoring.

The historical outcomes were already used by earlier registered experiments.
This experiment can reject or retain a current challenger; it cannot produce
prospective promotion evidence.

## Fixed gates

### Affected-player distribution

- at least 1% aggregate CRPS improvement;
- at least two target-season CRPS wins;
- no affected position CRPS regression greater than 5%;
- no appearance Brier regression; and
- no appearance log-loss regression.

### All-player safety

- no aggregate CRPS regression;
- no aggregate appearance Brier regression; and
- no aggregate appearance log-loss regression.

### Complete squad policy

- at least `+2.0` mean realised points;
- at least two target-season wins; and
- no target regression worse than two points.

All three groups had to pass.

## Distribution result

| Target | Affected players | Incumbent CRPS | Challenger CRPS | Result |
|---|---:|---:|---:|---|
| 2023/24 | 70 | 0.604752 | 0.552313 | win |
| 2024/25 | 64 | 0.748512 | 0.657808 | win |
| 2025/26 | 69 | 0.753469 | 0.706528 | win |

Affected-position CRPS changes were all favourable:

| Position | Relative CRPS change |
|---|---:|
| Goalkeeper | -29.16% |
| Defender | -3.52% |
| Midfielder | -11.29% |
| Forward | -5.79% |

Across the complete 15,712 player-Gameweek population, mean CRPS improved
from `0.905546` to `0.899072`. Appearance Brier improved from `0.203814` to
`0.197914`, and log loss improved from `0.609646` to `0.592410`.

## Policy result

| Target | Incumbent points | Challenger points | Difference | Squad membership |
|---|---:|---:|---:|---|
| 2023/24 | 461 | 461 | 0 | identical |
| 2024/25 | 392 | 395 | +3 | identical |
| 2025/26 | 378 | 378 | 0 | identical |
| **Mean** | **410.33** | **411.33** | **+1.00** | |

There were no losses, but stability without sufficient gain does not satisfy
the initial-squad promotion rule.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_promoted_opening_evaluation \
  --database /path/to/autofpl.db \
  --fbref-extraction 2021-22=/path/to/2021-22-extraction.json \
  --fbref-extraction 2022-23=/path/to/2022-23-extraction.json \
  --fbref-extraction 2023-24=/path/to/2023-24-extraction.json \
  --fbref-extraction 2024-25=/path/to/2024-25-extraction.json \
  --output /path/to/historical-promoted-opening-evaluation-v1.json
```

The retained result has data identity
`f35f876a39d7d8766513d83a8a63596b03003dcdc0d83b7889324d451eef707c`
and run identity
`9e57578c3666310e48a8c96efe196a549a849ded590ee0af2ca98a5c60081149`.

## What follows

Do not tune the blend or policy on these opened outcomes. Preserve the current
six-Gameweek v2 squad as the served prediction. The translated feature may be
frozen for current sensitivity analysis and future 2026/27 scoring, where new
outcomes can provide genuinely prospective decision evidence.

The next initial-squad work should target evidence capable of changing
complete decisions: current promoted-player temporal match logs, reliable
pre-deadline availability/lineup evidence, and the registered 3/6/8-Gameweek
robust policy comparison. Each remains a challenger until it improves full
decision utility.
