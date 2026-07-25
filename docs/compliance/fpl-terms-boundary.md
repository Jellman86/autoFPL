# FPL Access and Product Boundary

**Status:** proportionate engineering boundary for a private, non-commercial home research project. This is not legal advice.

## Project shape

autoFPL exists to make the best evidence-grounded FPL predictions possible. It may use publicly accessible football information, feeds, search, browser rendering, scraping, public read-only endpoints and free/self-hosted research tools. Direct written permission is not a general project requirement.

The product remains human-in-the-loop: it produces advice and the user makes changes in the FPL interface. That is the chosen product and security boundary, not an enterprise licensing gate.

## Current boundaries

- Do not collect or store FPL passwords, authentication tokens, cookies or sessions.
- Do not automate transfers, line-up/captain changes, chips or other account writes.
- Do not bypass a login, paid subscription or paywall, access private user data, or impersonate another user.
- Do not create harmful or abusive traffic; bound and cache automated retrieval.
- Do not commercially redistribute or publicly mirror third-party datasets or article corpora from this private research system.
- Stop or change method if a concrete legal prohibition applicable to the project is identified.

Publicly reachable read-only pages and endpoints may be researched and used without a special written-permission gate. Their evidence must still be point-in-time correct, reproducible and technically isolated.

## Changing the account-action boundary

A future issue may propose account integration only after the repository owner explicitly changes product scope. It would require a supported least-privilege authentication design, exact-action user approval, credential isolation, revocation, audit, abuse tests and a new ADR. It is not part of the current roadmap.

## Engineering enforcement

Security controls protect the home network, credentials, availability and scientific evidence. They must not be used to turn ordinary public-source research into a bureaucratic approval process.

Useful references:

- https://fantasy.premierleague.com/en/help/terms
- https://fantasy.premierleague.com/en/help/rules
- https://fantasy.premierleague.com/en/prizes
