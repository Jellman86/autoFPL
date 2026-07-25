using System.Text.Json.Serialization;

using AutoFpl.Domain.Outcomes;

namespace AutoFpl.Contracts.Outcomes;

public sealed record GameweekCaptaincyResolutionDocument(
    [property: JsonPropertyName("originalCaptainPlayerId")] int OriginalCaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")] int ViceCaptainPlayerId,
    [property: JsonPropertyName("effectiveCaptainPlayerId")] int? EffectiveCaptainPlayerId,
    [property: JsonPropertyName("captaincyTransferred")] bool CaptaincyTransferred)
{
    public static GameweekCaptaincyResolutionDocument FromDomain(
        GameweekCaptaincyResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return new(
            resolution.OriginalCaptainPlayerId,
            resolution.ViceCaptainPlayerId,
            resolution.EffectiveCaptainPlayerId,
            resolution.CaptaincyTransferred);
    }
}
