# FPL Form public forecast v1

## Status and purpose

- **Source ID:** `fpl-form-public-forecast/v1`
- **Status:** Admitted
- **Purpose:** private external predicted-points baseline
- **Provider:** FPL Form
- **Authentication:** none
- **Account access:** none

FPL Form publishes player predicted points and an optional probability of
appearing for future fixtures. Its export page explicitly permits personal use
in spreadsheets and other analysis tools while prohibiting redistribution.
autoFPL retains private captures, exposes only provenance/count summaries and
credits the source in any derived analysis.

Provider references:

- [prediction method and field semantics](https://fplform.com/help)
- [personal-use export and attribution terms](https://fplform.com/export-fpl-form-data)

## Existing research-stack collection boundary

The operator importer asks Quark's existing isolated Playwright MCP service to
fetch one fixed public page through its request context:

- `https://fplform.com/fpl-predicted-points`

autoFPL does not own a browser, proxy, crawler or general-purpose scraping
endpoint. The deployed Playwright MCP already runs an isolated browser behind
the hardened Quark research-egress policy. The caller cannot provide a URL,
script, proxy, header, cookie or credential. autoFPL verifies the MCP protocol
and Playwright server identity, opens one isolated session, invokes only
one versioned hard-coded `browser_run_code_unsafe` call, then closes the page
and deletes the session. The code does not render or execute the provider page:
full browser navigation exhausts the Node process on the unusually large
document. It reads the server-rendered response through Playwright's existing
proxy-aware request context instead.

The page currently embeds roughly 105 MB of multi-season data. The extraction
function first reads the small `data-nw` sentinel, so the off-season path never
parses the embedded player history. For an active Gameweek it scans one encoded
top-level player object at a time, decodes and parses only that bounded object,
and returns only active-Gameweek rows plus a chunked SHA-256 digest of the
complete encoded `data-players` attribute. It never constructs the complete
decoded player object graph. The bounded MCP response is at most 6 MiB and the
retained canonical evidence is at most 4 MiB. Spider MCP remains the appropriate
transport for bounded news/article text, but its intentional 128 KiB result cap
means it is not the right transport for this structured payload.

Run one capture with:

```text
dotnet AutoFpl.Api.dll --import-fpl-form-forecast
```

An instance may instead set
`AutoFpl__Research__FplFormPollIntervalMinutes=360` (or another bounded value
from 60 through 1440 minutes). The single application process reuses the same
fixed importer and persisted last-check time, so a restart waits out the
remaining interval rather than generating an extra request. There is no
caller-controlled schedule, URL or browser code.

The importer accepts only an active `data-nw` Gameweek from 1 through 38 and
normalises fixture predictions from the latest season in the page's embedded
`data-players` JSON. An off-season sentinel, unexpected final URL, wrong MCP
server, malformed extraction, missing prediction set, unsupported position,
duplicate player/fixture identity, invalid range or oversized response fails
without writing a partial capture.

The read-only API exposes capture provenance and counts at
`GET /api/v1/data/fpl-form-forecast/latest` and the latest bounded collection
check at `GET /api/v1/data/fpl-form-forecast/status`. The status distinguishes
an unchecked source, provider off-season/waiting, collection failure and a
successful capture; a failed check does not remove an earlier immutable
capture. It also exposes a derived,
non-provider-mirroring identity report at
`GET /api/v1/data/fpl-form-forecast/{captureId}/identity-coverage`. Neither
route triggers collection or exposes provider HTML, predicted values or raw
prediction rows.

## Timing, identity and correction semantics

The page does not provide a separately verifiable publication timestamp.
`publishedAtUtc` therefore remains `null`; `retrievedAtUtc` and
`availableAtUtc` equal the completed retrieval time. A forecast is eligible for
a Gameweek comparison only when that availability time is no later than the
official Gameweek deadline.

SHA-256 over the canonical retained active-Gameweek evidence is the immutable
capture identity. A second SHA-256 identifies the provider's complete encoded
embedded player attribute. The capture records transport
`playwright-mcp/v1` and extraction version `fpl-form-stream-extract/v2`; legacy
direct-HTTP and v1 MCP test/early-development rows remain distinguishable.
Identical active evidence
returns the earliest stored capture. Changed evidence creates a new immutable
capture. Historical predictions visible in a page retrieved after their
deadlines are not backdated or treated as point-in-time evidence.

FPL Form player and fixture identifiers are source identities. They are not
silently assumed to equal official FPL identifiers. The implemented coverage
join chooses only the newest official catalogue available no later than the
forecast retrieval and deadline. When a source ID exists, its normalized
player/fixture attributes must agree; it is never repaired by a name fallback.
When the ID is absent from the official catalogue, only a unique normalized
name/team/position or player-team/kickoff match is accepted. Ambiguity,
inconsistent IDs, invalid or ambiguous London-local kickoff times, missing
catalogues and post-deadline forecasts fail closed and are reported with
bounded issue records.

## Persisted fields

The private SQLite database retains:

- canonical compact extracted evidence compressed with Brotli, its hash, the
  full embedded-payload hash, fixed source URL, MCP transport and extraction
  version;
- season, active Gameweek, retrieval and availability times;
- source player and fixture identifiers;
- player name, team, position and provider-local kickoff text;
- fixture-level conditional predicted points; and
- optional fixture-level probability of appearing.

The local kickoff string is provenance, not an authoritative UTC instant. The
coverage report parses its exact documented shape in `Europe/London`, rejects
DST gaps and overlaps, and requires the resulting UTC instant to equal the
official fixture kickoff. Fixture and deadline authority remains the selected
official FPL capture.

## Scientific interpretation

FPL Form documents predicted points as conditional on the player appearing.
They are not unconditional expected points and are not expected minutes.
Probability of appearing is a separate optional field.

The implemented first evaluator reports the published conditional prediction
as its own external baseline and labels its mismatch with zero-minute outcomes.
Any probability-adjusted value is a separately named autoFPL-derived challenger,
not presented as the provider's published number. It also reports position and
missing-probability slices with deterministic source/outcome identities. The
source cannot become a model feature until real leakage-free comparisons,
missingness/failure slices and an ablation against the incumbent feature set
earn promotion.

No current capture can establish predictive quality before the 2026/27 source
publishes an active Gameweek and a later official outcome exists.

## Retention and responsible use

Captures remain private and are not committed or served as a data mirror.
Collection is operator-triggered and at most one fixed page per run. Do not
collect login/session data, load a manager's squad through the provider, or use
the site's credential-taking workflow. Do not add a second autoFPL browser,
crawler or egress proxy; use the existing Quark Playwright/Spider/SearXNG
research stack according to source type. Stop collection if the provider
rejects the client, removes public access, changes the personal-use boundary or
exposes a smaller supported API/export that should replace browser extraction.
