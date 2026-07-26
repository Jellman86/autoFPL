using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Intelligence;

public sealed record EvidenceClaimEvaluationDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("evaluatorVersion")] string EvaluatorVersion,
    [property: JsonPropertyName("researchStatus")] string ResearchStatus,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("seasonCode")] string? SeasonCode,
    [property: JsonPropertyName("candidateOutcomeCount")] int CandidateOutcomeCount,
    [property: JsonPropertyName("evaluatedGameweekCount")] int EvaluatedGameweekCount,
    [property: JsonPropertyName("evaluatedClaimCount")] int EvaluatedClaimCount,
    [property: JsonPropertyName("dataIdentitySha256")] string DataIdentitySha256,
    [property: JsonPropertyName("runIdentitySha256")] string RunIdentitySha256,
    [property: JsonPropertyName("slices")]
        IReadOnlyList<EvidenceClaimEvaluationSliceDocument> Slices,
    [property: JsonPropertyName("folds")]
        IReadOnlyList<EvidenceClaimEvaluationFoldDocument> Folds,
    [property: JsonPropertyName("exclusions")]
        IReadOnlyList<EvidenceClaimEvaluationExclusionDocument> Exclusions);

public sealed record EvidenceClaimEvaluationSliceDocument(
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("claimType")] string ClaimType,
    [property: JsonPropertyName("leadTimeBucket")] string LeadTimeBucket,
    [property: JsonPropertyName("sampleCount")] int SampleCount,
    [property: JsonPropertyName("positiveOutcomeCount")] int PositiveOutcomeCount,
    [property: JsonPropertyName("correctCount")] int CorrectCount,
    [property: JsonPropertyName("accuracy")] double Accuracy,
    [property: JsonPropertyName("probabilisticSampleCount")]
        int ProbabilisticSampleCount,
    [property: JsonPropertyName("brierScore")] double? BrierScore,
    [property: JsonPropertyName("logLoss")] double? LogLoss);

public sealed record EvidenceClaimEvaluationFoldDocument(
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("outcomeCaptureId")] long OutcomeCaptureId,
    [property: JsonPropertyName("outcomeAvailableAtUtc")]
        DateTimeOffset OutcomeAvailableAtUtc,
    [property: JsonPropertyName("outcomeLiveSha256")] string OutcomeLiveSha256,
    [property: JsonPropertyName("claimCount")] int ClaimCount,
    [property: JsonPropertyName("slices")]
        IReadOnlyList<EvidenceClaimEvaluationSliceDocument> Slices);

public sealed record EvidenceClaimEvaluationExclusionDocument(
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("outcomeCaptureId")] long OutcomeCaptureId,
    [property: JsonPropertyName("reason")] string Reason);
