using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record FbrefPlayerMatchLogDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("snapshotId")] long SnapshotId,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("extractionVersion")] string ExtractionVersion,
    [property: JsonPropertyName("identityBridgeVersion")]
        string IdentityBridgeVersion,
    [property: JsonPropertyName("contentSha256")] string ContentSha256,
    [property: JsonPropertyName("identityCaptureId")] long IdentityCaptureId,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("sourcePlayerId")] string SourcePlayerId,
    [property: JsonPropertyName("playerName")] string PlayerName,
    [property: JsonPropertyName("sourceTeamId")] string SourceTeamId,
    [property: JsonPropertyName("sourceTeamName")] string SourceTeamName,
    [property: JsonPropertyName("officialPlayerId")] int OfficialPlayerId,
    [property: JsonPropertyName("officialPlayerCode")] int OfficialPlayerCode,
    [property: JsonPropertyName("currentTeamName")] string CurrentTeamName,
    [property: JsonPropertyName("competitionSeason")] string CompetitionSeason,
    [property: JsonPropertyName("aggregateReconciliationStatus")]
        string AggregateReconciliationStatus,
    [property: JsonPropertyName("aggregateAppearanceCount")]
        int AggregateAppearanceCount,
    [property: JsonPropertyName("aggregateStartCount")] int AggregateStartCount,
    [property: JsonPropertyName("aggregateMinutes")] int AggregateMinutes,
    [property: JsonPropertyName("matchCount")] int MatchCount,
    [property: JsonPropertyName("appearanceCount")] int AppearanceCount,
    [property: JsonPropertyName("startCount")] int StartCount,
    [property: JsonPropertyName("minutes")] int Minutes,
    [property: JsonPropertyName("matches")]
        IReadOnlyList<FbrefPlayerMatchLogRowDocument> Matches);

public sealed record FbrefPlayerMatchLogRowDocument(
    [property: JsonPropertyName("sourceMatchId")] string SourceMatchId,
    [property: JsonPropertyName("matchDate")] DateOnly MatchDate,
    [property: JsonPropertyName("competition")] string Competition,
    [property: JsonPropertyName("round")] string Round,
    [property: JsonPropertyName("venue")] string Venue,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("sourceTeamId")] string SourceTeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("sourceOpponentId")] string SourceOpponentId,
    [property: JsonPropertyName("opponentName")] string OpponentName,
    [property: JsonPropertyName("started")] bool Started,
    [property: JsonPropertyName("minutes")] int Minutes,
    [property: JsonPropertyName("goals")] int Goals,
    [property: JsonPropertyName("assists")] int Assists,
    [property: JsonPropertyName("yellowCards")] int YellowCards,
    [property: JsonPropertyName("redCards")] int RedCards);
