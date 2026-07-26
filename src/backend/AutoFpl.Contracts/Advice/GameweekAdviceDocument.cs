using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Advice;

public sealed record GameweekAdviceDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("evidenceStatus")] string EvidenceStatus,
    [property: JsonPropertyName("isSynthetic")] bool IsSynthetic,
    [property: JsonPropertyName("snapshotId")] long? SnapshotId,
    [property: JsonPropertyName("snapshotRevision")] int? SnapshotRevision,
    [property: JsonPropertyName("decisionCutoffUtc")] DateTimeOffset? DecisionCutoffUtc,
    [property: JsonPropertyName("snapshotContentHash")] string? SnapshotContentHash,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("modelLabel")] string ModelLabel,
    [property: JsonPropertyName("recommendationSummary")] string RecommendationSummary,
    [property: JsonPropertyName("selection")] AdviceSelectionDocument Selection,
    [property: JsonPropertyName("alternatives")] IReadOnlyList<AdviceAlternativeDocument> Alternatives,
    [property: JsonPropertyName("aiAccess")] AdviceAiAccessDocument AiAccess);

public sealed record AdviceSelectionDocument(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("objective")] string Objective,
    [property: JsonPropertyName("expectedPoints")] decimal ExpectedPoints,
    [property: JsonPropertyName("players")] IReadOnlyList<AdvicePlayerDocument> Players);

public sealed record AdvicePlayerDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("clubShortName")] string ClubShortName,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("lineupPlace")] string LineupPlace,
    [property: JsonPropertyName("benchOrder")] int? BenchOrder,
    [property: JsonPropertyName("captaincy")] string? Captaincy,
    [property: JsonPropertyName("opponent")] string Opponent,
    [property: JsonPropertyName("isHome")] bool IsHome,
    [property: JsonPropertyName("expectedPoints")] decimal ExpectedPoints,
    [property: JsonPropertyName("lower80")] decimal Lower80,
    [property: JsonPropertyName("upper80")] decimal Upper80,
    [property: JsonPropertyName("expectedMinutes")] int ExpectedMinutes,
    [property: JsonPropertyName("reasons")] IReadOnlyList<string> Reasons,
    [property: JsonPropertyName("risks")] IReadOnlyList<string> Risks,
    [property: JsonPropertyName("photoUrl")] string? PhotoUrl = null,
    [property: JsonPropertyName("dossierPath")] string? DossierPath = null);

public sealed record AdviceAlternativeDocument(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("objective")] string Objective,
    [property: JsonPropertyName("expectedPoints")] decimal ExpectedPoints,
    [property: JsonPropertyName("difference")] decimal Difference);

public sealed record AdviceAiAccessDocument(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("plannedModes")] IReadOnlyList<string> PlannedModes);
