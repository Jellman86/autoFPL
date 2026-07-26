using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Forecasts;

public sealed record PlayerGameweekForecastDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("modelKey")] string ModelKey,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("decisionCutoffUtc")] DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("distributionStatus")] string DistributionStatus,
    [property: JsonPropertyName("players")]
        IReadOnlyList<PlayerGameweekForecastPlayerDocument> Players,
    [property: JsonPropertyName("limitations")] IReadOnlyList<string> Limitations,
    [property: JsonPropertyName("forecastArtifactId")] long? ForecastArtifactId = null,
    [property: JsonPropertyName("forecastArtifactContentSha256")]
        string? ForecastArtifactContentSha256 = null);

public sealed record PlayerGameweekForecastPlayerDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("clubShortName")] string ClubShortName,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("priceTenths")] int PriceTenths,
    [property: JsonPropertyName("officialStatus")] string OfficialStatus,
    [property: JsonPropertyName("officialChanceOfPlayingNextRound")]
        int? OfficialChanceOfPlayingNextRound,
    [property: JsonPropertyName("fixtureCount")] int FixtureCount,
    [property: JsonPropertyName("opponent")] string Opponent,
    [property: JsonPropertyName("isHome")] bool IsHome,
    [property: JsonPropertyName("expectedPoints")] decimal ExpectedPoints,
    [property: JsonPropertyName("lower80")] decimal Lower80,
    [property: JsonPropertyName("upper80")] decimal Upper80,
    [property: JsonPropertyName("expectedMinutes")] int ExpectedMinutes,
    [property: JsonPropertyName("startProbability")] decimal? StartProbability,
    [property: JsonPropertyName("sixtyMinuteProbability")] decimal? SixtyMinuteProbability,
    [property: JsonPropertyName("reasons")] IReadOnlyList<string> Reasons,
    [property: JsonPropertyName("risks")] IReadOnlyList<string> Risks,
    [property: JsonPropertyName("photoUrl")] string? PhotoUrl,
    [property: JsonPropertyName("dossierPath")] string DossierPath);
