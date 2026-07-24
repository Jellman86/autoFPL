# Predictive Feature Evidence Review

Copy this file to `docs/research/reviews/<topic>.md` before implementing a feature that makes a predictive, statistical, simulation, optimisation or data-derived performance claim. Ordinary UI, CRUD and infrastructure changes do not require a literature review unless they change one of those claims.

## Metadata

- **Review ID:**
- **Feature/decision:**
- **Issue:**
- **Owner:**
- **Status:** draft | accepted | superseded
- **Search completed at (UTC):**
- **Last refreshed at (UTC):**
- **Reviewer:**

## Decision question and scope

- Decision this work should improve:
- Population and forecast horizon:
- Target and required forecast distribution:
- Information available at the decision deadline:
- Current incumbent:
- Explicit non-goals:

## Search protocol

Record enough detail for another researcher to repeat the search.

- Databases/indexes searched:
- Exact search strings:
- Search date range:
- Inclusion criteria:
- Exclusion criteria:
- Backward/forward citation search:
- Relevant surveys or systematic reviews:
- Recent-author/venue search used to find frontier work:

Prefer peer-reviewed primary work and strong reviews. Label preprints, unpublished claims and vendor benchmarks clearly. Cite the DOI or immutable/versioned paper URL actually read, including an arXiv version suffix where applicable. Record negative and contradictory evidence, not only supporting papers.

## Evidence matrix

| Immutable citation | Review status/venue | Method and claim | Data/population | Temporal/information boundary | Validation and baselines | Metrics, calibration and uncertainty | Result | Assumptions and limitations | Code/data/licence | autoFPL applicability |
|---|---|---|---|---|---|---|---|---|---|---|
| | | | | | | | | | | |

## Synthesis

### Strong baselines

Describe naive, domain, market/crowd, incumbent and strong classical methods that a more complex approach must beat.

### Best established evidence

Describe methods with credible repeated or externally replicated evidence. Separate the paper's demonstrated result from extrapolation to FPL.

### Frontier candidates

Describe credible recent methods, why they might transfer, their maturity, compute/data needs and unresolved risks. “Newest” or “state of the art” is not itself evidence of suitability.

### Contradictory evidence and failure modes

Record null results, failed replications, dataset sensitivity, leakage risks, weak comparisons and assumptions likely to fail in FPL.

### Applicability assessment

Explain differences between published data and autoFPL: target definition, season/regime, publication latency, sample size, missingness, rules, compute budget and decision utility.

## Pre-implementation decision

- Baseline(s) to reproduce:
- Established candidate(s) to test:
- Frontier challenger(s) to test:
- Methods rejected before implementation and why:
- Minimum implementation needed for a fair comparison:
- Registered temporal split and final-test boundary:
- Primary proper scoring/calibration/decision metrics:
- Promotion threshold and uncertainty rule:
- Compute/time budget:
- Required dataset/model-card updates:

No candidate is selected for production in this review. Implementation produces local point-in-time, out-of-time evidence; promotion follows only if the preregistered rule and the full research standard are satisfied.

## Refresh and review record

Refresh the search before implementation if the review is stale for the field, and always before promotion when material new evidence may exist. Record changes rather than silently rewriting the original decision.

| Date | Change/new evidence | Effect on candidate set or promotion rule | Reviewer |
|---|---|---|---|
| | | | |
