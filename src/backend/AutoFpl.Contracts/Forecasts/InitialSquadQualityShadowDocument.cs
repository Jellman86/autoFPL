using System.Text.Json.Serialization;

using AutoFpl.Contracts.Selections;

namespace AutoFpl.Contracts.Forecasts;

public sealed record InitialSquadQualityShadowDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("artifactType")] string ArtifactType,
    [property: JsonPropertyName("artifactVersion")] string ArtifactVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isPromoted")] bool IsPromoted,
    [property: JsonPropertyName("influencesAdvice")] bool InfluencesAdvice,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")]
        DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("decisionCutoffUtc")]
        DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("officialCaptureId")]
        long OfficialCaptureId,
    [property: JsonPropertyName("scenarioCount")] int ScenarioCount,
    [property: JsonPropertyName("scenarioSource")]
        SelectionScenarioSourceDocument ScenarioSource,
    [property: JsonPropertyName("modelSource")]
        SelectionScenarioModelSourceDocument ModelSource,
    [property: JsonPropertyName("objective")]
        InitialSquadObjectiveDocument Objective,
    [property: JsonPropertyName("optimizer")]
        InitialSquadOptimizerDocument Optimizer,
    [property: JsonPropertyName("candidatePoolCount")]
        int CandidatePoolCount,
    [property: JsonPropertyName("budgetTenths")] int BudgetTenths,
    [property: JsonPropertyName("players")]
        IReadOnlyList<InitialSquadPlayerDocument> Players,
    [property: JsonPropertyName("model")]
        SelectionScenarioResultDocument Model,
    [property: JsonPropertyName("candidate")]
        SelectionScenarioResultDocument Candidate,
    [property: JsonPropertyName("candidateVsModel")]
        SelectionScenarioComparisonDocument CandidateVsModel,
    [property: JsonPropertyName("limitations")]
        IReadOnlyList<string> Limitations,
    [property: JsonPropertyName("dataIdentitySha256")]
        string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")]
        string RunIdentitySha256,
    [property: JsonPropertyName("initialSquadArtifactId")]
        long? InitialSquadArtifactId = null,
    [property: JsonPropertyName("initialSquadArtifactContentSha256")]
        string? InitialSquadArtifactContentSha256 = null);

public sealed record InitialSquadObjectiveDocument(
    [property: JsonPropertyName("objectiveKey")] string ObjectiveKey,
    [property: JsonPropertyName("starterWeight")] decimal StarterWeight,
    [property: JsonPropertyName("benchWeight")] decimal BenchWeight,
    [property: JsonPropertyName("captainBonusWeight")]
        decimal CaptainBonusWeight);

public sealed record InitialSquadOptimizerDocument(
    [property: JsonPropertyName("optimizerVersion")]
        string OptimizerVersion,
    [property: JsonPropertyName("solver")] string Solver,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mipGap")] decimal MipGap,
    [property: JsonPropertyName("benchWeight")] decimal BenchWeight);

public sealed record InitialSquadPlayerDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("webName")] string WebName,
    [property: JsonPropertyName("teamId")] int TeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("priceTenths")] int PriceTenths,
    [property: JsonPropertyName("officialStatus")] string OfficialStatus,
    [property: JsonPropertyName("officialChanceOfPlayingNextRound")]
        int? OfficialChanceOfPlayingNextRound,
    [property: JsonPropertyName("scenarioMeanPoints")]
        decimal ScenarioMeanPoints,
    [property: JsonPropertyName("pointModelMean")] decimal PointModelMean,
    [property: JsonPropertyName("appearanceProbability")]
        decimal AppearanceProbability,
    [property: JsonPropertyName("pointHistoryIdentityStatus")]
        string PointHistoryIdentityStatus,
    [property: JsonPropertyName("participationHistoryIdentityStatus")]
        string ParticipationHistoryIdentityStatus);
