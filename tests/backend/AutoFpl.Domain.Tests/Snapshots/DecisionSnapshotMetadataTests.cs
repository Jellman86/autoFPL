using AutoFpl.Domain.Snapshots;

namespace AutoFpl.Domain.Tests.Snapshots;

public sealed class DecisionSnapshotMetadataTests
{
    [Fact]
    public void CreateAcceptsSupportedVersionAndManualSource()
    {
        DecisionSnapshotMetadata metadata = DecisionSnapshotMetadata.Create(
            schemaVersion: "1.0",
            sourceType: "manual");

        Assert.Equal("1.0", metadata.SchemaVersion);
        Assert.Equal(DecisionSnapshotSourceType.Manual, metadata.SourceType);
    }

    [Fact]
    public void CreateAcceptsSyntheticSource()
    {
        DecisionSnapshotMetadata metadata = DecisionSnapshotMetadata.Create(
            schemaVersion: "1.0",
            sourceType: "synthetic");

        Assert.Equal(DecisionSnapshotSourceType.Synthetic, metadata.SourceType);
    }

    [Fact]
    public void CreateRejectsUnsupportedSchemaVersionWithStableCode()
    {
        DecisionSnapshotValidationException exception = Assert.Throws<DecisionSnapshotValidationException>(
            () => DecisionSnapshotMetadata.Create(
                schemaVersion: "2.0",
                sourceType: "manual"));

        Assert.Equal("snapshot.schema_version.unsupported", exception.Code);
        Assert.Equal("schemaVersion", exception.Field);
    }

    [Fact]
    public void CreateRejectsUnsupportedSourceTypeWithStableCode()
    {
        DecisionSnapshotValidationException exception = Assert.Throws<DecisionSnapshotValidationException>(
            () => DecisionSnapshotMetadata.Create(
                schemaVersion: "1.0",
                sourceType: "api"));

        Assert.Equal("snapshot.source_type.unsupported", exception.Code);
        Assert.Equal("sourceType", exception.Field);
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("SYNTHETIC")]
    [InlineData(" synthetic")]
    public void CreateRejectsNonCanonicalSourceType(string sourceType)
    {
        DecisionSnapshotValidationException exception = Assert.Throws<DecisionSnapshotValidationException>(
            () => DecisionSnapshotMetadata.Create(
                schemaVersion: "1.0",
                sourceType: sourceType));

        Assert.Equal("snapshot.source_type.unsupported", exception.Code);
    }
}
