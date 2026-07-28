using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record FbrefPlayerMatchOpportunityDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("extractionVersion")] string ExtractionVersion,
    [property: JsonPropertyName("identityBridgeVersion")]
        string IdentityBridgeVersion,
    [property: JsonPropertyName("playerMatchLogSnapshotId")]
        long PlayerMatchLogSnapshotId,
    [property: JsonPropertyName("playerMatchLogContentSha256")]
        string PlayerMatchLogContentSha256,
    [property: JsonPropertyName("playerMatchLogRetrievedAtUtc")]
        DateTimeOffset PlayerMatchLogRetrievedAtUtc,
    [property: JsonPropertyName("teamScheduleSnapshotId")]
        long TeamScheduleSnapshotId,
    [property: JsonPropertyName("teamScheduleContentSha256")]
        string TeamScheduleContentSha256,
    [property: JsonPropertyName("teamScheduleRetrievedAtUtc")]
        DateTimeOffset TeamScheduleRetrievedAtUtc,
    [property: JsonPropertyName("availableAtUtc")]
        DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("sourcePlayerId")] string SourcePlayerId,
    [property: JsonPropertyName("playerName")] string PlayerName,
    [property: JsonPropertyName("officialPlayerId")] int OfficialPlayerId,
    [property: JsonPropertyName("officialPlayerCode")] int OfficialPlayerCode,
    [property: JsonPropertyName("sourceTeamId")] string SourceTeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("competition")] string Competition,
    [property: JsonPropertyName("competitionSeason")] string CompetitionSeason,
    [property: JsonPropertyName("scheduledMatchCount")]
        int ScheduledMatchCount,
    [property: JsonPropertyName("observedPlayerRowCount")]
        int ObservedPlayerRowCount,
    [property: JsonPropertyName("excludedOtherCompetitionRowCount")]
        int ExcludedOtherCompetitionRowCount,
    [property: JsonPropertyName("noPlayerRowCount")] int NoPlayerRowCount,
    [property: JsonPropertyName("opportunities")]
        IReadOnlyList<FbrefPlayerMatchOpportunityRowDocument> Opportunities,
    [property: JsonPropertyName("rollingWindows")]
        IReadOnlyList<FbrefPlayerMatchOpportunityWindowDocument> RollingWindows);

public sealed record FbrefPlayerMatchOpportunityRowDocument(
    [property: JsonPropertyName("sourceMatchId")] string SourceMatchId,
    [property: JsonPropertyName("matchDate")] DateOnly MatchDate,
    [property: JsonPropertyName("kickoffUtc")] DateTimeOffset KickoffUtc,
    [property: JsonPropertyName("round")] string Round,
    [property: JsonPropertyName("venue")] string Venue,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("sourceOpponentId")] string SourceOpponentId,
    [property: JsonPropertyName("opponentName")] string OpponentName,
    [property: JsonPropertyName("playerEvidenceStatus")]
        string PlayerEvidenceStatus,
    [property: JsonPropertyName("observedRangeStatus")]
        string ObservedRangeStatus,
    [property: JsonPropertyName("started")] bool? Started,
    [property: JsonPropertyName("minutes")] int? Minutes,
    [property: JsonPropertyName("goals")] int? Goals,
    [property: JsonPropertyName("assists")] int? Assists,
    [property: JsonPropertyName("yellowCards")] int? YellowCards,
    [property: JsonPropertyName("redCards")] int? RedCards);

public sealed record FbrefPlayerMatchOpportunityWindowDocument(
    [property: JsonPropertyName("windowSize")] int WindowSize,
    [property: JsonPropertyName("windowStatus")] string WindowStatus,
    [property: JsonPropertyName("firstMatchDate")] DateOnly FirstMatchDate,
    [property: JsonPropertyName("lastMatchDate")] DateOnly LastMatchDate,
    [property: JsonPropertyName("scheduledMatchCount")]
        int ScheduledMatchCount,
    [property: JsonPropertyName("observedPlayerRowCount")]
        int ObservedPlayerRowCount,
    [property: JsonPropertyName("appearanceCount")] int AppearanceCount,
    [property: JsonPropertyName("startCount")] int StartCount,
    [property: JsonPropertyName("unusedBenchCount")] int UnusedBenchCount,
    [property: JsonPropertyName("noPlayerRowCount")] int NoPlayerRowCount,
    [property: JsonPropertyName("observedMinutes")] int ObservedMinutes);
