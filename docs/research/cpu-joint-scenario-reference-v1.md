# CPU joint-scenario reference v1

## Decision

autoFPL's first scenario engine is a deterministic NumPy CPU reference. It
scores complete FPL selections over supplied joint player-outcome rows and
compares candidates on the same rows. It does not turn the current
uncalibrated point intervals into a probability distribution and does not yet
influence advice.

The reference exists before a GPU implementation so that accelerator work has
an exact semantic target. GPU use is accepted only after parity and a
representative Quark/Riker benchmark show a material operational benefit.

## Inputs

One scenario row contains:

- integer FPL points for all 15 squad players;
- one played/not-played indicator for each player; and
- the dependence already implied by the row across players and matches.

Non-playing players must have zero points. A selection contains the ordered
starting XI, replacement goalkeeper, three ordered outfield substitutes,
captain, vice-captain and each squad player's position. The scorer revalidates
the 2/5/5/3 squad composition, legal starting formation, complete bench
partition and captaincy before evaluating a row.

Scenario generation is deliberately a separate responsibility. The initial
sampler draws complete rows with NumPy PCG64 and an explicit unsigned 64-bit
seed. It never samples player columns independently, so dependence present in
an empirical or model-generated joint support is retained. A future
match-state generator can feed the same scoring kernel.

## FPL resolution semantics

Each scenario mirrors the deterministic application domain:

1. a non-playing starting goalkeeper is replaced only when the replacement
   goalkeeper played;
2. playing outfield substitutes are considered in bench order;
3. each substitute replaces the first still-missing starter for which the
   resulting formation is legal;
4. non-playing starters that cannot be replaced remain visible and score zero;
5. captaincy stays with the captain when they played, otherwise passes to the
   vice-captain when they played; and
6. only effective players score, with one additional copy of the effective
   captain's points.

The current kernel intentionally excludes chips, transfer hits, multi-Gameweek
horizons and Assistant Manager rules. Those require separately versioned
inputs and exact reference cases.

## Outputs

The engine reports the empirical mean, population standard deviation, minimum,
10th percentile, median, 90th percentile, maximum, mean activated substitutes
and probability of at least one unreplaced starter. Paired comparisons report
the candidate-minus-reference distribution and win/tie/loss probabilities.

All candidate comparisons must use identical scenario rows. A difference
between two independently sampled batches is not an acceptable estimate of
selection gain.

## Verification and promotion boundary

Tiny exact cases cover all-played scoring, goalkeeper replacement, outfield
bench order, formation blocking, vice-captain transfer and unreplaced
starters. Seeded sampling is byte-for-byte reproducible and proves that every
draw is a complete source row rather than a hybrid.

This slice does not claim forecast calibration, strategy superiority or GPU
readiness. Product integration requires:

1. a registered, point-in-time player/match scenario generator;
2. out-of-time calibration and proper-score evidence for its components;
3. exact parity cases against the deterministic .NET outcome rules;
4. immutable scenario identity, configuration and seed provenance; and
5. UI copy that names the objective, horizon, uncertainty model and
   limitations.
