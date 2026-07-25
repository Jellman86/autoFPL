using System.Text.Json.Serialization;

using AutoFpl.Domain.Outcomes;

namespace AutoFpl.Contracts.Outcomes;

public sealed record GameweekScoreResolutionDocument(
    [property: JsonPropertyName("outcome")] GameweekOutcomeResolutionDocument Outcome,
    [property: JsonPropertyName("effectivePlayerScores")] IReadOnlyList<EffectivePlayerScoreDocument> EffectivePlayerScores,
    [property: JsonPropertyName("basePoints")] long BasePoints,
    [property: JsonPropertyName("captainBonusPoints")] long CaptainBonusPoints,
    [property: JsonPropertyName("totalPoints")] long TotalPoints)
{
    public static GameweekScoreResolutionDocument FromDomain(GameweekScoreResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        EffectivePlayerScoreDocument[] scores = resolution.EffectivePlayerScores
            .Select(EffectivePlayerScoreDocument.FromDomain)
            .ToArray();
        return new(
            GameweekOutcomeResolutionDocument.FromDomain(resolution.Outcome),
            Array.AsReadOnly(scores),
            resolution.BasePoints,
            resolution.CaptainBonusPoints,
            resolution.TotalPoints);
    }
}
