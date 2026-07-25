namespace AutoFpl.Api.Persistence;

public sealed record DecisionSnapshotCommand(
    string SchemaVersion,
    string SeasonCode,
    int Gameweek,
    DateTimeOffset DeadlineUtc,
    DateTimeOffset DecisionCutoffUtc,
    int BudgetTenths,
    IReadOnlyList<SnapshotPlayer> Players,
    IReadOnlyList<int> StartingPlayerIds,
    int CaptainPlayerId,
    int ViceCaptainPlayerId,
    int ReplacementGoalkeeperPlayerId,
    IReadOnlyList<int> OutfieldSubstitutePlayerIds,
    IReadOnlyList<SnapshotObservation> Observations,
    long? SupersedesSnapshotId);

public sealed record SnapshotPlayer(
    int PlayerId,
    string DisplayName,
    int ClubId,
    string Position,
    int PriceTenths);

public sealed record SnapshotObservation(
    string SourceKey,
    int PlayerId,
    string Metric,
    decimal Value,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset AvailableAtUtc,
    long? SupersedesObservationId);
