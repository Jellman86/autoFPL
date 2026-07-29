using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Forecasts;

public sealed record SelectedOpeningSquadShadowDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("artifactType")] string ArtifactType,
    [property: JsonPropertyName("artifactVersion")] string ArtifactVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isPromoted")] bool IsPromoted,
    [property: JsonPropertyName("influencesAdvice")] bool InfluencesAdvice,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("openingGameweek")] int OpeningGameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("decisionCutoffUtc")]
        DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("candidatePoolCount")] int CandidatePoolCount,
    [property: JsonPropertyName("scenarioCount")] int ScenarioCount,
    [property: JsonPropertyName("selectedPolicy")]
        SelectedOpeningPolicyDocument SelectedPolicy,
    [property: JsonPropertyName("selection")]
        SelectedOpeningSelectionDocument Selection,
    [property: JsonPropertyName("prospectiveScoreRegistration")]
        SelectedOpeningScoreRegistrationDocument
            ProspectiveScoreRegistration,
    [property: JsonPropertyName("preseasonScenarioScore")]
        SelectedOpeningPreseasonScoreDocument PreseasonScenarioScore,
    [property: JsonPropertyName("source")]
        SelectedOpeningSourceDocument Source,
    [property: JsonPropertyName("limitations")]
        IReadOnlyList<string> Limitations,
    [property: JsonPropertyName("dataIdentitySha256")]
        string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")]
        string RunIdentitySha256,
    [property: JsonPropertyName("selectedOpeningSquadArtifactId")]
        long? SelectedOpeningSquadArtifactId = null,
    [property: JsonPropertyName(
        "selectedOpeningSquadArtifactContentSha256")]
        string? SelectedOpeningSquadArtifactContentSha256 = null);

public sealed record SelectedOpeningPolicyDocument(
    [property: JsonPropertyName("evaluationPolicyKey")]
        string EvaluationPolicyKey,
    [property: JsonPropertyName("horizonGameweeks")]
        int HorizonGameweeks,
    [property: JsonPropertyName("optimizerPolicyKey")]
        string OptimizerPolicyKey,
    [property: JsonPropertyName("solver")]
        SelectedOpeningSolverDocument Solver,
    [property: JsonPropertyName("retrospectiveEvaluationSource")]
        SelectedOpeningEvaluationSourceDocument
            RetrospectiveEvaluationSource,
    [property: JsonPropertyName("modelEvaluationSource")]
        SelectedOpeningEvaluationSourceDocument?
            ModelEvaluationSource = null);

public sealed record SelectedOpeningSolverDocument(
    [property: JsonPropertyName("optimizerVersion")]
        string OptimizerVersion,
    [property: JsonPropertyName("solver")] string Solver,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mipGap")] decimal MipGap,
    [property: JsonPropertyName("reportedMipGap")] decimal ReportedMipGap,
    [property: JsonPropertyName("maximumNumericalMipGap")]
        decimal MaximumNumericalMipGap);

public sealed record SelectedOpeningEvaluationSourceDocument(
    [property: JsonPropertyName("artifactVersion")]
        string ArtifactVersion,
    [property: JsonPropertyName("dataIdentitySha256")]
        string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")]
        string RunIdentitySha256);

public sealed record SelectedOpeningSelectionDocument(
    [property: JsonPropertyName("playerIds")]
        IReadOnlyList<int> PlayerIds,
    [property: JsonPropertyName("budgetTenths")] int BudgetTenths,
    [property: JsonPropertyName("players")]
        IReadOnlyList<SelectedOpeningPlayerDocument> Players,
    [property: JsonPropertyName("gameweeks")]
        IReadOnlyList<SelectedOpeningGameweekDocument> Gameweeks);

public sealed record SelectedOpeningPlayerDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("webName")] string WebName,
    [property: JsonPropertyName("teamId")] int TeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("priceTenths")] int PriceTenths,
    [property: JsonPropertyName("officialStatus")] string OfficialStatus,
    [property: JsonPropertyName("officialChanceOfPlayingNextRound")]
        int? OfficialChanceOfPlayingNextRound,
    [property: JsonPropertyName("modelExpectedPoints")]
        decimal? ModelExpectedPoints = null,
    [property: JsonPropertyName("modelAppearanceProbability")]
        decimal? ModelAppearanceProbability = null,
    [property: JsonPropertyName("modelSixGameweekExpectedPoints")]
        decimal? ModelSixGameweekExpectedPoints = null);

public sealed record SelectedOpeningGameweekDocument(
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("startingPlayerIds")]
        IReadOnlyList<int> StartingPlayerIds,
    [property: JsonPropertyName("captainPlayerId")] int CaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")]
        int ViceCaptainPlayerId,
    [property: JsonPropertyName("replacementGoalkeeperPlayerId")]
        int ReplacementGoalkeeperPlayerId,
    [property: JsonPropertyName("outfieldSubstitutePlayerIds")]
        IReadOnlyList<int> OutfieldSubstitutePlayerIds);

public sealed record SelectedOpeningScoreRegistrationDocument(
    [property: JsonPropertyName("outcomeGameweeks")]
        IReadOnlyList<int> OutcomeGameweeks,
    [property: JsonPropertyName("squadMembership")]
        string SquadMembership,
    [property: JsonPropertyName("roles")] string Roles,
    [property: JsonPropertyName("realisedScorer")] string RealisedScorer,
    [property: JsonPropertyName("outcomeStatus")] string OutcomeStatus);

public sealed record SelectedOpeningPreseasonScoreDocument(
    [property: JsonPropertyName("weekly")]
        IReadOnlyList<SelectedOpeningWeeklyScoreDocument> Weekly,
    [property: JsonPropertyName("cumulative")]
        SelectedOpeningCumulativeScoreDocument Cumulative,
    [property: JsonPropertyName("lowerTailFraction")]
        decimal LowerTailFraction,
    [property: JsonPropertyName("lowerTailCvarPoints")]
        decimal LowerTailCvarPoints,
    [property: JsonPropertyName("pathTotalPoints")]
        IReadOnlyList<int> PathTotalPoints);

public sealed record SelectedOpeningWeeklyScoreDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("engineVersion")] string EngineVersion,
    [property: JsonPropertyName("gameweek")] int Gameweek,
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

public sealed record SelectedOpeningCumulativeScoreDocument(
    [property: JsonPropertyName("scenarioCount")] int ScenarioCount,
    [property: JsonPropertyName("meanPoints")] decimal MeanPoints,
    [property: JsonPropertyName("standardDeviationPoints")]
        decimal StandardDeviationPoints,
    [property: JsonPropertyName("minimumPoints")] int MinimumPoints,
    [property: JsonPropertyName("p10Points")] decimal P10Points,
    [property: JsonPropertyName("medianPoints")] decimal MedianPoints,
    [property: JsonPropertyName("p90Points")] decimal P90Points,
    [property: JsonPropertyName("maximumPoints")] int MaximumPoints);

public sealed record SelectedOpeningSourceDocument(
    [property: JsonPropertyName("artifactVersion")]
        string ArtifactVersion,
    [property: JsonPropertyName("dataIdentitySha256")]
        string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")]
        string RunIdentitySha256);
