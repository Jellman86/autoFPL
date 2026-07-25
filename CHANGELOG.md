# Changelog

All notable implemented changes to autoFPL are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases will use [Semantic Versioning](https://semver.org/) once product releases begin.

## Unreleased

### Changed

- **Predictive features now require evidence review before implementation.** Prediction, simulation and optimisation work begins with a structured, reproducible literature review covering strong baselines, established methods, credible frontier challengers, contradictory evidence and FPL applicability; paper-backed methods still require preregistered local out-of-time validation before promotion.
- **Development quality gates now prioritise correctness and research validity over ceremony.** `dev` requires comprehensive tests, governance and secret scanning while signatures, stale-base reruns and universal blocking on slower analyses are reserved or relaxed as appropriate; `main` remains the strict signed release boundary, and independent review is risk-based.

### Added

- **Effective captaincy is now deterministic executable product behavior.** A fail-closed endpoint composes a valid complete gameweek selection with manually supplied player-minute evidence, retaining the captain, promoting the vice-captain, or returning no effective captain according to the current official rule without status ingestion, substitution execution or scoring.
- **Complete manual gameweek selections are now executable product behavior.** A fail-closed endpoint composes the existing squad and starting-XI rules with a replacement goalkeeper and three ordered outfield substitutes, preserving bench priority without FPL account access, appearance data, substitution execution or scoring.
- **Starting-XI feasibility is now executable product behavior.** A manual-input API endpoint validates that 11 unique starters belong to a valid squad, satisfy current 2026/27 goalkeeper/defender/forward formation rules, and have distinct captain and vice-captain selections from the XI.
- **Manual squad feasibility is now executable product behavior.** A new API endpoint applies deterministic 15-player composition, unique-player, club-limit and exact integer-tenths budget rules without external FPL access or account automation.
- **Initial lawful source admissions now have an executable provenance contract.** A Draft-07 schema binds manual input and repository-authored synthetic fixtures to their admission evidence, purpose, rights and point-in-time metadata; checked-in examples are enforced without adding runtime ingestion or persistence.
- **The verified development API is privately deployed through Dockhand.** The Git-backed Compose stack pins the published GHCR manifest digest, runs with a read-only root filesystem and bounded resources, and exposes the service only on the trusted internal Docker network with no host port, proxy host or public DNS route.
- **A bounded private-development API and hardened container artifact are available from `dev`.** The service exposes liveness, readiness, decision-snapshot metadata validation and deterministic manual squad validation; rejects malformed, ambiguous and oversized requests; runs non-root in a digest-pinned chiseled image; and is built, scanned, smoke-tested and published by protected CI without external FPL access, persistence, providers or account automation.
- **Documentation now has a maintained standard and index.** Repository claims must be grounded in code, contracts, deployment definitions or versioned evidence; planned work is separated from implemented behaviour; safety, research and human-approval boundaries are required where applicable; and CI checks local documentation links.
- **The project roadmap now states priority outcomes and evidence gates.** It covers the private development service, lawful data ingestion, authoritative rules, forecast research, simulation and optimisation, advisory interfaces, human approval, and release hardening without promising dates.
- **Decision-snapshot metadata has a versioned JSON contract.** The `1.0` schema, examples and .NET contract model enforce canonical source types and reject schema drift before snapshot metadata can enter later decision workflows.
- **The public repository foundation is governed and reproducible.** Branch/release policy, security and compliance boundaries, engineering and research standards, threat modelling, dependency controls, architecture decisions and executable repository-policy tests establish the constraints for later product work.

[Unreleased]: https://github.com/Jellman86/autoFPL/compare/dev...HEAD
