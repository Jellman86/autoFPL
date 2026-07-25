# Research and Model Validation Standard

## North star

Use the strongest practical research-backed methods that improve FPL decisions on this private home-lab system. A method earns use through local point-in-time, out-of-time evidence—not paper reputation, novelty or one favourable season.

## Evidence-led implementation

Literature searches, strong open-source implementations and credible practitioner evidence should guide which methods are worth trying. They do not require a separately accepted document before code can be written. The repository review template is available as a working note for substantial investigations.

Implement simple baselines early, then established and frontier challengers where the expected value justifies the compute and complexity. Exploratory code and results must be labelled as such and cannot influence recommendations.

## Registration and promotion

Before opening a final holdout or making a confirmatory promotion claim, record:

- decision, population, target and horizon;
- information cutoff and point-in-time dataset;
- temporal training, validation and final-test windows;
- naive, strong and incumbent baselines;
- candidate set and tuning budget;
- proper scoring, calibration and decision-utility metrics; and
- the promotion threshold and failure conditions.

Dataset and model cards are required only for artefacts promoted into recommendations. Independent review applies at promotion, not before exploratory implementation.

## Temporal validity

- Use expanding-window or rolling-origin evaluation.
- Require `available_at <= decision_cutoff` for every predictive input.
- Fit preprocessing within each training fold.
- Use the injury, line-up, price, ownership and statistic revision available at the decision time.
- Do not use future entity mappings or corrected final values in historical features.
- Random splits cannot support temporal performance claims.
- Use the final holdout once for the registered claim; further tuning creates a new claim and holdout.

## Baselines and metrics

Start with methods such as rolling minutes/points rates, simple minutes × rate, no-change/current squad and any point-in-time market or incumbent baseline available. Compare more complex methods using proper scoring and calibration appropriate to the target, plus downstream decision utility.

Useful metrics may include log loss, Brier score, CRPS, calibration plots, interval coverage, realised points and regret against an information-matched oracle. Report meaningful slices such as position, club, price, injury state and fixture congestion where sample size permits.

Prefer the simplest method whose validated result is not meaningfully worse than the best challenger. Retain failed and negative runs.

## Research-backed methods

Statistical, Bayesian, tree-based, neural and ensemble methods are all valid candidates. Generative models may assist extraction, classification and explanation, but their output remains untrusted candidate data until source-linked validation succeeds. Complexity is welcome when it produces repeatable predictive or decision gain.

## Simulation and optimisation

Simulations should preserve material dependencies and record seeds and assumptions. Optimisation records its objective and constraints; tiny cases are checked by hand, brute force or a trusted implementation. “Optimal” means optimal for the stated forecast, objective and constraints.

## Promotion gate

A candidate may influence recommendations only when leakage checks pass, it beats or materially complements declared baselines across credible temporal windows, calibration and important failure slices are acceptable, and the run is reproducible. Roll back to the previous model if later evidence shows material degradation.
