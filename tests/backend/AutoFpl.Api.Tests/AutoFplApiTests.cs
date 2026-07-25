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

    [Fact]
    public async Task Squad_validation_returns_exact_budget_summary()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/v1/squads/validation",
            new { budgetTenths = 1_000, players = ValidPlayers() },
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(15, body.RootElement.GetProperty("playerCount").GetInt32());
        Assert.Equal(745, body.RootElement.GetProperty("totalCostTenths").GetInt32());
        Assert.Equal(1_000, body.RootElement.GetProperty("budgetTenths").GetInt32());
        Assert.Equal(255, body.RootElement.GetProperty("remainingBudgetTenths").GetInt32());
    }

    [Fact]
    public async Task Squad_validation_returns_stable_problem_for_infeasible_squad()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/v1/squads/validation",
            new { budgetTenths = 700, players = ValidPlayers() },
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("squad.budget.exceeded", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("budgetTenths", body.RootElement.GetProperty("field").GetString());
    }

    [Theory]
    [InlineData("{\"budgetTenths\":1000,\"players\":[],\"unexpected\":true}")]
    [InlineData("{\"budgetTenths\":1000,\"players\":\"not-an-array\"}")]
    [InlineData("{\"budgetTenths\":1000}")]
    [InlineData("{\"budgetTenths\":1000,\"players\":null}")]
    [InlineData("{\"players\":[]}")]
    [InlineData("[]")]
    public async Task Squad_validation_rejects_unknown_or_malformed_json(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _client.PostAsync(
            "/api/v1/squads/validation",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Squad_validation_rejects_duplicate_json_properties()
    {
        string body = JsonSerializer.Serialize(
            new { budgetTenths = 1_000, players = ValidPlayers() },
            JsonOptions);
        body = body.Replace(
            "\"budgetTenths\":1000",
            "\"budgetTenths\":1000,\"budgetTenths\":1000",
            StringComparison.Ordinal);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _client.PostAsync(
            "/api/v1/squads/validation",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lineup_validation_returns_formation_and_captaincy_summary()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/v1/lineups/validation",
            new
            {
                budgetTenths = 1_000,
                players = ValidPlayers(),
                startingPlayerIds = new[] { 1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14 },
                captainPlayerId = 8,
                viceCaptainPlayerId = 13,
            },
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(11, body.RootElement.GetProperty("playerCount").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("goalkeeperCount").GetInt32());
        Assert.Equal(3, body.RootElement.GetProperty("defenderCount").GetInt32());
        Assert.Equal(5, body.RootElement.GetProperty("midfielderCount").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("forwardCount").GetInt32());
        Assert.Equal("3-5-2", body.RootElement.GetProperty("formation").GetString());
        Assert.Equal(8, body.RootElement.GetProperty("captainPlayerId").GetInt32());
        Assert.Equal(13, body.RootElement.GetProperty("viceCaptainPlayerId").GetInt32());
    }

    [Fact]
    public async Task Lineup_validation_returns_stable_problem_for_invalid_formation()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/v1/lineups/validation",
            new
            {
                budgetTenths = 1_000,
                players = ValidPlayers(),
                startingPlayerIds = new[] { 1, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 },
                captainPlayerId = 8,
                viceCaptainPlayerId = 9,
            },
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(422, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Lineup is infeasible.", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "urn:autofpl:error:lineup.formation.invalid",
            body.RootElement.GetProperty("type").GetString());
        Assert.Equal("lineup.formation.invalid", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("startingPlayerIds", body.RootElement.GetProperty("field").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"budgetTenths\":1000,\"players\":[],\"startingPlayerIds\":[],\"captainPlayerId\":1}")]
    [InlineData("{\"budgetTenths\":null,\"players\":[],\"startingPlayerIds\":[],\"captainPlayerId\":1,\"viceCaptainPlayerId\":2}")]
    [InlineData("{\"budgetTenths\":1000,\"players\":null,\"startingPlayerIds\":[],\"captainPlayerId\":1,\"viceCaptainPlayerId\":2}")]
    [InlineData("{\"budgetTenths\":1000,\"players\":[],\"startingPlayerIds\":[null],\"captainPlayerId\":1,\"viceCaptainPlayerId\":2}")]
    [InlineData("{\"budgetTenths\":1000,\"players\":[null],\"startingPlayerIds\":[],\"captainPlayerId\":1,\"viceCaptainPlayerId\":2}")]
    [InlineData("{\"budgetTenths\":1000,\"players\":[{\"clubId\":1,\"position\":\"goalkeeper\",\"priceTenths\":45}],\"startingPlayerIds\":[],\"captainPlayerId\":1,\"viceCaptainPlayerId\":2}")]
    [InlineData("{\"budgetTenths\":1000,\"players\":[{\"playerId\":1,\"clubId\":1,\"position\":null,\"priceTenths\":45}],\"startingPlayerIds\":[],\"captainPlayerId\":1,\"viceCaptainPlayerId\":2}")]
    [InlineData("{\"budgetTenths\":1000,\"players\":[],\"startingPlayerIds\":\"bad\",\"captainPlayerId\":1,\"viceCaptainPlayerId\":2}")]
    [InlineData("{\"budgetTenths\":1000,\"budgetTenths\":1000,\"players\":[],\"startingPlayerIds\":[],\"captainPlayerId\":1,\"viceCaptainPlayerId\":2}")]
    [InlineData("{\"budgetTenths\":1000,\"players\":[],\"startingPlayerIds\":[],\"captainPlayerId\":1,\"viceCaptainPlayerId\":2,\"unexpected\":true}")]
    [InlineData("[]")]
    public async Task Lineup_validation_rejects_malformed_or_ambiguous_json(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _client.PostAsync(
            "/api/v1/lineups/validation",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static PlayerRequest[] ValidPlayers() =>
    [
        new(1, 1, "goalkeeper", 45),
        new(2, 2, "goalkeeper", 45),
        new(3, 1, "defender", 45),
        new(4, 2, "defender", 45),
        new(5, 3, "defender", 45),
        new(6, 4, "defender", 45),
        new(7, 5, "defender", 45),
        new(8, 1, "midfielder", 50),
        new(9, 2, "midfielder", 50),
        new(10, 3, "midfielder", 50),
        new(11, 4, "midfielder", 50),
        new(12, 5, "midfielder", 50),
        new(13, 3, "forward", 60),
        new(14, 4, "forward", 60),
        new(15, 5, "forward", 60),
    ];

    private sealed record PlayerRequest(
        int PlayerId,
        int ClubId,
        string Position,
        int PriceTenths);
}
