# Historical promoted-player appearance evaluation v1

## Decision

Retain the fixed prior-Championship appearance challenger for a separate full
point-distribution and initial-squad policy evaluation. Do not yet alter the
served player forecast or opening squad.

The equal-weight probability pool improved aggregate Brier score from
`0.244139` to `0.187690`, a relative improvement of `23.1217%`. It improved
log loss from `0.722060` to `0.557666`, reduced absolute calibration error,
won all three scored target seasons and improved every position slice. A
paired target-season/player cluster bootstrap placed the 95% Brier-delta
interval at `[-0.073307, -0.040243]`; the complete fixed screen passed.

This result authorises the next research stage only. It is not a product
promotion because these target outcomes were already available and the
experiment has not yet reconstructed point distributions or globally solved
and scored complete squads.

## Question

Does a promoted player's final Championship appearance, start and minute
record improve the retained opening appearance probability after a
league-translation model is learned only from earlier promotion classes?

The experiment deliberately tests participation first. Injecting raw
Championship rates directly into Premier League point forecasts would assume
an unmeasured cross-league translation and could distort the entire initial
squad.

## Registered data

Four immutable FBref playing-time extractions provide the source side:

| Source Championship season | Target FPL season | Promoted clubs |
|---|---|---|
| 2021/22 | 2022/23 | Fulham, Bournemouth, Nottingham Forest |
| 2022/23 | 2023/24 | Burnley, Sheffield United, Luton Town |
| 2023/24 | 2024/25 | Leicester City, Ipswich Town, Southampton |
| 2024/25 | 2025/26 | Leeds United, Burnley, Sunderland |

The fixed Vaastav FPL archives provide target Gameweek 1 rosters and exact
Gameweek 1–8 appearance outcomes. The 2021/22-to-2022/23 class is training
only. The later three classes are scored with an expanding origin: every
source model is fit only on earlier promotion classes.

The source pages were captured retrospectively in July 2026. Their final
prior-season aggregates represent information that existed before each target
season, but the captures are not historical point-in-time snapshots and may
include later FBref corrections. That limitation prevents this result from
being described as prospective evidence.

## Identity boundary

The evaluator bridges a player only when:

1. the FBref row belongs to one of the fixed promoted clubs;
2. the target player belongs to the corresponding club in the target FPL
   Gameweek 1 roster; and
3. normalised full names match exactly, or one full name is a unique
   two-or-more-token subset of the other within that club.

Unicode accents and explicit football-name letters are normalised
deterministically. Fuzzy similarity, web-name guesses and one-token proposals
are rejected. A player who represented more than one Championship club uses
the sum of those season rows only after the target roster uniquely establishes
which promoted club retained that player.

Coverage was:

| Target season | Promoted FPL players | Bridged | Coverage |
|---|---:|---:|---:|
| 2022/23 | 83 | 59 | 71.08% |
| 2023/24 | 102 | 70 | 68.63% |
| 2024/25 | 90 | 64 | 71.11% |
| 2025/26 | 112 | 69 | 61.61% |

New signings and unresolved aliases remain explicitly without this source
feature.

## Models

The fixed source model is an L2-regularised logistic regression over:

- Championship appearance rate;
- Championship start rate;
- Championship share of available minutes;
- minutes per appearance;
- target Gameweek fraction from 1 through 8; and
- target FPL position indicators.

The incumbent is the retained historical opening appearance-hurdle
classifier, reconstructed without reading target-season performance. The
challenger was fixed before target scoring as:

```text
0.5 × incumbent appearance probability
+ 0.5 × translated Championship appearance probability
```

The conservative pool tests whether the new evidence adds stable information
without discarding the broader incumbent history.

## Fixed screen

All gates had to pass:

- at least 1% aggregate Brier improvement;
- no aggregate log-loss regression;
- no more than `0.02` absolute calibration-error regression;
- no position Brier regression greater than `0.02`;
- at least two target-season Brier wins; and
- a non-positive upper bound from 5,000 paired
  target-season/player-cluster bootstrap replicates.

## Results

| Target | Players | Incumbent Brier | Challenger Brier | Delta |
|---|---:|---:|---:|---:|
| 2023/24 | 70 | 0.244449 | 0.196381 | -0.048068 |
| 2024/25 | 64 | 0.281538 | 0.206614 | -0.074924 |
| 2025/26 | 69 | 0.209135 | 0.161322 | -0.047813 |
| **All** | **203** | **0.244139** | **0.187690** | **-0.056449** |

Every position improved:

| Position | Challenger minus incumbent Brier |
|---|---:|
| Goalkeeper | -0.069084 |
| Defender | -0.034902 |
| Midfielder | -0.077290 |
| Forward | -0.036523 |

The source-only model also outperformed the incumbent, but it was not selected
after seeing the scores. The pre-fixed equal-weight pool remains the retained
challenger for the next stage.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_promoted_appearance_evaluation \
  --database /path/to/autofpl.db \
  --fbref-extraction 2021-22=/path/to/2021-22-extraction.json \
  --fbref-extraction 2022-23=/path/to/2022-23-extraction.json \
  --fbref-extraction 2023-24=/path/to/2023-24-extraction.json \
  --fbref-extraction 2024-25=/path/to/2024-25-extraction.json \
  --output /path/to/historical-promoted-appearance-evaluation-v1.json
```

The retained result has data identity
`ae4650b3357310975379888539958cd4157f737155428461968a17ad8cabbdc7`
and run identity
`55d13005b3cd6d107dddb2e5955d48623b590b8b6c1706dedcb6c9ca3699cc39`.

## Follow-on result

The full point-distribution and opening-policy evaluation is now complete.
Affected-player CRPS improved by 8.94%, every target and position won, and the
all-player distribution also improved. The complete-squad policy gained only
one point per target on average with one win, below its fixed +2 mean and
two-win gates. The translated appearance feature is therefore not added to the
current 2026/27 forecast. See the
[promoted-player opening evaluation](historical-promoted-opening-evaluation-v1.md).
