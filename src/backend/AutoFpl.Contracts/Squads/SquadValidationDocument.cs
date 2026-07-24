using System.Text.Json.Serialization;

using AutoFpl.Domain.Squads;

namespace AutoFpl.Contracts.Squads;

public sealed record SquadValidationDocument(
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("totalCostTenths")] int TotalCostTenths,
    [property: JsonPropertyName("budgetTenths")] int BudgetTenths,
    [property: JsonPropertyName("remainingBudgetTenths")] int RemainingBudgetTenths)
{
    public static SquadValidationDocument FromDomain(Squad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        return new(
            squad.PlayerCount,
            squad.TotalCostTenths,
            squad.BudgetTenths,
            squad.RemainingBudgetTenths);
    }
}
