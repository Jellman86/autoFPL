using System.Text.Json.Serialization;

using AutoFpl.Domain.Outcomes;

namespace AutoFpl.Contracts.Outcomes;

public sealed record GameweekSubstitutionResolutionDocument(
    [property: JsonPropertyName("originalStartingPlayerIds")] IReadOnlyList<int> OriginalStartingPlayerIds,
    [property: JsonPropertyName("effectivePlayerIds")] IReadOnlyList<int> EffectivePlayerIds,
    [property: JsonPropertyName("activatedSubstitutePlayerIds")] IReadOnlyList<int> ActivatedSubstitutePlayerIds,
    [property: JsonPropertyName("unreplacedStartingPlayerIds")] IReadOnlyList<int> UnreplacedStartingPlayerIds)
{
    public static GameweekSubstitutionResolutionDocument FromDomain(
        GameweekSubstitutionResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return new(
            resolution.OriginalStartingPlayerIds,
            resolution.EffectivePlayerIds,
            resolution.ActivatedSubstitutePlayerIds,
            resolution.UnreplacedStartingPlayerIds);
    }
}
