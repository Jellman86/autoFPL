using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record FplFormIdentityCoverageDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isComplete")] bool IsComplete,
    [property: JsonPropertyName("forecastCaptureId")] long ForecastCaptureId,
    [property: JsonPropertyName("forecastContentSha256")] string ForecastContentSha256,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("forecastAvailableAtUtc")]
        DateTimeOffset ForecastAvailableAtUtc,
    [property: JsonPropertyName("officialCaptureId")] long? OfficialCaptureId,
    [property: JsonPropertyName("officialAvailableAtUtc")]
        DateTimeOffset? OfficialAvailableAtUtc,
    [property: JsonPropertyName("officialBootstrapSha256")]
        string? OfficialBootstrapSha256,
    [property: JsonPropertyName("officialFixturesSha256")]
        string? OfficialFixturesSha256,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset? DeadlineUtc,
    [property: JsonPropertyName("sourcePlayerCount")] int SourcePlayerCount,
    [property: JsonPropertyName("matchedPlayerCount")] int MatchedPlayerCount,
    [property: JsonPropertyName("directPlayerMatchCount")] int DirectPlayerMatchCount,
    [property: JsonPropertyName("fallbackPlayerMatchCount")] int FallbackPlayerMatchCount,
    [property: JsonPropertyName("unmatchedPlayerCount")] int UnmatchedPlayerCount,
    [property: JsonPropertyName("conflictingPlayerCount")] int ConflictingPlayerCount,
    [property: JsonPropertyName("sourcePredictionCount")] int SourcePredictionCount,
    [property: JsonPropertyName("matchedPredictionCount")] int MatchedPredictionCount,
    [property: JsonPropertyName("directFixtureMatchCount")] int DirectFixtureMatchCount,
    [property: JsonPropertyName("fallbackFixtureMatchCount")] int FallbackFixtureMatchCount,
    [property: JsonPropertyName("unmatchedPredictionCount")] int UnmatchedPredictionCount,
    [property: JsonPropertyName("conflictingPredictionCount")] int ConflictingPredictionCount,
    [property: JsonPropertyName("issues")]
        IReadOnlyList<FplFormIdentityCoverageIssueDocument> Issues);

public sealed record FplFormIdentityCoverageIssueDocument(
    [property: JsonPropertyName("entityType")] string EntityType,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("sourcePlayerId")] int SourcePlayerId,
    [property: JsonPropertyName("sourceFixtureId")] int? SourceFixtureId,
    [property: JsonPropertyName("playerName")] string PlayerName);
