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
        IReadOnlyList<ResearchSourceSnapshotDocument> LatestSnapshots);

public sealed record ResearchSourceClaimExtractionDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("snapshotId")] long SnapshotId,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("extractionVersion")] string ExtractionVersion,
    [property: JsonPropertyName("candidateCount")] int CandidateCount,
    [property: JsonPropertyName("startClaimCount")] int StartClaimCount,
    [property: JsonPropertyName("availabilityCandidateCount")]
        int AvailabilityCandidateCount,
    [property: JsonPropertyName("availabilityClaimCount")]
        int AvailabilityClaimCount,
    [property: JsonPropertyName("unresolvedAvailabilityCount")]
        int UnresolvedAvailabilityCount,
    [property: JsonPropertyName("claimCount")] int ClaimCount,
    [property: JsonPropertyName("unresolvedPlayerCodes")]
        IReadOnlyList<int> UnresolvedPlayerCodes);
