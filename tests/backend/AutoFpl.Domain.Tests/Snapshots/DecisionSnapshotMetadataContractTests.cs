using System.Text.Json;

using AutoFpl.Contracts.Snapshots;
using AutoFpl.Domain.Snapshots;

using NJsonSchema;

namespace AutoFpl.Domain.Tests.Snapshots;

public sealed class DecisionSnapshotMetadataContractTests
{
    private static readonly string ContractDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "contracts",
        "decision-snapshot",
        "v1");

    [Theory]
    [InlineData("manual.json")]
    [InlineData("synthetic.json")]
    public async Task Checked_in_examples_conform_to_the_contract(string exampleName)
    {
        JsonSchema schema = await LoadSchemaAsync();
        string json = await File.ReadAllTextAsync(
            Path.Combine(ContractDirectory, "examples", exampleName),
            TestContext.Current.CancellationToken);

        Assert.Empty(schema.Validate(json));
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("synthetic")]
    public async Task Domain_metadata_maps_to_a_document_that_serializes_to_the_contract(string sourceType)
    {
        JsonSchema schema = await LoadSchemaAsync();
        DecisionSnapshotMetadata metadata = DecisionSnapshotMetadata.Create("1.0", sourceType);
        DecisionSnapshotMetadataDocument document = DecisionSnapshotMetadataDocument.FromDomain(metadata);
        string json = JsonSerializer.Serialize(document);

        Assert.Empty(schema.Validate(json));
    }

    [Theory]
    [InlineData("""{"schemaVersion":"2.0","sourceType":"manual"}""")]
    [InlineData("""{"schemaVersion":"1.0","sourceType":"Manual"}""")]
    [InlineData("""{"schemaVersion":"1.0","sourceType":" manual"}""")]
    [InlineData("""{"schemaVersion":"1.0"}""")]
    [InlineData("""{"schemaVersion":"1.0","sourceType":"manual","unexpected":true}""")]
    public async Task Invalid_metadata_is_rejected(string json)
    {
        JsonSchema schema = await LoadSchemaAsync();

        Assert.NotEmpty(schema.Validate(json));
    }

    private static Task<JsonSchema> LoadSchemaAsync() =>
        JsonSchema.FromFileAsync(Path.Combine(ContractDirectory, "metadata.schema.json"));
}
