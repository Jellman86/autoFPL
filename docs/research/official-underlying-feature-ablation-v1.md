# Official underlying feature ablation v1

## Status

`official-underlying-feature-ablation-v1` is an executable exploratory
comparison. It does not promote a model or claim that official underlying
statistics improve predictions. It measures that question on the existing
expanding-origin cohorts before the incumbent feature contract can change.

## Command

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.official_underlying_ablation \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/official-underlying-ablation.json
```

The command opens SQLite read-only, produces deterministic hash-identified
output and refuses to overwrite an existing report. It returns exit code `2`
with `insufficient-data` until at least one eligible official expanding-origin
fold exists.

## Comparison

Every eligible fold uses the same training Gameweeks, target Gameweek, players,
labels and cutoff-safe `official-temporal-v2` tables for all variants. The
report compares:

- the unchanged `temporal-ridge` and `hist-gradient-boosting` feature contract;
- `temporal-ridge+official-underlying`; and
- `hist-gradient-boosting+official-underlying`.

The four simple point baselines from the temporal ridge report remain in the
same cohort. This prevents a favourable player subset or easier target period
from making the underlying-feature variants look better.

## Fixed candidate features

The candidate adds rolling-three-Gameweek and exponentially weighted means for:

- expected goals;
- expected assists;
- expected goals conceded;
- ICT index;
- bonus-points-system score; and
- defensive contribution.

Each mean is paired with its observed sample count. These six provider measures
were fixed before inspecting real evaluation results. Expected goal
involvements is omitted because it overlaps expected goals and assists;
influence, creativity and threat are omitted because ICT already combines
them. The narrower candidate reduces avoidable collinearity and researcher
degrees of freedom.

Migration-10 imports provide these fields atomically. Legacy rows remain null,
and the observed counts distinguish a short underlying-stat history from a
genuine zero. Ridge median imputation, missing indicators and scaling are
fitted only on each training fold. The tree keeps native missing branches and
learns removable/constant columns only from training rows.

## Point-in-time safety

The evaluator inherits the temporal ridge chronology:

- each target uses its latest qualifying pre-deadline official replay;
- every training Gameweek uses its own pre-deadline replay;
- only earlier outcomes available at that replay's capture time enter player
  history; and
- the latest eligible correction per historical Gameweek wins.

The target outcome supplies only the label. It never enters its own features.
Report identities include every training and target feature-table hash and
outcome capture used by the fold.

## Interpretation and promotion

MAE, RMSE and mean error are diagnostic comparisons on the folds currently
available. A lower number on one small fold is not promotion evidence. The
candidate can replace or augment the official incumbent only after repeated
out-of-time improvement, stable position and availability slices, and the
broader promotion criteria in the roadmap. Until then the application continues
to show these values as historical evidence, not as fitted forecast inputs.
