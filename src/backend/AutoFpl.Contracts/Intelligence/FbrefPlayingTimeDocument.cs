using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record FbrefPlayingTimeDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("snapshotId")] long SnapshotId,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("extractionVersion")] string ExtractionVersion,
    [property: JsonPropertyName("identityBridgeVersion")]
        string? IdentityBridgeVersion,
    [property: JsonPropertyName("contentSha256")] string ContentSha256,
    [property: JsonPropertyName("identityCaptureId")] long IdentityCaptureId,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("competition")] string Competition,
    [property: JsonPropertyName("competitionSeason")] string CompetitionSeason,
    [property: JsonPropertyName("rowCount")] int RowCount,
    [property: JsonPropertyName("sourcePlayerCount")] int SourcePlayerCount,
    [property: JsonPropertyName("exactCurrentTeamMatchCount")]
        int ExactCurrentTeamMatchCount,
    [property: JsonPropertyName("reviewedIdentityCount")]
        int ReviewedIdentityCount,
    [property: JsonPropertyName("exactCurrentTeamProposalCount")]
        int ExactCurrentTeamProposalCount,
    [property: JsonPropertyName("players")]
        IReadOnlyList<FbrefPlayingTimePlayerDocument> Players,
    [property: JsonPropertyName("currentTeamCoverage")]
        IReadOnlyList<FbrefPlayingTimeTeamCoverageDocument> CurrentTeamCoverage);

public sealed record FbrefPlayingTimePlayerDocument(
    [property: JsonPropertyName("sourcePlayerId")] string SourcePlayerId,
    [property: JsonPropertyName("playerName")] string PlayerName,
    [property: JsonPropertyName("sourceTeamId")] string SourceTeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("appearances")] int Appearances,
    [property: JsonPropertyName("starts")] int Starts,
    [property: JsonPropertyName("minutes")] int Minutes,
    [property: JsonPropertyName("matchLogsUrl")] string MatchLogsUrl,
    [property: JsonPropertyName("identityStatus")] string IdentityStatus,
    [property: JsonPropertyName("officialPlayerId")] int? OfficialPlayerId,
    [property: JsonPropertyName("officialPlayerCode")] int? OfficialPlayerCode);

public sealed record FbrefPlayingTimeTeamCoverageDocument(
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("officialPlayerCount")] int OfficialPlayerCount,
    [property: JsonPropertyName("sourceRowCount")] int SourceRowCount,
    [property: JsonPropertyName("exactMatchCount")] int ExactMatchCount,
    [property: JsonPropertyName("reviewedMatchCount")] int ReviewedMatchCount,
    [property: JsonPropertyName("exactProposalCount")] int ExactProposalCount,
    [property: JsonPropertyName("unmatchedOfficialPlayerCodes")]
        IReadOnlyList<int> UnmatchedOfficialPlayerCodes,
    [property: JsonPropertyName("unmatchedSourcePlayerIds")]
        IReadOnlyList<string> UnmatchedSourcePlayerIds);
