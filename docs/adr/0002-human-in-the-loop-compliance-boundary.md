# ADR-0002: Human-in-the-loop compliance boundary

- **Status:** Accepted
- **Date:** 2026-07-24
- **Owners:** Jellman86
- **Decision class:** compliance

## Context

FPL is technically accessible through webpages and undocumented endpoints, but the 2026/27 terms restrict automated access/extraction, account sharing and commercial use of game information. Technical feasibility is not authorisation.

## Decision

The product is advisory only. It may research authorised sources, accept manual/permissioned state, calculate recommendations and explain them. It does not scrape FPL, hold FPL credentials/sessions or submit actions.

Changing this requires the complete permission and security gate in `docs/compliance/fpl-terms-boundary.md`.

## Consequences

Early work can prove forecast and decision quality without exposing accounts or violating terms. Some ownership, price and rival features may be unavailable until licensed. Recommendations remain manual.
