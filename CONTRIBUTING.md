# Contributing

## Start with a working slice

1. Start from current `dev` on a short-lived `feat/`, `fix/`, `research/`, `docs/`, `refactor/` or `chore/` branch.
2. State the user or research outcome and the smallest acceptance test that proves it.
3. Implement a vertical slice; do not substitute plans, contracts or governance for working behaviour.
4. Open a PR to `dev`. Only releases and emergency hotfixes target `main`.

Write an ADR only for a consequential, hard-to-reverse architecture, security, compliance or data decision. Update documentation only when current behaviour, operation or an important assumption changes.

## Product development

Use focused behavioural tests for production code:

1. Express the behaviour or reproduce the bug in a test.
2. Implement the smallest useful path.
3. Run the focused tests while iterating.
4. Run `make verify` and formatting before push.

Exploratory analytics may iterate before its interface stabilises. Move any pipeline used by the application into tested modules before relying on it.

## Predictive research

Use papers, credible practitioner evidence and established methods to choose worthwhile baselines and challengers. The literature-review template is an optional working aid, not a pre-implementation approval gate.

Before examining a final holdout or promoting a forecast into recommendations:

- register the target, horizon, cutoff, temporal split, baselines, metrics and promotion rule;
- use point-in-time data and rolling/walk-forward evaluation;
- compare with simple and strong baselines;
- report calibration, decision utility, uncertainty and material failure slices;
- retain negative results; and
- record enough code/data/configuration/seed provenance to reproduce the result.

Dataset and model cards are required for promoted recommendation artefacts, not every exploratory script.

## Pull requests

Use a Conventional Commit title. Keep the PR focused, explain what now works and include real verification output. Independent review is required for security/trust boundaries, deployment or supply-chain changes, authoritative FPL rules, promoted data/models, credentials and external actions. Ordinary low-risk changes use author review and passing CI.

## Required local verification

```bash
python3 -m pip install --require-hashes --requirement requirements-governance.txt
make verify
dotnet format src/backend/AutoFpl.slnx --verify-no-changes --no-restore
```

Run migration, container, security or deployment checks only when that boundary changed.

No exception can permit secrets, temporal leakage, automatic FPL account action or unsafe handling of untrusted input.
