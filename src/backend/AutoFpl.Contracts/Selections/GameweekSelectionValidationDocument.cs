using System.Text.Json.Serialization;

using AutoFpl.Domain.Selections;

namespace AutoFpl.Contracts.Selections;

public sealed record GameweekSelectionValidationDocument(
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("goalkeeperCount")] int GoalkeeperCount,
    [property: JsonPropertyName("defenderCount")] int DefenderCount,
    [property: JsonPropertyName("midfielderCount")] int MidfielderCount,
    [property: JsonPropertyName("forwardCount")] int ForwardCount,
    [property: JsonPropertyName("formation")] string Formation,
    [property: JsonPropertyName("captainPlayerId")] int CaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")] int ViceCaptainPlayerId,
    [property: JsonPropertyName("replacementGoalkeeperPlayerId")] int ReplacementGoalkeeperPlayerId,
    [property: JsonPropertyName("outfieldSubstitutePlayerIds")] IReadOnlyList<int> OutfieldSubstitutePlayerIds)
{
    public static GameweekSelectionValidationDocument FromDomain(GameweekSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new(
            selection.Lineup.PlayerCount,
            selection.Lineup.GoalkeeperCount,
            selection.Lineup.DefenderCount,
            selection.Lineup.MidfielderCount,
            selection.Lineup.ForwardCount,
            selection.Lineup.Formation,
            selection.Lineup.CaptainPlayerId,
            selection.Lineup.ViceCaptainPlayerId,
            selection.ReplacementGoalkeeperPlayerId,
            selection.OutfieldSubstitutePlayerIds);
    }
}
