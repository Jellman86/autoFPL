using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record OfficialFplCaptureDocument(
    [property: JsonPropertyName("captureId")] long CaptureId,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("bootstrapUrl")] string BootstrapUrl,
    [property: JsonPropertyName("fixturesUrl")] string FixturesUrl,
    [property: JsonPropertyName("publishedAtUtc")] DateTimeOffset? PublishedAtUtc,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("bootstrapSha256")] string BootstrapSha256,
    [property: JsonPropertyName("fixturesSha256")] string FixturesSha256,
    [property: JsonPropertyName("eventCount")] int EventCount,
    [property: JsonPropertyName("teamCount")] int TeamCount,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("fixtureCount")] int FixtureCount,
    [property: JsonPropertyName("nextGameweekNumber")] int? NextGameweekNumber,
    [property: JsonPropertyName("nextDeadlineUtc")] DateTimeOffset? NextDeadlineUtc,
    [property: JsonPropertyName("latestCompletedGameweek")] int? LatestCompletedGameweek);
