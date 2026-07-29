# Current multi-horizon player forecast v1

## Decision this artifact supports

The opening squad should not be chosen from Gameweek 1 alone. This read-only
artifact extends the frozen two-season player point model across Gameweeks 1–8
and records cumulative 3, 6 and 8-Gameweek point means for every eligible
current player.

It is the deterministic mean input to the next correlated-scenario and global
opening-squad optimisation slices. It is not served advice and cannot mutate an
owner squad.

## Fixed cutoff and horizons

The command accepts only the 2026/27 opening decision. It selects one official
Gameweek 1 capture and requires that same capture to contain a scheduled fixture
and kickoff for every current club in every target Gameweek.

The target Gameweeks are fixed at 1–8. The registered decision horizons are
fixed at 3, 6 and 8 Gameweeks. A fixture revision produces a different data
identity; later knowledge is never read into an earlier capture.

The model is fitted once to the exact 2024/25 and 2025/26 archives already bound
to the retained expanding-origin evaluation. Stable player codes carry history
across seasons. Each future target uses only archive history and fixture
information present at the opening cutoff.

## Availability boundary

Official status and chance of playing the next round are preserved as Gameweek
1 context. They do not adjust the raw future means because the current feed does
not say when an injured or suspended player will return. Treating today's
status as permanent across eight weeks would introduce a known directional
error.

A later availability model may create explicit recovery scenarios. Until then,
the optimiser must distinguish raw football point potential from the separately
modelled Gameweek 1 appearance ceiling.

## Output and limitations

Each player row contains:

- raw expected points for Gameweeks 1–8;
- cumulative raw means through Gameweeks 3, 6 and 8;
- current official identity and availability context; and
- cross-season history coverage.

The artifact also binds the official capture, cutoff, complete fixture schedule,
evaluated archive hashes, frozen model configuration and evaluation run.

These are point means, not a multi-Gameweek predictive distribution. They do
not yet model cross-Gameweek correlation, transfers, future price movements,
future news or fixture rescheduling. The immediate next step is one aligned
joint scenario matrix per target Gameweek, followed by a legal 15-player
optimisation whose objective is compared across the registered horizons.

Run it with:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_multi_horizon_player_forecast \
  --database /path/to/autofpl.db \
  --output /path/to/current-multi-horizon-player-forecast.json
```
