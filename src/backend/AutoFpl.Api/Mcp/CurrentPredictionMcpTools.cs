using System.ComponentModel;

using AutoFpl.Api.Advice;
using AutoFpl.Contracts.Advice;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AutoFpl.Api.Mcp;

[McpServerToolType]
public sealed class CurrentPredictionMcpTools
{
    private readonly BaselineForecastArtifactStore _store;

    public CurrentPredictionMcpTools(BaselineForecastArtifactStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    [McpServerTool(
        Name = "get_current_prediction",
        Title = "Get current prediction",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Get the latest persisted public autoFPL model squad, XI, bench, "
        + "captaincy, cutoff, evidence status and uncertainty. The response is "
        + "the provisional Baseline v0 prediction unless a later model is "
        + "explicitly identified; it contains no owner selection or private "
        + "configuration.")]
    public async Task<GameweekAdviceDocument> GetCurrentPredictionAsync(
        CancellationToken cancellationToken)
    {
        return await _store.GetLatestAsync(cancellationToken)
            ?? throw new McpException(
                "No persisted official autoFPL prediction is available.");
    }
}
