# Historical opening forecast reconstruction v1

## Decision

This artifact reconstructs the point-mean input for the frozen historical
opening-squad comparison without opening target performance outcomes.

For each target season, the unchanged multi-season histogram tree is fitted on
all player/Gameweek origins in strictly earlier registered archives:

| Target | Training seasons | Origins | Training rows | Opening players |
| --- | --- | ---: | ---: | ---: |
| 2023/24 | 2022/23 | 37 | 24,957 | 658 |
| 2024/25 | 2022/23–2023/24 | 75 | 53,699 | 616 |
| 2025/26 | 2022/23–2024/25 | 113 | 80,618 | 690 |

Stable official player code carries history across seasons. New players receive
the model's training-only missing-history treatment. The target cohort, club,
position and price come from the hash-verified Gameweek 1 constraint
reconstruction. Raw same-Gameweek `xP` is never selected.

## Target-season read boundary

Target forecast SQL selects only:

- Gameweek;
- own-team name;
- fixture ID;
- kickoff time; and
- home/away status.

It does not select target points, minutes, starts, expected events, scoring
events or defensive actions. The opening cohort loader runs with outcomes
disabled, leaving target point and minute arrays empty. This makes the
forecast reconstruction a distinct pre-outcome phase rather than relying on
discipline inside a combined evaluator.

## Fixture proxy limitation

An immutable historical opening-day fixture capture is not available. The
final pinned archive's realized Gameweek fixture structure is therefore a
fixed and labelled proxy. This matters in 2023/24: the Luton–Burnley fixture is
absent in Gameweek 2 and appears as an eleventh fixture in Gameweek 7.

The model sees that fixture count and timing but no result. A postponement or
rearrangement learned after the original deadline can still leak schedule
knowledge. Policy conclusions must retain this limitation, and future
historical fixture-release snapshots should replace the proxy if a reliable
pinned source is admitted.

## Reproduced identities

The first real read-only run produced these player-forecast hashes:

| Target | Forecast identity SHA-256 |
| --- | --- |
| 2023/24 | `c33e5aaceea268d777ed9fe4ffe15d4097f94285b31a6e6c0a67000cc66408d8` |
| 2024/25 | `b73de5553ebc11f1101121f38b1d9895d911feca28320d5996a3426d531a2a5a` |
| 2025/26 | `c9e93e91c3ea2eefaf99db4ea8ac8b6f94fc3269054556cd515ad17e3c275305` |

The complete artifact data identity is
`27c2e5178c3bd88dfa2048b61f4a7d097499ee4a0c512a662a4ab71e048e262f`.

## Command

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_opening_forecast_reconstruction \
  --database /path/to/autofpl.db \
  --output /path/to/historical-opening-forecasts.json
```

The next slice adds leakage-safe appearance estimates and whole-Gameweek
scenario paths, then freezes the common eight-Gameweek scoring and selection
rule before reading the separately reconstructed outcomes.
