using System.Text.Json.Serialization;

using AutoFpl.Contracts.Squads;

namespace AutoFpl.Contracts.Outcomes;

public sealed record GameweekSubstitutionResolutionRequest(
    [property: JsonPropertyName("budgetTenths")] int? BudgetTenths,
    [property: JsonPropertyName("players")] IReadOnlyList<SquadPlayerRequest?>? Players,
    [property: JsonPropertyName("startingPlayerIds")] IReadOnlyList<int?>? StartingPlayerIds,
    [property: JsonPropertyName("captainPlayerId")] int? CaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")] int? ViceCaptainPlayerId,
    [property: JsonPropertyName("replacementGoalkeeperPlayerId")] int? ReplacementGoalkeeperPlayerId,
    [property: JsonPropertyName("outfieldSubstitutePlayerIds")] IReadOnlyList<int?>? OutfieldSubstitutePlayerIds,
    [property: JsonPropertyName("playerIdsWhoPlayed")] IReadOnlyList<int?>? PlayerIdsWhoPlayed);
