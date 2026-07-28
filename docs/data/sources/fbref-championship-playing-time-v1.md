# FBref Championship playing time v1

- **Source ID:** `fbref-championship-playing-time/v1`
- **Status:** Admitted for private shadow capture
- **Evidence season:** 2025/26 Championship
- **Runtime source key:** `fbref-championship-playing-time-2025-26`

## Purpose and fields

The source is a bounded prior-competition coverage candidate for current
players whose 2025/26 history is absent from the official FPL archive. The
first capture retains FBref's Championship aggregate playing-time page. The
deterministic v1 extractor emits source player/team IDs, squad, appearances,
starts, minutes and each player's fixed 2025/26 summary match-log URL. It never
returns the retained page HTML.

The aggregate page does not itself supply match-order temporal form. Reviewed
match-log capture and deterministic chronology extraction now provide that
separate evidence boundary. Three fixed team schedules provide the complete
Championship match opportunities needed to distinguish an omitted player row
from a match that never occurred. No field influences a forecast until
identical-fold evaluation shows predictive gain.

The reviewed-player capture boundary is now implemented. The operator supplies
one official stable player code to
`--capture-fbref-player-match-log <official-player-code>`. autoFPL accepts the
code only when it resolves uniquely through bridge v1, reads that player's
fixed match-log URL from the reviewed aggregate extraction and sends only that
URL to Byparr. No HTTP caller or operator can supply a URL.

## Collection and provenance

autoFPL sends only the registered canonical URL to Byparr 2.1.0 at Riker's
private LAN origin. Byparr reaches the public page through Riker's existing
Gluetun proxy. The client:

- accepts no URL from an HTTP/API caller and sends no `X-Proxy-*` override;
- permits only the registered season URL and the observed FBref canonical
  redirect;
- requires a Championship playing-time content marker and HTTP 200;
- bounds the transport response at 8 MiB and retained HTML at 6 MiB; and
- records canonical/final URLs, retrieval/availability time, current official
  identity-capture context, content hash, byte count and `byparr/<version>`.

The raw HTML is Brotli-compressed in private SQLite and is not returned by the
inventory API. Collection is manual because this completed-season page does
not need the live-source six-hour poll cadence.

Reviewed match-log captures use the same size, timing, immutable-storage and
transport-provenance boundary. Each source key contains exactly the reviewed
eight-character FBref player ID and fixed `2025-26` season. The client permits
only the extractor-provided `/summary/` path and FBref's observed redirect
without that segment, and requires both the match-log title and
`matchlogs_all` table marker. A changed bridge snapshot, unresolved official
code, arbitrary path or non-FBref origin fails closed.

The three registered team-schedule sources use fixed 2025/26 Championship
schedule URLs for Coventry City, Hull City and Ipswich Town. They accept no
caller URL, require the team-specific page title and `matchlogs_for` table,
use Byparr through the same private origin and remain manual completed-season
captures.

## Timing, identity and limitations

`available_at_utc` is the actual retrieval time; it is not backdated to the
matches described by the page. Match observations parsed later must retain
their own kickoff times and precede every forecast target.

FBref identity is not an official FPL identity. Player names are review
evidence only. Bridge v1 freezes the 60 exact normalized full-name/current-club
matches as explicit source player/team ID to official stable-code mappings.
Every use revalidates the source name/team, official code/team and reviewed
snapshot content hash. A different content hash disables the bridge and leaves
new exact matches as proposals until review. No fuzzy matching is performed.

Snapshot 27 is the first retained production measurement. Extraction v1 found
944 player-team rows and 894 stable FBref player IDs. The conservative audit
reviewed 60 exact current-team matches among 85 current promoted-club players:
Coventry City 22/28, Hull City 18/29 and Ipswich Town 20/28. The other 25
official identities remain an explicit review/collection queue.

The page may be corrected after publication and its aggregates cannot establish
recent match sequence, health or current-club role. Current official
availability remains authoritative. Raw third-party content stays private and
is not republished as a dataset.

## Read boundary

The operator command
`--extract-fbref-playing-time <snapshot-id>` and read-only
`/api/v1/research/snapshots/{snapshotId}/fbref-playing-time` route run the same
versioned parser. Both fail closed on an unsupported source, post-deadline
capture, malformed or duplicate IDs, invalid counts, missing target clubs or an
unexpected population size. The response distinguishes `reviewed-v1`,
`exact-current-team-proposal`, `unresolved` and `not-in-scope` identity states
and publishes the applicable bridge version without exposing raw HTML.

The reviewed match-log operator command captures raw chronology pages. Parser
v1 revalidates the snapshot source against bridge v1 and emits bounded dated
rows containing competition, round, venue, result, stable team/opponent/match
IDs, starts, minutes, goals, assists and cards. It retains explicit matchday
bench rows as zero minutes, ignores undated separators, and rejects duplicate
or out-of-order matches and unsupported values. The typed document includes
only rows for the reviewed aggregate source team; internationals and other-team
rows remain excluded. It reports both aggregate and parsed
appearance/start/minute totals with an explicit `exact` or
`source-revision-mismatch` status, because FBref may correct the independently
retrieved pages. The CLI command
`--extract-fbref-player-match-log <snapshot-id>` and read-only
`/api/v1/research/snapshots/{snapshotId}/fbref-player-match-log` route expose
the same typed document without raw HTML. Extraction alone cannot influence a
forecast.

Team-schedule parser v1 emits the stable FBref match ID, date, exact UTC
kickoff, round, venue, result, score and stable opponent ID for 46–55 ordered
Championship and promotion-playoff matches. It rejects duplicate or
out-of-order IDs, inconsistent date/report links, missing kickoffs, incomplete
results and unsupported counts. The CLI command
`--extract-fbref-team-schedule <snapshot-id>` and read-only
`/api/v1/research/snapshots/{snapshotId}/fbref-team-schedule` route expose the
typed schedule without raw HTML.

These rows define opportunities, not player state. A later missing-aware
artifact must left-join each reviewed player chronology to the matching team
schedule by stable match ID. A missing player row then remains an explicit
unknown/absence observation according to a separately versioned rule; it is
never silently discarded or assumed to be zero minutes by this source parser.

## Coverage operations

The read-only
`/api/v1/research/fbref-player-match-log-coverage` route reports captured and
missing snapshot metadata for all 60 bridge-v1 identities, grouped by Coventry,
Hull and Ipswich. It never starts collection or returns raw content.

The operator command
`--capture-fbref-reviewed-match-logs <limit 1-5>` selects only missing reviewed
identities in deterministic team/player order. It runs sequentially through
the existing single-connection Byparr client, captures at most the requested
number of new snapshots and makes no more than twice that many attempts so a
single unavailable page does not block all later players. Existing coverage is
skipped, failures are isolated with a stable failure code and partial success
is retained. Repeated operation is deliberate and bounded rather than a
background crawl.
