using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record FbrefMatchLogCoverageDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("identityBridgeVersion")]
        string IdentityBridgeVersion,
    [property: JsonPropertyName("playingTimeSnapshotId")]
        long PlayingTimeSnapshotId,
    [property: JsonPropertyName("reviewedPlayerCount")] int ReviewedPlayerCount,
    [property: JsonPropertyName("capturedPlayerCount")] int CapturedPlayerCount,
    [property: JsonPropertyName("missingPlayerCount")] int MissingPlayerCount,
    [property: JsonPropertyName("teams")]
        IReadOnlyList<FbrefMatchLogTeamCoverageDocument> Teams,
    [property: JsonPropertyName("players")]
        IReadOnlyList<FbrefMatchLogPlayerCoverageDocument> Players);

public sealed record FbrefMatchLogTeamCoverageDocument(
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("reviewedPlayerCount")] int ReviewedPlayerCount,
    [property: JsonPropertyName("capturedPlayerCount")] int CapturedPlayerCount,
    [property: JsonPropertyName("missingPlayerCount")] int MissingPlayerCount);

public sealed record FbrefMatchLogPlayerCoverageDocument(
    [property: JsonPropertyName("sourcePlayerId")] string SourcePlayerId,
    [property: JsonPropertyName("playerName")] string PlayerName,
    [property: JsonPropertyName("sourceTeamId")] string SourceTeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("officialPlayerId")] int OfficialPlayerId,
    [property: JsonPropertyName("officialPlayerCode")] int OfficialPlayerCode,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("captureStatus")] string CaptureStatus,
    [property: JsonPropertyName("snapshotId")] long? SnapshotId,
    [property: JsonPropertyName("sourceRevision")] int? SourceRevision,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset? RetrievedAtUtc,
    [property: JsonPropertyName("contentSha256")] string? ContentSha256);

public sealed record FbrefMatchLogBatchCaptureDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("identityBridgeVersion")]
        string IdentityBridgeVersion,
    [property: JsonPropertyName("requestedCaptureLimit")]
        int RequestedCaptureLimit,
    [property: JsonPropertyName("reviewedPlayerCount")] int ReviewedPlayerCount,
    [property: JsonPropertyName("alreadyCapturedCount")]
        int AlreadyCapturedCount,
    [property: JsonPropertyName("attemptedCount")] int AttemptedCount,
    [property: JsonPropertyName("capturedCount")] int CapturedCount,
    [property: JsonPropertyName("failedCount")] int FailedCount,
    [property: JsonPropertyName("remainingCount")] int RemainingCount,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("results")]
        IReadOnlyList<FbrefMatchLogBatchCaptureResultDocument> Results);

public sealed record FbrefMatchLogBatchCaptureResultDocument(
    [property: JsonPropertyName("sourcePlayerId")] string SourcePlayerId,
    [property: JsonPropertyName("playerName")] string PlayerName,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("officialPlayerCode")] int OfficialPlayerCode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("snapshotId")] long? SnapshotId,
    [property: JsonPropertyName("failureCode")] string? FailureCode);
