using System.Text.Json.Serialization;

using AutoFpl.Domain.Lineups;

namespace AutoFpl.Contracts.Lineups;

public sealed record LineupValidationDocument(
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("goalkeeperCount")] int GoalkeeperCount,
    [property: JsonPropertyName("defenderCount")] int DefenderCount,
    [property: JsonPropertyName("midfielderCount")] int MidfielderCount,
    [property: JsonPropertyName("forwardCount")] int ForwardCount,
    [property: JsonPropertyName("formation")] string Formation,
    [property: JsonPropertyName("captainPlayerId")] int CaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")] int ViceCaptainPlayerId)
{
    public static LineupValidationDocument FromDomain(Lineup lineup)
    {
        ArgumentNullException.ThrowIfNull(lineup);
        return new(
            lineup.PlayerCount,
            lineup.GoalkeeperCount,
            lineup.DefenderCount,
            lineup.MidfielderCount,
            lineup.ForwardCount,
            lineup.Formation,
            lineup.CaptainPlayerId,
            lineup.ViceCaptainPlayerId);
    }
}
