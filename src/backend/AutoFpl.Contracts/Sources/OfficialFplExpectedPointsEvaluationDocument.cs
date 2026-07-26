using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record OfficialFplExpectedPointsEvaluationDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("evaluatorVersion")] string EvaluatorVersion,
    [property: JsonPropertyName("researchStatus")] string ResearchStatus,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("seasonCode")] string? SeasonCode,
    [property: JsonPropertyName("candidateGameweekCount")] int CandidateGameweekCount,
    [property: JsonPropertyName("eligiblePairCount")] int EligiblePairCount,
    [property: JsonPropertyName("dataIdentitySha256")] string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")] string RunIdentitySha256,
    [property: JsonPropertyName("metrics")]
        OfficialFplExpectedPointsMetricsDocument? Metrics,
    [property: JsonPropertyName("zeroMinuteMetrics")]
        OfficialFplExpectedPointsMetricsDocument? ZeroMinuteMetrics,
    [property: JsonPropertyName("positionSlices")]
        IReadOnlyList<OfficialFplExpectedPointsSliceDocument> PositionSlices,
    [property: JsonPropertyName("folds")]
        IReadOnlyList<OfficialFplExpectedPointsFoldDocument> Folds,
    [property: JsonPropertyName("excludedGameweeks")]
        IReadOnlyList<OfficialFplExpectedPointsExclusionDocument> ExcludedGameweeks);

public sealed record OfficialFplExpectedPointsFoldDocument(
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("forecastCaptureId")] long ForecastCaptureId,
    [property: JsonPropertyName("forecastAvailableAtUtc")]
        DateTimeOffset ForecastAvailableAtUtc,
    [property: JsonPropertyName("forecastBootstrapSha256")] string ForecastBootstrapSha256,
    [property: JsonPropertyName("forecastFixturesSha256")] string ForecastFixturesSha256,
    [property: JsonPropertyName("outcomeCaptureId")] long OutcomeCaptureId,
    [property: JsonPropertyName("outcomeAvailableAtUtc")]
        DateTimeOffset OutcomeAvailableAtUtc,
    [property: JsonPropertyName("outcomeLiveSha256")] string OutcomeLiveSha256,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("metrics")] OfficialFplExpectedPointsMetricsDocument Metrics,
    [property: JsonPropertyName("zeroMinuteMetrics")]
        OfficialFplExpectedPointsMetricsDocument? ZeroMinuteMetrics,
    [property: JsonPropertyName("positionSlices")]
        IReadOnlyList<OfficialFplExpectedPointsSliceDocument> PositionSlices);

public sealed record OfficialFplExpectedPointsExclusionDocument(
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("outcomeCaptureId")] long OutcomeCaptureId,
    [property: JsonPropertyName("forecastCaptureId")] long? ForecastCaptureId,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record OfficialFplExpectedPointsSliceDocument(
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("metrics")] OfficialFplExpectedPointsMetricsDocument Metrics);

public sealed record OfficialFplExpectedPointsMetricsDocument(
    [property: JsonPropertyName("sampleCount")] int SampleCount,
    [property: JsonPropertyName("mae")] double Mae,
    [property: JsonPropertyName("rmse")] double Rmse,
    [property: JsonPropertyName("bias")] double Bias);
