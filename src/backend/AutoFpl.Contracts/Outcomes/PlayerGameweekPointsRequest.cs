using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Outcomes;

public sealed record PlayerGameweekPointsRequest(
    [property: JsonPropertyName("playerId")] int? PlayerId,
    [property: JsonPropertyName("points")] int? Points);
