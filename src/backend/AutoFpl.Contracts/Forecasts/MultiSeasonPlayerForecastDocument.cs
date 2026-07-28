using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Forecasts;

public sealed record MultiSeasonPlayerForecastDocument(
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
        MultiSeasonPlayerForecastTrainingDocument Training,
    [property: JsonPropertyName("comparison")]
        MultiSeasonPlayerForecastComparisonDocument Comparison,
    [property: JsonPropertyName("distributionStatus")] string DistributionStatus,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("officialPlayerCount")] int OfficialPlayerCount,
    [property: JsonPropertyName("ineligiblePlayerCount")] int IneligiblePlayerCount,
    [property: JsonPropertyName("historicalIdentityCounts")]
        IReadOnlyDictionary<string, int> HistoricalIdentityCounts,
    [property: JsonPropertyName("players")]
        IReadOnlyList<MultiSeasonPlayerForecastPlayerDocument> Players,
    [property: JsonPropertyName("limitations")] IReadOnlyList<string> Limitations,
    [property: JsonPropertyName("dataIdentitySha256")] string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")] string RunIdentitySha256,
    [property: JsonPropertyName("forecastArtifactId")]
        long? ForecastArtifactId = null,
    [property: JsonPropertyName("forecastArtifactContentSha256")]
        string? ForecastArtifactContentSha256 = null);

public sealed record MultiSeasonPlayerForecastTrainingDocument(
    [property: JsonPropertyName("seasonCodes")]
        IReadOnlyList<string> SeasonCodes,
    [property: JsonPropertyName("historicalCaptures")]
        IReadOnlyList<MultiSeasonPlayerForecastCaptureDocument> HistoricalCaptures,
    [property: JsonPropertyName("trainingOriginCount")] int TrainingOriginCount,
    [property: JsonPropertyName("trainingRowCount")] int TrainingRowCount,
    [property: JsonPropertyName("selectedModel")] string SelectedModel,
    [property: JsonPropertyName("modelConfiguration")] JsonElement ModelConfiguration,
    [property: JsonPropertyName("modelDiagnostics")] JsonElement ModelDiagnostics,
    [property: JsonPropertyName("evaluationRunIdentitySha256")]
        string EvaluationRunIdentitySha256);

public sealed record MultiSeasonPlayerForecastCaptureDocument(
    [property: JsonPropertyName("captureId")] long CaptureId,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("sourceRevision")] string SourceRevision,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("playersSha256")] string PlayersSha256,
    [property: JsonPropertyName("gameweeksSha256")] string GameweeksSha256,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("playerGameweekCount")] int PlayerGameweekCount,
    [property: JsonPropertyName("stableCodeCount")] int StableCodeCount);

public sealed record MultiSeasonPlayerForecastComparisonDocument(
    [property: JsonPropertyName("baselineModelKey")] string BaselineModelKey,
    [property: JsonPropertyName("retrospectiveMaeImprovementOverBaselineFraction")]
        decimal RetrospectiveMaeImprovementOverBaselineFraction,
    [property: JsonPropertyName("matchedCurrentSeasonTreeMaeImprovementFraction")]
        decimal MatchedCurrentSeasonTreeMaeImprovementFraction,
    [property: JsonPropertyName("matchedCurrentSeasonTreeFoldWins")]
        int MatchedCurrentSeasonTreeFoldWins,
    [property: JsonPropertyName("matchedCurrentSeasonTreeFoldCount")]
        int MatchedCurrentSeasonTreeFoldCount,
    [property: JsonPropertyName("allPositionMaeNonWorse")]
        bool AllPositionMaeNonWorse,
    [property: JsonPropertyName("baselineStillDrivesAdvice")]
        bool BaselineStillDrivesAdvice);

public sealed record MultiSeasonPlayerForecastPlayerDocument(
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
    [property: JsonPropertyName("historicalIdentityStatus")]
        string HistoricalIdentityStatus,
    [property: JsonPropertyName("historicalSeasonCodes")]
        IReadOnlyList<string> HistoricalSeasonCodes,
    [property: JsonPropertyName("historicalGameweekCount")]
        int HistoricalGameweekCount,
    [property: JsonPropertyName("expectedPoints")] decimal ExpectedPoints,
    [property: JsonPropertyName("baselineV0ExpectedPoints")]
        decimal BaselineV0ExpectedPoints,
    [property: JsonPropertyName("differenceFromBaselineV0")]
        decimal DifferenceFromBaselineV0);
