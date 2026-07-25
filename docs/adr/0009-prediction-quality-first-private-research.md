# ADR-0009: Prediction-quality-first private research boundary

- **Status:** Accepted
- **Date:** 2026-07-25
- **Owners:** Jellman86
- **Decision class:** product, research, data, security
- **Supersedes:** ADR-0002's written-permission/data-admission gate plus ADR-0006 and ADR-0007 collection restrictions where inconsistent

## Context

autoFPL is a private, non-commercial home research project hosted in a free public GitHub repository. Earlier documentation treated ordinary data collection as an enterprise licensing programme and required written permission or affirmative source authorization in situations where the project only needed responsible public-web research. That framing would obstruct the actual goal.

## Decision

1. The overriding north star is to make FPL predictions as close to reality as possible using useful free or self-hosted methods.
2. Candidate data, tooling and models are judged primarily by leakage-free out-of-time predictive gain, calibration, decision utility, reliability, reproducibility and operational cost.
3. Public web pages, feeds, search results, browser-rendered content and public read-only endpoints may be collected and evaluated without direct written permission as a general project gate.
4. SearXNG, Spider, Playwright, Byparr, deterministic parsers and AI-assisted extraction are legitimate tools. A browser challenge does not by itself prohibit use.
5. Collection remains bounded to protect the home network and evidence quality: reject private-network targets, isolate cookies/browser state, bound domains/rate/concurrency/output, and retain provenance and decision-time availability.
6. The project does not bypass login or paid access, collect credentials or private user data, automate FPL account writes, create abusive traffic or publicly republish third-party article/data corpora.
7. A concrete applicable legal prohibition stops or changes the affected method. Speculative enterprise compliance concerns do not block research.
8. Every promoted source or feature must beat or materially complement simpler baselines in preregistered rolling/walk-forward evaluation. Negative findings are retained.
9. Retrieved text and AI output remain untrusted data; deterministic rules, arithmetic, temporal ordering and feasibility remain authoritative.

## Consequences

### Positive

- Engineering effort focuses on prediction quality and scientific evidence.
- Useful free collection methods are available without unnecessary permission bureaucracy.
- Source provenance supports temporal correctness and reproducibility rather than functioning as a legal approval certificate.
- Security controls remain proportionate to a private home deployment.

### Risks

- Public sources can change or disappear; snapshots, content identities and fallback sources are needed.
- Browser-based collection is operationally brittle and must be monitored.
- More data and complex models can overfit; out-of-time ablations and final holdouts remain mandatory.
- The owner must reconsider scope before any commercial service, public dataset redistribution or account automation.

## Verification

- Repository policy no longer requires written permission for public-source research.
- Data-source records prioritise timing, identity, quality and predictive value.
- Byparr and equivalent free/self-hosted collection tools can be proposed through normal engineering issues.
- Forecast promotion remains impossible without leakage-free out-of-time evidence and reproducible baselines.
