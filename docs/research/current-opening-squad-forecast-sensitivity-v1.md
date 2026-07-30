# Current opening-squad forecast sensitivity v1

## Question

How far can one selected player's current six-Gameweek forecast fall before
the exact FPL-constrained optimum removes them, and how far must a close
unselected player's forecast rise before they enter?

The v2 optimality audit established that the current squad is the zero-gap
solution to its declared model. It also found a flat scenario surface. This
audit measures that surface in forecast units so that a mathematically exact
selection is not presented as equally certain at all 15 positions.

## Method

The audit rebuilds the exact retained appearance-hurdle scenario at official
capture #19 and uses the selected six-Gameweek expected-points policy. For one
player at a time it multiplies every scenario point outcome across all six
Gameweeks by the same factor, preserves the appearance rows, and globally
re-solves the complete MILP with the unchanged:

- 15-player and positional constraints;
- £100.0m budget;
- three-player-per-club limit;
- weekly formation, starting XI and captain decisions; and
- linear expected-points, bench-weighted objective.

For each selected player, a fixed 16-step binary search finds the greatest
coherent forecast reduction the selection tolerates. Every one of the 545
unselected legal players is first forced into a globally reoptimised squad to
obtain an exact objective-regret screen. Detailed uplift thresholds are then
solved for the nearest five candidates in each position and every player who
enters any selected-player exclusion optimum. The resulting shortlist contains
21 players.

This is deliberately an offline diagnostic. It does not add latency to the
application and cannot change served advice.

## Selected-player result

The incumbent squad remains unchanged. Its forecast-change thresholds are:

| Selected player | Position | Six-GW mean | Forecast reduction before exit | First replacement |
| --- | --- | ---: | ---: | --- |
| Roefs | Goalkeeper | 21.47 | 0.73% | Kelleher |
| Rayan | Midfielder | 24.63 | 1.28% | Anderson |
| Enzo | Midfielder | 24.95 | 2.53% | Anderson |
| Szoboszlai | Midfielder | 24.95 | 2.53% | Anderson |
| Van Hecke | Defender | 24.00 | 3.29% | Milenković |
| Thiago | Forward | 22.11 | 4.28% | João Pedro |
| Mukiele | Defender | 23.71 | 5.03% | Milenković |
| Calvert-Lewin | Forward | 21.71 | 6.54% | João Pedro |
| Truffert | Defender | 24.95 | 6.96% | Milenković |
| Virgil | Defender | 25.11 | 7.55% | Milenković |
| Leno | Goalkeeper | 23.68 | 9.33% | Darlow |
| Tarkowski | Defender | 25.89 | 10.37% | Milenković |
| Le Fée | Midfielder | 25.18 | 10.40% | Anderson |
| Bruno Fernandes | Midfielder | 28.74 | 16.49% | Anderson |
| Watkins | Forward | 27.47 | 22.99% | João Pedro |

Six selected players change after a forecast reduction of at most 5%: Roefs,
Rayan, Enzo, Szoboszlai, Van Hecke and Thiago. These are monitoring priorities,
not automatic removals. Watkins and Bruno Fernandes are the most structurally
robust selections under this stress.

## Closest challengers

Seven shortlisted players enter after an uplift of at most 5%:

| Challenger | Position | Required forecast uplift | Incumbent displaced at boundary |
| --- | --- | ---: | --- |
| Kelleher | Goalkeeper | 0.74% | Roefs |
| Anderson | Midfielder | 1.30% | Rayan |
| Semenyo | Midfielder | 2.95% | Thiago and Rayan |
| Milenković | Defender | 3.40% | Van Hecke |
| Palmer | Midfielder | 3.48% | Thiago and Rayan |
| Mbeumo | Midfielder | 4.00% | Rayan |
| João Pedro | Forward | 4.48% | Thiago |

The two-player changes for Semenyo and Palmer demonstrate why this audit
re-solves the whole constrained squad instead of treating sensitivity as a
same-position swap table.

## Interpretation

The current prediction is the optimal squad for the retained forecast, but it
is not a uniquely compelling set of 15 player beliefs. Roefs versus Kelleher
is effectively a forecast tie. Midfield and the third forward are also
sensitive to small changes. New pre-deadline lineup, health, role or external
forecast evidence should therefore be evaluated first for:

1. Roefs and Kelleher;
2. Rayan, Enzo, Szoboszlai and the Anderson/Semenyo/Palmer/Mbeumo alternatives;
3. Van Hecke and Milenković; and
4. Thiago and João Pedro.

This result supports targeted evidence collection. It does not support adding
arbitrary safety margins, hand-editing expected points or replacing the
registered objective. A qualifying evidence source must still be
cutoff-correct and improve historical or prospective predictive performance
before it changes the model.

A multiplier scales all point outcomes, including rare negative outcomes, and
does not change the retained appearance rows. It is a coherent total-forecast
stress, not a literal injury model. Automatic substitutions are also outside
the linear threshold objective. The exact nonlinear scenario score and
prospective 2026/27 outcomes remain separate evaluation layers.

The artifact data identity is
`ab1de6a96e82e838553bca0a5c6241847cc90340d9d13f2386138e5635b7eff3`
and the run identity is
`864663510c86897a1ed752a8ffa7d73947c0fcbcb2215c7e51cc2e064957daef`.

## Reproduction

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_appearance_hurdle_opening_forecast_sensitivity \
  --database /path/to/autofpl.db \
  --output /path/to/current-opening-squad-forecast-sensitivity-v1.json
```

The command opens SQLite read-only and refuses to overwrite an existing
output.
