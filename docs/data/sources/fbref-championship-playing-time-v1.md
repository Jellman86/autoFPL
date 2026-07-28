# FBref Championship playing time v1

- **Source ID:** `fbref-championship-playing-time/v1`
- **Status:** Admitted for private shadow capture
- **Evidence season:** 2025/26 Championship
- **Runtime source key:** `fbref-championship-playing-time-2025-26`

## Purpose and fields

The source is a bounded prior-competition coverage candidate for current
players whose 2025/26 history is absent from the official FPL archive. The
first capture retains FBref's Championship aggregate playing-time page,
including source player links/IDs, squad, appearances, starts and minutes.

It does not yet supply match-order temporal form. No field influences a
forecast until a deterministic parser, explicit source-ID-to-official-code
mapping and identical-fold evaluation are implemented.

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
evidence only: automatic fuzzy matching is forbidden, ambiguous or missing
mappings remain unresolved, and a reviewed bridge must map one source player
ID to one official `player.code`.

The page may be corrected after publication and its aggregates cannot establish
recent match sequence, health or current-club role. Current official
availability remains authoritative. Raw third-party content stays private and
is not republished as a dataset.
