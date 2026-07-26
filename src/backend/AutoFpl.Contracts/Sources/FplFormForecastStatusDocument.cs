using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record FplFormForecastStatusDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("checkedAtUtc")] DateTimeOffset? CheckedAtUtc,
    [property: JsonPropertyName("reasonCode")] string? ReasonCode,
    [property: JsonPropertyName("latestCapture")]
        FplFormForecastCaptureDocument? LatestCapture);
