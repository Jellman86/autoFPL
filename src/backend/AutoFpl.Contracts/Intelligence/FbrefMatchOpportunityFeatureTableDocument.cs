using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record FbrefMatchOpportunityFeatureTableDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("featureVersion")] string FeatureVersion,
    [property: JsonPropertyName("identityBridgeVersion")]
        string IdentityBridgeVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("researchStatus")] string ResearchStatus,
    [property: JsonPropertyName("influencesForecast")] bool InfluencesForecast,
    [property: JsonPropertyName("target")]
        FbrefMatchOpportunityFeatureTargetDocument Target,
    [property: JsonPropertyName("reviewedPlayerCount")] int ReviewedPlayerCount,
    [property: JsonPropertyName("featureReadyPlayerCount")]
        int FeatureReadyPlayerCount,
    [property: JsonPropertyName("missingSourcePairPlayerCount")]
        int MissingSourcePairPlayerCount,
    [property: JsonPropertyName("players")]
        IReadOnlyList<FbrefMatchOpportunityPlayerFeatureDocument> Players);

public sealed record FbrefMatchOpportunityFeatureTargetDocument(
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("availableAtUtc")]
        DateTimeOffset AvailableAtUtc);

public sealed record FbrefMatchOpportunityPlayerFeatureDocument(
    [property: JsonPropertyName("officialPlayerId")] int OfficialPlayerId,
    [property: JsonPropertyName("officialPlayerCode")] int OfficialPlayerCode,
    [property: JsonPropertyName("playerName")] string PlayerName,
    [property: JsonPropertyName("currentTeamName")] string CurrentTeamName,
    [property: JsonPropertyName("sourcePlayerId")] string SourcePlayerId,
    [property: JsonPropertyName("sourceTeamId")] string SourceTeamId,
    [property: JsonPropertyName("sourceTeamName")] string SourceTeamName,
    [property: JsonPropertyName("featureStatus")] string FeatureStatus,
    [property: JsonPropertyName("playerMatchLogSnapshotId")]
        long? PlayerMatchLogSnapshotId,
    [property: JsonPropertyName("playerMatchLogContentSha256")]
        string? PlayerMatchLogContentSha256,
    [property: JsonPropertyName("teamScheduleSnapshotId")]
        long? TeamScheduleSnapshotId,
    [property: JsonPropertyName("teamScheduleContentSha256")]
        string? TeamScheduleContentSha256,
    [property: JsonPropertyName("availableAtUtc")]
        DateTimeOffset? AvailableAtUtc,
    [property: JsonPropertyName("season")]
        FbrefMatchOpportunityFeatureWindowDocument? Season,
    [property: JsonPropertyName("rollingWindows")]
        IReadOnlyList<FbrefMatchOpportunityFeatureWindowDocument>
            RollingWindows);

public sealed record FbrefMatchOpportunityFeatureWindowDocument(
    [property: JsonPropertyName("windowSize")] int? WindowSize,
    [property: JsonPropertyName("windowStatus")] string WindowStatus,
    [property: JsonPropertyName("scheduledMatchCount")]
        int ScheduledMatchCount,
    [property: JsonPropertyName("observedPlayerRowCount")]
        int ObservedPlayerRowCount,
    [property: JsonPropertyName("appearanceCount")] int AppearanceCount,
    [property: JsonPropertyName("startCount")] int StartCount,
    [property: JsonPropertyName("unusedBenchCount")] int UnusedBenchCount,
    [property: JsonPropertyName("noPlayerRowCount")] int NoPlayerRowCount,
    [property: JsonPropertyName("observedMinutes")] int ObservedMinutes,
    [property: JsonPropertyName("observedGoals")] int ObservedGoals,
    [property: JsonPropertyName("observedAssists")] int ObservedAssists,
    [property: JsonPropertyName("observedYellowCards")]
        int ObservedYellowCards,
    [property: JsonPropertyName("observedRedCards")] int ObservedRedCards);
