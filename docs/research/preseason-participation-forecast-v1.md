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

## Availability and limitations

The historical archive lacks decision-time injury state. Current official
availability is therefore authoritative and remains separate from the raw
model values; the generator does not pretend an old fit has incorporated
current injury news. Transfers, promoted clubs, new players and tactical
changes remain cross-season risks.

The next step is to run the artifact on Quark's exact safe archive backup,
inspect coverage, ranges, coherence and current-health conflicts, then add a
versioned backend import and player-dossier presentation without changing
served advice.
