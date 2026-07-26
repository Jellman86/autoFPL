using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record OfficialFplOutcomeCaptureDocument(
    [property: JsonPropertyName("outcomeCaptureId")] long OutcomeCaptureId,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("referenceCaptureId")] long ReferenceCaptureId,
    [property: JsonPropertyName("liveUrl")] string LiveUrl,
    [property: JsonPropertyName("publishedAtUtc")] DateTimeOffset? PublishedAtUtc,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("liveSha256")] string LiveSha256,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("gameweekFixtureCount")] int GameweekFixtureCount);
