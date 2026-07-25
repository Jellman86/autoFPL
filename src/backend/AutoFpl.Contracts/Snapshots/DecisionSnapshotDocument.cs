using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Snapshots;

public sealed record DecisionSnapshotDocument(
    [property: JsonPropertyName("snapshotId")] long SnapshotId,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("supersedesSnapshotId")] long? SupersedesSnapshotId,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("decisionCutoffUtc")] DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("contentHash")] string ContentHash,
    [property: JsonPropertyName("squad")] PersistedSquadDocument Squad,
    [property: JsonPropertyName("selection")] PersistedSelectionDocument Selection,
    [property: JsonPropertyName("observations")] IReadOnlyList<SourceObservationDocument> Observations);

public sealed record PersistedSquadDocument(
    [property: JsonPropertyName("budgetTenths")] int BudgetTenths,
    [property: JsonPropertyName("players")] IReadOnlyList<PersistedPlayerDocument> Players);

public sealed record PersistedPlayerDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("clubId")] int ClubId,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("priceTenths")] int PriceTenths);

public sealed record PersistedSelectionDocument(
    [property: JsonPropertyName("startingPlayerIds")] IReadOnlyList<int> StartingPlayerIds,
    [property: JsonPropertyName("captainPlayerId")] int CaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")] int ViceCaptainPlayerId,
    [property: JsonPropertyName("replacementGoalkeeperPlayerId")] int ReplacementGoalkeeperPlayerId,
    [property: JsonPropertyName("outfieldSubstitutePlayerIds")] IReadOnlyList<int> OutfieldSubstitutePlayerIds);

public sealed record SourceObservationDocument(
    [property: JsonPropertyName("observationId")] long ObservationId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("supersedesObservationId")] long? SupersedesObservationId,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("metric")] string Metric,
    [property: JsonPropertyName("value")] decimal Value,
    [property: JsonPropertyName("observedAtUtc")] DateTimeOffset ObservedAtUtc,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc);
