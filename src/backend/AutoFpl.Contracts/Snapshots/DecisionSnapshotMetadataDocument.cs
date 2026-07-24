using System.Text.Json.Serialization;

using AutoFpl.Domain.Snapshots;

namespace AutoFpl.Contracts.Snapshots;

public sealed record DecisionSnapshotMetadataDocument
{
    private DecisionSnapshotMetadataDocument(string schemaVersion, string sourceType)
    {
        SchemaVersion = schemaVersion;
        SourceType = sourceType;
    }

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; }

    [JsonPropertyName("sourceType")]
    public string SourceType { get; }

    public static DecisionSnapshotMetadataDocument FromDomain(DecisionSnapshotMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string sourceType = metadata.SourceType switch
        {
            DecisionSnapshotSourceType.Manual => "manual",
            DecisionSnapshotSourceType.Synthetic => "synthetic",
            _ => throw new ArgumentOutOfRangeException(
                nameof(metadata),
                metadata.SourceType,
                "Unsupported decision snapshot source type."),
        };

        return new(metadata.SchemaVersion, sourceType);
    }
}
