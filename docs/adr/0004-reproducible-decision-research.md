# ADR-0004: Reproducible decision-focused research

- **Status:** Accepted
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** research

## Context

Sports forecasting is vulnerable to temporal leakage, repeated model selection, unstable rules, correlated outcomes, retrospective storytelling and implementation choices made before relevant evidence is understood. A high backtest score or a paper's state-of-the-art claim is not sufficient evidence.

## Decision

Before implementing a predictive, statistical, simulation or optimisation feature, complete a versioned evidence review that records a reproducible search, immutable citations, strong baselines, established methods, credible frontier challengers, contradictory results and FPL applicability limits. Frontier methods are candidates rather than defaults.

Every candidate model then uses registered walk-forward evaluation, immutable point-in-time snapshots, proper probabilistic scoring, calibration, decision utility, declared baselines and full run provenance. Final test periods are used once per registered claim. Optimisers are verified independently from forecasts.

Promotion requires model/dataset cards, sensitivity and failure analysis, shadow evaluation and rollback evidence.

## Consequences

Predictive implementation begins later, but wasted implementation and fashion-driven model selection are reduced. Research remains auditable and resistant to false discoveries. Some attractive papers, methods and historical data will not support local claims when assumptions, publication time, correction history, compute or transferability are inadequate.
