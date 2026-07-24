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

Before predictive/statistical/simulation/optimisation implementation, copy `docs/research/literature-review-template.md` into `docs/research/reviews/`, complete its structured search and evidence matrix, and link the accepted review from the issue. Include strong baselines, established methods, credible frontier challengers, contradictory evidence and FPL applicability limits. A recent paper informs what to test; it does not authorize production use.

Every experiment then starts with a copied `docs/research/experiment-template.yaml` linked to that review. Register the hypothesis, candidate set, decision rule, data cutoff, split, baselines, metrics and promotion threshold before examining final test results. Complete a dataset card and model card for any artefact used in a recommendation.

### Generated work

AI-generated code, prose and extraction are proposals, not evidence. The author is accountable for understanding, testing and citing every retained change. Generated dependencies or licences must be reviewed independently.

### Documentation

Follow the [documentation standard](docs/standards/documentation.md). Ground claims in current code, tests, contracts, deployment definitions or versioned evidence; distinguish planned work from implemented behaviour; and state applicable safety, compliance, uncertainty and human-approval boundaries.

Link every new maintained reader-facing page from the [documentation index](docs/index.md). Add user- or operator-relevant implemented behaviour to the **Unreleased** section of [the changelog](CHANGELOG.md); keep future outcomes in [the roadmap](docs/roadmap.md).

## Commits and pull requests

Use Conventional Commit titles such as:

```text
feat(api): add recommendation snapshot endpoint
fix(analytics): prevent post-deadline feature leakage
research(minutes): register walk-forward baseline
```

PRs must be small enough to review, link the issue/ADR/experiment, include the test evidence and complete the repository PR template. Independent review is required for security/trust boundaries, deployment or supply-chain changes, authoritative domain logic, data/research promotion, credentials and external actions. Ordinary low-risk changes require focused author review and passing CI, not ceremonial multi-review.

## Required local verification

```bash
python3 -m pip install --require-hashes --requirement requirements-governance.txt
make verify
```

Run any additional formatting, container, migration, security and application-specific gates required by the changed boundary.

## Exceptions

A standard may be temporarily waived only through a dated exception in the PR containing:

- owner;
- precise scope;
- rationale and risk;
- compensating control;
- expiry date;
- linked remediation issue.

“Prototype”, “urgent” and “AI-generated” are not exemptions from security, compliance or data-leakage rules.
