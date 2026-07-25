using System.Text.Json.Serialization;

using AutoFpl.Domain.Outcomes;

namespace AutoFpl.Contracts.Outcomes;

public sealed record EffectivePlayerScoreDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("points")] int Points,
    [property: JsonPropertyName("multiplier")] int Multiplier,
    [property: JsonPropertyName("countedPoints")] long CountedPoints)
{
    public static EffectivePlayerScoreDocument FromDomain(EffectivePlayerScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        return new(score.PlayerId, score.Points, score.Multiplier, score.CountedPoints);
    }
}
