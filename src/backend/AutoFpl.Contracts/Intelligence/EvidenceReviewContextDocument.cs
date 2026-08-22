using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record EvidenceReviewContextDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reviewMode")] string ReviewMode,
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
    [property: JsonPropertyName("reviewPolicy")]
        EvidenceReviewPolicyDocument ReviewPolicy,
    [property: JsonPropertyName("coverage")]
        EvidenceReviewCoverageDocument Coverage,
    [property: JsonPropertyName("targets")]
        IReadOnlyList<EvidenceReviewTargetDocument> Targets,
    [property: JsonPropertyName("limitations")]
        IReadOnlyList<string> Limitations);

public sealed record EvidenceReviewPolicyDocument(
    [property: JsonPropertyName("promptVersion")] string PromptVersion,
    [property: JsonPropertyName("outputSchemaVersion")]
        string OutputSchemaVersion,
    [property: JsonPropertyName("allowedVerdicts")]
        IReadOnlyList<string> AllowedVerdicts,
    [property: JsonPropertyName("requiredBehaviors")]
        IReadOnlyList<string> RequiredBehaviors,
    [property: JsonPropertyName("prohibitedBehaviors")]
        IReadOnlyList<string> ProhibitedBehaviors);

public sealed record EvidenceReviewCoverageDocument(
    [property: JsonPropertyName("decisionRelevantScenarioCount")]
        int DecisionRelevantScenarioCount,
    [property: JsonPropertyName("targetPlayerCount")] int TargetPlayerCount,
    [property: JsonPropertyName("referencedClaimCount")]
        int ReferencedClaimCount,
    [property: JsonPropertyName("includedClaimCount")] int IncludedClaimCount,
    [property: JsonPropertyName("sourceCount")] int SourceCount);

public sealed record EvidenceReviewTargetDocument(
    [property: JsonPropertyName("player")]
        EvidenceReviewPlayerDocument Player,
    [property: JsonPropertyName("scenarioKeys")]
        IReadOnlyList<string> ScenarioKeys,
    [property: JsonPropertyName("sourceKeys")]
        IReadOnlyList<string> SourceKeys,
    [property: JsonPropertyName("referencedClaimIds")]
        IReadOnlyList<long> ReferencedClaimIds,
    [property: JsonPropertyName("alternativePlayerNames")]
        IReadOnlyList<string> AlternativePlayerNames,
    [property: JsonPropertyName("pointsIfAdverseEvidenceTrue")]
        decimal PointsIfAdverseEvidenceTrue,
    [property: JsonPropertyName("pointsIfAdverseEvidenceFalse")]
        decimal PointsIfAdverseEvidenceFalse,
    [property: JsonPropertyName("claims")]
        IReadOnlyList<EvidenceReviewClaimDocument> Claims);

public sealed record EvidenceReviewPlayerDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("webName")] string WebName,
    [property: JsonPropertyName("teamId")] int TeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("position")] string Position);

public sealed record EvidenceReviewClaimDocument(
    [property: JsonPropertyName("claimId")] long ClaimId,
    [property: JsonPropertyName("isScenarioReferenced")]
        bool IsScenarioReferenced,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("canonicalUrl")] string CanonicalUrl,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("publishedAtUtc")] DateTimeOffset? PublishedAtUtc,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("claimType")] string ClaimType,
    [property: JsonPropertyName("availabilityStatus")]
        string? AvailabilityStatus,
    [property: JsonPropertyName("startStatus")] string? StartStatus,
    [property: JsonPropertyName("forecastProbability")]
        decimal? ForecastProbability,
    [property: JsonPropertyName("expectedMinutes")] int? ExpectedMinutes,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("directness")] string Directness,
    [property: JsonPropertyName("sourceSpan")] string SourceSpan,
    [property: JsonPropertyName("duplicateClusterKey")]
        string? DuplicateClusterKey,
    [property: JsonPropertyName("claimContentSha256")]
        string ClaimContentSha256);
