# Current public-projection opening squad v1

## Question

Does an independent public points model identify a better 2026/27 opening
squad than the best-supported autoFPL v2 prediction?

The source currently exposes only a ranked subset and no retained historical
folds. The honest answer cannot be known before prospective outcomes. This
artifact therefore makes the comparison operational while keeping the serving
boundary closed.

## Frozen method

For the latest exact official Gameweek 1 capture, the generator:

1. loads the retained appearance-hurdle joint scenarios and selected v2 squad;
2. selects the latest Solio snapshot captured before the official deadline;
3. verifies exact source lineage, byte count, SHA-256 and source chronology;
4. matches published rows by normalized player name, official team short name,
   position and current price, with no fuzzy or manual identity bridge;
5. replaces only matched players' Gameweek 1 optimizer means with the public
   mean while retaining autoFPL for unpublished players and Gameweeks 2–8;
6. globally solves the unchanged legal six-Gameweek expected-value policy at
   zero numerical MIP gap; and
7. freezes both squads' roles for all eight registered outcome Gameweeks using
   the same preseason completion rule; and
8. scores incumbent and challenger on the unchanged retained autoFPL paths to
   expose the cost if the external overlay contains no information.

The public mean is constant across the optimization surrogate. It does not
claim an external distribution and is not used to manufacture uncertainty.

## Product boundary

The worker writes
`public-projection-opening-squad-capture-<capture-id>.json` for every new
eligible source revision. The application validates the exact capture and
source snapshot, both legal squads, projection identities and immutable
content before insertion.

`GET /api/v1/forecasts/public-projection-opening-squad-shadow/current` returns
the latest exact-capture challenger. The decision room shows agreement,
coverage and proposed changes beside a clear prospective-only warning. It is:

- `status: prospective-external-challenger-unscored`;
- `isPromoted: false`; and
- `influencesAdvice: false`.

The current selected-opening-squad route and draft workflow remain bound to
the historically retained v2 model.

## Command

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.current_public_projection_opening_squad \
  --database /path/to/autofpl.db \
  --output /path/to/current-public-projection-opening-squad.json
```

The application-side import is:

```text
dotnet AutoFpl.Api.dll \
  --import-public-projection-opening-squad-shadow <json-file>
```

## Evaluation gate

The final predeadline artifact is frozen by source snapshot. Both squads are
registered as fixed, transfer-free squads with all eight weekly roles frozen
before outcomes. After official outcomes exist, compare the challenger with
the same-capture v2 incumbent under the same exact captain-fallback and ordered
auto-substitution scorer:

```bash
PYTHONPATH=src/analytics python3 \
  -m autofpl_analytics.public_projection_opening_squad_outcome_evaluation \
  --database /path/to/autofpl.db \
  --output /path/to/public-projection-opening-squad-outcome.json
```

The evaluator reports partial Gameweek 1–7 evidence without making a review
decision. Once all eight outcomes exist, a positive cumulative delta and at
least as many weekly wins as losses support a source-promotion review. The
artifact never promotes the source automatically: one opening period cannot
precisely estimate value across seasons. Until that prospective comparison is
complete and reviewed, the source cannot replace or blend into the
recommendation. See the
[outcome evaluation specification](public-projection-opening-squad-prospective-outcome-evaluation-v1.md).
