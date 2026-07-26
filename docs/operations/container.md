# autoFPL API container

## Purpose and current boundary

The container is a private development application for the Gameweek decision room, deterministic FPL rules, authoritative SQLite decision snapshots and operator-triggered official FPL reference/outcome capture. Collection uses fixed public read-only URL templates and no credentials. The application has no autonomous actions or account-write client. Its snapshot write route is an internal development boundary and must not be exposed beyond the trusted private environment before authentication and authorisation exist.

Routes:

| Method | Path | Behavior |
|---|---|---|
| `GET` | `/` | Renders the responsive Gameweek decision room |
| `GET` | `/api/v1/advice/demo` | Returns the latest persisted Baseline v0 artifact, or the typed synthetic acceptance fixture when no qualifying capture exists |
| `GET` | `/api/v1/data/fpl-form-forecast/latest` | Returns provenance and counts for the latest immutable public FPL Form forecast capture |
| `GET` | `/api/v1/data/fpl-form-forecast/status` | Distinguishes not checked, provider waiting, collection failure and retained forecast states |
| `GET` | `/api/v1/data/fpl-form-forecast/{captureId}/identity-coverage` | Reports deterministic cutoff-correct official player/fixture coverage for one immutable forecast capture |
| `GET` | `/openapi/v1.json` | Returns the generated OpenAPI 3.1 HTTP contract |
| `GET` | `/healthz` | Liveness response: `{"status":"healthy"}` |
| `GET` | `/readyz` | Returns ready only when the current SQLite migration is present |
| `GET` | `/api/v1/data/official-fpl/latest` | Returns provenance, timing, hashes and counts for the latest private official FPL capture, or 404 before the first import |
| `GET` | `/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/pre-deadline` | Selects the newest immutable capture that was available no later than the deadline recorded in that capture |
| `GET` | `/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/players/{playerId}` | Returns cutoff-correct player identity, an official portrait URL, prior outcomes and upcoming fixtures |
| `GET` | `/api/v1/data/official-fpl/outcomes/{seasonCode}/{gameweek}/latest` | Returns the latest immutable final per-player outcome capture metadata, or 404 |
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
Leave the setting absent to retain operator-only collection.

The web process does not expose an import route, accept a source URL or send
cookies/credentials. The metadata GET route does not return raw provider
content. The pre-deadline replay route orders candidates by `availableAtUtc`,
rejects every capture retrieved after its own recorded Gameweek deadline and
returns only provenance, counts and the selected immutable capture identity.
A capture remains reference evidence only until rolling evaluation admits
specific fields into a forecast.

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

## SQLite operations

The application uses one file from `AutoFpl__DatabasePath`. The container default is `/data/autofpl.db`; local execution defaults under the application output directory. Startup applies ten explicit forward migrations, enables foreign keys and WAL, and uses a five-second busy timeout.

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
