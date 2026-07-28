# Multi-season preseason shadow forecast v1

## Purpose

This generator fits the frozen multi-season histogram tree to the complete
2024/25 and 2025/26 archives, then emits raw 2026/27 Gameweek 1 player point
means beside the exact-capture Baseline v0 values.

It is a read-only shadow artifact. It cannot change advice, select a squad or
claim promotion.

## Fixed inputs and cutoff

The generator accepts only 2026/27 Gameweek 1. It requires:

- the exact two archive hashes used by
  `multi-season-expanding-origin-v1`;
- an official target capture created after both archives and before the
  Gameweek deadline;
- the exact Baseline v0 player artifact for that official capture; and
- complete official player, team and target-fixture coverage.

Training contains 76 chronological origins and 56,257 player-Gameweek rows.
Players join across seasons and into the current catalog only through official
stable codes. The same legacy-null, observed-count, rolling and EWMA feature
contract used by the retained evaluation is reused without tuning.

Current official availability is retained as authoritative context but does
not alter the raw fitted mean. Players marked unavailable by the official
catalog are excluded exactly as in the existing preseason artifact. Baseline
v0 remains the only model that drives served advice.

## First real artifact

The first run used official capture `15`, available at
`2026-07-28T22:38:41.5790173Z`, for the 21 August 2026 deadline. It produced
560 eligible forecasts from 563 official players:

| Historical identity | Players |
| --- | ---: |
| Both archived seasons | 325 |
| 2025/26 only | 129 |
| 2024/25 only | 22 |
| No archived match | 84 |

Every row contains the raw shadow point mean, exact Baseline v0 mean,
difference, official availability and identity coverage. The artifact has no
calibrated interval, start probability or minutes distribution.

The retained summary is
[`multi-season-preseason-shadow-2026-27-gw1-v1.json`](results/multi-season-preseason-shadow-2026-27-gw1-v1.json).

## Persistence and interpretation

The underlying retrospective screen improved tree MAE by only 0.2607% over
the matched current-season-only tree, won three of eight folds and regressed
two position slices. The current artifact is therefore useful comparison
evidence, not a better squad claim.

Migration 25 persists this exact contract in a dedicated immutable artifact
family. Operators import it with:

```bash
dotnet AutoFpl.Api.dll \
  --import-multi-season-player-forecast /path/to/two-season-shadow.json
```

`GET /api/v1/forecasts/multi-season-shadow/latest` exposes the complete
artifact. Each player dossier also includes a `multiSeasonShadow` comparison.
Both surfaces retain the model status, evidence boundary, exact content hash
and `influencesAdvice: false`.

The UI must label it as a shadow and keep it out of selection scoring until
prospective 2026/27 outcomes support promotion.
