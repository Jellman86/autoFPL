# autoFPL API container

## Purpose and current boundary

The container is a private development application for the Gameweek decision room, deterministic FPL rules, authoritative SQLite decision snapshots and bounded official FPL reference/outcome capture. Collection uses fixed public read-only URL templates and no credentials. The application has no autonomous FPL account actions or account-write client. Its snapshot write route is an internal development boundary and must not be exposed beyond the trusted private environment before authentication and authorisation exist.

Routes:

| Method | Path | Behavior |
|---|---|---|
| `GET` | `/` | Renders the responsive Gameweek decision room |
| `GET` | `/api/v1/advice/demo` | Returns the latest persisted Baseline v0 artifact, or the typed synthetic acceptance fixture when no qualifying capture exists |
| `GET` | `/api/v1/forecasts/player-gameweek/latest` | Returns the latest immutable provisional Baseline v0 artifact for every eligible official player |
| `GET` | `/api/v1/forecasts/preseason-challenger/latest` | Returns the latest immutable holdout-supported GW1 point-mean challenger; it cannot influence advice |
| `GET` | `/api/v1/forecasts/multi-season-shadow/latest` | Returns the latest immutable two-season GW1 shadow comparison; Baseline v0 still drives advice |
| `GET` | `/api/v1/forecasts/multi-season-shadow/readiness` | Reports whether the latest shadow matches the latest official capture, is stale or is missing |
| `GET` | `/api/v1/forecasts/joint-scenario-shadow/latest` | Returns the latest immutable joint point/appearance matrix; it cannot influence advice |
| `GET` | `/api/v1/forecasts/joint-scenario-shadow/readiness` | Reports whether the latest scenario matrix matches the latest official capture, is stale or is missing |
| `GET` | `/api/v1/data/fpl-form-forecast/latest` | Returns provenance and counts for the latest immutable public FPL Form forecast capture |
| `GET` | `/api/v1/data/fpl-form-forecast/status` | Distinguishes not checked, provider waiting, collection failure and retained forecast states |
| `GET` | `/api/v1/data/fpl-form-forecast/{captureId}/identity-coverage` | Reports deterministic cutoff-correct official player/fixture coverage for one immutable forecast capture |
| `GET` | `/api/v1/data/historical-fpl/{seasonCode}` | Returns provenance and normalized coverage counts for a registered historical archive; raw CSV and player rows remain private |
| `GET` | `/api/v1/data/historical-fpl/identity-coverage/{fromSeasonCode}/{toSeasonCode}` | Revalidates two pinned archives and audits exact stable-code overlap without a name fallback |
| `GET` | `/api/v1/evidence/claims/{seasonCode}/{gameweek}?decisionCutoffUtc=...` | Returns immutable quarantined typed claims available by the requested cutoff; claims do not influence forecasts |
| `GET` | `/api/v1/evidence/review-context/current` | Returns the bounded, content-addressed context for semantic review of claims that can change the current squad under an explicit stress |
| `GET` | `/api/v1/evidence/review/current` | Returns the immutable provider or operator review only when it matches the exact current context; reviews never influence forecasts |
| `POST` | `/mcp` | Stateless Streamable HTTP MCP endpoint; advertises anonymous read-only public prediction, player-dossier, strategy-comparison and evidence-review tools |
| `GET` | `/openapi/v1.json` | Returns the generated OpenAPI 3.1 HTTP contract |
| `GET` | `/healthz` | Liveness response: `{"status":"healthy"}` |
| `GET` | `/readyz` | Returns ready only when the current SQLite migration is present |
| `GET` | `/api/v1/data/official-fpl/latest` | Returns provenance, timing, hashes and counts for the latest private official FPL capture, or 404 before the first import |
| `GET` | `/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/pre-deadline` | Selects the newest immutable capture that was available no later than the deadline recorded in that capture |
| `GET` | `/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/players/{playerId}` | Returns cutoff-correct player identity, an official portrait URL, prior outcomes and upcoming fixtures |
| `GET` | `/api/v1/data/official-fpl/outcomes/{seasonCode}/{gameweek}/latest` | Returns the latest immutable final per-player outcome capture metadata, or 404 |
| `GET` | `/api/v1/data/official-fpl/outcomes/readiness` | Reports automatic final-outcome capture and complete pre-deadline replay-pair coverage |
| `GET` | `/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/outcome` | Pairs a cutoff-safe replay with the latest final outcome only when every replay player matches |
| `POST` | `/api/v1/decision-snapshots` | Persists validated squad/selection state and creates an immutable cutoff-correct snapshot |
| `GET` | `/api/v1/decision-snapshots/{snapshotId}` | Reads one immutable snapshot after creation or restart |
| `POST` | `/api/v1/decision-snapshot-metadata/validation` | Returns canonical metadata or a stable 400/422 problem response |
| `POST` | `/api/v1/squads/validation` | Validates a manually supplied 15-player squad and returns its exact integer-tenths budget summary |
| `POST` | `/api/v1/lineups/validation` | Validates a manually supplied starting XI, formation, captain and vice-captain against a valid squad |
| `POST` | `/api/v1/gameweek-selections/validation` | Validates the starting XI plus replacement goalkeeper and three ordered outfield substitutes against a valid squad |
| `POST` | `/api/v1/gameweek-outcomes/captaincy-resolution` | Resolves the effective captain from a valid complete selection and manually supplied player IDs with minutes |
| `POST` | `/api/v1/gameweek-outcomes/substitution-resolution` | Resolves automatic substitutions from a valid complete selection and manually supplied player IDs that played |
| `POST` | `/api/v1/gameweek-outcomes/effective-resolution` | Returns one consistent substitution and captaincy outcome from the same valid selection and manual play evidence |
| `POST` | `/api/v1/gameweek-outcomes/effective-score` | Scores the effective XI and normal captain multiplier from complete manual per-player points evidence |

Decision-snapshot metadata request fields are exact and case-sensitive. Missing or `null` required fields, duplicate or undeclared fields, non-string field values and malformed payloads fail with 400. Present string values that are unsupported fail with the stable domain error code and 422. The request body is bounded to 16 KiB by Kestrel.

The public MCP surface deliberately exposes only `get_player_dossier`,
`get_current_prediction`, `get_current_strategies` and
`get_current_evidence_review_context`. The dossier tool reads
the same cutoff-correct service as the dashboard and returns official identity,
form, fixtures and quarantined public research claims as structured content.
Its advertised annotations are read-only, non-destructive and closed-world.
It cannot read a user's draft or locked selection, trigger collection, run a
forecast or mutate any state. `get_current_prediction` returns only the latest
persisted public model squad and its exact cutoff, evidence status, uncertainty
and artifact identity. It never returns an owner-authored squad or private
configuration. `get_current_strategies` returns the exact current balanced,
safer and higher-ceiling fixed-squad shadow artifact with paired model
comparisons. Its status remains unpromoted and its bounded search is not
described as globally optimal. User-specific MCP tools remain absent until the
owner identity and OAuth 2.1 resource-server boundary are implemented.
`get_current_evidence_review_context` returns only the cited claim spans and
latest competing assertions for players in a decision-relevant stress. It
explicitly requires semantic comparison and abstention, treats source text as
untrusted, assigns no probability or source weight and cannot mutate a
forecast, squad or approval.

## Optional evidence semantic-review provider

Automated semantic review is disabled by default. When enabled, the background
poller submits each exact current decision-relevant context at most once to one
fixed OpenAI-compatible Chat Completions endpoint. It requests strict structured
output and then sends the result through the same application validator as the
operator import. Reviews remain unpromoted and cannot alter a forecast, squad,
selection revision or approval.

Configure the owner-controlled runtime secret and route through Dockhand using
double-underscore environment names. Do not commit the API key or place it in
SQLite:

```text
AutoFpl__Ai__EvidenceSemanticReview__Enabled=true
AutoFpl__Ai__EvidenceSemanticReview__Provider=openrouter
AutoFpl__Ai__EvidenceSemanticReview__Endpoint=https://openrouter.ai/api/v1/chat/completions
AutoFpl__Ai__EvidenceSemanticReview__AllowedEndpointHosts=openrouter.ai
AutoFpl__Ai__EvidenceSemanticReview__Model=<owner-fixed-model>
AutoFpl__Ai__EvidenceSemanticReview__ApiKey=<runtime-secret>
AutoFpl__Ai__EvidenceSemanticReview__RoutingPolicyVersion=owner-fixed-route-v1
AutoFpl__Ai__EvidenceSemanticReview__PollIntervalMinutes=5
AutoFpl__Ai__EvidenceSemanticReview__TimeoutSeconds=45
AutoFpl__Ai__EvidenceSemanticReview__MaximumOutputTokens=4096
AutoFpl__Ai__EvidenceSemanticReview__MaximumRequestBytes=524288
AutoFpl__Ai__EvidenceSemanticReview__MaximumResponseBytes=524288
```

Use `Provider=openai` with the exact OpenAI endpoint and allowlisted host, or
`Provider=hermes` with an explicitly allowlisted private endpoint. OpenAI uses
`max_completion_tokens`; OpenRouter and Hermes default to `max_tokens`. Override
`TokenLimitParameter` only when the fixed compatible endpoint requires the
other supported spelling. OpenAI and OpenRouter require a bearer key; a private
Hermes route may omit it. Redirects and automatic decompression are disabled,
canonical public-provider paths and the private-provider path suffix are
validated at startup, and provider bodies are never logged or persisted. A
changed context creates a new review; a retained complete,
insufficient, refused or unavailable artifact suppresses repeat spend for that
same context.

The repo-owned development plugin package is
[`plugins/autofpl`](../../plugins/autofpl/README.md). Its manifest connects
ChatGPT and Codex to the production Streamable HTTP endpoint without embedding
credentials. Validate it from the plugin-creator skill directory with:

```bash
python3 scripts/validate_plugin.py /path/to/autoFPL/plugins/autofpl
```

The package is not yet a public-directory submission. Privacy and terms URLs,
domain verification, host prompt evaluations and authenticated owner tools
remain release work.

Decision-snapshot writes require a complete valid squad and selection plus UTC observation, retrieval, availability, deadline and cutoff timestamps. Only the latest revision of each observation with `availableAtUtc <= decisionCutoffUtc` enters the materialised snapshot. Corrections must name the observation and snapshot they supersede; historical rows remain readable. Values are parameterised and decimal observations are stored canonically as text.

On an empty development database, startup creates one clearly labelled synthetic acceptance snapshot (`demo-2026`, Gameweek 1). The decision room reads its snapshot ID, revision, deadline, cutoff and selection state from SQLite while forecast values remain the explicitly synthetic UI fixture. Set `AutoFpl__SeedDemoSnapshot=false` for isolated tests or an operator-managed database.

When an operator-managed database contains a qualifying official pre-deadline
capture, the demo advice route deterministically builds a legal Baseline v0
squad, starting XI, bench and captaincy. Its transparent score uses official
price, ownership, availability, capture-reported starts and points, and target
fixture context. It enforces the £100m budget, position quotas, three-per-club
limit and legal formation, then exposes official portraits and cutoff-aware
player dossiers. This is a selection heuristic and deliberately wide,
unvalidated preseason baseline—not a promoted model or optimisation claim.
Migration 13 stores one immutable forecast document and content hash for each
exact official capture and model key. Startup backfills the latest qualifying
capture; explicit official import and the background official poller persist a
new artifact after a successful capture. The advice route and “Refresh
prediction” action only read the latest artifact; they never start collection
or mutate forecast state.

Migration 17 stores a separate all-player forecast artifact from the same exact
capture. It retains each eligible player's official identity, fixture context,
expected points, deliberately wide interval and availability-weighted expected
minutes. Its status is `provisional-unvalidated`, its distribution status is
`interval-only-uncalibrated`, and start/60-minute probabilities remain `null`
until a fitted temporal model earns promotion. The selected 15-player advice
artifact remains the downstream squad decision result.

## Provisional preseason challenger

Generate the capture-specific artifact with the analytics command documented in
`src/analytics/README.md`, copy that JSON into the container's private data
volume and run:

```text
dotnet AutoFpl.Api.dll \
  --import-preseason-player-forecast <json-file>
```

The input is strict JSON bounded to 2 MiB. Migration 20 accepts only the fixed
holdout-supported model/evaluation and pinned archive identities, the exact
current official capture and Baseline v0 artifact, and complete eligible-player
coverage. It recomputes cohort, stable-code history and point differences from
SQLite before an immutable, content-hashed insert. Repeating identical content
is idempotent; a conflict fails closed.

The command is the only write boundary. The web process exposes a read-only
latest-artifact route, and the cutoff-aware dossier shows the per-player point
mean and comparison warnings. The artifact has no calibrated distribution and
cannot alter advice, a selection revision or Baseline v0.

## Two-season preseason shadow

Generate the fixed 2026/27 GW1 artifact with the analytics command documented
in `src/analytics/README.md`, copy the JSON into the container's private data
volume and run:

```text
dotnet AutoFpl.Api.dll \
  --import-multi-season-player-forecast <json-file>
```

Migration 25 uses a separate immutable table and validates the exact two
evaluated archives, their decision-time availability, the current official
capture, Baseline v0 player values, stable-code history states and all
per-player point differences. The strict 2 MiB JSON boundary rejects unknown
fields. Identical content is idempotent and conflicting content for the same
official capture fails closed.

The latest route and player dossier expose the shadow only as comparison
evidence. Its retrospective status, weak cross-season ablation result, lack of
a calibrated distribution and `influencesAdvice: false` remain explicit.

Readiness is a separate lightweight contract:

```text
GET /api/v1/forecasts/multi-season-shadow/readiness
```

`current` requires the shadow's exact `officialCaptureId` to match the latest
official capture. `stale` exposes both identities when they differ, while
`missing` distinguishes the absence of official evidence from the absence of a
shadow artifact. The player dossier continues to require an exact capture
match and never substitutes an older shadow. This route is the machine-readable
view used to verify the companion handoff; it does not itself trigger
generation or collection.

Enable the private filesystem handoff only when the separately published
analytics worker and shared inbox volume are present:

```text
AutoFpl__Analytics__ShadowInboxPollIntervalMinutes=1
AutoFpl__Analytics__ShadowInboxPath=/analytics-inbox
AutoFpl__Analytics__SnapshotPollIntervalMinutes=1
AutoFpl__Analytics__SnapshotPath=/analytics-snapshot/autofpl.db
```

Both intervals are bounded from one through 60 minutes and absent by default.
The application uses SQLite's online-backup API to publish a consistent,
integrity-checked, delete-journal snapshot through an atomic rename only when
the relevant official or historical capture, forecast, scenario, selection or
lock identity changes.
The worker reads `/analytics-snapshot/autofpl.db` through a dedicated read-only
mount, checks exact official/shadow capture identity every minute in the
published image and atomically writes at most one
`multi-season-shadow-capture-<id>.json` file. Once that prerequisite is
current, it can write one
`joint-scenario-shadow-capture-<id>.json` file. Separate application pollers
import at most one pending file of each type per cycle through strict 2 MiB
validators, then rename each handoff `.imported` or `.rejected`. Neither side
follows a public write route: the worker cannot write SQLite and the web
application remains the only import authority. The snapshot and result inbox
are separate mounts: the worker receives the former read-only and the latter
writable.

## Joint scenario shadow

Migration 26 retains the complete scenario matrix in a separate immutable
table. The import validator requires the exact official target, retained
2025/26 archive, already imported two-season point artifact, frozen
retrospective-screen identities and one ordered current-player column set. It
checks all 38 row widths, integer point bounds, non-player zeroes, donor counts
and the producer's canonical matrix hash before inserting a content-addressed
document. Identical input is idempotent and conflicting content fails closed.

The operator equivalent of the private inbox import is:

```text
dotnet AutoFpl.Api.dll \
  --import-joint-scenario-shadow <json-file>
```

`GET /api/v1/forecasts/joint-scenario-shadow/readiness` reports `current` only
when the matrix and latest official capture IDs match. The latest route exposes
the complete rows for the future deterministic comparison service, but the
artifact remains prospectively unscored, unpromoted and unable to alter advice
or a user selection.

## Initial-squad quality shadow

Once the exact point and joint-scenario artifacts exist, the analytics worker
generates one
`initial-squad-quality-capture-<official-capture-id>.json` handoff before
advancing to owner-selection scoring. The application imports it through the
same private inbox, archives accepted files with `.imported` and rejected files
with `.rejected`, and remains the sole SQLite writer.

The operator equivalent is:

```text
dotnet AutoFpl.Api.dll \
  --import-initial-squad-quality-shadow <json-file>
```

The current exact candidate is available at
`GET /api/v1/forecasts/initial-squad-quality-shadow/latest`. A `404` means the
latest official capture, scenario and served forecast do not yet have a
matching zero-gap optimiser artifact. Older results are never substituted.
This surface is read-only, shadow-only and cannot replace advice.

To score the frozen candidate after its final official result is captured,
run the read-only evaluator against the standalone analytics snapshot:

```text
python -m autofpl_analytics.initial_squad_outcome_evaluation \
  --database /analytics-snapshot/autofpl.db \
  --output /path/to/initial-squad-outcome.json
```

Before the result it returns `waiting-for-official-outcome` and writes
nothing. The application republishes the standalone snapshot when either an
initial-squad artifact or official outcome identity changes, so the evaluator
never needs access to the live WAL database.

## Selected opening-squad prospective shadow

After the exact initial-squad-quality handoff is persisted, the analytics
worker generates
`selected-opening-squad-capture-<official-capture-id>.json`. The artifact
binds the frozen six-Gameweek expected-points policy and fixes all eight
Gameweek role decisions before outcomes. The application validates the latest
official capture, player identities, prices, availability, legal squad, legal
weekly roles, zero-gap solver and retained historical evaluation identity.

The operator equivalent is:

```text
dotnet AutoFpl.Api.dll \
  --import-selected-opening-squad-shadow <json-file>
```

The current exact artifact is available from
`GET /api/v1/forecasts/selected-opening-squad-shadow/current`. A `404` means
the latest official capture has not completed the selected-policy handoff.
Older squads are never substituted. The route is typed in OpenAPI, read-only,
unpromoted and unable to influence advice.

Insertion changes the analytics snapshot source identity. This preserves the
exact pre-outcome artifact in the standalone read-only snapshot used by the
prospective scorer.

## Public-projection opening-squad challenger

When a cutoff-eligible `solio-public-projections` snapshot exists for the exact
official capture, the worker generates
`public-projection-opening-squad-capture-<official-capture-id>.json` after the
best-supported v2 squad. The application validates source bytes and lineage,
exact player identities, both legal squads, all eight frozen weekly role sets
and the zero-gap six-Gameweek optimizer before immutable insertion.

The operator equivalent is:

```text
dotnet AutoFpl.Api.dll \
  --import-public-projection-opening-squad-shadow <json-file>
```

The current exact challenger is available from
`GET /api/v1/forecasts/public-projection-opening-squad-shadow/current`. A `404`
means no eligible source revision has completed the handoff for the latest
official capture. The route is read-only and the artifact remains unpromoted,
non-serving and unable to change advice or a user selection.

## Official published opening-squad baseline

After the best-supported v2 opening squad is persisted, the analytics worker
generates
`official-published-opening-squad-capture-<official-capture-id>.json`. The
application verifies exact official source hashes, full eligible-player
`ep_next` coverage, the persisted v2 incumbent identity, both legal squads,
eight frozen role sets and zero-gap solver lineage before immutable insertion.

The operator equivalent is:

```text
dotnet AutoFpl.Api.dll \
  --import-official-published-opening-squad-shadow <json-file>
```

The exact latest-capture baseline is available from
`GET /api/v1/forecasts/official-published-opening-squad-shadow/current`. A
`404` means the handoff is absent for the latest official capture; an older
baseline is never substituted. The artifact is read-only, non-serving and
prospectively unscored.

## Official FPL capture

Run the bounded fixed-origin import as an operator command:

```text
dotnet AutoFpl.Api.dll --import-official-fpl
```

The command retrieves only `bootstrap-static` and `fixtures` from
`https://fantasy.premierleague.com`, writes one JSON summary to stdout and
records `availableAtUtc` as the completed retrieval time. Redirects are
disabled; response type, schema, cross-references, a 4 MiB per-resource size
bound and a 20-second request timeout fail closed. Raw JSON remains private in
SQLite. Identical hash pairs reuse the earliest capture; changed content
creates a new immutable revision.

Set `AutoFpl__Research__OfficialFplPollIntervalMinutes` to an integer from `60`
through `1440` to run the same fixed-origin import automatically. Each attempt
is persisted even when an unchanged payload reuses its earliest immutable
capture, so restarts wait the remaining configured interval. Bounded provider
or transport failure leaves the web application and prior captures available.
After each successful reference refresh, the same poll cycle checks the latest
declared completed Gameweek, fills the oldest outcome gaps first and imports at
most three fixed `event/<gameweek>/live` resources. Once gaps are filled, it
rechecks the latest completed Gameweek so provider corrections create a new
immutable outcome only when content changes. Per-Gameweek failure is isolated,
and the readiness route keeps missing outcomes, missing pre-deadline replays
and incomplete identity pairs explicit. Leave the setting absent to retain
operator-only collection.

The web process does not expose an import route, accept a source URL or send
cookies/credentials. The metadata GET route does not return raw provider
content. The pre-deadline replay route orders candidates by `availableAtUtc`,
rejects every capture retrieved after its own recorded Gameweek deadline and
returns only provenance, counts and the selected immutable capture identity.
A capture remains reference evidence only until rolling evaluation admits
specific fields into a forecast.

## Historical FPL season archive

Run any fixed, commit-pinned historical import as an operator command:

```text
dotnet AutoFpl.Api.dll --import-historical-fpl-season 2022-23
dotnet AutoFpl.Api.dll --import-historical-fpl-season 2023-24
dotnet AutoFpl.Api.dll --import-historical-fpl-season 2024-25
dotnet AutoFpl.Api.dll --import-historical-fpl-season 2025-26
```

Omitting the season retains the original 2025/26 default. The command accepts
only the four registered season codes and no URL or revision. It downloads
only the registered CSV resources, disables redirects, bounds each response
to 6 MiB, verifies exact SHA-256 and normalized row-count identities, maps
season element IDs to stable official player codes and writes atomically.
Exact raw bytes stay compressed in private SQLite. The normalized schema
deliberately has no `xP` column; unavailable defensive metrics remain null.
Re-running a pinned revision is idempotent.

This archive did not exist in autoFPL at the original Gameweek deadlines.
Accordingly, it supplies historical outcomes and an early-season durability
prior; it is not evidence that archived final status or source `xP` was
decision-time available. See the
[source record](../data/sources/vaastav-fpl-historical-v1.md).

After the provider marks a Gameweek final, run:

```text
dotnet AutoFpl.Api.dll --import-official-fpl-outcome <gameweek>
```

This first refreshes the two reference resources, then retrieves only
`https://fantasy.premierleague.com/api/event/<gameweek>/live/`. It writes
nothing unless the refreshed event is finished and data-checked, at least one
target fixture exists, every target fixture is finished, the live payload is
non-empty and its player IDs exactly cover the refreshed reference capture.
The exact live bytes, SHA-256 identity and bounded points/minutes/scoring-event
fields are retained immutably, together with official expected goals/assists,
ICT/BPS and defensive-action outcomes. The cutoff-safe player dossier exposes
those underlying values for prior Gameweeks and keeps pre-migration values
explicitly null. The provider supplies no separately verifiable publication
time, so availability is the completed retrieval time. The web process exposes
no collection route.

## Public forecast capture

Run the fixed-origin FPL Form capture as an operator command:

```text
dotnet AutoFpl.Api.dll --import-fpl-form-forecast
```

The command uses Quark's existing isolated Playwright MCP browser to visit one
fixed public page and return only the active next-Gameweek prediction set. It
records the bounded check result and rejects the provider's off-season sentinel
without writing a capture. The decision room reads that status separately from
any retained immutable forecast, so provider waiting and collection failure are
not presented as equivalent. autoFPL
verifies the MCP server and final source URL, invokes only a hard-coded
versioned extraction, hashes the complete embedded provider payload and stores
bounded canonical extracted evidence. It does not deploy or control another
browser, crawler or proxy. Published points are conditional on appearing, not
expected minutes or an autoFPL-promoted forecast. Set
`AutoFpl__Research__PlaywrightMcpUrl` only when the internal MCP endpoint differs
from the container default `http://playwright-mcp:8931/mcp`. See the
[source record](../data/sources/fpl-form-public-forecast-v1.md).

Set `AutoFpl__Research__FplFormPollIntervalMinutes` to an integer from `60`
through `1440` to enable conservative in-process collection. The web process
checks immediately only when no recent check exists, then waits the configured
interval from the persisted last check. Restarts therefore do not create an
extra provider request. Waiting and bounded collection failures remain visible
through the status route and do not stop the core application. Leave the
setting absent to retain operator-only collection.

Before evaluation, request the capture-specific identity-coverage route. A
report is complete only when the forecast was available by the deadline and
every source player and fixture has one deterministic official match from a
catalogue available at that time. The route returns hashes, timing, aggregate
match methods and bounded unresolved issue metadata; it never returns the
provider's predicted values.

After a later official outcome is available, run the read-only external
evaluation:

```text
dotnet AutoFpl.Api.dll --evaluate-fpl-form-forecast [season-code]
```

The command selects the latest deadline-eligible forecast per Gameweek,
requires complete identity coverage, pairs the latest final outcome and emits a
deterministic hash-identified JSON report. It scores the published conditional
values and the separately labelled appearance-probability adjustment, including
position, zero-minute and missing-probability diagnostics. It exits `2` with an
`insufficient-data` report until at least one complete pair exists. See the
[evaluation specification](../research/fpl-form-external-evaluation-v1.md).

Official FPL's retained next-Gameweek expected-points values have a separate
read-only evaluator:

```text
dotnet AutoFpl.Api.dll \
  --evaluate-official-fpl-expected-points [season-code]
```

It selects the latest official capture available by the target deadline,
requires that Gameweek to be the capture's recorded next event, refuses partial
forecast or outcome coverage, and reports overall, position and zero-minute
MAE/RMSE/bias with deterministic content identities. It exits `2` with
`insufficient-data` until a captured forecast has a later final outcome. See
the [evaluation specification](../research/official-fpl-published-expected-points-evaluation-v1.md).

## Quarantined evidence claims

Typed output from an admitted operator-side source adapter can be imported
without exposing a web write route:

```text
dotnet AutoFpl.Api.dll --import-evidence-claim <json-file>
```

The input is strict, case-sensitive JSON bounded to 64 KiB. It records one
version `1.0` availability, start, minutes or role claim with canonical source
URL and revision, publication/retrieval/availability times, source SHA-256,
target season/Gameweek/player, directness, a bounded supporting span,
extraction method/version/confidence and optional duplicate-cluster SHA-256.
The source content itself is not imported. The claimed player must exist in an
official FPL capture for that season and Gameweek that was already available
when the claim became available.

Imports are content-idempotent and permanently quarantined. They cannot change
Baseline v0, a user selection or authoritative state. The read route requires
`decisionCutoffUtc` and returns only claims whose `availableAtUtc` is no later
than that instant. Later source admission, outcome scoring and same-fold
ablation determine whether a derived feature ever becomes a model challenger.
See the [evidence-fusion programme](../research/player-source-evidence-fusion-v1.md).

## Research source shadow capture

The fixed source inventory and latest private capture metadata are available at:

```text
GET /api/v1/research/sources
```

Capture one allowlisted source explicitly:

```text
dotnet AutoFpl.Api.dll \
  --capture-research-source premier-league-injuries
dotnet AutoFpl.Api.dll \
  --capture-research-source ffscout-predicted-lineups
dotnet AutoFpl.Api.dll \
  --capture-research-source straightred-lineup-consensus
dotnet AutoFpl.Api.dll \
  --capture-research-source \
  fbref-championship-playing-time-<2021-22|2022-23|2023-24|2024-25|2025-26>
```

Static sources use Quark's hardened Spider MCP endpoint, defaulting to
`http://spider-mcp:8080/mcp`; set
`AutoFpl__Research__SpiderMcpUrl` only when that internal endpoint differs.
The Premier League injury source reuses the existing isolated Playwright MCP
endpoint documented above. Its fixed operation requires 20 rendered club
sections and one through 200 valid player rows, accepts a missing update link
only when the official row itself displays no linked details, and validates
every supplied link as HTTPS. It then retains a bounded canonical payload and
a hash of the exact rendered widget. This deliberately fails closed on the
site's application shell or partial render. The application shares only the
existing private collector networks and deploys no additional browser.

Set `AutoFpl__Research__ResearchSourcePollIntervalMinutes` to an integer from
`60` through `1440` to refresh the complete fixed inventory in the background.
The setting is absent by default, preserving operator-only collection. An
enabled instance captures each allowlisted source once on startup and then at
the bounded interval. FFScout, strAIghtred and Premier League injury captures
immediately run their existing fail-closed deterministic extractors. A failed
source does not suppress the other sources in that cycle.

When this interval exactly matches
`AutoFpl__Research__OfficialFplPollIntervalMinutes`, a successful official
capture requests one coalesced portfolio refresh within a minute and resets
the ordinary source timer. This keeps source snapshots on the exact official
identity needed by downstream challengers without attaching an older capture.
Different or disabled cadences remain independent; the signal never overrides
those schedules. A matching-cadence request is one coalesced pending refresh,
not an additional repeating collection timer.

Each successful capture is tied to the latest official capture available at
retrieval and that capture's recorded next Gameweek/deadline. Source text is
compressed in private SQLite storage for later deterministic extraction and
replay; the API returns only source class, dependence group, timing, revision,
size and hash metadata. For the latest FFScout snapshot it also returns
snapshot-linked start-classification counts by official club. Complete,
partial and missing states describe identity coverage only; missing players
remain unknown. Repeated identical content is idempotent. Every source remains
`shadow-only` and cannot alter predictions or selections, while supported
derived claims remain `quarantined`.

The five FBref aggregate sources are manual completed-season captures through
the configured Byparr origin. Historical seasons extract bounded population
rows without current official identities; only the 2025/26 source can use the
snapshot-bound current bridge. Capture each source key separately and inspect
it through `--extract-fbref-playing-time <snapshot-id>`.

Capture a single prior-season match-log page only after its FBref identity is
part of the reviewed bridge:

```text
dotnet AutoFpl.Api.dll \
  --capture-fbref-player-match-log <official-player-code>
```

This command accepts an official stable code, not a URL. It resolves the fixed
FBref URL through the exact reviewed aggregate snapshot, uses the configured
private Byparr origin and stores only an immutable `shadow-only` raw snapshot.
It does not poll or promote the page. Extract its typed chronology explicitly:

```text
dotnet AutoFpl.Api.dll \
  --extract-fbref-player-match-log <snapshot-id>

GET /api/v1/research/snapshots/{snapshotId}/fbref-player-match-log
```

Both reads revalidate the snapshot against the reviewed bridge and return
bounded dated match rows without exposing raw HTML. They do not create model
features or alter forecasts.

Inspect and expand reviewed-player coverage in bounded batches:

```text
GET /api/v1/research/fbref-player-match-log-coverage

dotnet AutoFpl.Api.dll \
  --capture-fbref-reviewed-match-logs <limit 1-5>
```

The GET route is metadata-only and never starts collection. The operator
command skips captured players, runs missing players sequentially, retains at
most one to five new snapshots and stops after at most twice the requested
attempts. A failed player does not roll back successful captures; the JSON
result reports `complete`, `partial`, `failed` or `no-op` and exits non-zero
when any attempt failed.

Enable automatic completion of the same reviewed queue with:

```text
AutoFpl__Research__FbrefMatchLogCaptureIntervalMinutes=60
AutoFpl__Research__FbrefMatchLogCaptureBatchSize=5
```

The interval is restricted to `60` through `1440` minutes. Batch size is
optional, defaults to `5` and is restricted to `1` through `5`. The worker runs
one sequential bounded batch immediately after application startup and one per
interval thereafter. It skips snapshots already present and retries failures
on the next interval. Inspect the coverage route above for durable progress;
the worker does not write source content or capture failures to application
logs. Leave the interval unset to disable background collection; configuring a
batch size without an interval fails startup so a partial configuration cannot
appear active.

Capture each fixed promoted-club Championship schedule through the same generic
research-source operator:

```text
dotnet AutoFpl.Api.dll \
  --capture-research-source fbref-team-schedule-f7e3dfe9-2025-26
dotnet AutoFpl.Api.dll \
  --capture-research-source fbref-team-schedule-bd8769d1-2025-26
dotnet AutoFpl.Api.dll \
  --capture-research-source fbref-team-schedule-b74092de-2025-26
```

Extract and audit one retained schedule without exposing raw HTML:

```text
dotnet AutoFpl.Api.dll \
  --extract-fbref-team-schedule <snapshot-id>

GET /api/v1/research/snapshots/{snapshotId}/fbref-team-schedule
```

The parser returns only stable match IDs and bounded schedule facts. The rows
define match opportunities for a later missing-aware join; these commands do
not infer player absence or create model features.

Join one reviewed player log to its exact team schedule using explicit
immutable snapshot IDs:

```text
dotnet AutoFpl.Api.dll \
  --extract-fbref-player-match-opportunities \
  <player-match-log-snapshot-id> <team-schedule-snapshot-id>

GET /api/v1/research/fbref-player-match-opportunities\
?playerMatchLogSnapshotId=<player-match-log-snapshot-id>\
&teamScheduleSnapshotId=<team-schedule-snapshot-id>
```

The join requires the same team and season and validates stable match ID,
date, opponent and venue. A missing player row remains `no-player-row` with
null player fields. The last-3/6/8 summaries report only observed counts and
minutes plus explicit missing-row counts. This read does not persist a feature,
alter a forecast or infer why the player row is absent.

Inspect source-pair readiness across the reviewed cohort:

```text
GET /api/v1/research/fbref-player-match-opportunity-coverage
```

This metadata-only route reports the applicable team-schedule snapshot and
player-log snapshot for every reviewed identity. `source-pair-ready` is an
input-availability state, not model approval. The cohort remains
`blocked-incomplete-player-logs` until all reviewed logs exist and remains
`blocked-incomplete-team-schedules` if any registered schedule is absent.

Inspect the cutoff-bound shadow feature table:

```text
GET /api/v1/research/fbref-player-match-opportunity-features
```

The route reads the latest official target, reuses one parsed reviewed
playing-time snapshot and one parsed schedule per team, then processes player
logs sequentially. It returns all 60 reviewed identities and preserves missing
source pairs as null features. Ready rows expose raw season and last-3/6/8
counts with exact snapshot hashes and availability time. The response remains
`exploratory-not-promoted`, reports `influencesForecast: false`, and must not be
fed into served advice before the registered temporal evaluation gate passes.
An available pair that fails stable-match validation is retained as
`rejected-incompatible-source-pair`; the cohort reports
`blocked-incompatible-source-pairs` while still returning every valid and
missing row for diagnosis.

Set
`AutoFpl__Research__ByparrUrl` to Riker's private-LAN origin when the default
container-local name is not available.

Extract identity-checked claims from one supported retained snapshot explicitly:

```text
dotnet AutoFpl.Api.dll \
  --extract-research-source-claims <snapshot-id>
```

The deterministic extractors read only the private compressed snapshot. The
FFScout lineup adapter resolves predicted-XI players through the official
Premier League photo code embedded in each retained card, with a
lower-confidence unique team-scoped name fallback for stale photo identities.
A team block produces `does-not-start` complement claims for its other
registered players only when exactly eleven distinct starters resolve to one
official team; partial or unresolved lineups remain unknown. FFScout
availability v1 separately parses only `Out` and percentage-bearing `Doubts`,
maps known source team labels to the exact official team, normalizes diacritics
and requires one unique official name match. It retains a supplied doubt
percentage as the claim probability, excludes the distinct `Banned` section,
and reports unmatched or ambiguous identities without writing claims for them.
Repeating the command is idempotent. Imported claims remain `quarantined`;
extraction confidence records parser and identity certainty, not football
truth.

strAIghtred consensus v1 accepts one bounded fixture block containing one to
eleven player/percentage pairs. It retains the displayed upstream-source count
in the supporting span, requires a unique team-scoped official name and writes
the supplied percentage as a quarantined start probability. Its duplicate
cluster is the same `start × season × Gameweek × player` identity used by
FFScout, preventing dependent agreement from masquerading as an independent
vote. Premier League injury v1 requires the fixed 20-club rendered schema and
resolves each player only within the named official team. A listed injury
creates a `doubtful` availability claim without inventing a probability,
return date or unavailable status. Unmatched and ambiguous names remain
explicitly unresolved, and the adapter shares the same
`availability × season × Gameweek × player` duplicate cluster as FFScout.

After a completed, data-checked Gameweek outcome has been imported, score
pre-deadline start claims without writing the database:

```text
dotnet AutoFpl.Api.dll --evaluate-evidence-claims [season-code]
```

The evaluator joins claim and outcome players through stable official codes and
uses the official per-player Gameweek `starts` count as the start-event truth.
For each source and player it scores only the latest assertion available before
the deadline, so repeated source revisions cannot inflate the sample. It
reports categorical accuracy, a confusion matrix and Gameweek count in fixed
`0-6h`, `6-24h`, `24-72h` and `72h+` lead-time buckets. Sensitivity and
specificity use separate Jeffreys beta-binomial posterior means, shrinking
sparse perfect or failed records towards uncertainty; balanced reliability is
reported only after both outcome classes have been observed. Where the source
supplied an explicit probability, the same slice also reports Brier score and
natural-log loss. The latest corrected official outcome is used; log loss
applies a fixed `1e-15` numerical clamp to exact zero/one probabilities, and
incomplete identity excludes the whole Gameweek fold.
Availability claims are deliberately not scored against minutes or appearance
because those are not equivalent to availability. The command exits `2` with
`insufficient-data` until one scorable pair exists, and no result promotes a
claim into a forecast.

See the [source portfolio](../research/research-source-portfolio-v1.md).

## SQLite operations

The application uses one file from `AutoFpl__DatabasePath`. The container default is `/data/autofpl.db`; local execution defaults under the application output directory. Startup applies 40 explicit forward migrations, enables foreign keys and WAL, and uses a five-second busy timeout.

The root filesystem stays read-only. Production must mount a private, UID
`1654`-writable persistent directory at `/data`; the CI smoke test uses an
ephemeral `/data` tmpfs. The deployed Git-backed stack provides that private
mount. Schema-changing promotions still require a verified backup and
compatible rollback plan.

Run the built-in integrity and online-backup commands with the same database configuration:

```text
dotnet AutoFpl.Api.dll --database-integrity-check
dotnet AutoFpl.Api.dll --database-backup /data/backups/autofpl-YYYYMMDD.db
```

Integrity prints `ok` and exits zero only when both SQLite integrity and foreign-key checks pass. Backup refuses to overwrite an existing file and uses SQLite's online-backup API so the result is consistent with WAL activity.

Squad requests reject undeclared fields and malformed JSON. They accept no external data or account credentials. Positions are `goalkeeper`, `defender`, `midfielder` and `forward`; money is represented as integer tenths rather than floating point.

Lineup requests use the same manual squad boundary, require 11 unique squad members, exactly one goalkeeper, at least three defenders and at least one forward, and require distinct captain and vice-captain IDs from the starting XI. Structural failures return 400; rule-invalid lineups return a stable 422 problem response.

Gameweek-selection requests preserve the starting-XI and captaincy rules, then require the non-starting squad goalkeeper as the replacement goalkeeper and exactly three distinct non-goalkeeper substitutes in priority order. The starting XI and bench must partition the 15-player squad exactly once. Structural failures return 400; rule-invalid selections return a stable 422 problem response. This endpoint validates the submitted bench order only; it does not ingest player appearances, execute automatic substitutions or calculate points.

Captaincy-resolution requests compose the same valid squad and complete gameweek selection with a distinct list of squad-player IDs that played at least one minute. Captaincy remains with the selected captain when present, transfers to the vice-captain when only the vice-captain played minutes, and resolves to `null` when neither played. Structural failures return 400; duplicate or non-squad minute evidence returns a stable 422 problem response. Minute evidence is manually supplied: the endpoint does not retrieve appearances, points or account data.

Substitution-resolution requests compose the same valid squad and complete selection with distinct, manually supplied squad-player IDs representing players who played in the Gameweek. A non-playing starting goalkeeper is replaced only by the replacement goalkeeper when that goalkeeper played. Played outfield substitutes are considered in bench-priority order and activated only when the existing formation rules remain valid; unresolved non-playing starters are reported explicitly. Structural failures return 400, and duplicate or non-squad play evidence returns a stable 422 problem response. The endpoint does not retrieve appearances or cards, calculate scores, persist outcomes or perform account actions.

Effective-resolution requests pass one immutable manual play-evidence snapshot through the existing substitution and captaincy resolvers, then return both results together. This prevents clients from applying different evidence to the two deterministic rules. It retains the same strict 400/422 boundary and does not add scoring, chips, external retrieval, persistence, forecasting, simulation or account actions.

Effective-score requests add exactly one integer points entry for every squad player to that composed outcome. Non-playing players must have zero points; unused bench points are excluded; the effective captain receives the normal `2` multiplier; and all totals use wide integers to avoid overflow across the accepted point values. Missing, duplicate, non-squad or contradictory point evidence returns a stable 422 response. The route consumes pre-calculated manual points and does not derive official scoring events, support chips, retrieve data, forecast, simulate, optimise, persist or act on an account.

## Image construction

`Dockerfile` uses digest-pinned Microsoft .NET images:

- SDK: `10.0.302-noble`
- runtime: `10.0.10-noble-chiseled-extra`

The final image:

- runs as the base image's non-root UID `1654`;
- contains no shell or package manager;
- has an application-native Docker health check;
- carries OCI source, revision and AGPL licence labels;
- contains no test packages or JSON Schema validator;
- supports a read-only root filesystem with small `/tmp` and test-only `/data` tmpfs mounts;
- is scanned for HIGH and CRITICAL OS and .NET vulnerabilities before publication.

Build and smoke-test locally from the repository root:

```bash
docker build \
  --build-arg "SOURCE_REVISION=$(git rev-parse HEAD)" \
  --tag autofpl:local .

bash scripts/ci_container_smoke.sh autofpl:local "$(git rev-parse HEAD)"
```

The smoke test launches the image with a read-only filesystem, all Linux capabilities dropped, `no-new-privileges`, and no fixed host port.

`Dockerfile.analytics` builds hash-locked scientific wheels with a
digest-pinned Python 3.13 slim Trixie stage, then copies only the runtime
packages and generator into a digest-pinned distroless Python Debian 13 final
image. The companion runs as UID `1654`, contains no shell or package manager,
has a read-only root filesystem, exposes no port, reads SQLite only through a
read-only mount and writes only capture-named artifacts to a private inbox. Its
smoke test imports the exact NumPy and scikit-learn versions and starts the real
command surface:

```bash
make analytics-container-verify
```

Keeping the scientific runtime separate prevents an analytics dependency or
long-running fit from expanding or taking down the serving image.

The analytics Trivy gate scans both OS and Python packages and fails on
HIGH/CRITICAL findings for which an actionable fixed version exists. Newly
disclosed vendor records with no available fix remain covered by every fresh
Trivy database evaluation but do not make the package permanently
unpublishable; the final distroless image, read-only filesystem, non-root user,
dropped capabilities and absence of network listeners bound their runtime
exposure.

## CI and publication

`.github/workflows/container.yml` runs on pull requests to `dev`/`main` and pushes to `dev`.

1. Build a local candidate from locked dependencies and digest-pinned bases.
2. Save the image to an archive.
3. Scan the archive with digest-pinned Trivy without exposing the Docker socket to the scanner.
4. Run the hardened container smoke test.
5. On a protected `dev` push only, grant `packages: write`, repeat the gates, authenticate to GHCR, and publish:
   - `ghcr.io/jellman86/autofpl:dev`
   - `ghcr.io/jellman86/autofpl:sha-<full-commit-sha>`
6. Tag and push the exact scanned candidate, immutable SHA tag first and mutable `dev` tag second, then verify the SHA tag is readable.

Pull requests never receive registry write permission and never publish images.

The independent `Analytics container` workflow applies the same build, Trivy,
non-root smoke, SHA-tag and `dev`-tag flow to:

- `ghcr.io/jellman86/autofpl-analytics:dev`
- `ghcr.io/jellman86/autofpl-analytics:sha-<full-commit-sha>`

It publishes a bounded generator only. Exact capture freshness checks and the
strict `.NET` inbox import are implemented but disabled until the Dockhand
compose definition mounts the shared private volume and explicitly enables
both pollers.

## Private Dockhand deployment

The Git-backed Dockhand definition lives in `autofpl/` in the separate `docker-configs` repository and deploys to Quark/Fedora.

- It pins `ghcr.io/jellman86/autofpl@sha256:<published-manifest-digest>`; `:dev` is never deployed directly.
- It joins only the external trusted `general_brg` network, where internal consumers can use `http://autofpl-api:8080`.
- Do not publish a host port in the steady-state stack.
- Use `read_only: true`, `cap_drop: [ALL]`, `security_opt: [no-new-privileges:true]`, and a bounded `/tmp` tmpfs.
- Mount a private persistent host directory at `/data`, writable only by UID/GID `1654`; keep database and backups off public shares.
- The home-lab DNS record and Nginx Proxy Manager host expose `autofpl.pownet.uk` through the trusted deployment environment. Do not broaden that route or treat it as a multi-user authenticated product until identity and authorisation are implemented.
- Store future secrets only in Dockhand; SQLite configuration contains no credential.

All stack lifecycle changes must follow the `docker-configs` repository's Git-backed Dockhand procedure. Do not run direct `docker compose pull` or `up` commands on the host.

## Rollback

When rolling back to a prior verified digest:

1. Select the previously verified image digest from Git/GHCR history.
2. Revert the `docker-configs` digest change and push it through review.
3. Redeploy the Git-backed stack through Dockhand.
4. Verify container health and representative 200/400/422 behavior.

Before deploying a schema migration, create an online backup and verify the
live database with `--database-integrity-check`. Older code rejects a newer
database, so rollback across a migration requires restoring the matching
pre-migration backup before redeploying the older image. Apply that restore
through a separately reviewed operational change. Never copy the live WAL
database file directly while the application is running.
