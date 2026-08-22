using System.ComponentModel;

using AutoFpl.Api.Intelligence;
using AutoFpl.Contracts.Intelligence;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AutoFpl.Api.Mcp;

[McpServerToolType]
public sealed class CurrentEvidenceSemanticReviewMcpTools
{
    private readonly EvidenceSemanticReviewStore _store;

    public CurrentEvidenceSemanticReviewMcpTools(
        EvidenceSemanticReviewStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    [McpServerTool(
        Name = "get_current_evidence_semantic_review",
        Title = "Get current evidence semantic review",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Get the current immutable semantic review of third-party evidence for "
        + "the opening squad pressure. The review contains per-player verdicts, "
        + "evidence quality, citations, and rationale, and is only to explain "
        + "evidence interpretation. It does not forecast probabilities.")]
    public async Task<EvidenceSemanticReviewDocument> GetCurrentEvidenceSemanticReviewAsync(
        CancellationToken cancellationToken)
    {
        return await _store.GetCurrentAsync(cancellationToken)
            ?? throw new McpException(
                "No current evidence-semantic-review artifact is available.");
    }
}
