# Documentation standard

Documentation is part of autoFPL's safety, research-integrity and human-approval model. It must let a contributor, operator or reviewer distinguish shipped behaviour from plans and understand the evidence and limits behind any recommendation.

## Audience

Write for one of these audiences and make the intended reader clear from the page:

- contributors implementing or reviewing the platform;
- operators running the self-hosted services;
- researchers evaluating data, forecasts, simulations and optimisation results;
- people reviewing an autoFPL recommendation before deciding whether to act.

Assume basic familiarity with Git, containers and Fantasy Premier League (FPL), but do not assume knowledge of autoFPL's architecture or vocabulary.

## Source of truth

Ground every claim in the current repository:

- Code and tests are the source of truth for implemented behaviour.
- Versioned schemas and generated API artefacts are the source of truth for wire contracts.
- Deployment definitions are the source of truth for runnable examples.
- Governance, accepted architecture decision records (ADRs), compliance records and security standards are the source of truth for project boundaries.
- Versioned datasets, experiment records, model cards and evaluation outputs are the source of truth for research claims.
- Existing prose is context, not proof. Correct it when authoritative artefacts change.

Do not invent capabilities, data rights, guarantees, support promises, performance claims, release dates or research results. Mark an unverified assumption explicitly or remove it. Never describe roadmap work as available.

## Information architecture

Use this structure:

| Kind | Purpose | Location or example |
|---|---|---|
| README | Project orientation, current status and links onward. | [`../../README.md`](../../README.md) |
| Index | Route readers to the right maintained page. | [`../index.md`](../index.md) |
| Tutorial | A first successful workflow with expected results. | Add under `docs/tutorials/` when an end-user workflow exists. |
| How-to/runbook | One operational task, including verification and recovery. | Add under `docs/operations/` with the runnable service it supports. |
| Reference | Complete lookup information for contracts, configuration or APIs. | [`../../contracts/README.md`](../../contracts/README.md) |
| Explanation | Architecture, trade-offs, compliance boundaries and research method. | [`../architecture/README.md`](../architecture/README.md), [`../research/evidence-base.md`](../research/evidence-base.md) |
| Standard | Enforceable engineering, research, security, data and documentation rules. | `docs/standards/` |
| ADR | One durable architecture or integration decision and its consequences. | `docs/adr/` |
| Roadmap | Prioritised future outcomes and their evidence gates. | [`../roadmap.md`](../roadmap.md) |
| Changelog | User- and operator-relevant changes that are implemented. | [`../../CHANGELOG.md`](../../CHANGELOG.md) |

Keep detail out of the README when a focused page can carry it. The README should orient, the index should route, and each maintained page should solve one reader need.

## Page patterns

For task pages, prefer this order:

1. Outcome.
2. Prerequisites, permissions and safety boundary.
3. Smallest working path.
4. Expected result and verification.
5. Optional variants.
6. Failure handling, rollback or the next link.

Use short numbered steps for procedures. Include exact commands and a concise **You should see** or **If it fails** section when success is not obvious.

For reference pages, use stable headings, consistent tables, exact accepted values, and request/response examples. Link to the concept or decision record rather than repeating its rationale.

For research pages, state the decision deadline, data availability boundary, hypothesis, baseline, evaluation design, uncertainty, limitations and reproducibility artefacts. Follow the [research standard](research.md).

## Safety and evidence requirements

Pages that discuss data collection, credentials, external services, recommendations, automation, deployment or model results must state the applicable boundary:

- autoFPL provides decision support; a human approves every external account action;
- no scraping, credential collection, session replay or automated FPL writes are permitted without documented Premier League permission and supported access;
- data must be legally usable and point-in-time correct for the decision being evaluated;
- forecasts and simulations are uncertain evidence, not facts or guarantees;
- deterministic rules, scoring, money and feasibility checks remain authoritative over model output;
- AI-generated extraction, synthesis or explanation is non-authoritative until validated;
- secrets and private user data belong in approved secret/runtime stores, never examples or committed documentation;
- a private development route is not a public security boundary;
- destructive or connectivity-affecting operations require explicit approval, recovery steps and verified rollback where applicable.

Label material claims as observed fact, sourced evidence, assumption or judgement when the distinction is not otherwise obvious. Link sourced claims to the evidence record. Never imply that a recommendation was executed merely because it was generated or approved.

## Roadmap, changelog and issue boundaries

Keep these records distinct:

- [`../roadmap.md`](../roadmap.md) answers what outcomes are next and what evidence gates them. It contains no promises or invented dates.
- [`../../CHANGELOG.md`](../../CHANGELOG.md) records implemented, user- or operator-relevant changes. Add entries under **Unreleased** in the same change that delivers the behaviour.
- GitHub issues hold actionable scope, acceptance criteria and current discussion.
- ADRs record durable decisions, not task progress.
- Reproducible defects remain GitHub issues until a dedicated known-issues page is justified. Fixed defects move to the changelog.

Use [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) categories where applicable: **Added**, **Changed**, **Deprecated**, **Removed**, **Fixed** and **Security**. Changelog entries describe outcomes and important boundaries, not a dump of commits.

## Style

- Use plain English, active voice and short paragraphs.
- Use `you` for instructions; use **autoFPL** for product behaviour.
- Avoid vague passive phrasing, marketing language, generic filler and unexplained acronyms.
- Use sentence-case headings.
- Use bold for exact UI labels when a UI exists.
- Use monospace for paths, commands, endpoints, environment variables, schema fields and code identifiers.
- Prefer specific measured language over words such as “fast”, “safe”, “accurate” or “optimal” without evidence.
- Use consistent terms from governing standards and contracts. Define a new domain term on first use.
- Use ISO dates (`YYYY-MM-DD`) and UTC when time is material.
- Wrap prose for readable diffs where practical; do not sacrifice valid tables or command examples.

## Commands and examples

Commands must be copyable and match the repository:

- use current repository paths, service names and image references;
- use `docker compose`, not the legacy `docker-compose` command;
- use locked/frozen dependency installation where the project provides it;
- use placeholders that cannot be mistaken for real credentials or private hostnames;
- redact tokens, cookies, personal squad data and private infrastructure details;
- include expected output or a verification command when it materially helps;
- test a command against the documented version before publishing it.

Never copy local development secrets or generated evidence into documentation merely to make an example look realistic.

## API and contract documentation

Keep human API guidance separate from workflow documentation. It must state:

- version and stability;
- authentication, authorisation and network-exposure boundary;
- request and response fields with exact casing and types;
- validation and common status codes;
- idempotency and side effects for writes;
- approval and audit requirements;
- examples grounded in a committed schema or generated API contract.

Do not document an endpoint or field unless it exists in code and its versioned contract. Once generated OpenAPI is introduced, CI must check the committed API reference for drift.

## Accessibility and media

Use descriptive link text instead of “click here”. Images and diagrams need meaningful alt text that conveys their purpose. Do not rely on colour alone to communicate status.

Screenshots and examples must use fabricated or explicitly redistributable data. Never expose real credentials, private hostnames, personal squad history or third-party copyrighted assets without permission.

## Validation checklist

Before finishing a documentation change:

1. Read every changed page from top to bottom.
2. Check behavioural claims against code and tests.
3. Check contract examples against schemas or generated API artefacts.
4. Check commands and deployment claims against runnable definitions.
5. Check research and data claims against versioned evidence and rights records.
6. Check safety and approval boundaries against governance, compliance and threat-model documents.
7. Run `python3 tools/governance/check_documentation.py`.
8. Run `python3 -m unittest discover -s tests -p 'test_*.py' -v`.
9. Run `git diff --check`.
10. Confirm [`../index.md`](../index.md) links to each new maintained reader-facing page.

If the change implements user- or operator-relevant behaviour, update [`../../CHANGELOG.md`](../../CHANGELOG.md). Pure documentation corrections should not create a product changelog entry unless they correct materially unsafe guidance.
