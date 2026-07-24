# Contributing

## Before starting

1. Read `AGENTS.md`, `GOVERNANCE.md` and the relevant standards under `docs/standards/`.
2. Confirm the work is allowed by `docs/compliance/fpl-terms-boundary.md`.
3. Open or reference an issue with acceptance criteria, non-goals, risks and evidence requirements.
4. For a material architectural, data, model, security or compliance decision, write an ADR before implementation.

## Branches

Create a short-lived branch from `dev`:

- `feat/<description>`
- `fix/<description>`
- `research/<description>`
- `docs/<description>`
- `refactor/<description>`
- `chore/<description>`

PRs normally target `dev`. Only reviewed release and emergency hotfix PRs target `main`.

## Development discipline

### Behaviour changes

Use strict test-driven development:

1. Write one failing behavioural test.
2. Run it and confirm the expected failure.
3. Add the smallest implementation that passes.
4. Run the focused and full test suites.
5. Refactor only while green.

### Research changes

Every experiment starts with a copied `docs/research/experiment-template.yaml`. Register the hypothesis, decision rule, data cutoff, split, baselines and metrics before examining final test results. Complete a dataset card and model card for any artefact used in a recommendation.

### Generated work

AI-generated code, prose and extraction are proposals, not evidence. The author is accountable for understanding, testing and citing every retained change. Generated dependencies or licences must be reviewed independently.

## Commits and pull requests

Use Conventional Commit titles such as:

```text
feat(api): add recommendation snapshot endpoint
fix(analytics): prevent post-deadline feature leakage
research(minutes): register walk-forward baseline
```

PRs must be small enough to review, link the issue/ADR/experiment, include the test evidence and complete the repository PR template. Do not self-certify a required independent review.

## Required local verification

```bash
python3 -m pip install --require-hashes --requirement requirements-governance.txt
python3 -m unittest discover -s tests -p 'test_*.py' -v
python3 tools/governance/check_repository.py
```

Application-specific build, formatting, type, test, migration and security commands will become mandatory as each project is introduced.

## Exceptions

A standard may be temporarily waived only through a dated exception in the PR containing:

- owner;
- precise scope;
- rationale and risk;
- compensating control;
- expiry date;
- linked remediation issue.

“Prototype”, “urgent” and “AI-generated” are not exemptions from security, compliance or data-leakage rules.
