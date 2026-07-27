# Current official availability ceiling v1

## Purpose and status

The current official FPL player state contains decision-time information that
the prior-season participation models could not have learned. This rule
registers a deterministic, timestamped comparison variant before 2026/27
outcomes are known. It is **prospective and unscored**, not a promoted model,
and cannot influence advice.

## Fixed mapping

Official status and `chance_of_playing_next_round` define an upper bound on
appearance probability:

| Official state | Required chance | Appearance ceiling |
| --- | ---: | ---: |
| Available (`a`) | null or 100 | 1.00 |
| Doubtful (`d`) | 1–99 | chance / 100 |
| Injured (`i`) | 0 | 0.00 |
| Not available (`n`) | 0 | 0.00 |
| Suspended (`s`) | 0 | 0.00 |
| Unavailable (`u`) | 0 | 0.00 |

Unknown states, out-of-range values and inconsistent state/chance pairs fail
closed. The current forecast continues to exclude unavailable players under
its existing eligibility rule.

## Why a ceiling

The official percentage is not multiplied by raw appearance. Multiplication
would treat the official assessment and historical participation model as
independent probabilities, which is neither known nor evaluated. Instead:

```text
constrained appearance = min(model appearance, official ceiling)
constrained start =
    constrained appearance × P(start | appearance)
constrained 60+ =
    constrained appearance × P(60+ | appearance)
```

This preserves the frozen conditional child rates and guarantees both child
marginals remain no greater than appearance. Available players are unchanged;
zero-chance players are zeroed; and a doubtful player is adjusted only when
the model exceeds the official ceiling.

The coherent conditional model failed its historical start fold gate, so this
availability-constrained variant remains a comparison candidate rather than a
serving forecast.

## Prospective evaluation

Every current artifact retains the exact official capture, decision cutoff,
availability rule version and raw/conditional evaluation identities. Once
outcomes exist, compare these fixed variants on identical player-Gameweek
rows:

1. raw independent probabilities;
2. coherent factorized probabilities; and
3. official-ceiling factorized probabilities.

Report Brier score, log loss and ten-bin calibration overall and by Gameweek,
position and official status. Do not select a variant before the fixed
minimum-fold requirement is met. Later official captures form new immutable
forecast revisions; they do not rewrite an earlier cutoff.

## First registered artifact

The first v1.2 Quark artifact retained the exact v1.1 official capture,
decision cutoff and 557-player cohort. The raw variant still had 17 incoherent
players; both factorized variants were coherent for all 557.

The official ceiling changed 32 players:

| Official state | Captured players | Appearance capped |
| --- | ---: | ---: |
| Doubtful at 75% | 18 | 5 |
| Injured at 0% | 24 | 24 |
| Suspended at 0% | 3 | 3 |

No available player was changed. Mean appearance moved from 0.520447 to
0.505113; mean start moved from 0.380548 in the coherent factorization to
0.371343 after the ceiling; and mean 60+ moved from 0.354548 to 0.345873.
These are descriptive changes, not evidence of improved accuracy.

The complete per-player artifact is retained as
[`preseason-participation-forecast-2026-27-gw1-v1.2.json`](results/preseason-participation-forecast-2026-27-gw1-v1.2.json)
so future outcomes can score the exact probabilities. Its file SHA-256 is
`524c02c095fc7f62f4962fa54301c749bcd0550a09ff7d95c55225271f4110c2`;
its internal run identity is
`4f3a4fa8829eee7bf0ad58e8fca316a75332b8281dc53a1d8f50ce5ee5553e5c`.

## Product boundary

The ceiling resolves how current official evidence will be represented, but
not whether it improves forecasts. Product import remains blocked until the
prospective outcome gate passes, promoted/new-player coverage is evaluated
and a complete point distribution is ready for decision use.
