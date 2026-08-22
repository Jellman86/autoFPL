# AI evidence semantic review v1

## Purpose

Third-party lineup, injury, pundit and news text is not naturally reducible to
a fixed hand-written score. A report may concern a later Gameweek, repeat
another source, hedge a return date, describe training rather than match
availability or contradict the deterministic extractor's interpretation.

autoFPL therefore uses AI first as a **semantic evidence adjudicator**, not as a
probability generator. The model may interpret and compare retained claims,
identify time scope, distinguish direct reporting from prediction or opinion,
surface corroboration and contradiction, and abstain. It does not assign a
source weight, edit expected points or choose an authoritative squad.

## Current product slice

`GET /api/v1/evidence/review-context/current` and the read-only MCP tool
`get_current_evidence_review_context` expose one bounded context:

- only current stress scenarios whose conditional alternative can improve the
  squad if the adverse evidence is true;
- every claim ID referenced by those scenarios;
- the latest competing claim per source, player and claim type;
- the exact decision cutoff, deadline and stress-artifact identity;
- the source span, source URL, directness and duplicate-cluster identity; and
- the conditional points upside and downside already calculated by the exact
  optimiser.

The context is deterministic and content-addressed. Source text is explicitly
untrusted: instructions inside an article or claim span have no authority.

## Review output contract

The application-managed reviewer will use strict structured output with these
verdicts:

- `supports-adverse-interpretation`;
- `contradicts-adverse-interpretation`;
- `mixed-or-time-dependent`; or
- `insufficient-evidence`.

Every material conclusion must cite retained claim IDs. The review must state
the relevant Gameweek/time horizon, separate independent corroboration from
duplicate or dependent reporting, list conflicts and assumptions, and abstain
when the supplied evidence does not support a clear reading. It must not emit a
forecast probability, numerical source weight or uncited fact.

OpenAI's Structured Outputs documentation recommends a strict JSON Schema when
the application needs a typed model response. OpenRouter documents the same
`response_format` shape for compatible routed models. autoFPL will also validate
the returned object itself and represent refusal, truncation, provider failure
or invalid output as an explicit unavailable review:

- [OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs)
- [OpenRouter Structured Outputs](https://openrouter.ai/docs/guides/features/structured-outputs)

## Numerical influence and evaluation

Semantic review is a challenger feature. It may determine that a captured claim
was misread, time-misaligned, duplicated or genuinely adverse; it does not
decide how many percentage points to move a player.

Numerical influence is learned only from cutoff-correct realised outcomes:

1. freeze the deterministic extraction and the AI review before the deadline;
2. label claim interpretation and eventual start/appearance/minutes outcomes;
3. compare deterministic-only and AI-reviewed variants on identical temporal
   folds;
4. calibrate by source, lead-time bucket and review category with shrinkage;
5. propagate only a preregistered passing challenger through the unchanged
   player distribution and initial-squad policy screens; and
6. retain abstentions, negative results and model/provider provenance.

This separation gives the product richer reading of qualitative evidence
without replacing empirical calibration with persuasive model prose.
