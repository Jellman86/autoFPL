using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Forecasts;

public sealed record JointScenarioReadinessDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reasonCode")] string ReasonCode,
    [property: JsonPropertyName("influencesAdvice")] bool InfluencesAdvice,
    [property: JsonPropertyName("latestOfficialCapture")]
        JointScenarioOfficialTargetDocument? LatestOfficialCapture,
    [property: JsonPropertyName("latestScenario")]
        JointScenarioArtifactIdentityDocument? LatestScenario);

public sealed record JointScenarioOfficialTargetDocument(
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int? Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset? DeadlineUtc,
    [property: JsonPropertyName("availableAtUtc")]
        DateTimeOffset AvailableAtUtc);

public sealed record JointScenarioArtifactIdentityDocument(
    [property: JsonPropertyName("scenarioArtifactId")] long ScenarioArtifactId,
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("decisionCutoffUtc")]
        DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("scenarioCount")] int ScenarioCount,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("scenarioContentSha256")]
        string ScenarioContentSha256,
    [property: JsonPropertyName("scenarioArtifactContentSha256")]
        string ScenarioArtifactContentSha256);
