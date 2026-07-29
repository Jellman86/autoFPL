# Current selected opening-squad shadow v1

## Purpose

This artifact turns the registered historical policy decision into one
operational 2026/27 prospective candidate. It is not a new policy search. It
must use the frozen six-Gameweek expected-points policy and the exact retained
historical evaluation identities:

- data identity
  `e99bb4fce3615c91ecaf642037c6cebb3d36efffc2df54857ffbc4c27714e048`;
- run identity
  `a79acdbc991768c31f4bf3abc1bcaa5724dcdfbdb89f0e3f0c0b63a530c5008e`.

The artifact is regenerated whenever a new exact official opening capture
changes the available player pool, prices, availability or fixtures. A stale
capture is never returned as current.

## Prospective freeze

The selected policy optimises one legal 15-player squad over Gameweeks 1–6.
The registered historical outcome comparison scores every opening squad over
Gameweeks 1–8, so leaving Gameweeks 7–8 undecided would allow future
information into the prospective evaluation.

This artifact therefore freezes:

- the 15-player squad and opening prices;
- a legal XI for every Gameweek from 1 through 8;
- captain and vice-captain for every Gameweek;
- reserve goalkeeper and ordered outfield bench for every Gameweek; and
- the exact FPL captain-fallback and formation-preserving substitution scorer.

Within Gameweeks 1–6 it retains the globally optimised roles. Gameweeks 7–8
use the registered maximum preseason scenario-mean role rule on the same fixed
squad. No target-season result is used.

## Product boundary

The analytics worker writes
`selected-opening-squad-capture-<capture-id>.json` only after the exact point,
joint-scenario and initial-squad-quality prerequisites are persisted. The
application validates the frozen evaluation identity, optimizer status,
latest official capture, player identity, price, availability, squad
constraints and all eight role decisions before immutable insertion.

The typed OpenAPI route
`GET /api/v1/forecasts/selected-opening-squad-shadow/current` returns the
artifact only when it matches the latest official capture. It remains:

- `status: prospective-shadow-unscored`;
- `isPromoted: false`; and
- `influencesAdvice: false`.

This read-only research route does not change the served initial prediction.
The SQLite snapshot publisher includes the immutable artifact so a later
outcome evaluator can score the exact pre-outcome decision.

## Command

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_selected_opening_squad \
  --database /path/to/autofpl.db \
  --output /path/to/current-selected-opening-squad.json
```

The application-side operator import is:

```text
dotnet AutoFpl.Api.dll \
  --import-selected-opening-squad-shadow <json-file>
```

## Remaining gate

The prospective method and promotion-evidence thresholds are now frozen in
[selected opening-squad prospective outcome evaluation v1](selected-opening-squad-prospective-outcome-evaluation-v1.md).
Official 2026/27 outcomes are joined only after each Gameweek is finished and
data-checked. The evaluator compares the frozen candidate with the exact
same-capture served and single-Gameweek benchmarks without changing any squad
or role. It cannot be promoted into served advice until all eight outcomes
exist and the registered rule passes.
