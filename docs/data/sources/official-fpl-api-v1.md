# Official FPL read-only API v1

## Status and purpose

- **Source ID:** `official-fpl-api/v1`
- **Status:** Admitted
- **Purpose:** private point-in-time reference data and later historical replay
- **Provider:** Fantasy Premier League / Premier League
- **Authentication:** none
- **Account access:** none

This source supplies the first real player catalogue, Gameweek deadlines,
teams, fixtures and fixture outcomes used by autoFPL. Admission means the
bounded collector may retain private captures for research. It does not mean
that every provider field is a valid predictive feature.

## Fixed resources

The importer performs one GET against each fixed HTTPS URL:

- `https://fantasy.premierleague.com/api/bootstrap-static/`
- `https://fantasy.premierleague.com/api/fixtures/`

The caller cannot provide a URL, proxy, header, cookie or credential. Redirects
are disabled. Each response must be JSON, complete within the configured
20-second request timeout and remain at or below 4 MiB. The client permits at
most two connections to the provider because the two resources form one
capture.

Run an operator capture with:

```text
dotnet AutoFpl.Api.dll --import-official-fpl
```

The command writes one JSON summary to stdout. A new database may be created;
an existing database receives migrations first. The supported HTTP API exposes
only capture metadata and counts at
`GET /api/v1/data/official-fpl/latest`; it does not expose retained raw JSON or
trigger collection.

## Timing and correction semantics

The provider payload does not establish a separately verifiable publication
timestamp, so `publishedAtUtc` remains `null`. `retrievedAtUtc` is recorded
after both responses have been received and validated. `availableAtUtc` equals
that completed retrieval time. A backtest must not use the capture before that
instant.

SHA-256 is calculated independently over the exact bootstrap and fixture
response bytes. The pair is the immutable content identity:

- an identical pair returns the earliest existing capture rather than creating
  duplicate evidence;
- any changed resource creates a new capture and leaves the earlier bytes and
  normalised rows intact; and
- downstream features select captures by `availableAtUtc`, never by a later
  corrected value.

## Persisted fields

The private SQLite capture retains the exact two JSON responses plus bounded
normalised fields:

- season code derived from the first Gameweek deadline;
- Gameweek ID, name, deadline and completion/current/next flags;
- team ID, provider code, name and short name;
- player ID/code, team, position, names, price, availability status/news,
  selection percentage, total points, minutes and starts; and
- fixture ID, Gameweek, teams, kickoff, state and final/provisional score.

Foreign keys and range checks reject unknown teams, events, positions,
one-sided scores, duplicate IDs and schema/type drift before a transaction is
written.

## Known limitations and feature boundary

- The endpoints are public read-only application resources, not a formally
  versioned provider API. Fields and behaviour may change without notice.
- Pre-season cumulative player statistics can reflect prior-season context.
  No stored statistic is treated as a same-season feature until its semantics
  and decision-time availability are tested.
- Provider player IDs are capture/season-scoped inputs. Cross-season identity
  matching must use explicit provider codes and reviewed matching logic.
- Availability/news values are provider state, not ground truth. Their
  predictive contribution requires rolling evaluation and ablation.
- One current capture cannot establish historical replay. Repeated captures or
  properly timestamped archives plus later outcomes are still required.

The current slice does not join this data into a decision snapshot, render it
as advice, fit a model or submit any FPL action.

## Retention and responsible use

Captures remain in the private runtime database and are not committed or
republished as a third-party data mirror. Collection is operator-triggered,
bounded and cache-friendly. Stop collection if the provider rejects the client,
the resources cease to be public, or a concrete applicable restriction is
identified.

Provider references:

- [FPL terms](https://fantasy.premierleague.com/en/help/terms)
- [FPL rules](https://fantasy.premierleague.com/en/help/rules)

## Verification record

On 2026-07-25 the fixed-origin importer accepted the live 2026/27 payload with
38 Gameweeks, 20 teams, 558 players and 380 fixtures, then passed SQLite
integrity checking. Counts are observations from that capture, not stable
provider guarantees.
