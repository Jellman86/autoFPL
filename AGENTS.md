# AGENTS.md — mandatory working agreement

These instructions apply to every contributor.

## Mission

autoFPL is a private, non-commercial, self-hosted home-lab project. Its north star is the highest practical FPL prediction quality using useful free or self-hosted data and research-backed methods. Judge work by leakage-free out-of-time predictive gain, calibration, decision utility, reliability and operational simplicity—not enterprise ceremony.

Public-web search, scraping, browser rendering, public read-only endpoints and bounded Byparr-assisted collection are in scope. Do not collect credentials or session material, bypass login or paid access, create abusive traffic, republish third-party corpora or automate FPL account actions. The user acts manually.

## Delivery workflow

1. Start from current `dev` on a short-lived branch.
2. Build the smallest end-to-end slice that produces working application behaviour or a measurable research result.
3. Write focused tests for production behaviour and bug fixes. Exploratory research code may iterate quickly but must be tested before it becomes a relied-on pipeline.
4. Run focused checks while developing and `make verify` before push.
5. Update only documentation or decisions that would otherwise become materially misleading.
6. Open a PR to `dev`; release PRs alone target `main`.
7. Do not merge failing required checks.

Plans, contracts, reviews and documentation support implementation; they must not replace or indefinitely delay it.

## Scientific integrity

- Research literature and strong existing methods guide the candidate set, but implementation may begin without a separately accepted review.
- Reproduce simple and strong baselines before assuming a complex method is better.
- Split and evaluate by time; random train/test splits cannot support temporal FPL claims.
- Fit transforms, imputers, encoders, calibrators and selectors using training data only.
- Preserve the decision-time cutoff and an `available_at` timestamp for every predictive input.
- Before opening the final holdout or promoting a model, register the target, horizon, temporal split, baselines, metrics and promotion rule.
- Never select a model, feature, horizon or seed using final holdout results.
- Promotion requires local rolling/walk-forward evidence, calibration, useful failure slices and comparison with declared baselines.
- Label exploratory results clearly and retain negative findings.
- A promoted result records enough code, data, configuration and seed provenance to reproduce it.

## Engineering integrity

- Prefer a single deployable application and SQLite. Add services or another database only after a measured need.
- Keep domain rules deterministic and isolated from network, clock and random-number sources.
- Store FPL money as integer tenths of a million and timestamps as timezone-aware UTC instants.
- Version stable external, MCP and persisted-data boundaries. Private internal v0.x APIs may evolve with tests and migrations; they do not require an ADR for every change.
- LLM output is untrusted data. It cannot directly mutate authoritative state or execute a recommendation.
- Keep logs useful and free of secrets or unnecessary personal data.

## Security and supply chain

- Never commit credentials, tokens, cookies, private keys, `.env` files or production data.
- GitHub Actions use minimal permissions and third-party actions pinned to full 40-character SHAs.
- Lock dependencies used in CI or deployment and pin deployed container images by digest.
- No `curl | sh`, privileged containers, Docker socket mounts or public ports by default.
- Validate untrusted inputs and use parameterised database access.
- Update the threat model only when a trust boundary materially changes.

## Definition of done

A development slice is done when its acceptance behaviour works, focused and repository tests pass, and the changed user/operator behaviour is documented where needed. Research-promotion and release/deployment gates apply only when the change actually reaches those stages.
