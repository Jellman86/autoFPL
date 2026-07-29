using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Forecasts;

public sealed record JointScenarioShadowDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("artifactType")] string ArtifactType,
    [property: JsonPropertyName("artifactVersion")] string ArtifactVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isPromoted")] bool IsPromoted,
    [property: JsonPropertyName("influencesAdvice")] bool InfluencesAdvice,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("decisionCutoffUtc")]
        DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("scenarioModelKey")] string ScenarioModelKey,
    [property: JsonPropertyName("appearanceVariant")] string AppearanceVariant,
    [property: JsonPropertyName("pointAvailabilityFusion")]
        string PointAvailabilityFusion,
    [property: JsonPropertyName("scenarioCount")] int ScenarioCount,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("sourceGameweeks")]
        IReadOnlyList<int> SourceGameweeks,
    [property: JsonPropertyName("players")]
        IReadOnlyList<JointScenarioPlayerDocument> Players,
    [property: JsonPropertyName("pointRows")]
        IReadOnlyList<IReadOnlyList<int>> PointRows,
    [property: JsonPropertyName("playedRows")]
        IReadOnlyList<IReadOnlyList<bool>> PlayedRows,
    [property: JsonPropertyName("scenarioContentSha256")]
        string ScenarioContentSha256,
    [property: JsonPropertyName("scenarioDiagnostics")]
        JointScenarioDiagnosticsDocument ScenarioDiagnostics,
    [property: JsonPropertyName("training")]
        JointScenarioTrainingDocument Training,
    [property: JsonPropertyName("distributionStatus")]
        string DistributionStatus,
    [property: JsonPropertyName("limitations")]
        IReadOnlyList<string> Limitations,
    [property: JsonPropertyName("dataIdentitySha256")]
        string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")]
        string RunIdentitySha256,
    [property: JsonPropertyName("scenarioArtifactId")]
        long? ScenarioArtifactId = null,
    [property: JsonPropertyName("scenarioArtifactContentSha256")]
        string? ScenarioArtifactContentSha256 = null);

public sealed record JointScenarioPlayerDocument(
    [property: JsonPropertyName("columnIndex")] int ColumnIndex,
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("playerCode")] int PlayerCode,
    [property: JsonPropertyName("webName")] string WebName,
    [property: JsonPropertyName("teamId")] int TeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("pointMean")] decimal PointMean,
    [property: JsonPropertyName("pointMeanBeforeAvailability")]
        decimal PointMeanBeforeAvailability,
    [property: JsonPropertyName("pointAvailabilityMultiplier")]
        decimal PointAvailabilityMultiplier,
    [property: JsonPropertyName("appearanceProbability")]
        decimal AppearanceProbability,
    [property: JsonPropertyName("officialStatus")] string OfficialStatus,
    [property: JsonPropertyName("officialChanceOfPlayingNextRound")]
        int? OfficialChanceOfPlayingNextRound,
    [property: JsonPropertyName("pointHistoryIdentityStatus")]
        string PointHistoryIdentityStatus,
    [property: JsonPropertyName("participationHistoryIdentityStatus")]
        string ParticipationHistoryIdentityStatus);

public sealed record JointScenarioDiagnosticsDocument(
    [property: JsonPropertyName("meanAbsolutePointMeanDelta")]
        decimal MeanAbsolutePointMeanDelta,
    [property: JsonPropertyName("maximumAbsolutePointMeanDelta")]
        decimal MaximumAbsolutePointMeanDelta,
    [property: JsonPropertyName(
        "meanAbsoluteAppearanceProbabilityDelta")]
        decimal MeanAbsoluteAppearanceProbabilityDelta,
    [property: JsonPropertyName(
        "maximumAbsoluteAppearanceProbabilityDelta")]
        decimal MaximumAbsoluteAppearanceProbabilityDelta,
    [property: JsonPropertyName("nonPlayingNonZeroPointCount")]
        int NonPlayingNonZeroPointCount);

public sealed record JointScenarioTrainingDocument(
    [property: JsonPropertyName("sourceSeasonCode")]
        string SourceSeasonCode,
    [property: JsonPropertyName("sourceHistoricalCaptureId")]
        long SourceHistoricalCaptureId,
    [property: JsonPropertyName("sourcePlayersSha256")]
        string SourcePlayersSha256,
    [property: JsonPropertyName("sourceGameweeksSha256")]
        string SourceGameweeksSha256,
    [property: JsonPropertyName("pointForecastRunIdentitySha256")]
        string PointForecastRunIdentitySha256,
    [property: JsonPropertyName(
        "participationForecastRunIdentitySha256")]
        string ParticipationForecastRunIdentitySha256,
    [property: JsonPropertyName("retrospectiveScreen")]
        JointScenarioScreenDocument RetrospectiveScreen,
    [property: JsonPropertyName("selfDonorAssignments")]
        int SelfDonorAssignments,
    [property: JsonPropertyName("fallbackDonorAssignments")]
        int FallbackDonorAssignments);

public sealed record JointScenarioScreenDocument(
    [property: JsonPropertyName("evaluatorVersion")]
        string EvaluatorVersion,
    [property: JsonPropertyName("dataIdentitySha256")]
        string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")]
        string RunIdentitySha256,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("meanCrps")] decimal MeanCrps,
    [property: JsonPropertyName("aggregateCrpsImprovementFraction")]
        decimal AggregateCrpsImprovementFraction,
    [property: JsonPropertyName("foldWins")] int FoldWins,
    [property: JsonPropertyName("foldCount")] int FoldCount,
    [property: JsonPropertyName("isPromoted")] bool IsPromoted);
