# autoFPL plugin

This package connects ChatGPT or Codex to autoFPL's production Streamable HTTP
MCP endpoint at `https://autofpl.pownet.uk/mcp`.

The package exposes three anonymous, read-only tools:

- `get_current_prediction` returns the persisted public model squad, XI, bench,
  captaincy, cutoff, artifact identity, evidence status and uncertainty.
- `get_player_dossier` returns one cutoff-correct public player evidence
  dossier.
- `get_current_strategies` returns the current balanced, safer and
  higher-ceiling role strategies with paired scenario comparisons against the
  public model.

All tools are non-destructive and closed-world. They do not return owner
drafts, locked selections, integration settings or secrets, and they cannot
write to FPL. The current prediction is still the explicitly provisional
Baseline v0 unless its returned artifact says otherwise. Strategy results are
unpromoted prospective shadow evidence and are not global optima.

This is a development package for private host testing. Public directory
submission still requires the release materials, policy URLs, domain
verification and prompt evaluations described in the
[roadmap](../../docs/roadmap.md). Owner-specific conversation requires
autoFPL identity and OAuth before additional tools can expose private state.
