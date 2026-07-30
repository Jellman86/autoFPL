# Current promoted-player opening sensitivity v1

## Decision

Retain the served v2 opening squad unchanged. The frozen
Championship-to-Premier-League appearance translation materially changes
forecasts for 60 reviewed Coventry, Hull and Ipswich players, but none belongs
to the current v2 squad and none enters after a fresh global six-Gameweek
solve.

The incumbent and sensitivity squads contain the same 15 players, receive
identical roles and tie on every retained scenario path. This is diagnostic
evidence only: the historical complete-squad promotion gate already failed,
and current 2026/27 outcomes do not yet exist.

## Purpose

The historical evaluation established two facts:

1. prior-Championship playing time improves promoted-player appearance and
   point distributions; and
2. it did not improve complete historical opening decisions enough to pass the
   fixed serving gate.

This current analysis asks a narrower operational question: does applying that
frozen translation expose any sensitivity in the actual v2 initial squad?

It cannot promote the feature. It can identify whether a current selected
player or close alternative depends on the incumbent's missing-history
fallback.

## Inputs and identity

- current official FPL roster capture: `19`;
- reviewed FBref playing-time snapshot: `27`;
- reviewed prior-competition players: `60`;
- historical appearance screen data identity:
  `ae4650b3357310975379888539958cd4157f737155428461968a17ad8cabbdc7`;
- historical full-policy data identity:
  `f35f876a39d7d8766513d83a8a63596b03003dcdc0d83b7889324d451eef707c`.

Snapshot 27's reviewed identities were created against official capture 13.
The join to capture 19 uses official player codes, the stable cross-capture
identity, and requires every reviewed code to be present in the current
forecast roster. Source-local player IDs and names are not guessed.

A later source capture reproduced all 944 extracted playing-time rows
byte-for-byte but correctly remained an unreviewed proposal set because its
snapshot/content identity changed. The sensitivity continues to use the exact
reviewed snapshot rather than silently transferring approval.

## Method

The source logistic model is fit on all four completed historical promotion
classes. For the 60 reviewed current players only:

```text
0.5 × retained v2 appearance probability
+ 0.5 × translated Championship appearance probability
```

Expected points are recomputed as the pooled appearance probability times the
unchanged conditional expected points. Non-promoted and unbridged players are
unchanged. The standard whole-row donor scenarios and globally constrained
six-Gameweek policy are then rebuilt. The served v2 squad and a freshly
optimised sensitivity squad are scored on those identical paths.

## Result

- affected current players: `60`;
- affected players in served v2 squad: `0`;
- affected players in sensitivity optimum: `0`;
- shared squad players: `15/15`;
- added or removed players: none;
- mean paired path difference: `0.0`;
- minimum/median/maximum path difference: `0/0/0`;
- tie probability: `100%`.

The largest probability movements are meaningful at player level. For
example, Kasey McAteer's Gameweek 1 estimate falls from `0.878928` to
`0.621972`, while Matt Grimes rises from `0.435554` to `0.631010`. Neither is
close enough to the fixed six-Gameweek squad optimum to change the decision.

Robin Roefs is unaffected: Sunderland are not part of the current newly
promoted cohort. His previously identified fragility remains governed by the
v2 optimality audit and future current evidence.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_promoted_opening_sensitivity \
  --database /path/to/autofpl.db \
  --fbref-extraction 2021-22=/path/to/2021-22-extraction.json \
  --fbref-extraction 2022-23=/path/to/2022-23-extraction.json \
  --fbref-extraction 2023-24=/path/to/2023-24-extraction.json \
  --fbref-extraction 2024-25=/path/to/2024-25-extraction.json \
  --current-fbref-extraction /path/to/reviewed-2025-26-extraction.json \
  --output /path/to/current-promoted-opening-sensitivity-v1.json
```

The result has data identity
`4b27cee1437f7762350f6889965781c2c4adc27a54f71feb6e80ff86f3f415bf`
and run identity
`344471381ac91c4f0ca62ce08ec4ef0d2f25c7acf1bf183377e9da6b9a23d581`.

## Next

Stop spending initial-squad effort on aggregate promoted-player playing time:
it does not change the current decision. Preserve it for future player
dossiers and prospective outcome scoring.

The next prediction slice should address players actually selected or close
to the optimiser boundary: cutoff-safe availability and lineup evidence,
temporal match evidence for fragile selections, or an objective/robustness
change that can be evaluated under the registered 3/6/8-Gameweek policies.
