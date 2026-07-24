# ADR-0004: Reproducible decision-focused research

- **Status:** Accepted
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** research

## Context

Sports forecasting is vulnerable to temporal leakage, repeated model selection, unstable rules, correlated outcomes and retrospective storytelling. A high backtest score is not sufficient evidence.

## Decision

Every candidate model uses registered walk-forward evaluation, immutable point-in-time snapshots, proper probabilistic scoring, calibration, decision utility, declared baselines and full run provenance. Final test periods are used once per registered claim. Optimisers are verified independently from forecasts.

Promotion requires model/dataset cards, sensitivity and failure analysis, shadow evaluation and rollback evidence.

## Consequences

Research is slower but auditable and resistant to false discoveries. Some attractive historical data cannot support claims when its publication time or correction history is unknown.
