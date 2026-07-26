using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Forecasts;

public sealed record PreseasonPlayerForecastDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("artifactType")] string ArtifactType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("modelKey")] string ModelKey,
    [property: JsonPropertyName("isPromoted")] bool IsPromoted,
    [property: JsonPropertyName("influencesAdvice")] bool InfluencesAdvice,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("decisionCutoffUtc")] DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("training")]
        PreseasonPlayerForecastTrainingDocument Training,
    [property: JsonPropertyName("comparison")]
        PreseasonPlayerForecastComparisonDocument Comparison,
    [property: JsonPropertyName("distributionStatus")] string DistributionStatus,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("officialPlayerCount")] int OfficialPlayerCount,
    [property: JsonPropertyName("ineligiblePlayerCount")] int IneligiblePlayerCount,
    [property: JsonPropertyName("priorSeasonIdentityMatchCount")]
        int PriorSeasonIdentityMatchCount,
    [property: JsonPropertyName("priorSeasonIdentityMissingCount")]
        int PriorSeasonIdentityMissingCount,
    [property: JsonPropertyName("players")]
        IReadOnlyList<PreseasonPlayerForecastPlayerDocument> Players,
    [property: JsonPropertyName("limitations")] IReadOnlyList<string> Limitations,
    [property: JsonPropertyName("dataIdentitySha256")] string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")] string RunIdentitySha256,
    [property: JsonPropertyName("forecastArtifactId")]
        long? ForecastArtifactId = null,
    [property: JsonPropertyName("forecastArtifactContentSha256")]
        string? ForecastArtifactContentSha256 = null);

public sealed record PreseasonPlayerForecastTrainingDocument(
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("historicalCaptureId")] long HistoricalCaptureId,
    [property: JsonPropertyName("trainingGameweekCount")] int TrainingGameweekCount,
    [property: JsonPropertyName("trainingRowCount")] int TrainingRowCount,
    [property: JsonPropertyName("sourceRevision")] string SourceRevision,
    [property: JsonPropertyName("playersSha256")] string PlayersSha256,
    [property: JsonPropertyName("gameweeksSha256")] string GameweeksSha256,
    [property: JsonPropertyName("selectedModel")] string SelectedModel,
    [property: JsonPropertyName("modelConfiguration")] JsonElement ModelConfiguration,
    [property: JsonPropertyName("modelDiagnostics")] JsonElement ModelDiagnostics,
    [property: JsonPropertyName("evaluationDataIdentitySha256")]
        string EvaluationDataIdentitySha256,
    [property: JsonPropertyName("evaluationRunIdentitySha256")]
        string EvaluationRunIdentitySha256);

public sealed record PreseasonPlayerForecastComparisonDocument(
    [property: JsonPropertyName("baselineModelKey")] string BaselineModelKey,
    [property: JsonPropertyName("selectionMetric")] string SelectionMetric,
    [property: JsonPropertyName("lockedHoldoutMaeImprovementFraction")]
        decimal LockedHoldoutMaeImprovementFraction,
    [property: JsonPropertyName("baselineStillDrivesAdvice")]
        bool BaselineStillDrivesAdvice);

public sealed record PreseasonPlayerForecastPlayerDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("playerCode")] int PlayerCode,
    [property: JsonPropertyName("webName")] string WebName,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("teamId")] int TeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("officialStatus")] string OfficialStatus,
    [property: JsonPropertyName("officialChanceOfPlayingNextRound")]
        int? OfficialChanceOfPlayingNextRound,
    [property: JsonPropertyName("availabilityStatus")] string AvailabilityStatus,
    [property: JsonPropertyName("priorSeasonIdentityStatus")]
        string PriorSeasonIdentityStatus,
    [property: JsonPropertyName("priorSeasonGameweekCount")]
        int PriorSeasonGameweekCount,
    [property: JsonPropertyName("expectedPoints")] decimal ExpectedPoints,
    [property: JsonPropertyName("baselineV0ExpectedPoints")]
        decimal BaselineV0ExpectedPoints,
    [property: JsonPropertyName("differenceFromBaselineV0")]
        decimal DifferenceFromBaselineV0);
