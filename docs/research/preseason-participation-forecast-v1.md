# Provisional preseason participation forecast v1

## Purpose

This artifact fits the three historical participation classifiers that passed
the retained 2025/26 locked holdout to every eligible current GW1 player. It
also carries the retained transparent exact-minutes baseline. It is comparison
evidence for the next product import; it is not a promoted forecast and cannot
influence advice.

## Fixed identities

The command requires the same prior-season source revision and player/Gameweek
content hashes used by the retained participation evaluation. Each fitted
classifier records evaluation data identity
`a6d3c3c2c9a64c123ef69584c46aa7200fc6f467eab7858b4e88dd0e12cb5113`
and complete-run identity
`c39a19a93b0233e0130fe278af63b0278a4170c937db571ff056861c92451063`.
An archive mismatch fails closed.

Current players join prior history only through stable official player code.
The target official capture, decision cutoff, fixture count and home-fixture
rate are retained in the artifact identity. Unavailable official players are
excluded consistently with the existing provisional point artifact.

## Outputs

For every eligible current player the artifact provides:

- raw appearance probability;
- raw start probability;
- raw probability of playing at least 60 minutes;
- the retained player-last expected-minutes baseline;
- current official status and chance of playing;
- prior-season identity coverage; and
- an explicit probability-coherence status.

The classifiers retain the exact fixed histogram configuration that improved
locked-holdout Brier score by 21.5706%, 21.7959% and 18.6653%, respectively.
The exact-minutes histogram tree is not fitted because it failed its gate.
Players with stable prior identity use their latest prior-season Gameweek
minutes. Missing identities use the full-archive position mean and are labelled
accordingly.

The three classifiers were evaluated independently. The generator does not
silently clip or reorder their values: if start or 60-minute probability
exceeds appearance probability, the player is explicitly marked incoherent.
A later coherent joint-state challenger must pass an identical out-of-time
gate before replacing these raw outputs.

The artifact's product-import readiness is fail-closed. It remains blocked
while current official availability is not fused, any raw probabilities
violate event nesting or any player requires an unevaluated missing-history
fallback. This readiness flag is separate from research artifact generation:
the output remains useful for diagnosing those exact gaps.

## First current-player run

The first Quark run used official capture `6` at
`2026-07-26T18:17:34.7193696Z` and produced 557 eligible players from 558
official rows. It matched 453 players to stable prior-season identity and
reported 104 missing identities. Coventry, Hull and Ipswich each account for
26 of those gaps, so the selected minutes fallback would assign most of three
complete promoted squads only a position mean.

The raw probability ranges were finite and non-degenerate:

| Output | Minimum | Mean | Maximum |
| --- | ---: | ---: | ---: |
| Appearance | 0.012013 | 0.520447 | 0.947284 |
| Start | 0.005280 | 0.383612 | 0.941033 |
| 60+ minutes | 0.004912 | 0.356796 | 0.931499 |
| Baseline minutes | 0.000000 | 33.802954 | 90.000000 |

However, 17 players violated event nesting. The largest start-minus-appearance
gap was 0.294891 and the largest 60-minute-minus-appearance gap was 0.277102.
Separately, 27 players had current official chance zero while six retained raw
history-only appearance probability of at least 0.50. These are expected
diagnostics of models trained without decision-time injury state, but they
make raw product use unsafe.

The artifact correctly returned product import `blocked` for all three
reasons. No backend import or dossier exposure was added. The retained
machine-readable summary is
[`preseason-participation-forecast-2026-27-gw1-v1.json`](results/preseason-participation-forecast-2026-27-gw1-v1.json).

## Availability and limitations

The historical archive lacks decision-time injury state. Current official
availability is therefore authoritative and remains separate from the raw
model values; the generator does not pretend an old fit has incorporated
current injury news. Transfers, promoted clubs, new players and tactical
changes remain cross-season risks.

The next step is to evaluate a coherent projection or joint-state model on the
identical historical holdout, define a separately labelled current-official
availability fusion rule and close promoted/new-player history coverage using
the existing Quark research stack or a bounded prior-league source. Only then
may a versioned backend import and player-dossier presentation be added.
