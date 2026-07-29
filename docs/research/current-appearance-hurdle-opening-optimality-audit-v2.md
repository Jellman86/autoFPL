# Current appearance-hurdle opening optimality audit v2

## Question

Is the best-supported v2 opening squad the exact optimum for its declared
model, and how sensitive are its 15 player choices to the finite set of
retained scenario paths?

The earlier audit applied to the direct-tree v1 squad. This separately
versioned audit rebuilds the appearance-hurdle paths, binds both the retained
point-model and complete historical opening-policy evaluations, and repeats:

- the zero-gap six-Gameweek expected-points solve;
- the best globally optimal squad constrained to differ by at least one player;
- 15 global player-exclusion re-optimisations; and
- 200 fixed-seed nonparametric bootstraps of the 38 paired scenario paths.

## Exact current result

The capture #19 squad is the zero-gap global optimum for the declared linear
surrogate over the current 560-player legal candidate pool:

- Leno and Roefs;
- Truffert, Van Hecke, Tarkowski, Virgil and Mukiele;
- Rayan, Enzo, Szoboszlai, Bruno Fernandes and Le Fée; and
- Watkins, Thiago and Calvert-Lewin.

The best distinct squad changes only Roefs to Kelleher. Its surrogate regret is
`0.012631` points, its exact scenario mean is `0.789473` points lower, and it
beats the incumbent on none of the 38 paired paths. This verifies the exact
choice under the retained inputs, while also showing that the linear objective
surface is nearly flat around the second goalkeeper.

## Selection stability

Five selections are core under the fixed 80% threshold:

| Player | Position | Bootstrap selection frequency |
| --- | --- | ---: |
| Watkins | Forward | 99.0% |
| Bruno Fernandes | Midfielder | 93.5% |
| Leno | Goalkeeper | 92.5% |
| Tarkowski | Defender | 91.0% |
| Truffert | Defender | 88.0% |

Roefs is the only fragile selected player under the fixed 50% threshold, at
28.5%. Rayan is marginal but not classified as fragile at 53.5%.

The 200 resamples produced 195 distinct squads. No bootstrap replicate exactly
reproduced all 15 incumbent players; median overlap was 11, with a range of
7–14. This is not a solver failure: every replicate is independently solved to
global optimality after changing the empirical path weights. It shows that
model and scenario uncertainty still dominate the tiny mathematical gaps
between many legal squads.

## Interpretation

The v2 squad is mathematically optimal for the retained appearance-hurdle
means, 38 paired paths, six-Gameweek expected-points objective, prices and FPL
constraints. The historical opening-policy screen also improved realised
eight-Gameweek points in all three target seasons.

It is not possible to claim an empirically optimal 2026/27 squad before those
matches occur. The audit deliberately distinguishes:

- exact in-model optimality, which is established;
- historical decision support, which is favourable but retrospective; and
- prospective current-season accuracy, which remains unscored.

Player explanations should therefore present core and fragile status rather
than implying equal certainty across all 15 picks. Roefs is the clearest
candidate for close monitoring as new lineup, health and external forecast
evidence arrives before the deadline.

The capture #19 data identity is
`fca0ef84cbecb8eb08808c92ffd580df769937c2400f44168f26c761cf64bacf`
and the run identity is
`a9390a02dc001636fc0c778a4058fccf9615366b44c75e9f4f3fe756272fc594`.
An independent complete rerun was byte-identical; the emitted file SHA-256 was
`cf392fbbf3f8515dd36d6766ab480f66a7b5a59dda1dbcafe9e37f537bccfe79`.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_appearance_hurdle_opening_optimality_audit \
  --database /path/to/autofpl.db \
  --output /path/to/current-hurdle-opening-optimality-audit.json
```

The command is deterministic, opens SQLite read-only and refuses to overwrite
an existing output.
