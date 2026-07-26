# Research source portfolio v1

Reviewed 26 July 2026. This is a shadow-capture inventory, not an admitted
feature set or evidence that any source improves autoFPL.

## Portfolio design

The external-evidence programme deliberately avoids one consensus track. The
structured player model remains the reference forecast. Candidate sources span
different error mechanisms:

| Evidence family | Initial candidate | Intended target | Independence treatment |
|---|---|---|---|
| official availability | [Premier League injury updates](https://www.premierleague.com/en/latest-player-injuries) and linked club reporting | availability and return-to-play | official/club reporting cluster |
| specialist selection forecast | [Fantasy Football Scout predicted line-ups](https://cdn.fantasyfootballscout.co.uk/team-news) | likely start and explicit out/doubt/banned state | FFScout editorial cluster |
| derived lineup consensus | [strAIghtred](https://www.straightred.ai/) | source agreement about likely start | dependent aggregator; never an extra independent vote |
| public quantitative challenger | [FPL Form](../data/sources/fpl-form-public-forecast-v1.md) | conditional points and appearance-adjusted points | provider model cluster |
| reproducible quantitative model | [OpenFPL](https://github.com/daniegr/OpenFPL) | one-, two- and three-Gameweek points | local version-pinned reproduction, not web sentiment |
| free quantitative forecast | [FPL Review Free Model](https://docs.fplreview.com/the-model/projections/free-model/) | three-Gameweek expected points | capture only if a stable public, non-credentialed projection surface is verified |
| named expert decisions | stable public pre-deadline teams or explicit claims from identified high-skill managers | selection-policy and occasional typed player claims | author-specific; copied reports share a duplicate cluster |
| market/team strength | lawful, reproducible free odds or goal-expectation inputs | team scoring, clean-sheet and match-state prior | provider/market cluster |

Magnus Carlsen is a plausible named-expert candidate because of demonstrated
FPL skill, but reputation does not create a probability or model weight.
Sporadic team reveals are constrained decisions and may be unavailable before
the relevant deadline. They are therefore scored as an author-specific policy
signal only when a stable, attributable, point-in-time source exists.

## Prior-season and current-health state

External sources complement rather than replace player history. The model
programme must join cutoff-safe prior-season and current-season match data,
including:

- minutes, starts, substitutions and recency-weighted FPL outcomes;
- xG, xA, xGC, shots, chances, saves, bonus and defensive contributions where
  retained with honest missingness;
- team, manager, position and competition changes;
- known injury, suspension, rehabilitation and return-to-play chronology; and
- preseason or early-season participation as a separately labelled weak
  readiness signal.

Candidate lookbacks include last match, rolling 3/5/10 matches, exponentially
decayed history and a partially pooled long-run player/team ability state.
Lookback and decay choices are fitted inside temporal training windows. A new
season does not zero durable ability, while old performance cannot override
current availability or role evidence.

## Admission and aggregation

Each captured source remains `shadow-only`. It must retain exact target,
retrieval/availability time, content identity and source revision. Claims are
scored by target and lead time against later official outcomes before any
feature ablation.

The comparison order is:

1. structured model without the candidate;
2. source alone on the identical player/fold cohort;
3. structured model plus one candidate source;
4. structured model plus the smallest complementary source set; and
5. a dependence-aware stacked or Bayesian update only after the earlier
   comparisons justify it.

Copied reports, aggregators and common upstream press-conference evidence are
clustered. Coverage volume, popularity and repeated mentions are never treated
as independent corroboration. Negative and inconclusive source results remain
part of the research record.

## Implemented boundary

The fixed inventory currently supports explicit Spider MCP shadow capture of
the Premier League injury page, FFScout predicted line-ups and the strAIghtred
consensus page. Captures are private, compressed, immutable, content-deduplicated
and tied to the latest cutoff-eligible official target. The read API exposes
metadata and limitations, never retained source text.

Deterministic extraction now populates only quarantined FFScout start and
availability claims plus dependent strAIghtred start-probability claims. The
two start sources share player/target duplicate clusters so consensus is never
counted as an extra independent vote. Premier League injury extraction, outcome
scoring, source reliability and any forecast influence remain future slices.
