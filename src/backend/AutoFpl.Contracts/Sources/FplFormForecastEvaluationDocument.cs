using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record FplFormForecastEvaluationDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("evaluatorVersion")] string EvaluatorVersion,
    [property: JsonPropertyName("researchStatus")] string ResearchStatus,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("seasonCode")] string? SeasonCode,
    [property: JsonPropertyName("candidateCaptureCount")] int CandidateCaptureCount,
    [property: JsonPropertyName("eligiblePairCount")] int EligiblePairCount,
    [property: JsonPropertyName("dataIdentitySha256")] string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")] string RunIdentitySha256,
    [property: JsonPropertyName("folds")]
        IReadOnlyList<FplFormForecastEvaluationFoldDocument> Folds,
    [property: JsonPropertyName("excludedCaptures")]
        IReadOnlyList<FplFormForecastEvaluationExclusionDocument> ExcludedCaptures,
    [property: JsonPropertyName("models")]
        IReadOnlyList<FplFormForecastEvaluationModelDocument> Models);

public sealed record FplFormForecastEvaluationFoldDocument(
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("forecastCaptureId")] long ForecastCaptureId,
    [property: JsonPropertyName("forecastAvailableAtUtc")]
        DateTimeOffset ForecastAvailableAtUtc,
    [property: JsonPropertyName("forecastContentSha256")] string ForecastContentSha256,
    [property: JsonPropertyName("identityCaptureId")] long IdentityCaptureId,
    [property: JsonPropertyName("identityBootstrapSha256")] string IdentityBootstrapSha256,
    [property: JsonPropertyName("identityFixturesSha256")] string IdentityFixturesSha256,
    [property: JsonPropertyName("outcomeCaptureId")] long OutcomeCaptureId,
    [property: JsonPropertyName("outcomeAvailableAtUtc")]
        DateTimeOffset OutcomeAvailableAtUtc,
    [property: JsonPropertyName("outcomeLiveSha256")] string OutcomeLiveSha256,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("fixturePredictionCount")] int FixturePredictionCount,
    [property: JsonPropertyName("probabilityAdjustedPlayerCount")]
        int ProbabilityAdjustedPlayerCount,
    [property: JsonPropertyName("publishedConditionalMetrics")]
        FplFormForecastEvaluationMetricsDocument PublishedConditionalMetrics,
    [property: JsonPropertyName("probabilityAdjustedMetrics")]
        FplFormForecastEvaluationMetricsDocument? ProbabilityAdjustedMetrics);

public sealed record FplFormForecastEvaluationExclusionDocument(
    [property: JsonPropertyName("forecastCaptureId")] long ForecastCaptureId,
    [property: JsonPropertyName("forecastContentSha256")] string ForecastContentSha256,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record FplFormForecastEvaluationModelDocument(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("interpretation")] string Interpretation,
    [property: JsonPropertyName("sampleCount")] int SampleCount,
    [property: JsonPropertyName("missingPlayerCount")] int MissingPlayerCount,
    [property: JsonPropertyName("metrics")]
        FplFormForecastEvaluationMetricsDocument? Metrics,
    [property: JsonPropertyName("zeroMinuteMetrics")]
        FplFormForecastEvaluationMetricsDocument? ZeroMinuteMetrics,
    [property: JsonPropertyName("positionSlices")]
        IReadOnlyList<FplFormForecastEvaluationSliceDocument> PositionSlices);

public sealed record FplFormForecastEvaluationSliceDocument(
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("metrics")]
        FplFormForecastEvaluationMetricsDocument Metrics);

public sealed record FplFormForecastEvaluationMetricsDocument(
    [property: JsonPropertyName("sampleCount")] int SampleCount,
    [property: JsonPropertyName("mae")] double Mae,
    [property: JsonPropertyName("rmse")] double Rmse,
    [property: JsonPropertyName("bias")] double Bias);
