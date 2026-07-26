using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Selections;

public sealed record SelectionDraftFromForecastRequest(
    [property: JsonPropertyName("forecastArtifactId")] long? ForecastArtifactId);

public sealed record SelectionRevisionEditRequest(
    [property: JsonPropertyName("startingPlayerIds")]
        IReadOnlyList<int?>? StartingPlayerIds,
    [property: JsonPropertyName("captainPlayerId")] int? CaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")] int? ViceCaptainPlayerId,
    [property: JsonPropertyName("replacementGoalkeeperPlayerId")]
        int? ReplacementGoalkeeperPlayerId,
    [property: JsonPropertyName("outfieldSubstitutePlayerIds")]
        IReadOnlyList<int?>? OutfieldSubstitutePlayerIds);

public sealed record SelectionRevisionDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("selectionRevisionId")] long SelectionRevisionId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("supersedesSelectionRevisionId")]
        long? SupersedesSelectionRevisionId,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("deadlineUtc")] DateTimeOffset DeadlineUtc,
    [property: JsonPropertyName("forecastArtifactId")] long ForecastArtifactId,
    [property: JsonPropertyName("forecastArtifactContentHash")]
        string ForecastArtifactContentHash,
    [property: JsonPropertyName("selectionContentHash")] string SelectionContentHash,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("lockedAtUtc")] DateTimeOffset? LockedAtUtc,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("canLock")] bool CanLock,
    [property: JsonPropertyName("selection")] LockedSelectionDocument Selection);

public sealed record LockedSelectionDocument(
    [property: JsonPropertyName("startingPlayerIds")]
        IReadOnlyList<int> StartingPlayerIds,
    [property: JsonPropertyName("captainPlayerId")] int CaptainPlayerId,
    [property: JsonPropertyName("viceCaptainPlayerId")] int ViceCaptainPlayerId,
    [property: JsonPropertyName("replacementGoalkeeperPlayerId")]
        int ReplacementGoalkeeperPlayerId,
    [property: JsonPropertyName("outfieldSubstitutePlayerIds")]
        IReadOnlyList<int> OutfieldSubstitutePlayerIds);
