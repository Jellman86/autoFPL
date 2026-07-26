# Provisional preseason player forecast v1

## Purpose

`historical-preseason-player-gameweek-forecast` is the first fitted
current-player point artifact. It bridges the period after the archived model
passes its locked holdout and before any 2026/27 outcome fold exists.

It is a comparison artifact, not a promoted forecast. Baseline v0 continues to
drive squad advice.

## Fixed model and training identity

The generator accepts only 2026/27 Gameweek 1 and only the exact 2025/26
archive that produced the retained evaluation:

- source revision
  `f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a`;
- player content
  `412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b`;
- Gameweek content
  `0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d`;
  and
- evaluation run
  `5ed600cf5ad5ebf830d814d615a1bd6648ba74db25d8939831bdb40116f2ca2b`.

Any identity change fails closed and requires a new evaluation. The selected
histogram-gradient-boosting configuration and seed remain unchanged. After
selection and holdout assessment, fitting may use every archived
player-Gameweek because GW1 is later than the complete training season.

## Current target

The target is the latest official GW1 capture that was available before its
recorded deadline. Its Baseline v0 comparison must refer to the exact same
capture and eligible-player cohort.

Current players join history only through official stable player code.
Season-local element IDs and names are not fallback identities. Players without
prior identity remain in the artifact with explicit missing history; players
whose official status is `u` remain outside the same eligible cohort as
Baseline v0.

## Availability and distribution boundary

The fitted model learned historical participation but has no trustworthy
historical decision-time injury chronology. Each row therefore carries current
official status and chance as authoritative context without multiplying,
zeroing or otherwise changing the fitted mean.

This exposes an important disagreement honestly: an injured current player can
still have a strong raw history-based mean. Served advice must not use the
challenger until an evaluated appearance/availability layer resolves that
boundary.

The artifact contains a fitted point mean only. It does not invent a start
probability, expected minutes or calibrated point interval.

## First real generation

The first Quark snapshot run was deterministic across repeated executions. It
used:

- 38 historical Gameweeks and 29,338 aggregated training samples;
- 557 current eligible players from 558 official players;
- 453 stable-code prior-season matches; and
- 104 explicit missing prior-season identities.

The generated artifact had data identity
`13d53029b305606fa419df39443efd1281b11382d00a042102e255eabe624f8b`
and run identity
`9821f4131c3712b12c03c9b7bd76ce81e84215ab25a66237fab889e6a3dd1c86`.
Those identities describe that capture-specific research run, not a persisted
product artifact.

## Product boundary and next gate

Migration 20 and the operator importer validate schema, fixed model/evaluation
identity, exact official and historical captures, the exact Baseline v0 cohort,
stable identities, finite bounded values and full eligible-player coverage
before immutable persistence. Identical content is idempotent and conflicting
content for the same capture fails closed. The read-only API and player dossier
show the comparison, immutable artifact hash and its limitations.

Advice remains on Baseline v0. The next gate is a separately evaluated
appearance/start/minutes model plus an empirical or calibrated point
distribution. Availability must improve rolling out-of-time scoring and
decision utility before it can transform this raw fitted mean.
