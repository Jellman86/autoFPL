using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class AutoFplApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public AutoFplApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/healthz", "healthy")]
    [InlineData("/readyz", "ready")]
    public async Task Probe_endpoint_reports_expected_status(string path, string expectedStatus)
    {
        using HttpResponseMessage response = await _client.GetAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(expectedStatus, body.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("synthetic")]
    public async Task Validation_accepts_supported_metadata(string sourceType)
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/v1/decision-snapshot-metadata/validation",
            new { schemaVersion = "1.0", sourceType },
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("1.0", body.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(sourceType, body.RootElement.GetProperty("sourceType").GetString());
    }

    [Theory]
    [InlineData("2.0", "manual", "snapshot.schema_version.unsupported", "schemaVersion")]
    [InlineData("1.0", "api", "snapshot.source_type.unsupported", "sourceType")]
    [InlineData("", "manual", "snapshot.schema_version.unsupported", "schemaVersion")]
    [InlineData("1.0", "", "snapshot.source_type.unsupported", "sourceType")]
    public async Task Validation_returns_stable_problem_for_invalid_metadata(
        string? schemaVersion,
        string? sourceType,
        string expectedCode,
        string expectedField)
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/v1/decision-snapshot-metadata/validation",
            new { schemaVersion, sourceType },
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(422, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Decision snapshot metadata is invalid.", body.RootElement.GetProperty("title").GetString());
        Assert.Equal($"urn:autofpl:error:{expectedCode}", body.RootElement.GetProperty("type").GetString());
        Assert.Equal(expectedCode, body.RootElement.GetProperty("code").GetString());
        Assert.Equal(expectedField, body.RootElement.GetProperty("field").GetString());
        Assert.False(body.RootElement.TryGetProperty("value", out _));
    }

    [Fact]
    public async Task Validation_rejects_undeclared_json_properties()
    {
        using var content = new StringContent(
            """{"schemaVersion":"1.0","sourceType":"manual","unexpected":true}""",
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await _client.PostAsync(
            "/api/v1/decision-snapshot-metadata/validation",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{\"SchemaVersion\":\"1.0\",\"SourceType\":\"manual\"}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"sourceType\":")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"schemaVersion\":\"1.0\",\"sourceType\":\"manual\"}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"schemaVersion\":\"2.0\",\"sourceType\":\"manual\"}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"sourceType\":\"manual\",\"sourceType\":\"synthetic\"}")]
    [InlineData("{\"schemaVersion\":1,\"sourceType\":\"manual\"}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"sourceType\":{}}")]
    [InlineData("{\"schemaVersion\":null,\"sourceType\":\"manual\"}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"sourceType\":null}")]
    [InlineData("{\"sourceType\":\"manual\"}")]
    [InlineData("{\"schemaVersion\":\"1.0\"}")]
    [InlineData("[]")]
    public async Task Validation_rejects_noncanonical_malformed_or_duplicate_json(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _client.PostAsync(
            "/api/v1/decision-snapshot-metadata/validation",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
