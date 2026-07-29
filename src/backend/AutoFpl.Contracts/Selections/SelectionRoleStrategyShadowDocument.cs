using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Selections;

public sealed record SelectionRoleStrategyShadowDocument(
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
    [property: JsonPropertyName("scenarioCount")] int ScenarioCount,
    [property: JsonPropertyName("selectionScoreSource")]
        SelectionRoleStrategyScoreSourceDocument SelectionScoreSource,
    [property: JsonPropertyName("fixedSquadPlayerIds")]
        IReadOnlyList<int> FixedSquadPlayerIds,
    [property: JsonPropertyName("model")]
        SelectionScenarioResultDocument Model,
    [property: JsonPropertyName("strategies")]
        SelectionRoleStrategySetDocument Strategies,
    [property: JsonPropertyName("search")]
        SelectionRoleStrategySearchDocument Search,
    [property: JsonPropertyName("limitations")]
        IReadOnlyList<string> Limitations,
    [property: JsonPropertyName("dataIdentitySha256")]
        string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")]
        string RunIdentitySha256,
    [property: JsonPropertyName("strategyArtifactId")]
        long? StrategyArtifactId = null,
    [property: JsonPropertyName("strategyArtifactContentSha256")]
        string? StrategyArtifactContentSha256 = null);

public sealed record SelectionRoleStrategyScoreSourceDocument(
    [property: JsonPropertyName("dataIdentitySha256")]
        string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")]
        string RunIdentitySha256,
    [property: JsonPropertyName("scenarioSource")]
        SelectionScenarioSourceDocument ScenarioSource,
    [property: JsonPropertyName("modelSource")]
        SelectionScenarioModelSourceDocument ModelSource);

public sealed record SelectionRoleStrategySetDocument(
    [property: JsonPropertyName("balanced")]
        SelectionRoleStrategyDocument Balanced,
    [property: JsonPropertyName("safer")]
        SelectionRoleStrategyDocument Safer,
    [property: JsonPropertyName("higherCeiling")]
        SelectionRoleStrategyDocument HigherCeiling);

public sealed record SelectionRoleStrategyDocument(
    [property: JsonPropertyName("strategyId")] string StrategyId,
    [property: JsonPropertyName("objective")]
        SelectionRoleStrategyObjectiveDocument Objective,
    [property: JsonPropertyName("isDistinctFromModel")]
        bool IsDistinctFromModel,
    [property: JsonPropertyName("result")]
        SelectionScenarioResultDocument Result,
    [property: JsonPropertyName("vsModel")]
        SelectionScenarioComparisonDocument VsModel);

public sealed record SelectionRoleStrategyObjectiveDocument(
    [property: JsonPropertyName("objectiveKey")] string ObjectiveKey,
    [property: JsonPropertyName("tailFraction")] decimal? TailFraction,
    [property: JsonPropertyName("objectiveValue")] decimal ObjectiveValue);

public sealed record SelectionRoleStrategySearchDocument(
    [property: JsonPropertyName("searchVersion")] string SearchVersion,
    [property: JsonPropertyName("beamWidth")] int BeamWidth,
    [property: JsonPropertyName("iterations")] int Iterations,
    [property: JsonPropertyName("tailFraction")] decimal TailFraction,
    [property: JsonPropertyName("uniqueCandidatesEvaluated")]
        int UniqueCandidatesEvaluated,
    [property: JsonPropertyName("newCandidatesByStrategy")]
        IReadOnlyDictionary<string, int> NewCandidatesByStrategy,
    [property: JsonPropertyName("searchStatus")] string SearchStatus);
