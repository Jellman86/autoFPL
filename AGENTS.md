# AGENTS.md — mandatory working agreement

These instructions apply to every human and automated contributor. More specific `AGENTS.md` files may add constraints but may not weaken this file.

## Mission boundary

autoFPL is a human-in-the-loop FPL decision-support research project. Do not implement scraping, credential collection, session replay, transfer submission, chip activation or other automated FPL account actions without a repository ADR recording written Premier League permission and a reviewed supported integration.

## Required workflow

1. Start from current `dev`; use a short-lived `feat/`, `fix/`, `docs/`, `research/`, `refactor/` or `chore/` branch.
2. Define acceptance criteria and evidence before implementation.
3. For behaviour changes, follow strict RED-GREEN-REFACTOR. Record the failing test before production code.
4. Keep changes vertically sliced and within the approved issue/ADR scope.
5. Run the narrow test, then the complete verification suite.
6. Update documentation, ADRs, dataset/model cards and experiment manifests when behaviour or assumptions change.
7. Open a PR to `dev`; release PRs alone target `main`.
8. Do not merge with failing, skipped or unresolved required checks.

## Scientific integrity

- Split and evaluate by time; random train/test splits are forbidden for temporal FPL claims.
- Fit every transform, imputer, encoder, calibrator and selector using training data only.
- Preserve immutable pre-deadline snapshots and an `available_at` timestamp for every feature.
- Never select a model, feature, horizon or seed using final test results.
- Before implementing a predictive, statistical, simulation, optimisation or data-derived performance feature, complete and link a versioned evidence review using `docs/research/literature-review-template.md`.
- Reproduce strong baselines before frontier candidates; current papers inform the candidate set but never bypass local point-in-time validation.
- Compare against declared naive and incumbent baselines.
- Report uncertainty, calibration and failure slices, not a single headline score.
- Label exploratory results; do not promote them as confirmatory evidence.
- Negative and inconclusive results are retained.
- Every published result records code SHA, data snapshot/hash, dependency lock hash, seeds, configuration, hardware class and exact command.
- Do not claim “optimal” without naming the objective, constraints, forecast distribution, horizon and uncertainty assumptions.

## Engineering integrity

- No production code before a failing automated test.
- Domain logic is deterministic and isolated from network, clock and random-number sources.
- Monetary values use integer tenths of a million; timestamps are timezone-aware UTC internally.
- Migrations are forward-only, transactional where supported, backward-compatible during rollout and tested from a production-like prior schema.
- APIs are contract-first and versioned; breaking changes require an ADR and migration path.
- LLM output is untrusted data. It cannot directly mutate authoritative state or execute a recommendation.
- Logs must be structured, correlation-aware and free of secrets or unnecessary personal data.
- Warnings are errors in CI unless an explicit, expiring exception is documented.

## Security and supply chain

- Never commit credentials, tokens, cookies, private keys, `.env` files or production data.
- GitHub Actions use minimal permissions and third-party actions pinned to full 40-character SHAs.
- Lock every dependency set; verify lockfiles in CI; pin container base images by digest before deployment.
- No `curl | sh`, unreviewed generated code, privileged containers, Docker socket mounts or public ports by default.
- Validate all untrusted inputs at the boundary and use parameterised database access.
- Security-sensitive changes require threat-model and abuse-case updates.

## Commands

```bash
python3 -m unittest discover -s tests -p 'test_*.py' -v
python3 tools/governance/check_repository.py
```

Future language-specific commands must be added to `Makefile` and CI together.

## Definition of done

A task is not done when code exists. It is done only when the acceptance criteria, tests, research integrity, security review, documentation, observability, rollback plan and provenance requirements in `docs/standards/definition-of-done.md` are satisfied.
