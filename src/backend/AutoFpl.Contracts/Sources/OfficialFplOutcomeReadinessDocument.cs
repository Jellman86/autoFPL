using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record OfficialFplOutcomeReadinessDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("officialCaptureId")] long OfficialCaptureId,
    [property: JsonPropertyName("officialAvailableAtUtc")]
        DateTimeOffset OfficialAvailableAtUtc,
    [property: JsonPropertyName("latestCompletedGameweek")]
        int? LatestCompletedGameweek,
    [property: JsonPropertyName("capturedOutcomeGameweeks")]
        IReadOnlyList<int> CapturedOutcomeGameweeks,
    [property: JsonPropertyName("pairedGameweeks")]
        IReadOnlyList<int> PairedGameweeks,
    [property: JsonPropertyName("missingOutcomeGameweeks")]
        IReadOnlyList<int> MissingOutcomeGameweeks,
    [property: JsonPropertyName("missingReplayGameweeks")]
        IReadOnlyList<int> MissingReplayGameweeks,
    [property: JsonPropertyName("incompletePairGameweeks")]
        IReadOnlyList<int> IncompletePairGameweeks,
    [property: JsonPropertyName("maximumOutcomeImportsPerPoll")]
        int MaximumOutcomeImportsPerPoll);
