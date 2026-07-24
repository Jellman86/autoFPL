using System.Text.Json.Nodes;

using NJsonSchema;

namespace AutoFpl.Domain.Tests.Sources;

public sealed class SourceRecordContractTests
{
    private static readonly string ContractDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "contracts",
        "data-source",
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

    [Fact]
    public async Task Missing_admission_evidence_is_rejected()
    {
        JsonSchema schema = await LoadSchemaAsync();
        JsonObject document = await LoadExampleAsync("manual.json");
        document.Remove("admissionRecord");

        Assert.NotEmpty(schema.Validate(document.ToJsonString()));
    }

    [Fact]
    public async Task Unknown_source_is_rejected()
    {
        JsonSchema schema = await LoadSchemaAsync();
        JsonObject document = await LoadExampleAsync("manual.json");
        document["sourceId"] = "external-fpl-feed/v1";

        Assert.NotEmpty(schema.Validate(document.ToJsonString()));
    }

    [Fact]
    public async Task Additional_fields_are_rejected()
    {
        JsonSchema schema = await LoadSchemaAsync();
        JsonObject document = await LoadExampleAsync("manual.json");
        document["providerPayload"] = true;

        Assert.NotEmpty(schema.Validate(document.ToJsonString()));
    }

    [Fact]
    public async Task Rights_cannot_be_cross_wired_between_sources()
    {
        JsonSchema schema = await LoadSchemaAsync();
        JsonObject manual = await LoadExampleAsync("manual.json");
        JsonObject synthetic = await LoadExampleAsync("synthetic.json");
        manual["rights"] = synthetic["rights"]?.DeepClone();

        Assert.NotEmpty(schema.Validate(manual.ToJsonString()));
    }

    private static Task<JsonSchema> LoadSchemaAsync() => JsonSchema.FromFileAsync(
        Path.Combine(ContractDirectory, "source-record.schema.json"),
        TestContext.Current.CancellationToken);

    private static async Task<JsonObject> LoadExampleAsync(string name)
    {
        string json = await File.ReadAllTextAsync(
            Path.Combine(ContractDirectory, "examples", name),
            TestContext.Current.CancellationToken);
        return JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException($"Example {name} is not a JSON object.");
    }
}
