using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record FplFormForecastCaptureDocument(
    [property: JsonPropertyName("captureId")] long CaptureId,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("sourceUrl")] string SourceUrl,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("publishedAtUtc")] DateTimeOffset? PublishedAtUtc,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("contentSha256")] string ContentSha256,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("fixturePredictionCount")] int FixturePredictionCount,
    [property: JsonPropertyName("appearanceProbabilityCount")] int AppearanceProbabilityCount);
