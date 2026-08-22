using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record ResearchSourceDefinitionDocument(
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("sourceClass")] string SourceClass,
    [property: JsonPropertyName("canonicalUrl")] string CanonicalUrl,
    [property: JsonPropertyName("dependenceGroup")] string DependenceGroup,
    [property: JsonPropertyName("targetTypes")] IReadOnlyList<string> TargetTypes,
    [property: JsonPropertyName("rendering")] string Rendering,
    [property: JsonPropertyName("admissionStatus")] string AdmissionStatus,
    [property: JsonPropertyName("limitations")] IReadOnlyList<string> Limitations);

public sealed record ResearchSourceSnapshotDocument(
    [property: JsonPropertyName("snapshotId")] long SnapshotId,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("sourceClass")] string SourceClass,
    [property: JsonPropertyName("canonicalUrl")] string CanonicalUrl,
    [property: JsonPropertyName("finalUrl")] string FinalUrl,
    [property: JsonPropertyName("dependenceGroup")] string DependenceGroup,
    [property: JsonPropertyName("transportKey")] string TransportKey,
    [property: JsonPropertyName("transportVersion")] string TransportVersion,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("identityCaptureId")] long IdentityCaptureId,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("isPreDeadline")] bool IsPreDeadline,
    [property: JsonPropertyName("sourceRevision")] int SourceRevision,
    [property: JsonPropertyName("contentSha256")] string ContentSha256,
    [property: JsonPropertyName("contentBytes")] int ContentBytes,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc);

public sealed record ResearchSourceInventoryDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("sources")]
        IReadOnlyList<ResearchSourceDefinitionDocument> Sources,
    [property: JsonPropertyName("latestSnapshots")]
        IReadOnlyList<ResearchSourceSnapshotDocument> LatestSnapshots,
    [property: JsonPropertyName("latestStartCoverage")]
        IReadOnlyList<ResearchSourceStartCoverageDocument> LatestStartCoverage);

public sealed record ResearchSourceStartCoverageDocument(
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("snapshotId")] long SnapshotId,
    [property: JsonPropertyName("identityCaptureId")] long IdentityCaptureId,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("retrievedAtUtc")]
        DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("classifiedPlayerCount")]
        int ClassifiedPlayerCount,
    [property: JsonPropertyName("predictedStarterCount")]
        int PredictedStarterCount,
    [property: JsonPropertyName("predictedNonStarterCount")]
        int PredictedNonStarterCount,
    [property: JsonPropertyName("availabilityPlayerCount")]
        int AvailabilityPlayerCount,
    [property: JsonPropertyName("completeTeamCount")] int CompleteTeamCount,
    [property: JsonPropertyName("partialTeamCount")] int PartialTeamCount,
    [property: JsonPropertyName("missingTeamCount")] int MissingTeamCount,
    [property: JsonPropertyName("teams")]
        IReadOnlyList<ResearchSourceTeamStartCoverageDocument> Teams);

public sealed record ResearchSourceTeamStartCoverageDocument(
    [property: JsonPropertyName("teamId")] int TeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("teamShortName")] string TeamShortName,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("classifiedPlayerCount")]
        int ClassifiedPlayerCount,
    [property: JsonPropertyName("predictedStarterCount")]
        int PredictedStarterCount,
    [property: JsonPropertyName("predictedNonStarterCount")]
        int PredictedNonStarterCount,
    [property: JsonPropertyName("availabilityPlayerCount")]
        int AvailabilityPlayerCount,
    [property: JsonPropertyName("status")] string Status);

public sealed record ResearchSourceClaimExtractionDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("snapshotId")] long SnapshotId,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("extractionVersion")] string ExtractionVersion,
    [property: JsonPropertyName("candidateCount")] int CandidateCount,
    [property: JsonPropertyName("startClaimCount")] int StartClaimCount,
    [property: JsonPropertyName("unresolvedStartCount")]
        int UnresolvedStartCount,
    [property: JsonPropertyName("availabilityCandidateCount")]
        int AvailabilityCandidateCount,
    [property: JsonPropertyName("availabilityClaimCount")]
        int AvailabilityClaimCount,
    [property: JsonPropertyName("unresolvedAvailabilityCount")]
        int UnresolvedAvailabilityCount,
    [property: JsonPropertyName("claimCount")] int ClaimCount,
    [property: JsonPropertyName("unresolvedPlayerCodes")]
        IReadOnlyList<int> UnresolvedPlayerCodes);

public sealed record ResearchSourceRefreshHealthDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("expectedIntervalMinutes")]
        int? ExpectedIntervalMinutes,
    [property: JsonPropertyName("staleSourceKeys")]
        IReadOnlyList<string> StaleSourceKeys,
    [property: JsonPropertyName("sources")]
        IReadOnlyList<ResearchSourceRefreshStateDocument> Sources);

public sealed record ResearchSourceRefreshStateDocument(
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("pollAutomatically")] bool PollAutomatically,
    [property: JsonPropertyName("lastRetrievedAtUtc")]
        DateTimeOffset? LastRetrievedAtUtc,
    [property: JsonPropertyName("ageMinutes")] int? AgeMinutes,
    [property: JsonPropertyName("status")] string Status);
