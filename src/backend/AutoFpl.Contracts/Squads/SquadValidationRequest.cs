using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Squads;

public sealed record SquadValidationRequest(
    [property: JsonPropertyName("budgetTenths")] int? BudgetTenths,
    [property: JsonPropertyName("players")] IReadOnlyList<SquadPlayerRequest?>? Players);

public sealed record SquadPlayerRequest(
    [property: JsonPropertyName("playerId")] int? PlayerId,
    [property: JsonPropertyName("clubId")] int? ClubId,
    [property: JsonPropertyName("position")] string? Position,
    [property: JsonPropertyName("priceTenths")] int? PriceTenths);
