# Foundation Evidence Base

Reviewed 24 July 2026. Primary and strong peer-reviewed sources are preferred; live documents must be rechecked when relied on because they can change.

## Scientific and reproducible research

- [PRISMA 2020 statement](https://doi.org/10.1136/bmj.n71) — transparent search, selection and reporting practices adapted proportionately for feature evidence reviews; autoFPL does not claim each focused review is a full systematic review.
- [NeurIPS Paper Checklist Guidelines](https://neurips.cc/public/guides/PaperChecklist) — claims, assumptions, limitations, reproducibility, baselines, uncertainty, compute and asset/licence disclosure.
- [The Turing Way: reproducible research](https://the-turing-way.netlify.app/reproducible-research/overview/overview-definitions.html) — version control, reproducible environments, testing, documentation and responsible handling of non-shareable data.
- [FAIR software recommendations](https://fair-software.eu/) — repository, licence, registry, citation and checklist practices.
- [scikit-learn common pitfalls](https://scikit-learn.org/stable/common_pitfalls.html) — train/test separation, pipelines and reproducible randomness.
- [Gneiting & Raftery, Strictly Proper Scoring Rules](https://doi.org/10.1198/016214506000001437) — evaluate probabilistic forecasts with incentives for honest distributions.
- [Waghmare & Ziegel, Proper scoring rules for estimation and forecast evaluation](https://arxiv.org/abs/2504.01781v1) — current review of scoring-rule foundations and applications; version-pinned preprint evidence, not a substitute for local validation.
- [Model Cards for Model Reporting](https://doi.org/10.1145/3287560.3287596) — intended use, evaluation conditions, limitations and disaggregated reporting.
- [Datasheets for Datasets](https://doi.org/10.1145/3458723) — dataset motivation, composition, collection, processing, uses, distribution and maintenance.
- [NIST AI Risk Management Framework](https://www.nist.gov/itl/ai-risk-management-framework) — govern, map, measure and manage AI risk.

## FPL and decision research

This foundation list establishes the programme's initial boundaries; it does not authorize a predictive implementation by itself. Each predictive feature requires a topic-specific review created from [`literature-review-template.md`](literature-review-template.md), including an updated search for established and frontier methods.

- [Matthews, Ramchurn & Chalkiadakis, AAAI 2012](https://doi.org/10.1609/aaai.v26i1.8259) — sequential team formation under partial observability; historically relevant, not evidence of current repeatability.
- [O'Brien, Gleeson & O'Sullivan, PLOS ONE 2021](https://doi.org/10.1371/journal.pone.0246698) — evidence of skill, planning and persistent manager performance alongside luck.
- [Venter & van Vuuren, ORiON 2024](https://doi.org/10.5784/40-1-753) — prediction plus combinatorial FPL optimisation; single retrospective season limits the claim.
- [Bhatt et al., ICWSM 2019](https://doi.org/10.1609/icwsm.v13i01.3213) — crowd diversity and captaincy decisions.
- [Ramezani, 2025 preprint](https://arxiv.org/abs/2505.02170) — integer/robust optimisation and simulation; useful design evidence, not peer-reviewed proof.
- [Open FPL Solver](https://github.com/solioanalytics/open-fpl-solver) — active Apache-2.0 reference implementation; pin and audit before reuse.
- [Groos, OpenFPL 2025](https://arxiv.org/abs/2508.09992) and its [MIT implementation](https://github.com/daniegr/OpenFPL) — prospective public-data FPL/Understat position ensembles and a valuable feature benchmark; local temporal reproduction and ablation remain required.
- [Vaastav FPL Historical Dataset](https://github.com/vaastav/Fantasy-Premier-League) — useful historical FPL/Understat backfill whose maintainers explicitly warn that same-Gameweek `xP` may contain post-match lookahead; exclude that field unless a pre-deadline capture proves its timing.

## Engineering and supply chain

- [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) — .NET 10 is active LTS as of this review.
- [Python version status](https://devguide.python.org/versions/) — Python 3.14 is in bugfix support; package compatibility still governs the analytics pin.
- [GitHub secure use reference](https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions) — least privilege and full-SHA Action pinning.
- [SLSA requirements](https://slsa.dev/spec/v1.2/requirements) — source/build provenance and verifiable artefacts.
- [OpenSSF Scorecard](https://scorecard.dev/) — automated assessment of risky repository practices.
- [NIST Secure Software Development Framework](https://csrc.nist.gov/Projects/ssdf) — prepare, protect, produce and respond practices.
- [OWASP ASVS](https://owasp.org/www-project-application-security-verification-standard/) — testable web application security controls.

## Competition rules and data boundary

- [FPL terms](https://fantasy.premierleague.com/en/help/terms)
- [FPL rules](https://fantasy.premierleague.com/en/help/rules)
- [FPL prizes](https://fantasy.premierleague.com/en/prizes)

The current terms page contains apparently stale date text in some 2026/27 prize-notification clauses. Exact live obligations and season rules must be captured and reviewed before implementation that depends on them.
