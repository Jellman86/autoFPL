using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record OfficialFplReplayOutcomeDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("replay")] OfficialFplReplayDocument Replay,
    [property: JsonPropertyName("outcome")] OfficialFplOutcomeCaptureDocument Outcome,
    [property: JsonPropertyName("matchedPlayerCount")] int MatchedPlayerCount);
