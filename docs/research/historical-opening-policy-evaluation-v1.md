# Historical opening-policy evaluation v1

## Decision

The registered comparison selected the six-Gameweek expected-points policy
for prospective 2026/27 scoring. This is a policy freeze, not a production
promotion: the current initial-squad artifact remains shadow-only and the
selected policy may not influence served advice until new-season outcomes
pass the prospective gate.

The ranked retrospective leader was the three-Gameweek downside-balanced
policy. It averaged 405.33 realised points over Gameweeks 1–8, 15.67 more
than the registered six-Gameweek expected-points reference, and beat the
reference in two of three target seasons. It lost 21 points to the reference
in 2023/24, however, breaching the predeclared maximum two-point
single-season regression. The registered rule therefore retained the
reference.

The eight-Gameweek downside-balanced policy was directionally encouraging:
it averaged 13 points more than the reference and won two targets, with a
seven-point worst regression. It also failed the frozen stability threshold
and was not selected. These results support continued prospective study of
downside-aware optimisation, not retrospective relaxation of the gate.

## Frozen method

The evaluator first reconstructs the outcome-free scenario and requires the
exact registration data identity
`b9d28cac497af35fc0762b7080db7e369759678872f78d47135e050f1920b675`.
Only after that check succeeds does it load target points and minutes.

For each of the three expanding-season targets, it solves all six registered
policies:

- 3, 6 and 8 Gameweek horizons;
- expected-points and 15% downside-balanced objectives;
- exact FPL squad, budget, club and formation constraints; and
- HiGHS optimal status with a reported relative MIP gap no greater than
  `1e-12`.

The selected 15-player squad is held for all eight Gameweeks without
transfers. Within the optimisation horizon, the optimiser's XI and captain
are used. After a shorter horizon, roles are chosen from the fixed squad by
maximum preseason scenario mean under a legal formation. Realised points use
the shared exact scorer for captain fallback, goalkeeper replacement,
formation-preserving outfield substitutions and bench order.

## Results

| Policy | 2023/24 | 2024/25 | 2025/26 | Mean | Mean vs ref | Worst vs ref |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 3 downside-balanced | 403 | 421 | 392 | 405.33 | +15.67 | -21 |
| 3 expected-points | 409 | 388 | 392 | 396.33 | +6.67 | -15 |
| 6 downside-balanced | 410 | 427 | 357 | 398.00 | +8.33 | -14 |
| 6 expected-points | 424 | 388 | 357 | 389.67 | 0.00 | 0 |
| 8 downside-balanced | 417 | 427 | 364 | 402.67 | +13.00 | -7 |
| 8 expected-points | 413 | 388 | 355 | 385.33 | -4.33 | -11 |

The retained result is
[historical-opening-policy-evaluation-v1.json](results/historical-opening-policy-evaluation-v1.json).
Its data identity is
`e99bb4fce3615c91ecaf642037c6cebb3d36efffc2df54857ffbc4c27714e048`;
its run identity is
`a79acdbc991768c31f4bf3abc1bcaa5724dcdfbdb89f0e3f0c0b63a530c5008e`.
An independent second run produced the same artifact byte for byte.

## Interpretation and limitations

Three seasons cannot precisely estimate a six-policy ranking. The stability
gate is intentionally conservative because choosing the largest observed
mean from this small comparison would compound winner's-curse and
multiple-comparison risk.

The evaluation measures a fixed opening squad across Gameweeks 1–8. It does
not evaluate transfer timing, chips, price changes or an interactive manager.
Historical opening injury/status captures remain unavailable, and the target
fixture structure is the registered final-archive proxy. The selected policy
must now be applied unchanged to the current 2026/27 opening forecast and
scored prospectively as official outcomes arrive.
