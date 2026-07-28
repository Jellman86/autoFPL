using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Selections;

public sealed record SelectionComparisonDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("selectionRevisionId")] long SelectionRevisionId,
    [property: JsonPropertyName("forecastArtifactId")] long ForecastArtifactId,
    [property: JsonPropertyName("playerForecastArtifactId")]
        long PlayerForecastArtifactId,
    [property: JsonPropertyName("playerForecastArtifactContentHash")]
        string PlayerForecastArtifactContentHash,
    [property: JsonPropertyName("distributionStatus")] string DistributionStatus,
    [property: JsonPropertyName("model")] SelectionProjectionDocument Model,
    [property: JsonPropertyName("user")] SelectionProjectionDocument User,
    [property: JsonPropertyName("projectedPointsDelta")] decimal ProjectedPointsDelta,
    [property: JsonPropertyName("playersAdded")] IReadOnlyList<int> PlayersAdded,
    [property: JsonPropertyName("playersRemoved")] IReadOnlyList<int> PlayersRemoved,
    [property: JsonPropertyName("limitations")] IReadOnlyList<string> Limitations);

public sealed record SelectionProjectionDocument(
    [property: JsonPropertyName("projectedPoints")] decimal ProjectedPoints,
    [property: JsonPropertyName("startingExpectedPoints")]
        decimal StartingExpectedPoints,
    [property: JsonPropertyName("captainBonus")] decimal CaptainBonus,
    [property: JsonPropertyName("squadCostTenths")] int SquadCostTenths,
    [property: JsonPropertyName("remainingBudgetTenths")] int RemainingBudgetTenths,
    [property: JsonPropertyName("squadPlayerIds")] IReadOnlyList<int> SquadPlayerIds);
