using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record FbrefTeamScheduleDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("snapshotId")] long SnapshotId,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("extractionVersion")] string ExtractionVersion,
    [property: JsonPropertyName("contentSha256")] string ContentSha256,
    [property: JsonPropertyName("identityCaptureId")] long IdentityCaptureId,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("sourceTeamId")] string SourceTeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("competition")] string Competition,
    [property: JsonPropertyName("competitionSeason")] string CompetitionSeason,
    [property: JsonPropertyName("matchCount")] int MatchCount,
    [property: JsonPropertyName("matches")]
        IReadOnlyList<FbrefTeamScheduleRowDocument> Matches);

public sealed record FbrefTeamScheduleRowDocument(
    [property: JsonPropertyName("sourceMatchId")] string SourceMatchId,
    [property: JsonPropertyName("matchDate")] DateOnly MatchDate,
    [property: JsonPropertyName("kickoffUtc")] DateTimeOffset KickoffUtc,
    [property: JsonPropertyName("round")] string Round,
    [property: JsonPropertyName("venue")] string Venue,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("goalsFor")] int GoalsFor,
    [property: JsonPropertyName("goalsAgainst")] int GoalsAgainst,
    [property: JsonPropertyName("sourceOpponentId")] string SourceOpponentId,
    [property: JsonPropertyName("opponentName")] string OpponentName);
