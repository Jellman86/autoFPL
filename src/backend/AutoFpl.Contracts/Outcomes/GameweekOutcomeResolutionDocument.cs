using System.Text.Json.Serialization;

using AutoFpl.Domain.Outcomes;

namespace AutoFpl.Contracts.Outcomes;

public sealed record GameweekOutcomeResolutionDocument(
    [property: JsonPropertyName("originalStartingPlayerIds")] IReadOnlyList<int> OriginalStartingPlayerIds,
    [property: JsonPropertyName("effectivePlayerIds")] IReadOnlyList<int> EffectivePlayerIds,
    [property: JsonPropertyName("activatedSubstitutePlayerIds")] IReadOnlyList<int> ActivatedSubstitutePlayerIds,
    [property: JsonPropertyName("unreplacedStartingPlayerIds")] IReadOnlyList<int> UnreplacedStartingPlayerIds,
    [property: JsonPropertyName("originalCaptainPlayerId")] int OriginalCaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")] int ViceCaptainPlayerId,
    [property: JsonPropertyName("effectiveCaptainPlayerId")] int? EffectiveCaptainPlayerId,
    [property: JsonPropertyName("captaincyTransferred")] bool CaptaincyTransferred)
{
    public static GameweekOutcomeResolutionDocument FromDomain(GameweekOutcomeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return new(
            resolution.OriginalStartingPlayerIds,
            resolution.EffectivePlayerIds,
            resolution.ActivatedSubstitutePlayerIds,
            resolution.UnreplacedStartingPlayerIds,
            resolution.OriginalCaptainPlayerId,
            resolution.ViceCaptainPlayerId,
            resolution.EffectiveCaptainPlayerId,
            resolution.CaptaincyTransferred);
    }
}
