using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Forecasts;

public sealed record ExternalEvidenceStressDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("artifactType")] string ArtifactType,
    [property: JsonPropertyName("artifactVersion")] string ArtifactVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isPromoted")] bool IsPromoted,
    [property: JsonPropertyName("influencesAdvice")] bool InfluencesAdvice,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("openingGameweek")] int OpeningGameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("forecastDecisionCutoffUtc")]
        DateTimeOffset ForecastDecisionCutoffUtc,
    [property: JsonPropertyName("evidenceDecisionCutoffUtc")]
        DateTimeOffset EvidenceDecisionCutoffUtc,
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("scenarioCount")] int ScenarioCount,
    [property: JsonPropertyName("candidatePoolCount")] int CandidatePoolCount,
    [property: JsonPropertyName("stressMethod")]
        ExternalEvidenceStressMethodDocument StressMethod,
    [property: JsonPropertyName("policy")]
        ExternalEvidenceStressPolicyDocument Policy,
    [property: JsonPropertyName("incumbent")]
        ExternalEvidenceStressIncumbentDocument Incumbent,
    [property: JsonPropertyName("coverage")]
        ExternalEvidenceStressCoverageDocument Coverage,
    [property: JsonPropertyName("selectedAdversePlayers")]
        IReadOnlyList<ExternalEvidenceStressPlayerDocument>
            SelectedAdversePlayers,
    [property: JsonPropertyName("adverseClaims")]
        IReadOnlyList<ExternalEvidenceStressClaimDocument> AdverseClaims,
    [property: JsonPropertyName("stressScenarios")]
        IReadOnlyList<ExternalEvidenceStressScenarioDocument>
            StressScenarios,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("limitations")]
        IReadOnlyList<string> Limitations,
    [property: JsonPropertyName("source")]
        ExternalEvidenceStressSourceDocument Source,
    [property: JsonPropertyName("dataIdentitySha256")]
        string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")]
        string RunIdentitySha256,
    [property: JsonPropertyName("externalEvidenceStressArtifactId")]
        long? ExternalEvidenceStressArtifactId = null,
    [property: JsonPropertyName(
        "externalEvidenceStressArtifactContentSha256")]
        string? ExternalEvidenceStressArtifactContentSha256 = null);

public sealed record ExternalEvidenceStressMethodDocument(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("affectedGameweeks")]
        IReadOnlyList<int> AffectedGameweeks,
    [property: JsonPropertyName("laterGameweeksUnchanged")]
        bool LaterGameweeksUnchanged,
    [property: JsonPropertyName("assignsSourceProbability")]
        bool AssignsSourceProbability,
    [property: JsonPropertyName("interpretation")] string Interpretation);

public sealed record ExternalEvidenceStressPolicyDocument(
    [property: JsonPropertyName("evaluationPolicyKey")]
        string EvaluationPolicyKey,
    [property: JsonPropertyName("horizonGameweeks")] int HorizonGameweeks,
    [property: JsonPropertyName("optimizerPolicyKey")]
        string OptimizerPolicyKey,
    [property: JsonPropertyName("optimizerVersion")]
        string OptimizerVersion,
    [property: JsonPropertyName("benchWeight")] decimal BenchWeight,
    [property: JsonPropertyName("cvarWeight")] decimal CvarWeight);

public sealed record ExternalEvidenceStressIncumbentDocument(
    [property: JsonPropertyName("playerIds")]
        IReadOnlyList<int> PlayerIds,
    [property: JsonPropertyName("players")]
        IReadOnlyList<ExternalEvidenceStressPlayerDocument> Players,
    [property: JsonPropertyName("budgetTenths")] int BudgetTenths,
    [property: JsonPropertyName("solverStatus")] string SolverStatus,
    [property: JsonPropertyName("reportedMipGap")] decimal ReportedMipGap,
    [property: JsonPropertyName("exactScenarioMeanPoints")]
        decimal ExactScenarioMeanPoints);

public sealed record ExternalEvidenceStressCoverageDocument(
    [property: JsonPropertyName("latestClaimCount")] int LatestClaimCount,
    [property: JsonPropertyName("adverseClaimCount")] int AdverseClaimCount,
    [property: JsonPropertyName("selectedAdversePlayerCount")]
        int SelectedAdversePlayerCount,
    [property: JsonPropertyName("stressScenarioCount")]
        int StressScenarioCount,
    [property: JsonPropertyName("squadChangingScenarioCount")]
        int SquadChangingScenarioCount,
    [property: JsonPropertyName("decisionRelevantScenarioCount")]
        int DecisionRelevantScenarioCount);

public sealed record ExternalEvidenceStressScenarioDocument(
    [property: JsonPropertyName("scenarioKey")] string ScenarioKey,
    [property: JsonPropertyName("scenarioType")] string ScenarioType,
    [property: JsonPropertyName("sourceKeys")]
        IReadOnlyList<string> SourceKeys,
    [property: JsonPropertyName("affectedPlayerIds")]
        IReadOnlyList<int> AffectedPlayerIds,
    [property: JsonPropertyName("claimIds")]
        IReadOnlyList<long> ClaimIds,
    [property: JsonPropertyName("affectedPlayerCount")]
        int AffectedPlayerCount,
    [property: JsonPropertyName("affectedSelectedPlayers")]
        IReadOnlyList<ExternalEvidenceStressPlayerDocument>
            AffectedSelectedPlayers,
    [property: JsonPropertyName("selectionPlayerIds")]
        IReadOnlyList<int> SelectionPlayerIds,
    [property: JsonPropertyName("selectionBudgetTenths")]
        int SelectionBudgetTenths,
    [property: JsonPropertyName("solverStatus")] string SolverStatus,
    [property: JsonPropertyName("reportedMipGap")] decimal ReportedMipGap,
    [property: JsonPropertyName("squadChanged")] bool SquadChanged,
    [property: JsonPropertyName("gameweekOneRolesChanged")]
        bool GameweekOneRolesChanged,
    [property: JsonPropertyName("removedPlayers")]
        IReadOnlyList<ExternalEvidenceStressPlayerDocument> RemovedPlayers,
    [property: JsonPropertyName("addedPlayers")]
        IReadOnlyList<ExternalEvidenceStressPlayerDocument> AddedPlayers,
    [property: JsonPropertyName(
        "unappliedAdverseEvidenceForAddedPlayers")]
        IReadOnlyList<ExternalEvidenceStressClaimDocument>
            UnappliedAdverseEvidenceForAddedPlayers,
    [property: JsonPropertyName("isSourceConsistentAlternative")]
        bool IsSourceConsistentAlternative,
    [property: JsonPropertyName(
        "exactScenarioMeanDifferenceIfStressTruePoints")]
        decimal ExactScenarioMeanDifferenceIfStressTruePoints,
    [property: JsonPropertyName(
        "exactScenarioMeanDifferenceIfStressFalsePoints")]
        decimal ExactScenarioMeanDifferenceIfStressFalsePoints,
    [property: JsonPropertyName("stressDecision")] string StressDecision);

public sealed record ExternalEvidenceStressPlayerDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("webName")] string WebName,
    [property: JsonPropertyName("teamId")] int TeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("priceTenths")] int PriceTenths);

public sealed record ExternalEvidenceStressClaimDocument(
    [property: JsonPropertyName("claimId")] long ClaimId,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("claimType")] string ClaimType,
    [property: JsonPropertyName("availableAtUtc")]
        DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("webName")] string WebName,
    [property: JsonPropertyName("teamId")] int TeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("startStatus")] string? StartStatus,
    [property: JsonPropertyName("availabilityStatus")]
        string? AvailabilityStatus,
    [property: JsonPropertyName("forecastProbability")]
        decimal? ForecastProbability,
    [property: JsonPropertyName("claimContentSha256")]
        string ClaimContentSha256,
    [property: JsonPropertyName("duplicateClusterKey")]
        string? DuplicateClusterKey);

public sealed record ExternalEvidenceStressSourceDocument(
    [property: JsonPropertyName("scenarioArtifactVersion")]
        string ScenarioArtifactVersion,
    [property: JsonPropertyName("scenarioContentSha256")]
        string ScenarioContentSha256,
    [property: JsonPropertyName("scenarioRunIdentitySha256")]
        string ScenarioRunIdentitySha256);
