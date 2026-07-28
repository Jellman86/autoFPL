using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record HistoricalFplIdentityCoverageDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("identityRule")] string IdentityRule,
    [property: JsonPropertyName("usesNameFallback")] bool UsesNameFallback,
    [property: JsonPropertyName("fromSeason")]
        HistoricalFplIdentityCoverageSeasonDocument FromSeason,
    [property: JsonPropertyName("toSeason")]
        HistoricalFplIdentityCoverageSeasonDocument ToSeason,
    [property: JsonPropertyName("sharedPlayerCodeCount")] int SharedPlayerCodeCount,
    [property: JsonPropertyName("departedPlayerCodeCount")] int DepartedPlayerCodeCount,
    [property: JsonPropertyName("introducedPlayerCodeCount")]
        int IntroducedPlayerCodeCount,
    [property: JsonPropertyName("fromSeasonRetentionFraction")]
        decimal FromSeasonRetentionFraction,
    [property: JsonPropertyName("toSeasonPriorIdentityCoverageFraction")]
        decimal ToSeasonPriorIdentityCoverageFraction,
    [property: JsonPropertyName("identitySetSha256")] string IdentitySetSha256);

public sealed record HistoricalFplIdentityCoverageSeasonDocument(
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("captureId")] long CaptureId,
    [property: JsonPropertyName("sourceRevision")] string SourceRevision,
    [property: JsonPropertyName("playersSha256")] string PlayersSha256,
    [property: JsonPropertyName("gameweeksSha256")] string GameweeksSha256,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("playerCount")] int PlayerCount,
    [property: JsonPropertyName("stableCodeCount")] int StableCodeCount);
