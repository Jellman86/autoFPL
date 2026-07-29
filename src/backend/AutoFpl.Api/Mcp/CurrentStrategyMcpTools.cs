using System.ComponentModel;

using AutoFpl.Api.Selections;
using AutoFpl.Contracts.Selections;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AutoFpl.Api.Mcp;

[McpServerToolType]
public sealed class CurrentStrategyMcpTools
{
    private readonly SelectionRoleStrategyShadowStore _store;

    public CurrentStrategyMcpTools(SelectionRoleStrategyShadowStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    [McpServerTool(
        Name = "get_current_strategies",
        Title = "Get current strategies",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Get the current balanced, safer and higher-ceiling role strategies "
        + "for the public model's fixed 15-player squad, including their "
        + "paired scenario distributions and comparison with the model. "
        + "Strategies are unpromoted prospective shadow evidence, are not "
        + "global optima and cannot mutate an owner selection.")]
    public async Task<SelectionRoleStrategyShadowDocument>
        GetCurrentStrategiesAsync(CancellationToken cancellationToken)
    {
        return await _store.GetCurrentAsync(cancellationToken)
            ?? throw new McpException(
                "No current autoFPL strategy comparison is available.");
    }
}
