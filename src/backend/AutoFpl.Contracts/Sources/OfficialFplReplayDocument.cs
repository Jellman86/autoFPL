using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record OfficialFplReplayDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("selectedCaptureId")] long SelectedCaptureId,
    [property: JsonPropertyName("captureAvailableAtUtc")] DateTimeOffset CaptureAvailableAtUtc,
    [property: JsonPropertyName("captureLeadTimeSeconds")] long CaptureLeadTimeSeconds,
    [property: JsonPropertyName("bootstrapSha256")] string BootstrapSha256,
    [property: JsonPropertyName("fixturesSha256")] string FixturesSha256,
    [property: JsonPropertyName("teamCount")] int TeamCount,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("gameweekFixtureCount")] int GameweekFixtureCount);
