using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Snapshots;

public sealed record DecisionSnapshotPersistenceRequest(
    [property: JsonPropertyName("schemaVersion")] string? SchemaVersion,
    [property: JsonPropertyName("seasonCode")] string? SeasonCode,
    [property: JsonPropertyName("gameweek")] int? Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset? DeadlineUtc,
    [property: JsonPropertyName("decisionCutoffUtc")] DateTimeOffset? DecisionCutoffUtc,
    [property: JsonPropertyName("budgetTenths")] int? BudgetTenths,
    [property: JsonPropertyName("players")] IReadOnlyList<DecisionSnapshotPlayerRequest?>? Players,
    [property: JsonPropertyName("startingPlayerIds")] IReadOnlyList<int?>? StartingPlayerIds,
    [property: JsonPropertyName("captainPlayerId")] int? CaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")] int? ViceCaptainPlayerId,
    [property: JsonPropertyName("replacementGoalkeeperPlayerId")] int? ReplacementGoalkeeperPlayerId,
    [property: JsonPropertyName("outfieldSubstitutePlayerIds")] IReadOnlyList<int?>? OutfieldSubstitutePlayerIds,
    [property: JsonPropertyName("observations")] IReadOnlyList<SourceObservationRequest?>? Observations,
    [property: JsonPropertyName("supersedesSnapshotId")] long? SupersedesSnapshotId);

public sealed record DecisionSnapshotPlayerRequest(
    [property: JsonPropertyName("playerId")] int? PlayerId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("clubId")] int? ClubId,
    [property: JsonPropertyName("position")] string? Position,
    [property: JsonPropertyName("priceTenths")] int? PriceTenths);

public sealed record SourceObservationRequest(
    [property: JsonPropertyName("sourceKey")] string? SourceKey,
    [property: JsonPropertyName("playerId")] int? PlayerId,
    [property: JsonPropertyName("metric")] string? Metric,
    [property: JsonPropertyName("value")] decimal? Value,
    [property: JsonPropertyName("observedAtUtc")] DateTimeOffset? ObservedAtUtc,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset? RetrievedAtUtc,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset? AvailableAtUtc,
    [property: JsonPropertyName("supersedesObservationId")] long? SupersedesObservationId);
