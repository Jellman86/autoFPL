using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public interface IEvidenceSemanticReviewProvider
{
    Task<EvidenceSemanticReviewProviderResponse> ReviewAsync(
        EvidenceReviewContextDocument context,
        CancellationToken cancellationToken = default);
}

public sealed record EvidenceSemanticReviewProviderResponse(
    string Status,
    string? Reason,
    IReadOnlyList<EvidenceSemanticReviewProviderResult> Results,
    string ProviderModel,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long LatencyMilliseconds,
    string Outcome,
    string ResponseSha256);

public sealed record EvidenceSemanticReviewProviderResult(
    int PlayerId,
    string Verdict,
    string EvidenceQuality,
    string TimeHorizon,
    IReadOnlyList<string> ScenarioKeys,
    IReadOnlyList<long> SupportingClaimIds,
    IReadOnlyList<long> ContradictingClaimIds,
    IReadOnlyList<long> DependentClaimIds,
    IReadOnlyList<string> CorroboratingSourceKeys,
    IReadOnlyList<string> ContradictingSourceKeys,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Uncertainties,
    string Scope,
    string Rationale,
    bool IsAbstained);

public sealed class EvidenceSemanticReviewProviderException : Exception
{
    public EvidenceSemanticReviewProviderException(string outcome)
        : base($"Evidence semantic review provider failed with outcome '{outcome}'.")
    {
        Outcome = outcome;
    }

    public string Outcome { get; }
}
