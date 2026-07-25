using System.Text.Json.Serialization;

using AutoFpl.Contracts.Squads;

namespace AutoFpl.Contracts.Lineups;

public sealed record LineupValidationRequest(
    [property: JsonPropertyName("budgetTenths")] int? BudgetTenths,
    [property: JsonPropertyName("players")] IReadOnlyList<SquadPlayerRequest?>? Players,
    [property: JsonPropertyName("startingPlayerIds")] IReadOnlyList<int?>? StartingPlayerIds,
    [property: JsonPropertyName("captainPlayerId")] int? CaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")] int? ViceCaptainPlayerId);
