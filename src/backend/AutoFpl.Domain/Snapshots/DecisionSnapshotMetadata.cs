namespace AutoFpl.Domain.Snapshots;

public enum DecisionSnapshotSourceType
{
    Manual,
    Synthetic,
}

public sealed record DecisionSnapshotMetadata
{
    private DecisionSnapshotMetadata(
        string schemaVersion,
        DecisionSnapshotSourceType sourceType)
    {
        SchemaVersion = schemaVersion;
        SourceType = sourceType;
    }

    public string SchemaVersion { get; }

    public DecisionSnapshotSourceType SourceType { get; }

    public static DecisionSnapshotMetadata Create(
        string schemaVersion,
        string sourceType)
    {
        if (!StringComparer.Ordinal.Equals(schemaVersion, "1.0"))
        {
            throw new DecisionSnapshotValidationException(
                code: "snapshot.schema_version.unsupported",
                field: "schemaVersion");
        }

        DecisionSnapshotSourceType parsedSourceType;
        if (StringComparer.Ordinal.Equals(sourceType, "manual"))
        {
            parsedSourceType = DecisionSnapshotSourceType.Manual;
        }
        else if (StringComparer.Ordinal.Equals(sourceType, "synthetic"))
        {
            parsedSourceType = DecisionSnapshotSourceType.Synthetic;
        }
        else
        {
            throw new DecisionSnapshotValidationException(
                code: "snapshot.source_type.unsupported",
                field: "sourceType");
        }

        return new(schemaVersion, parsedSourceType);
    }
}
