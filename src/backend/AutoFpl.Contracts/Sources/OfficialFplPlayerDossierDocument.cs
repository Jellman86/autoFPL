using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record OfficialFplPlayerDossierDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("targetGameweek")] int TargetGameweek,
    [property: JsonPropertyName("decisionCutoffUtc")] DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("selectedCaptureId")] long SelectedCaptureId,
    [property: JsonPropertyName("captureAvailableAtUtc")] DateTimeOffset CaptureAvailableAtUtc,
    [property: JsonPropertyName("player")] OfficialFplPlayerIdentityDocument Player,
    [property: JsonPropertyName("publishedExpectedPoints")]
        OfficialFplPublishedExpectedPointsDocument? PublishedExpectedPoints,
    [property: JsonPropertyName("preseasonChallenger")]
        OfficialFplPreseasonChallengerDocument? PreseasonChallenger,
    [property: JsonPropertyName("recentOutcomes")]
        IReadOnlyList<OfficialFplPlayerOutcomeDocument> RecentOutcomes,
    [property: JsonPropertyName("upcomingFixtures")]
        IReadOnlyList<OfficialFplPlayerFixtureDocument> UpcomingFixtures,
    [property: JsonPropertyName("researchEvidence")]
        OfficialFplPlayerResearchEvidenceDocument ResearchEvidence);

public sealed record OfficialFplPlayerIdentityDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("playerCode")] int PlayerCode,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("webName")] string WebName,
    [property: JsonPropertyName("teamId")] int TeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("teamShortName")] string TeamShortName,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("priceTenths")] int PriceTenths,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("news")] string News,
    [property: JsonPropertyName("photoIdentifier")] string? PhotoIdentifier,
    [property: JsonPropertyName("photoUrl")] string? PhotoUrl);

public sealed record OfficialFplPublishedExpectedPointsDocument(
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("targetGameweek")] int TargetGameweek,
    [property: JsonPropertyName("expectedPoints")] decimal ExpectedPoints,
    [property: JsonPropertyName("evidenceStatus")] string EvidenceStatus);

public sealed record OfficialFplPreseasonChallengerDocument(
    [property: JsonPropertyName("modelKey")] string ModelKey,
    [property: JsonPropertyName("expectedPoints")] decimal ExpectedPoints,
    [property: JsonPropertyName("baselineV0ExpectedPoints")]
        decimal BaselineV0ExpectedPoints,
    [property: JsonPropertyName("differenceFromBaselineV0")]
        decimal DifferenceFromBaselineV0,
    [property: JsonPropertyName("availabilityStatus")] string AvailabilityStatus,
    [property: JsonPropertyName("priorSeasonIdentityStatus")]
        string PriorSeasonIdentityStatus,
    [property: JsonPropertyName("distributionStatus")] string DistributionStatus,
    [property: JsonPropertyName("lockedHoldoutMaeImprovementFraction")]
        decimal LockedHoldoutMaeImprovementFraction,
    [property: JsonPropertyName("influencesAdvice")] bool InfluencesAdvice,
    [property: JsonPropertyName("forecastArtifactId")] long ForecastArtifactId,
    [property: JsonPropertyName("forecastArtifactContentSha256")]
        string ForecastArtifactContentSha256);

public sealed record OfficialFplPlayerOutcomeDocument(
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("outcomeCaptureId")] long OutcomeCaptureId,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("isGameweekAggregate")] bool IsGameweekAggregate,
    [property: JsonPropertyName("fixtures")]
        IReadOnlyList<OfficialFplPlayerFixtureDocument> Fixtures,
    [property: JsonPropertyName("minutes")] int Minutes,
    [property: JsonPropertyName("starts")] int Starts,
    [property: JsonPropertyName("totalPoints")] int TotalPoints,
    [property: JsonPropertyName("goalsScored")] int GoalsScored,
    [property: JsonPropertyName("assists")] int Assists,
    [property: JsonPropertyName("cleanSheets")] int CleanSheets,
    [property: JsonPropertyName("goalsConceded")] int GoalsConceded,
    [property: JsonPropertyName("saves")] int Saves,
    [property: JsonPropertyName("bonus")] int Bonus,
    [property: JsonPropertyName("yellowCards")] int YellowCards,
    [property: JsonPropertyName("redCards")] int RedCards,
    [property: JsonPropertyName("ownGoals")] int? OwnGoals,
    [property: JsonPropertyName("penaltiesSaved")] int? PenaltiesSaved,
    [property: JsonPropertyName("penaltiesMissed")] int? PenaltiesMissed,
    [property: JsonPropertyName("bps")] int? Bps,
    [property: JsonPropertyName("influence")] decimal? Influence,
    [property: JsonPropertyName("creativity")] decimal? Creativity,
    [property: JsonPropertyName("threat")] decimal? Threat,
    [property: JsonPropertyName("ictIndex")] decimal? IctIndex,
    [property: JsonPropertyName("clearancesBlocksInterceptions")]
        int? ClearancesBlocksInterceptions,
    [property: JsonPropertyName("recoveries")] int? Recoveries,
    [property: JsonPropertyName("tackles")] int? Tackles,
    [property: JsonPropertyName("defensiveContribution")] int? DefensiveContribution,
    [property: JsonPropertyName("expectedGoals")] decimal? ExpectedGoals,
    [property: JsonPropertyName("expectedAssists")] decimal? ExpectedAssists,
    [property: JsonPropertyName("expectedGoalInvolvements")]
        decimal? ExpectedGoalInvolvements,
    [property: JsonPropertyName("expectedGoalsConceded")] decimal? ExpectedGoalsConceded);

public sealed record OfficialFplPlayerFixtureDocument(
    [property: JsonPropertyName("fixtureId")] int FixtureId,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("kickoffUtc")] DateTimeOffset? KickoffUtc,
    [property: JsonPropertyName("opponentTeamId")] int OpponentTeamId,
    [property: JsonPropertyName("opponentName")] string OpponentName,
    [property: JsonPropertyName("opponentShortName")] string OpponentShortName,
    [property: JsonPropertyName("isHome")] bool IsHome,
    [property: JsonPropertyName("started")] bool Started,
    [property: JsonPropertyName("finished")] bool Finished);

public sealed record OfficialFplPlayerResearchEvidenceDocument(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("influencesForecast")] bool InfluencesForecast,
    [property: JsonPropertyName("claimCount")] int ClaimCount,
    [property: JsonPropertyName("sourceCount")] int SourceCount,
    [property: JsonPropertyName("dependentClusterCount")] int DependentClusterCount,
    [property: JsonPropertyName("hasContradictions")] bool HasContradictions,
    [property: JsonPropertyName("claims")]
        IReadOnlyList<OfficialFplPlayerResearchClaimDocument> Claims);

public sealed record OfficialFplPlayerResearchClaimDocument(
    [property: JsonPropertyName("claimId")] long ClaimId,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("canonicalUrl")] string CanonicalUrl,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("leadTimeSeconds")] long LeadTimeSeconds,
    [property: JsonPropertyName("claimType")] string ClaimType,
    [property: JsonPropertyName("availabilityStatus")] string? AvailabilityStatus,
    [property: JsonPropertyName("startStatus")] string? StartStatus,
    [property: JsonPropertyName("forecastProbability")] decimal? ForecastProbability,
    [property: JsonPropertyName("expectedMinutes")] int? ExpectedMinutes,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("directness")] string Directness,
    [property: JsonPropertyName("sourceSpan")] string SourceSpan,
    [property: JsonPropertyName("extractionVersion")] string ExtractionVersion,
    [property: JsonPropertyName("extractionConfidence")] decimal ExtractionConfidence,
    [property: JsonPropertyName("duplicateClusterKey")] string? DuplicateClusterKey,
    [property: JsonPropertyName("isDependent")] bool IsDependent);
