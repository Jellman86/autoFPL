using System.ComponentModel;

using AutoFpl.Api.Intelligence;
using AutoFpl.Contracts.Intelligence;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AutoFpl.Api.Mcp;

[McpServerToolType]
public sealed class CurrentEvidenceReviewMcpTools
{
    private readonly CurrentEvidenceReviewContextStore _store;

    public CurrentEvidenceReviewMcpTools(
        CurrentEvidenceReviewContextStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    [McpServerTool(
        Name = "get_current_evidence_review_context",
        Title = "Review current external evidence",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Get the bounded third-party claim context for evidence that can change "
        + "the current opening squad under an explicit stress. Semantically "
        + "compare claims, cite claim IDs, identify contradictions and source "
        + "dependence, and abstain when the evidence is insufficient. Do not "
        + "invent probabilities or change the forecast.")]
    public async Task<EvidenceReviewContextDocument>
        GetCurrentEvidenceReviewContextAsync(
            CancellationToken cancellationToken)
    {
        return await _store.GetCurrentAsync(cancellationToken)
            ?? throw new McpException(
                "No current external-evidence stress artifact is available.");
    }
}
