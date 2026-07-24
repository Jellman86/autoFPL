# FPL Terms and Product Boundary

**Status:** mandatory safety boundary based on the live 2026/27 FPL terms reviewed on 24 July 2026. This is engineering guidance, not legal advice; terms must be re-reviewed before each season and material product change.

## Current allowed product shape

The repository may implement a human-in-the-loop decision-support system using sources the project is authorised to use. It may accept manually entered or permissioned imported squad state, calculate forecasts/optimisation and display recommendations for the user to enact manually.

## Prohibited without written Premier League permission

- automated access to or extraction from the FPL game;
- reliance on undocumented FPL JSON endpoints as a production data feed;
- collection or storage of Premier League/FPL passwords, cookies or sessions;
- browser automation or request replay against a user's FPL account;
- automatic transfers, line-up/captain changes or chip activation;
- commercial reproduction/distribution of FPL game information without rights;
- implying that technical access, a community library or robots policy is official API permission.

## Required gate to change this boundary

All of the following are required:

1. written permission or contract from the relevant rights holder;
2. supported data and authentication/write interfaces;
3. legal review of data, competition and commercial terms;
4. threat model, privacy assessment and credential lifecycle design;
5. exact-action human approval and auditable revocation unless the permission explicitly supports autonomous action;
6. ADR approved by the repository owner;
7. isolated integration and abuse tests.

## Enforcement

Any issue or PR proposing a prohibited capability must be closed or kept research-only until the gate is met. No “experimental”, “personal use” or feature flag bypass is permitted in shared code.

Official references:

- https://fantasy.premierleague.com/en/help/terms
- https://fantasy.premierleague.com/en/help/rules
- https://fantasy.premierleague.com/en/prizes
