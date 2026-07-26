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

The reference importer performs one GET against each fixed HTTPS URL:

- `https://fantasy.premierleague.com/api/bootstrap-static/`
- `https://fantasy.premierleague.com/api/fixtures/`

The caller cannot provide a URL, proxy, header, cookie or credential. Redirects
are disabled. Each response must be JSON, complete within the configured
20-second request timeout and remain at or below 4 MiB. The client permits at
most two connections to the provider because the two resources form one
capture.

The final-outcome importer first refreshes that reference capture and then
performs one GET against the bounded Gameweek template:

- `https://fantasy.premierleague.com/api/event/{gameweek}/live/`

`gameweek` must be an integer from 1 through 38; no other origin, path, query,
header or credential is caller-controlled.

Run an operator capture with:

```text
dotnet AutoFpl.Api.dll --import-official-fpl
dotnet AutoFpl.Api.dll --import-official-fpl-outcome <gameweek>
```

An instance may set
`AutoFpl__Research__OfficialFplPollIntervalMinutes=360` (or another bounded
value from 60 through 1440 minutes) for automatic reference capture. The
persisted attempt time throttles unchanged responses and restarts; the URL set,
validation and immutable deduplication are identical to the operator command.
Final outcome import remains an explicit command because it requires a
finished, data-checked Gameweek number.

The command writes one JSON summary to stdout. A new database may be created;
an existing database receives migrations first. The supported HTTP API exposes
only capture metadata and counts at
`GET /api/v1/data/official-fpl/latest`. It selects the newest qualifying
pre-deadline capture at
`GET /api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/pre-deadline`.
Final outcome metadata is available at
`GET /api/v1/data/official-fpl/outcomes/{seasonCode}/{gameweek}/latest`, and a
fully matched pair at
`GET /api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/outcome`.
None of these routes exposes retained raw JSON or triggers collection.

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

Outcome SHA-256 is calculated over the exact live response bytes. Identical
season/Gameweek hashes reuse the earliest stored outcome; corrected bytes create
a new immutable outcome. Before any write, the reference event must be finished
and data-checked, all of its fixtures must be finished, and live player IDs must
exactly match the post-event reference players. This deliberately rejects the
provider's current pre-season empty `elements` response.

## Persisted fields

The private SQLite capture retains the exact two JSON responses plus bounded
normalised fields:

- season code derived from the first Gameweek deadline;
- Gameweek ID, name, deadline and completion/current/next flags;
- team ID, provider code, name and short name;
- player ID/code, team, position, names, price, availability status/news,
  official photo identifier, selection percentage, total points, minutes and
  starts; and
- fixture ID, Gameweek, teams, kickoff, state and final/provisional score.

Final outcome rows retain player ID, minutes, starts, total points, goals,
assists, clean sheets, goals conceded, saves, bonus, cards, own goals and
penalty outcomes. They also retain the provider's BPS, influence, creativity,
threat, ICT, defensive-action counts, defensive contribution, expected goals,
expected assists, expected goal involvements and expected goals conceded.
These are observed Gameweek outcomes, not pre-match predictions. The exact live
JSON remains private for later parser correction and audit.

Foreign keys and range checks reject unknown teams, events, positions,
one-sided scores, duplicate IDs and schema/type drift before a transaction is
written.

The photo identifier must contain only a positive numeric official asset code
plus `.jpg` or `.png`. The player-dossier read model removes that validated
extension and constructs a PNG URL only beneath
`https://resources.premierleague.com/premierleague/photos/players/110x140/`.
It never accepts or stores a caller/provider-supplied arbitrary URL. Re-running
an identical capture after the photo migration fills only missing normalised
photo identifiers from the unchanged retained bootstrap bytes.

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
- The pairing route proves chronology and complete player identity for one
  Gameweek; it does not by itself establish a useful sample size, target
  quality or model validity. Multiple completed cutoff-safe Gameweeks are still
  required for rolling evaluation.

The current UI renders capture provenance and replay readiness alongside an
explicitly unvalidated Baseline v0 when a qualifying capture exists. That
baseline uses price, ownership, availability, capture-reported aggregates and
fixture context to produce an initial legal selection; it is not a fitted or
promoted model. One immutable Baseline v0 document and content hash is persisted
against the exact official capture, so UI reloads do not silently recompute or
change the prediction. Without qualifying evidence, the UI retains its
explicitly synthetic acceptance fixture. Neither path submits an FPL action.

The player dossier route at
`GET /api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/players/{playerId}`
selects the same pre-deadline reference capture, then returns identity, the
latest five prior official outcomes whose corrections were available by that
deadline and fixtures from the selected capture. Retained underlying statistics
are exposed for new outcome captures and remain explicitly null for legacy rows
that predate their normalisation. A single-fixture outcome is match-specific;
double-Gameweek statistics remain visibly aggregated while the individual
opponents are listed.

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
provider guarantees. On 2026-07-26 the Gameweek 1 live resource returned an
empty `elements` array before the season started; the outcome importer correctly
treats that state as unavailable rather than a final result.
