using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Forecasts;

public sealed record MultiSeasonPlayerForecastReadinessDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reasonCode")] string ReasonCode,
    [property: JsonPropertyName("influencesAdvice")] bool InfluencesAdvice,
    [property: JsonPropertyName("latestOfficialCapture")]
        MultiSeasonPlayerForecastOfficialTargetDocument? LatestOfficialCapture,
    [property: JsonPropertyName("latestShadow")]
        MultiSeasonPlayerForecastArtifactIdentityDocument? LatestShadow);

public sealed record MultiSeasonPlayerForecastOfficialTargetDocument(
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int? Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset? DeadlineUtc,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc);

public sealed record MultiSeasonPlayerForecastArtifactIdentityDocument(
    [property: JsonPropertyName("forecastArtifactId")] long ForecastArtifactId,
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("decisionCutoffUtc")]
        DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("forecastArtifactContentSha256")]
        string ForecastArtifactContentSha256);
