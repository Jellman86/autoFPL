using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record FbrefMatchOpportunityCoverageDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("identityBridgeVersion")]
        string IdentityBridgeVersion,
    [property: JsonPropertyName("readinessStatus")] string ReadinessStatus,
    [property: JsonPropertyName("reviewedPlayerCount")] int ReviewedPlayerCount,
    [property: JsonPropertyName("capturedPlayerLogCount")]
        int CapturedPlayerLogCount,
    [property: JsonPropertyName("missingPlayerLogCount")]
        int MissingPlayerLogCount,
    [property: JsonPropertyName("capturedTeamScheduleCount")]
        int CapturedTeamScheduleCount,
    [property: JsonPropertyName("missingTeamScheduleCount")]
        int MissingTeamScheduleCount,
    [property: JsonPropertyName("sourcePairReadyPlayerCount")]
        int SourcePairReadyPlayerCount,
    [property: JsonPropertyName("teams")]
        IReadOnlyList<FbrefMatchOpportunityTeamCoverageDocument> Teams,
    [property: JsonPropertyName("players")]
        IReadOnlyList<FbrefMatchOpportunityPlayerCoverageDocument> Players);

public sealed record FbrefMatchOpportunityTeamCoverageDocument(
    [property: JsonPropertyName("sourceTeamId")] string SourceTeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("reviewedPlayerCount")] int ReviewedPlayerCount,
    [property: JsonPropertyName("capturedPlayerLogCount")]
        int CapturedPlayerLogCount,
    [property: JsonPropertyName("sourcePairReadyPlayerCount")]
        int SourcePairReadyPlayerCount,
    [property: JsonPropertyName("scheduleCaptureStatus")]
        string ScheduleCaptureStatus,
    [property: JsonPropertyName("teamScheduleSnapshotId")]
        long? TeamScheduleSnapshotId,
    [property: JsonPropertyName("teamScheduleContentSha256")]
        string? TeamScheduleContentSha256);

public sealed record FbrefMatchOpportunityPlayerCoverageDocument(
    [property: JsonPropertyName("sourcePlayerId")] string SourcePlayerId,
    [property: JsonPropertyName("playerName")] string PlayerName,
    [property: JsonPropertyName("sourceTeamId")] string SourceTeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("officialPlayerCode")] int OfficialPlayerCode,
    [property: JsonPropertyName("playerLogCaptureStatus")]
        string PlayerLogCaptureStatus,
    [property: JsonPropertyName("playerMatchLogSnapshotId")]
        long? PlayerMatchLogSnapshotId,
    [property: JsonPropertyName("teamScheduleSnapshotId")]
        long? TeamScheduleSnapshotId,
    [property: JsonPropertyName("sourcePairStatus")] string SourcePairStatus);
