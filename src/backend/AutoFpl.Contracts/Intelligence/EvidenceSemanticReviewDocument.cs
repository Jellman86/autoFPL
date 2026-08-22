using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record EvidenceSemanticReviewDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("artifactType")] string ArtifactType,
    [property: JsonPropertyName("artifactVersion")] string ArtifactVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("isPromoted")] bool IsPromoted,
    [property: JsonPropertyName("influencesForecast")] bool InfluencesForecast,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("decisionCutoffUtc")]
        DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("stressArtifactId")] long StressArtifactId,
    [property: JsonPropertyName("stressArtifactContentSha256")]
        string StressArtifactContentSha256,
    [property: JsonPropertyName("contextIdentitySha256")]
        string ContextIdentitySha256,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("providerModel")] string ProviderModel,
    [property: JsonPropertyName("promptVersion")] string PromptVersion,
    [property: JsonPropertyName("outputSchemaVersion")]
        string OutputSchemaVersion,
    [property: JsonPropertyName("requestedAtUtc")] DateTimeOffset RequestedAtUtc,
    [property: JsonPropertyName("completedAtUtc")] DateTimeOffset CompletedAtUtc,
    [property: JsonPropertyName("dataIdentitySha256")] string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")] string RunIdentitySha256,
    [property: JsonPropertyName("coverage")]
        EvidenceSemanticReviewCoverageDocument Coverage,
    [property: JsonPropertyName("results")]
        IReadOnlyList<EvidenceSemanticReviewResultDocument> Results,
    [property: JsonPropertyName("artifactId")] long? EvidenceSemanticReviewArtifactId,
    [property: JsonPropertyName("artifactContentSha256")]
        string? EvidenceSemanticReviewArtifactContentSha256,
    [property: JsonPropertyName("providerAdapter")]
        string? ProviderAdapter = null,
    [property: JsonPropertyName("providerRoutingPolicyVersion")]
        string? ProviderRoutingPolicyVersion = null,
    [property: JsonPropertyName("providerOutcome")]
        string? ProviderOutcome = null,
    [property: JsonPropertyName("providerLatencyMilliseconds")]
        long? ProviderLatencyMilliseconds = null,
    [property: JsonPropertyName("providerResponseSha256")]
        string? ProviderResponseSha256 = null);

public sealed record EvidenceSemanticReviewCoverageDocument(
    [property: JsonPropertyName("contextTargetCount")]
        int ContextTargetCount,
    [property: JsonPropertyName("resultTargetCount")]
        int ResultTargetCount,
    [property: JsonPropertyName("abstainedTargetCount")]
        int AbstainedTargetCount,
    [property: JsonPropertyName("unavailableTargetCount")]
        int UnavailableTargetCount,
    [property: JsonPropertyName("citedClaimCount")]
        int CitedClaimCount,
    [property: JsonPropertyName("claimedSourceCount")]
        int ClaimedSourceCount);

public sealed record EvidenceSemanticReviewResultDocument(
    [property: JsonPropertyName("player")] EvidenceReviewPlayerDocument Player,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("evidenceQuality")] string EvidenceQuality,
    [property: JsonPropertyName("timeHorizon")] string TimeHorizon,
    [property: JsonPropertyName("scenarioKeys")]
        IReadOnlyList<string> ScenarioKeys,
    [property: JsonPropertyName("supportingClaimIds")]
        IReadOnlyList<long> SupportingClaimIds,
    [property: JsonPropertyName("contradictingClaimIds")]
        IReadOnlyList<long> ContradictingClaimIds,
    [property: JsonPropertyName("dependentClaimIds")]
        IReadOnlyList<long> DependentClaimIds,
    [property: JsonPropertyName("corroboratingSourceKeys")]
        IReadOnlyList<string> CorroboratingSourceKeys,
    [property: JsonPropertyName("contradictingSourceKeys")]
        IReadOnlyList<string> ContradictingSourceKeys,
    [property: JsonPropertyName("assumptions")] IReadOnlyList<string> Assumptions,
    [property: JsonPropertyName("uncertainties")] IReadOnlyList<string> Uncertainties,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("isAbstained")] bool IsAbstained,
    [property: JsonPropertyName("isUnavailable")] bool IsUnavailable,
    [property: JsonPropertyName("unavailableReason")]
        string? UnavailableReason);
