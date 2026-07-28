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

It does not yet supply match-order temporal form. No field influences a
forecast until the source-ID-to-official-code bridge is reviewed, match
chronology is retained and identical-fold evaluation shows predictive gain.

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
