# Current selection scenario score shadow v1

## Purpose

This artifact is the first deterministic bridge from the persisted joint
player-Gameweek scenario matrix to an FPL selection decision. It scores the
exact model selection and, when available, the latest user-owned revision on
the same complete joint rows.

It is a prospective shadow. It is not promoted and cannot influence advice.
Its job is to establish exact lineage, official substitution and captaincy
semantics, paired comparison and reproducible distribution summaries before
alternative generation or GPU acceleration.

## Bound inputs

`current-selection-scenario-score-v1` requires:

- the latest content-verified joint scenario artifact;
- the exact official-capture Baseline v0 forecast;
- the latest immutable user selection revision linked to that forecast, when
  one exists; and
- `cpu-joint-scenario-reference-v1`.

The scorer reconstructs each complete 15-player selection, slices the shared
scenario matrix to those exact players, resolves goalkeeper and ordered
outfield substitutions, transfers captaincy to the vice-captain when required
and retains unreplaced starters explicitly. Candidate and reference scores
therefore use identical scenario rows.

The command opens SQLite read-only, refuses an existing output path and verifies
the stored JSON bytes against every persisted content hash. Production
automation must run it against an application-produced stable SQLite snapshot.
It must not weaken the analytics container's read-only database mount or mark a
changing live database as immutable.

## Current GW1 evidence

A checked temporary snapshot of the deployed capture 16 database produced:

- 38 paired joint rows;
- model mean 29.2105 points, median 28, p10 15.7 and p90 44.3;
- mean activated substitutes 0.8158;
- a 42.1053% probability of at least one unreplaced starter;
- data identity
  `33e4a20717d2789e8bb4bc6d98607922a6f39cb25487b59ecb9bd7b1862a7bc2`;
  and
- run identity
  `a087850c45b4ec0c329e05804dd0bcf51c968aa97fb355c1f5fa593721696081`.

The current user revision is an unchanged copy of the model selection, so its
paired delta is exactly zero on every row. That is a useful identity check, not
evidence that user and model strategies generally perform the same.

The 38 retained rows make central summaries useful for product development but
tail estimates coarse. The next boundary persists this score artifact, adds an
application-produced stable database snapshot handoff and then generates legal
safer and higher-ceiling candidates on the identical rows.

## Reproduction

Run against a stable database snapshot:

```bash
PYTHONPATH=src/analytics python -m \
  autofpl_analytics.current_scenario_selection_score \
  --database /path/to/stable-autofpl.db \
  --output /path/to/current-selection-scenario-score.json
```

