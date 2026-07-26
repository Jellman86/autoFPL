using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class OpenApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public OpenApiContractTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task V1_document_exposes_the_supported_http_contract()
    {
        using HttpResponseMessage response = await _client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        await using Stream content = await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(
            content,
            cancellationToken: TestContext.Current.CancellationToken);

        JsonElement root = document.RootElement;
        Assert.StartsWith("3.1.", root.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.Equal("1.0.0", root.GetProperty("info").GetProperty("version").GetString());

        JsonElement paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/advice/demo", out JsonElement advicePath));
        Assert.Equal(
            "GetDemoGameweekAdvice",
            advicePath.GetProperty("get").GetProperty("operationId").GetString());
        Assert.True(paths.TryGetProperty("/api/v1/squads/validation", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/decision-snapshots",
                out JsonElement decisionSnapshotsPath));
        Assert.Equal(
            "CreateDecisionSnapshot",
            decisionSnapshotsPath.GetProperty("post").GetProperty("operationId").GetString());
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/decision-snapshots/{snapshotId}",
                out JsonElement decisionSnapshotPath));
        Assert.Equal(
            "GetDecisionSnapshot",
            decisionSnapshotPath.GetProperty("get").GetProperty("operationId").GetString());
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/data/official-fpl/latest",
                out JsonElement officialFplPath));
        Assert.Equal(
            "GetLatestOfficialFplCapture",
            officialFplPath.GetProperty("get").GetProperty("operationId").GetString());
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/data/fpl-form-forecast/latest",
                out JsonElement fplFormForecastPath));
        Assert.Equal(
            "GetLatestFplFormForecastCapture",
            fplFormForecastPath
                .GetProperty("get")
                .GetProperty("operationId")
                .GetString());
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/pre-deadline",
                out JsonElement officialFplReplayPath));
        Assert.Equal(
            "GetOfficialFplPreDeadlineReplay",
            officialFplReplayPath.GetProperty("get").GetProperty("operationId").GetString());
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/players/{playerId}",
                out JsonElement officialFplPlayerDossierPath));
        Assert.Equal(
            "GetOfficialFplPlayerDossier",
            officialFplPlayerDossierPath
                .GetProperty("get")
                .GetProperty("operationId")
                .GetString());
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/data/official-fpl/outcomes/{seasonCode}/{gameweek}/latest",
                out JsonElement officialFplOutcomePath));
        Assert.Equal(
            "GetLatestOfficialFplOutcome",
            officialFplOutcomePath.GetProperty("get").GetProperty("operationId").GetString());
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek}/outcome",
                out JsonElement officialFplReplayOutcomePath));
        Assert.Equal(
            "GetOfficialFplReplayOutcome",
            officialFplReplayOutcomePath
                .GetProperty("get")
                .GetProperty("operationId")
                .GetString());
        Assert.True(paths.TryGetProperty("/api/v1/gameweek-outcomes/effective-score", out _));
    }

    [Fact]
    public async Task Decision_snapshot_write_operation_has_typed_request_and_created_response()
    {
        using JsonDocument document = await GetDocumentAsync();
        JsonElement operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/decision-snapshots")
            .GetProperty("post");

        Assert.True(
            operation
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .TryGetProperty("$ref", out _));
        Assert.True(
            operation
                .GetProperty("responses")
                .GetProperty("201")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .TryGetProperty("$ref", out _));
    }

    [Fact]
    public async Task Advice_operation_describes_success_schema_and_has_no_security_secrets()
    {
        using JsonDocument document = await GetDocumentAsync();
        JsonElement root = document.RootElement;
        JsonElement operation = root
            .GetProperty("paths")
            .GetProperty("/api/v1/advice/demo")
            .GetProperty("get");
        JsonElement response = operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        Assert.True(response.TryGetProperty("$ref", out _));

        string rawDocument = root.GetRawText();
        Assert.DoesNotContain("apiKey", rawDocument, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", rawDocument, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", rawDocument, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        Stream content = await _client.GetStreamAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        return await JsonDocument.ParseAsync(
            content,
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
