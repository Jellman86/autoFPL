using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Selections;

public sealed record SelectionScenarioScoreShadowDocument(
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
    [property: JsonPropertyName("engineVersion")] string EngineVersion,
    [property: JsonPropertyName("scenarioCount")] int ScenarioCount,
    [property: JsonPropertyName("scenarioSource")]
        SelectionScenarioSourceDocument ScenarioSource,
    [property: JsonPropertyName("modelSource")]
        SelectionScenarioModelSourceDocument ModelSource,
    [property: JsonPropertyName("userSource")]
        SelectionScenarioUserSourceDocument UserSource,
    [property: JsonPropertyName("model")]
        SelectionScenarioResultDocument Model,
    [property: JsonPropertyName("user")]
        SelectionScenarioResultDocument? User,
    [property: JsonPropertyName("userVsModel")]
        SelectionScenarioComparisonDocument? UserVsModel,
    [property: JsonPropertyName("limitations")]
        IReadOnlyList<string> Limitations,
    [property: JsonPropertyName("dataIdentitySha256")]
        string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")]
        string RunIdentitySha256,
    [property: JsonPropertyName("scoreArtifactId")]
        long? ScoreArtifactId = null,
    [property: JsonPropertyName("scoreArtifactContentSha256")]
        string? ScoreArtifactContentSha256 = null);

public sealed record SelectionScenarioSourceDocument(
    [property: JsonPropertyName("scenarioArtifactId")]
        long ScenarioArtifactId,
    [property: JsonPropertyName("scenarioArtifactContentSha256")]
        string ScenarioArtifactContentSha256,
    [property: JsonPropertyName("scenarioContentSha256")]
        string ScenarioContentSha256,
    [property: JsonPropertyName("scenarioRunIdentitySha256")]
        string ScenarioRunIdentitySha256);

public sealed record SelectionScenarioModelSourceDocument(
    [property: JsonPropertyName("forecastArtifactId")]
        long ForecastArtifactId,
    [property: JsonPropertyName("forecastArtifactContentSha256")]
        string ForecastArtifactContentSha256,
    [property: JsonPropertyName("modelLabel")] string ModelLabel,
    [property: JsonPropertyName("selectionContentSha256")]
        string SelectionContentSha256);

public sealed record SelectionScenarioUserSourceDocument(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("selectionRevisionId")]
        long? SelectionRevisionId,
    [property: JsonPropertyName("revision")] int? Revision,
    [property: JsonPropertyName("selectionContentSha256")]
        string? SelectionContentSha256,
    [property: JsonPropertyName("lockedAtUtc")]
        DateTimeOffset? LockedAtUtc);

public sealed record SelectionScenarioResultDocument(
    [property: JsonPropertyName("selection")]
        SelectionScenarioDefinitionDocument Selection,
    [property: JsonPropertyName("summary")]
        SelectionScenarioSummaryDocument Summary,
    [property: JsonPropertyName("totalPointRows")]
        IReadOnlyList<int> TotalPointRows,
    [property: JsonPropertyName("captainBonusPointRows")]
        IReadOnlyList<int> CaptainBonusPointRows);

public sealed record SelectionScenarioDefinitionDocument(
    [property: JsonPropertyName("playerIds")]
        IReadOnlyList<int> PlayerIds,
    [property: JsonPropertyName("positions")]
        IReadOnlyList<string> Positions,
    [property: JsonPropertyName("startingPlayerIds")]
        IReadOnlyList<int> StartingPlayerIds,
    [property: JsonPropertyName("replacementGoalkeeperPlayerId")]
        int ReplacementGoalkeeperPlayerId,
    [property: JsonPropertyName("outfieldSubstitutePlayerIds")]
        IReadOnlyList<int> OutfieldSubstitutePlayerIds,
    [property: JsonPropertyName("captainPlayerId")]
        int CaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")]
        int ViceCaptainPlayerId);

public sealed record SelectionScenarioSummaryDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("engineVersion")] string EngineVersion,
    [property: JsonPropertyName("scenarioCount")] int ScenarioCount,
    [property: JsonPropertyName("meanPoints")] decimal MeanPoints,
    [property: JsonPropertyName("standardDeviationPoints")]
        decimal StandardDeviationPoints,
    [property: JsonPropertyName("minimumPoints")] int MinimumPoints,
    [property: JsonPropertyName("p10Points")] decimal P10Points,
    [property: JsonPropertyName("medianPoints")] decimal MedianPoints,
    [property: JsonPropertyName("p90Points")] decimal P90Points,
    [property: JsonPropertyName("maximumPoints")] int MaximumPoints,
    [property: JsonPropertyName("meanActivatedSubstitutes")]
        decimal MeanActivatedSubstitutes,
    [property: JsonPropertyName("probabilityOfUnreplacedStarter")]
        decimal ProbabilityOfUnreplacedStarter);

public sealed record SelectionScenarioComparisonDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("engineVersion")] string EngineVersion,
    [property: JsonPropertyName("scenarioCount")] int ScenarioCount,
    [property: JsonPropertyName("meanPointsDelta")]
        decimal MeanPointsDelta,
    [property: JsonPropertyName("p10PointsDelta")]
        decimal P10PointsDelta,
    [property: JsonPropertyName("medianPointsDelta")]
        decimal MedianPointsDelta,
    [property: JsonPropertyName("p90PointsDelta")]
        decimal P90PointsDelta,
    [property: JsonPropertyName("probabilityCandidateWins")]
        decimal ProbabilityCandidateWins,
    [property: JsonPropertyName("probabilityTie")]
        decimal ProbabilityTie,
    [property: JsonPropertyName("probabilityCandidateLoses")]
        decimal ProbabilityCandidateLoses);
