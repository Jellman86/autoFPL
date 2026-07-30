# Analytics boundary

Python owns point-in-time feature generation, forecasting, simulation,
backtesting and optimisation where the scientific ecosystem is useful. It
returns versioned candidate artefacts; the backend validates and records any
artefact promoted into product state.

Install the hash-locked scientific dependencies before running tabular
challengers:

```bash
python3 -m pip install --require-hashes \
  --requirement requirements-analytics.txt
```

## Historical preseason evaluation v1

The preseason bridge evaluates fixed ridge and histogram-tree point
challengers against simple baselines on the pinned historical archive:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_preseason_evaluation \
  --database /path/to/autofpl.db \
  --season 2025-26 \
  --output /path/to/historical-preseason-report.json
```

Model selection uses expanding-origin development folds through Gameweek 30.
The locked Gameweek 31–38 holdout runs only the selected challenger alongside
the simple baselines, while the gate remains tied to the baseline selected on
development data. The report is deterministic, read-only and refuses output
overwrite. A passing result supports a separately labelled preseason
challenger; it cannot promote or silently replace Baseline v0.

The design, limitations and first retained result are recorded in the
[historical preseason specification](../../docs/research/historical-preseason-evaluation-v1.md).

## Historical opening-policy data v1

The opening-policy audit reconstructs the target-season Gameweek 1 player
cohort and price constraint from the hash-verified immutable raw archive, then
joins official Gameweek 1–8 outcomes:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_opening_policy_data \
  --database /path/to/autofpl.db \
  --output /path/to/historical-opening-policy-data.json
```

It creates three expanding-season targets: 2023/24 trains on 2022/23, 2024/25
trains on both earlier seasons, and 2025/26 trains on all three earlier
seasons. Historical `value` is used only as the target-season budget
constraint; same-Gameweek `xP` is explicitly excluded. The report verifies
raw hashes, stable-code joins, legal-pool feasibility and outcome coverage
without writing SQLite or selecting a policy. See the
[opening-policy data specification](../../docs/research/historical-opening-policy-data-v1.md).

## Historical opening forecast reconstruction v1

The next read-only stage fits the unchanged multi-season histogram tree on
strictly earlier archives and reconstructs Gameweek 1–8 point means for every
target opening cohort:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_opening_forecast_reconstruction \
  --database /path/to/autofpl.db \
  --output /path/to/historical-opening-forecasts.json
```

The target-season SQL reads only fixture identity, kickoff and home/away
context; it does not select target points, minutes or event fields. Because no
opening-day historical fixture snapshot exists, final-archive fixture
structure is an explicit retrospective proxy. The artifact remains an
input reconstruction and does not generate scenarios, read outcomes or select
a policy. See the
[forecast reconstruction specification](../../docs/research/historical-opening-forecast-reconstruction-v1.md).

## Historical opening scenarios and policy registration v1

The outcome-free scenario phase combines the reconstructed point means with a
missingness-safe prior-season appearance classifier and whole-Gameweek donor
rows:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_opening_scenario_reconstruction \
  --database /path/to/autofpl.db \
  --output /path/to/historical-opening-scenarios.json

PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_opening_policy_registration \
  --database /path/to/autofpl.db \
  --output /path/to/historical-opening-policy-registration.json
```

The registration fixes all six policies, a common exact-FPL GW1–8 outcome
score, a neutral six-week expected-points reference and stability thresholds
before the policy evaluator reads target outcomes. The retained registration
identity and limitations are in the
[policy registration specification](../../docs/research/historical-opening-policy-registration-v1.md).

The registered evaluator is the only stage that may open the three target
seasons' points and minutes:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_opening_policy_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-opening-policy-evaluation.json
```

It first reproduces the exact frozen registration identity, solves all 18
season-policy combinations, and scores the fixed opening squads with the
shared exact FPL rules. The stability gate retained the six-Gameweek
expected-points reference for prospective scoring; it remains unable to
influence advice. See the
[policy evaluation specification](../../docs/research/historical-opening-policy-evaluation-v1.md).

## Multi-season expanding-origin evaluation v1

The multi-season evaluator asks whether carrying exact-code 2024/25 history
improves the fixed 2025/26 ridge and histogram-tree candidates. It compares
multi-season and current-season-only variants on the same late-season target
players:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.multi_season_evaluation \
  --database /path/to/autofpl.db \
  --season 2024-25 \
  --season 2025-26 \
  --output /path/to/multi-season-report.json
```

Legacy defensive metrics remain explicitly missing with observed-count
features, transforms fit inside each fold and cross-season identity is exact
official player code only. The result is a retrospective shadow-candidate
screen, never a promotion decision. See the
[multi-season specification](../../docs/research/multi-season-expanding-origin-v1.md).

The training-window ablation keeps that complete feature and model contract
fixed while comparing the retained two-season tree with all four pinned
historical seasons:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_training_window_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-training-window-evaluation.json
```

Both variants receive the same 2025/26 Gameweek 31–38 target cohorts. The
fixed gate requires material aggregate MAE improvement, RMSE non-regression,
majority fold wins and position stability. Reused target outcomes mean a pass
can retain only a prospective current shadow. See the
[training-window specification](../../docs/research/historical-training-window-evaluation-v1.md).

The appearance-hurdle point ablation tests whether an explicit
`P(appearance) × E(points | appearance)` factorization improves the same
unconditional total-point target:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_appearance_hurdle_points_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-appearance-hurdle-points.json
```

The classifier uses all earlier rows while the conditional point tree trains
only where minutes were positive. Both use the incumbent feature and
hyperparameter contract. The fixed gate compares unconditional point MAE,
RMSE, fold wins and position stability on identical folds; component
probability diagnostics cannot override it. See the
[hurdle-point specification](../../docs/research/historical-appearance-hurdle-points-evaluation-v1.md).

The retained factorization can now be fitted to the current Gameweek 1–8
decision and carried through availability-coherent scenario paths to a
zero-gap opening squad:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_appearance_hurdle_player_forecast \
  --database /path/to/autofpl.db \
  --output /path/to/current-hurdle-points.json

PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_appearance_hurdle_opening_squad \
  --database /path/to/autofpl.db \
  --output /path/to/current-hurdle-opening-squad.json
```

The first solve replaced Pickford, Rodon and Anderson with Leno, Tarkowski and
Rayan, the same three slots independently flagged as least stable. The
challenger remains prospective and does not overwrite the persisted incumbent.
See the
[current hurdle opener specification](../../docs/research/current-appearance-hurdle-opening-squad-v1.md).

The fixture-strength ablation asks whether cutoff-correct recent team output
and opponent points allowed by position add useful signal to the retained
two-season tree on the exact same expanding-origin folds:

```shell
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.fixture_strength_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/fixture-strength-report.json
```

Its fixed gate and research boundary are recorded in the
[fixture-strength specification](../../docs/research/fixture-strength-expanding-origin-v1.md).

The latent team-strength evaluator reconstructs match scores from the immutable
archive and compares a fixed time-decayed Dixon–Coles model with a weighted
league-rate Poisson baseline on expanding origins:

```shell
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.team_goal_strength_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/team-goal-strength-report.json
```

The complete likelihood, temporal split and fixed retention gate are in the
[team goal-strength specification](../../docs/research/time-decayed-dixon-coles-expanding-origin-v1.md).

The expected-goals ablation keeps the same latent-strength model and folds but
fits attack/defence rates from team xG aggregated from official player rows:

```shell
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.xg_team_strength_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/xg-team-strength-report.json
```

Its dual-reference gate and non-promotion boundary are in the
[expected-goals team-strength specification](../../docs/research/expected-goals-team-strength-expanding-origin-v1.md).

## Multi-season preseason shadow forecast v1

The frozen leading two-season tree can emit a current GW1 comparison artifact
without changing served advice:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.multi_season_player_forecast \
  --database /path/to/autofpl.db \
  --output /path/to/two-season-shadow.json
```

Both evaluated archives must have been available by the exact official target
cutoff. The artifact reports raw point means beside Baseline v0, preserves
official availability and exact-code identity gaps, has no calibrated
distribution and cannot influence selection. See the
[shadow forecast specification](../../docs/research/multi-season-preseason-shadow-forecast-v1.md).

## Current multi-horizon player forecast v1

The opening-decision extension fits the same frozen two-season tree once and
emits raw player point means for Gameweeks 1–8 plus the registered cumulative
3, 6 and 8-Gameweek horizons:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_multi_horizon_player_forecast \
  --database /path/to/autofpl.db \
  --output /path/to/current-multi-horizon-player-forecast.json
```

Every future fixture must exist in the same official Gameweek 1 cutoff capture.
Current next-round availability is retained as context but is not incorrectly
propagated through eight weeks. This prospective shadow is the mean input for
multi-Gameweek correlated scenarios and the global opening-squad optimiser; it
cannot yet influence served advice. See the
[multi-horizon specification](../../docs/research/current-multi-horizon-player-forecast-v1.md).

## Current multi-horizon joint scenarios v1

The scenario extension preserves each historical whole-Gameweek donor row and
pairs eight weekly marginals into fixed-seed paths for the registered opening
horizons:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_multi_horizon_joint_scenarios \
  --database /path/to/autofpl.db \
  --output /path/to/current-multi-horizon-joint-scenarios.json
```

Gameweek 1 exactly retains the existing official availability ceiling.
Gameweeks 2–8 revert to the raw preseason appearance estimate rather than
guessing an injury duration. Weekly cross-player dependence is preserved;
cross-Gameweek row pairing is an explicit fixed reference assumption. The
artifact remains prospective and non-serving. See the
[multi-horizon scenario specification](../../docs/research/current-multi-horizon-joint-scenarios-v1.md).

## Current multi-horizon initial squad v1

The decision layer solves one legal opening squad with independently legal
weekly roles for each registered horizon. It compares an expected-points
policy with a fixed worst-20% CVaR challenger and requires a zero-gap global
MILP result:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_multi_horizon_initial_squad \
  --database /path/to/autofpl.db \
  --output /path/to/current-multi-horizon-initial-squad.json
```

Every selection is then scored with exact FPL auto-substitution and captaincy
on the paired paths. The v2 artifact exposes all six frozen 3/6/8
horizon-policy results and marks the retrospectively selected six-Gameweek
expected-points policy for prospective scoring. It remains non-serving. See the
[multi-horizon initial-squad specification](../../docs/research/current-multi-horizon-initial-squad-v1.md).

The selected-policy projection freezes the exact squad and all eight weekly
role decisions before any current-season outcome is available:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_selected_opening_squad \
  --database /path/to/autofpl.db \
  --output /path/to/current-selected-opening-squad.json
```

It binds the retained evaluation identities, extends the selected six-week
policy through Gameweeks 7–8 using the registered preseason-only role rule and
remains as the immutable v1 comparator. See the
[selected opening-squad specification](../../docs/research/current-selected-opening-squad-shadow-v1.md).

The versioned product handoff freezes the best-supported appearance-hurdle
candidate with its separate point-model evaluation identity:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_best_supported_opening_squad \
  --database /path/to/autofpl.db \
  --output /path/to/current-best-supported-opening-squad.json
```

The v2 artifact exposes model-consistent GW1 and six-week player means, keeps
all eight role decisions frozen and is the current decision-room prediction.
It remains explicitly prospectively unscored and unpromoted. See the
[best-supported v2 specification](../../docs/research/current-best-supported-opening-squad-v2.md).

The model-level opening-policy screen reconstructs the direct and hurdle
distributions on identical historical opening targets, globally solves the
same registered six-Gameweek policy and compares exact eight-Gameweek realised
FPL scores:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_appearance_hurdle_opening_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-hurdle-opening-evaluation.json
```

The first fixed run retained the hurdle challenger with a 20.67-point mean
gain and three wins from three targets. Because those outcomes were already
opened, the result strengthens the current prospective choice but cannot
promote it. See the
[historical hurdle opening-policy evaluation](../../docs/research/historical-appearance-hurdle-opening-policy-evaluation-v1.md).

The matching proper-score screen compares the full direct and hurdle
player-Gameweek distributions and appearance probabilities:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_appearance_hurdle_opening_distribution_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-hurdle-opening-distributions.json
```

The first fixed run improved CRPS by 1.38%, won all three opening targets and
all four positions, and improved appearance Brier and log loss. The empirical
ranges remain conservative and finite-sample limited. See the
[historical hurdle opening-distribution evaluation](../../docs/research/historical-appearance-hurdle-opening-distribution-evaluation-v1.md).

The next fixed feature ablation adds only prior BPS, influence, creativity and
threat summaries inspired by OpenFPL's public feature portfolio:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_official_creative_opening_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-official-creative-opening-evaluation.json
```

It must improve both player-distribution proper scores and the complete
six-week opening policy. The first run improved CRPS by 0.62% but lost six
realised squad points on average and regressed by 31 in the worst season, so
the enrichment is rejected and does not alter v2. See the
[official-creative opening evaluation](../../docs/research/historical-official-creative-opening-evaluation-v1.md).

The next fixed ablation attaches cutoff-safe team and opponent expected-goal
rates to the conditional-point model while leaving appearance unchanged:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_team_fixture_strength_opening_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-team-fixture-strength-opening-evaluation.json
```

The first run regressed CRPS by 1.125%, lost all three season comparisons and
lost 31.67 realised squad points on average. The representation is rejected
without tuning on the opened targets; v2 remains unchanged. See the
[team-fixture-strength opening evaluation](../../docs/research/historical-team-fixture-strength-opening-evaluation-v1.md).

The promoted-player appearance evaluator learns a Championship-to-Premier
League participation translation on expanding promotion classes and pools it
with the retained opening appearance probability:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_promoted_appearance_evaluation \
  --database /path/to/autofpl.db \
  --fbref-extraction 2021-22=/path/to/2021-22-extraction.json \
  --fbref-extraction 2022-23=/path/to/2022-23-extraction.json \
  --fbref-extraction 2023-24=/path/to/2023-24-extraction.json \
  --fbref-extraction 2024-25=/path/to/2024-25-extraction.json \
  --output /path/to/historical-promoted-appearance-evaluation.json
```

The fixed equal-weight pool improved Brier score by 23.12%, won all three
scored target seasons and all four positions, and retained a fully favourable
player-cluster bootstrap interval. This authorises full point-distribution and
opening-policy evaluation, not product promotion. See the
[promoted-player appearance evaluation](../../docs/research/historical-promoted-appearance-evaluation-v1.md).

The follow-on evaluator changes only those bridged appearance probabilities,
rebuilds joint point paths and applies the unchanged global six-Gameweek
opening policy:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_promoted_opening_evaluation \
  --database /path/to/autofpl.db \
  --fbref-extraction 2021-22=/path/to/2021-22-extraction.json \
  --fbref-extraction 2022-23=/path/to/2022-23-extraction.json \
  --fbref-extraction 2023-24=/path/to/2023-24-extraction.json \
  --fbref-extraction 2024-25=/path/to/2024-25-extraction.json \
  --output /path/to/historical-promoted-opening-evaluation.json
```

Affected-player CRPS improved by 8.94% and the all-player distribution was
non-worse, but complete squads gained only one realised point per target on
average with one win. The fixed +2 mean and two-win policy gates failed, so the
feature does not alter the current v2 initial squad. See the
[promoted-player opening evaluation](../../docs/research/historical-promoted-opening-evaluation-v1.md).

The non-serving current sensitivity applies that frozen translation to the 60
reviewed Coventry, Hull and Ipswich players and repeats the global solve:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_promoted_opening_sensitivity \
  --database /path/to/autofpl.db \
  --fbref-extraction 2021-22=/path/to/2021-22-extraction.json \
  --fbref-extraction 2022-23=/path/to/2022-23-extraction.json \
  --fbref-extraction 2023-24=/path/to/2023-24-extraction.json \
  --fbref-extraction 2024-25=/path/to/2024-25-extraction.json \
  --current-fbref-extraction /path/to/reviewed-2025-26-extraction.json \
  --output /path/to/current-promoted-opening-sensitivity.json
```

No affected player belongs to v2 or enters the sensitivity optimum. The squad
and every retained path score remain identical, so the served prediction stays
unchanged. See the
[current promoted-player sensitivity](../../docs/research/current-promoted-opening-sensitivity-v1.md).

The conditional-optimality audit distinguishes an exact solver result from a
robust player choice:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_opening_squad_optimality_audit \
  --database /path/to/autofpl.db \
  --output /path/to/current-opening-squad-optimality-audit.json
```

It finds the best distinct legal squad, globally reoptimises after excluding
each selected player and runs a deterministic paired-path bootstrap. Player
regret and selection frequency remain diagnostics of the current forecast,
not empirical proof or a promotion path. See the
[optimality-audit specification](../../docs/research/current-opening-squad-optimality-audit-v1.md).

The v2-specific audit runs the same global exclusions and paired-path
bootstrap on the retained appearance-hurdle distributions and binds the
complete historical opening-policy evidence:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_appearance_hurdle_opening_optimality_audit \
  --database /path/to/autofpl.db \
  --output /path/to/current-hurdle-opening-optimality-audit.json
```

Capture #19 confirms the shown squad as the zero-gap model optimum, classifies
five core players and identifies Roefs as the only fragile selection. See the
[v2 optimality-audit specification](../../docs/research/current-appearance-hurdle-opening-optimality-audit-v2.md).

The same fixed command is packaged as the non-root
`ghcr.io/jellman86/autofpl-analytics` companion image. Its default invocation
polls the read-only `/analytics-snapshot/autofpl.db`, does nothing while the
exact shadow is current, and atomically writes one capture-named JSON artifact
to `/analytics-inbox` when the latest supported target is missing. Once the
point shadow is present, the same worker creates the separately named joint
scenario and initial-squad-quality handoffs, freezes the selected eight-week
opening squad, generates the separately identified best-supported v2 handoff,
then advances to selection scoring and fixed-squad role strategies. It never
skips a prerequisite or combines import states. Mount the
database read-only and a separate private inbox writable by UID `1654`:

```bash
docker run \
  --read-only \
  --volume /private/autofpl-snapshot:/analytics-snapshot:ro \
  --volume autofpl-analytics-inbox:/analytics-inbox \
  ghcr.io/jellman86/autofpl-analytics:dev
```

The application publishes the standalone integrity-checked snapshot with
SQLite's online-backup operation and atomically replaces it when relevant
source identity changes. The worker never writes SQLite, exposes no port and
supports only the frozen 2026/27 GW1 target. The `.NET` application remains the
only strict product import boundary: when its bounded inbox poll is enabled, it
imports at most one file of each type per cycle and renames it `.imported` or
`.rejected`. The worker treats either marker as final for that exact source
identity, avoiding a regeneration loop.

## Current initial-squad quality shadow v1

The first full-squad replacement candidate uses the exact current joint
scenario artifact and official prices to solve squad, XI and captaincy
together:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_initial_squad_candidate \
  --database /path/to/autofpl.db \
  --output /path/to/current-initial-squad-shadow.json
```

The MILP enforces budget, club, composition and formation rules and must reach
a zero-gap optimum for its fixed transparent linear surrogate. The resulting
selection is then scored with exact FPL captaincy and auto-substitution rules on
the retained joint rows. It remains prospective shadow evidence and cannot
replace advice. See the
[initial-squad shadow specification](../../docs/research/current-initial-squad-quality-shadow-v1.md).

## Initial-squad prospective outcome evaluation v1

The frozen model squad, optimiser candidate, point components and appearance
probabilities can be scored without retraining once their later final official
outcome exists:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.initial_squad_outcome_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/initial-squad-outcome.json
```

The selected multi-Gameweek opening policy has a separate preregistered,
incremental Gameweek 1–8 evaluator:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.selected_opening_squad_outcome_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/selected-opening-squad-outcome.json
```

It freezes the final predeadline selected artifact, scores the exact stored
weekly roles with the reference FPL scorer, and compares them with two
same-capture GW1 benchmarks held unchanged. Before an official outcome exists
it exits successfully in a waiting state and writes no artifact. Gameweek 1–7
reports are partial and cannot pass the promotion-evidence gate.

The rejected transfer-aware historical challenger remains reproducible:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_transfer_aware_opening_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-transfer-aware-opening.json
```

It globally solves a six-Gameweek opening plan with at most one free transfer
before each later Gameweek, then holds the final squad through Gameweek 8 for
common exact-FPL scoring. The three reused historical targets reject it:
`-14`, `-74` and `+24` points versus the fixed incumbent. It remains research
evidence and is not generated by the current worker.

Before the outcome, the command returns
`waiting-for-official-outcome` without writing a report. The evaluated report
uses official points and appearance, exact FPL auto-substitution/captaincy,
point MAE/RMSE/bias, empirical CRPS and appearance Brier/log loss. It remains
one prospective fold rather than a promotion decision. See the
[prospective evaluation specification](../../docs/research/initial-squad-prospective-outcome-evaluation-v1.md).

## CPU joint-scenario reference v1

The first simulation kernel scores a complete selection over supplied joint
points/participation rows:

```python
from autofpl_analytics.scenario_reference import (
    sample_joint_scenarios,
    score_selection_scenarios,
)
```

Sampling uses an explicit PCG64 seed and selects whole support rows, preserving
their cross-player dependence. Scoring applies the same goalkeeper/outfield
auto-substitution, formation and captaincy order as the deterministic domain.
Candidate comparisons are paired on identical rows. The kernel is
research-only: it does not manufacture a distribution from the current
uncalibrated intervals and cannot influence advice until calibrated,
point-in-time scenario inputs are registered. See the
[CPU reference specification](../../docs/research/cpu-joint-scenario-reference-v1.md).

## Joint player-Gameweek scenario shadow v1

The read-only expanding-origin evaluator compares one whole-Gameweek residual
row per eligible training Gameweek with degenerate tree, player-empirical and
position-empirical distributions:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_joint_scenario_evaluation \
  --database /path/to/autofpl.db \
  --season 2025-26 \
  --output /path/to/historical-joint-scenario.json
```

After that frozen candidate passes its retrospective screen, the current
command binds the exact point, participation and source-archive artifacts and
emits a complete matrix for the supported target:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_joint_scenario_forecast \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 1 \
  --output /path/to/current-joint-scenario.json
```

The current artifact contains integer point and played/not-played rows,
source-Gameweek provenance, player-column identities, availability-adjusted
point means, matrix diagnostics and a content hash. It is prospectively
unscored and cannot influence advice. The application imports it only through
the strict private handoff described in the operations guide. See the
[scenario research note](../../docs/research/joint-player-gameweek-scenario-shadow-v1.md).

## Historical participation evaluation v1

The companion evaluator tests fixed appearance, start, 60-minute and uncapped
total-minutes challengers on the same pinned archive and locked temporal split:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_participation_evaluation \
  --database /path/to/autofpl.db \
  --season 2025-26 \
  --output /path/to/historical-participation-report.json
```

Binary targets use Brier score, log loss and calibration error; minutes uses
MAE and RMSE. Comparator selection occurs only on expanding-origin development
folds before the Gameweek 31–38 holdout opens. The report is deterministic,
read-only and cannot alter the preseason artifact or served advice. See the
[historical participation specification](../../docs/research/historical-participation-evaluation-v1.md).
The first retained result supports provisional appearance, start and
60-minute classifiers, while rejecting the exact-minutes tree in favour of
the player-last baseline.

## Historical participation coherence diagnostic v1

The coherence evaluator applies one fixed equal-weight Euclidean projection to
the independently fitted appearance, start and 60-minute probabilities on the
same historical folds:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_participation_coherence_evaluation \
  --database /path/to/autofpl.db \
  --season 2025-26 \
  --output /path/to/historical-participation-coherence.json
```

It requires appearance probability to be at least start and 60-minute
probability, leaves already coherent vectors unchanged and compares raw with
projected Brier, log-loss, calibration, fold and position results. Because the
underlying holdout had already been opened before this correction was
motivated, the output is always a secondary diagnostic rather than a new
promotion test and can never authorize product import. See the
[historical participation coherence specification](../../docs/research/historical-participation-coherence-evaluation-v1.md).
The first real run removed all 55 historical nesting violations but failed the
fixed predictive-quality gate: appearance Brier and log loss regressed, while
60-minute Brier was non-worse in only three of eight folds. The projection is
therefore rejected for the current artifact.

## Historical joint participation evaluation v1

The next evaluator fits one five-state classifier and derives coherent
appearance, start and 60-minute marginals from its probability distribution:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_joint_participation_evaluation \
  --database /path/to/autofpl.db \
  --raw-report ../../docs/research/results/historical-participation-coherence-2025-26-v1.json \
  --season 2025-26 \
  --output /path/to/historical-joint-participation.json
```

It verifies and reuses the retained raw comparator identities instead of
refitting 24 unchanged classifiers. Because this model was designed after the
historical holdout was opened, its result is an exploratory candidate screen
only; the exact model must face genuinely new 2026/27 folds before product use.
See the
[historical joint participation specification](../../docs/research/historical-joint-participation-evaluation-v1.md).
The first real screen produced zero coherence violations and improved start
and 60-minute Brier/log loss in seven of eight folds, but appearance regressed
on aggregate Brier, log loss and the fold gate. The full joint model is
therefore rejected; a raw-appearance plus conditional-child factorization is
next.

## Historical conditional participation evaluation v1

The conditional challenger preserves the fixed raw appearance classifier,
fits start and 60-minute classifiers only on historical appearance-positive
rows and multiplies each conditional probability by raw appearance. This
guarantees coherent child marginals without sacrificing appearance:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_conditional_participation_evaluation \
  --database /path/to/autofpl.db \
  --raw-report ../../docs/research/results/historical-participation-coherence-2025-26-v1.json \
  --output /path/to/historical-conditional-participation.json
```

The historical screen is exploratory because its model family was chosen
after the holdout was opened. See the
[historical conditional participation specification](../../docs/research/historical-conditional-participation-evaluation-v1.md).
The first real screen preserved appearance and improved aggregate start and
60-minute Brier/log loss, but start was non-worse in only four of eight folds.
The fixed gate therefore rejects the combined candidate. Raw, joint and
factorized variants are now frozen for prospective 2026/27 comparison.

## Historical conditional-minutes evaluation v1

The minutes hurdle candidate preserves the fixed supported appearance
classifier, fits a minutes regressor only on historical appearance-positive
rows and multiplies the two predictions:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_conditional_minutes_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-conditional-minutes.json
```

It compares the resulting unconditional minutes mean against both the retained
player-last baseline and the rejected zero-heavy unconditional histogram tree
on identical Gameweek 31–38 folds. The fixed dual-reference gate, fixture
bound and reused-holdout boundary are in the
[conditional-minutes specification](../../docs/research/historical-conditional-minutes-evaluation-v1.md).

The follow-on distribution evaluator combines the fixed appearance probability
with each player's positive-minutes empirical support and compares the exact
weighted distribution with player, last-value and position references:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.historical_minutes_distribution_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/historical-minutes-distribution.json
```

CRPS is primary; interval coverage remains diagnostic. See the
[minutes-distribution specification](../../docs/research/historical-minutes-distribution-evaluation-v1.md).

The passing historical specification can be frozen against the exact current
GW1 participation snapshot without changing served expected minutes:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_minutes_distribution_forecast \
  --database /path/to/autofpl.db \
  --output /path/to/current-minutes-distribution.json
```

The output preserves both the historically screened raw appearance input and a
separately labelled official-availability ceiling variant. See the
[current minutes-distribution shadow](../../docs/research/current-minutes-distribution-shadow-v1.md).

## Current official availability ceiling v1

The current participation artifact carries a prospective comparison variant
that treats official chance as an appearance ceiling, then re-derives start
and 60-minute marginals from the frozen conditional rates. It does not
multiply two unproven independent probabilities, does not alter the raw
fields and cannot influence advice before real 2026/27 scoring. Unknown or
inconsistent official state fails closed. See the
[availability ceiling specification](../../docs/research/current-official-availability-ceiling-v1.md).

## Provisional preseason participation forecast v1

The current-player generator fits only the three supported classifiers and
retains baseline-labelled exact minutes:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.preseason_participation_forecast \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 1 \
  --output /path/to/gw1-preseason-participation.json
```

It requires the exact retained evaluation identities, joins prior history by
stable official code and reports raw event-nesting incoherence. Artifact v1.2
also retains coherent-factorized and official-ceiling-factorized comparison
variants without replacing the raw fields. The artifact is deterministic,
read-only, refuses overwrite, carries a fail-closed product-import readiness
decision and cannot influence advice. See the
[provisional participation forecast specification](../../docs/research/preseason-participation-forecast-v1.md).
Its first v1.1 current-player run is retained there. Product import remains
blocked by unsupported coherence candidates, unscored prospective
availability evidence and promoted/new-player coverage gaps.
The complete first v1.2 per-player artifact is retained before outcomes; its
official ceiling changed 32 of 557 players while both factorized variants
remained coherent for every player.

## Provisional preseason player forecast v1

After the locked holdout supports the fixed candidate, the current-player
generator fits that unchanged histogram-tree configuration to every archived
2025/26 player-Gameweek sample and compares its GW1 point means with the exact
Baseline v0 player artifact:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.preseason_player_forecast \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 1 \
  --output /path/to/gw1-preseason-challenger.json
```

The command requires the exact source revision and content hashes that passed
the retained evaluation. Current players join prior history only by stable
official code; missing identities remain explicit. Current official
availability accompanies every prediction but does not silently alter the
fitted point mean. The artifact is deterministic, refuses overwrite and cannot
influence served advice.

See the
[provisional preseason forecast specification](../../docs/research/preseason-player-forecast-v1.md).

## Baseline evaluation v4

The first local command reads the authoritative SQLite database in read-only
mode and evaluates deterministic total-points and expected-minutes baselines
plus smoothed probability-of-60-minutes and empirical point/minutes
distribution baselines with expanding Gameweek origins:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --output /path/to/baseline-report.json
```

It exits `2` with a machine-readable `insufficient-data` report until at least
one target Gameweek has the configured number of prior complete outcomes that
were available by that target deadline. Existing output files are never
overwritten. Reports remain explicitly exploratory and cannot populate player
cards or influence advice.

The target, temporal split, metrics, baselines and current limitations are
recorded in the
[baseline evaluation specification](../../docs/research/baseline-evaluation-v4.md).

## Temporal feature table v2

The read-only feature command materialises player and team information that was
actually available with one selected official pre-deadline capture:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.feature_table \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 2 \
  --output /path/to/features-2026-27-gw2.json
```

It includes player outcome lags, rolling 1/3/5-Gameweek statistics,
exponentially weighted form, explicit missingness, target fixtures and rest
gaps, plus rolling team attack/defence/result form split by venue. Official
xG/xA/xGC, ICT/BPS and defensive outcomes retain null legacy values and expose
per-metric sample counts instead of inventing zeroes. Earlier outcome
corrections are admitted only when they were available by the selected replay's
actual capture time. Output is deterministic, hash-identified, exploratory and
never overwrites an existing file.

The exact boundary and limitations are recorded in the
[temporal feature table specification](../../docs/research/temporal-feature-table-v2.md).

## Cross-season player state v1

The read-only cross-season command joins the pinned 2025/26 archive to one
current official target through stable player codes:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.cross_season_player_state \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 1 \
  --output /path/to/cross-season-state.json
```

Every current player remains present. Missing prior identity is explicit;
prior-season 1/3/5/10-Gameweek and EWMA performance/durability summaries never
override current official health. The artifact is deterministic, read-only,
hash-identified and explicitly does not influence a forecast. See the
[cross-season state specification](../../docs/research/cross-season-player-state-v1.md).

The prior-competition coverage audit turns the remaining current-player
history gaps into a source-integration target:

```text
python -m autofpl_analytics.prior_competition_coverage \
  --forecast /path/to/preseason-participation-v1.2.json \
  --prior-competition-club "Coventry City" \
  --prior-competition-club "Hull City" \
  --prior-competition-club "Ipswich Town" \
  --output /path/to/prior-competition-coverage.json
```

It verifies the complete frozen source artifact, preserves official stable
codes, distinguishes prior-competition squads from other new/transferred
players and emits the exact bounded capture fields needed from Quark's existing
research services. Names are never an automatic identity fallback and the
audit cannot influence a forecast. See the
[coverage specification](../../docs/research/prior-competition-player-history-coverage-v1.md).

## Cross-season feature ablation v1

The promotion-gate command compares unchanged ridge/tree models with
cross-season-state variants on identical source-complete folds:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.cross_season_ablation \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/cross-season-ablation.json
```

Missing prior player identity remains explicit; a prior-season archive missing
at any fold cutoff excludes that fold from every variant. Archived final health
and source `xP` are not candidate features. The report remains unpromoted and
returns exit code `2` until a source-complete fold exists. See the
[cross-season ablation specification](../../docs/research/cross-season-feature-ablation-v1.md).

## Official underlying feature ablation v1

The official feature ablation compares the unchanged ridge/tree contract with
fixed xG/xA/xGC, ICT/BPS and defensive-contribution additions on identical
expanding-origin folds:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.official_underlying_ablation \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/official-underlying-ablation.json
```

Rolling-three and exponentially weighted means carry observed sample counts.
Ridge preprocessing and tree feature removal remain training-fold local. The
report is deterministic, read-only and unpromoted, and returns exit code `2`
until an eligible official fold exists.

See the
[official underlying feature ablation specification](../../docs/research/official-underlying-feature-ablation-v1.md).

## Temporal ridge challenger v1

The first feature-consuming challenger fits a fixed regularised linear model
inside honest expanding Gameweek origins:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.temporal_ridge \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/temporal-ridge-report.json
```

All median imputation, missing indicators, standardisation and fitting are
training-fold local. The deterministic report compares ridge with four simple
point baselines, remains explicitly exploratory, and exits `2` when too few
complete historical feature/outcome pairs exist.

The fixed feature and evaluation design are recorded in the
[temporal ridge specification](../../docs/research/temporal-ridge-v1.md).

## Temporal histogram-tree challenger v1

The nonlinear comparison reuses the ridge evaluator's exact folds, target
players and incumbents:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.temporal_tree \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/temporal-tabular-report.json
```

Its scikit-learn implementation and transitive scientific dependencies are
version- and hash-locked. Missing branches and unsplittable feature removal are
learned inside each training fold. The model remains exploratory and fixed
before real evaluation.

See the
[temporal tree specification](../../docs/research/temporal-tree-v1.md).

## Cutoff-safe FPL Form features v1

The first scraped-forecast modelling bridge joins a retained FPL Form capture
only when it was available by the official feature replay's exact capture
time:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.fpl_form_feature_table \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --gameweek 4 \
  --output /path/to/fpl-form-features-gw4.json
```

It validates direct player and fixture identities again, aggregates
fixture-level conditional and appearance-adjusted values, preserves missing
players/probabilities, and remains an unpromoted feature artefact. See the
[FPL Form temporal feature specification](../../docs/research/fpl-form-temporal-feature-v1.md).

## FPL Form model-feature ablation v1

The source ablation recomputes official-only and FPL-Form-enhanced ridge/tree
models on identical, source-complete expanding-origin folds:

```bash
PYTHONPATH=src/analytics python3 -m autofpl_analytics.fpl_form_ablation \
  --database /path/to/autofpl.db \
  --season 2026-27 \
  --minimum-training-gameweeks 3 \
  --output /path/to/fpl-form-ablation.json
```

Every training and target Gameweek must have a forecast captured by its own
official feature cutoff. Missing source history excludes the fold from every
variant; missing player predictions and appearance probabilities remain
explicit model inputs. The report is read-only, deterministic, unpromoted and
returns exit code `2` until at least one source-complete fold exists. See the
[FPL Form feature ablation specification](../../docs/research/fpl-form-feature-ablation-v1.md).
