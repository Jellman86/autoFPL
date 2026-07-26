using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record HistoricalFplSeasonCaptureDocument(
    [property: JsonPropertyName("captureId")] long CaptureId,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("sourceRevision")] string SourceRevision,
    [property: JsonPropertyName("playersUrl")] string PlayersUrl,
    [property: JsonPropertyName("gameweeksUrl")] string GameweeksUrl,
    [property: JsonPropertyName("publishedAtUtc")] DateTimeOffset PublishedAtUtc,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("playersSha256")] string PlayersSha256,
    [property: JsonPropertyName("gameweeksSha256")] string GameweeksSha256,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("playerGameweekCount")] int PlayerGameweekCount,
    [property: JsonPropertyName("stableCodeCount")] int StableCodeCount);
