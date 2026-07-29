using System.Net;
using System.Net.Http.Json;

using AutoFpl.Contracts.Advice;

using Microsoft.AspNetCore.Mvc.Testing;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class AdvicePreviewTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AdvicePreviewTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Mcp_endpoint_advertises_only_public_read_only_tools()
    {
        await using var transport = new HttpClientTransport(
            new()
            {
                Endpoint = new Uri(_client.BaseAddress!, "/mcp"),
                Name = "autoFPL inventory test",
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            _client);
        await using McpClient mcpClient = await McpClient.CreateAsync(
            transport,
            cancellationToken: TestContext.Current.CancellationToken);

        IList<McpClientTool> tools =
            await mcpClient.ListToolsAsync(
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                "get_current_prediction",
                "get_current_strategies",
                "get_player_dossier",
            ],
            tools.Select(tool => tool.Name).Order().ToArray());
        Assert.All(
            tools,
            tool =>
            {
                Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
                Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint);
                Assert.False(tool.ProtocolTool.Annotations?.OpenWorldHint);
            });

        McpClientTool tool = Assert.Single(
            tools,
            candidate => candidate.Name == "get_player_dossier");
        Assert.Contains(
            "quarantined",
            tool.Description,
            StringComparison.OrdinalIgnoreCase);

        McpClientTool predictionTool = Assert.Single(
            tools,
            candidate => candidate.Name == "get_current_prediction");
        Assert.Contains(
            "provisional",
            predictionTool.Description,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(predictionTool.ProtocolTool.OutputSchema);

        McpClientTool strategyTool = Assert.Single(
            tools,
            candidate => candidate.Name == "get_current_strategies");
        Assert.Contains(
            "prospective shadow",
            strategyTool.Description,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "not global",
            strategyTool.Description,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(strategyTool.ProtocolTool.OutputSchema);

        CallToolResult invalid = await mcpClient.CallToolAsync(
            "get_player_dossier",
            new Dictionary<string, object?>
            {
                ["seasonCode"] = "invalid",
                ["gameweek"] = 1,
                ["playerId"] = 1,
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(invalid.IsError);
        Assert.Contains(
            "seasonCode",
            Assert.Single(invalid.Content.OfType<TextContentBlock>()).Text,
            StringComparison.Ordinal);

        CallToolResult missingPrediction = await mcpClient.CallToolAsync(
            "get_current_prediction",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(missingPrediction.IsError);
        Assert.Contains(
            "No persisted official",
            Assert.Single(
                missingPrediction.Content.OfType<TextContentBlock>()).Text,
            StringComparison.Ordinal);

        CallToolResult missingStrategies = await mcpClient.CallToolAsync(
            "get_current_strategies",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(missingStrategies.IsError);
        Assert.Contains(
            "No current autoFPL strategy",
            Assert.Single(
                missingStrategies.Content.OfType<TextContentBlock>()).Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decision_room_is_served_from_the_application_root()
    {
        using HttpResponseMessage response = await _client.GetAsync(
            "/",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Gameweek squad plan", body, StringComparison.Ordinal);
        Assert.Contains("Primary navigation", body, StringComparison.Ordinal);
        Assert.Contains("Breadcrumb", body, StringComparison.Ordinal);
        Assert.Contains("Model squad", body, StringComparison.Ordinal);
        Assert.Contains("Strategies", body, StringComparison.Ordinal);
        Assert.Contains("My squad", body, StringComparison.Ordinal);
        Assert.Contains(
            "Compare matchday strategies",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "id=\"strategy-list\"",
            body,
            StringComparison.Ordinal);
        Assert.Contains("Ask about this exact selection", body, StringComparison.Ordinal);
        Assert.Contains("Player evidence", body, StringComparison.Ordinal);
        Assert.Contains("Back to squad", body, StringComparison.Ordinal);
        Assert.Contains("Recent form", body, StringComparison.Ordinal);
        Assert.Contains("Research tape", body, StringComparison.Ordinal);
        Assert.Contains("Quarantined · not used", body, StringComparison.Ordinal);
        Assert.Contains("does not change", body, StringComparison.Ordinal);
        Assert.Contains(
            "Open a player to see the full evidence page",
            body,
            StringComparison.Ordinal);
        Assert.Contains("Official data footing", body, StringComparison.Ordinal);
        Assert.Contains("Capture to deadline provenance", body, StringComparison.Ordinal);
        Assert.Contains("External forecast challenger", body, StringComparison.Ordinal);
        Assert.Contains("Refresh prediction", body, StringComparison.Ordinal);
        Assert.Contains("Selection lifecycle", body, StringComparison.Ordinal);
        Assert.Contains("id=\"selection-workflow\"", body, StringComparison.Ordinal);
        Assert.Contains("id=\"my-squad\"", body, StringComparison.Ordinal);
        Assert.Contains("Use prediction as draft", body, StringComparison.Ordinal);
        Assert.Contains("Build your Gameweek squad", body, StringComparison.Ordinal);
        Assert.Contains("Exact-snapshot player pool", body, StringComparison.Ordinal);
        Assert.Contains("Squad check", body, StringComparison.Ordinal);
        Assert.Contains("Choose a replacement", body, StringComparison.Ordinal);
        Assert.Contains("Save new draft", body, StringComparison.Ordinal);
        Assert.Contains("Lock this selection?", body, StringComparison.Ordinal);
        Assert.Contains("does not submit or change", body, StringComparison.Ordinal);
        Assert.Contains("<dt>Outcome</dt>", body, StringComparison.Ordinal);
        Assert.Contains(
            "<dt id=\"artifact-reference-label\">Snapshot</dt>",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "Two-season research shadow",
            body,
            StringComparison.Ordinal);
        string script = await _client.GetStringAsync(
            "/app.js",
            TestContext.Current.CancellationToken);
        Assert.Contains(
            "priorSeasonIdentityStatus !== \"stable-code-match\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "official availability shown, not modelled",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "matchedCurrentSeasonTreeFoldWins",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "advice.selection.expectedPoints + userDelta",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "/api/v1/selections/current/role-strategies-shadow",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Preview only; your saved selection is unchanged.",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "baseRevision.forecastArtifactId !== advice.forecastArtifactId",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "matched-by-stable-code",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_advice_exposes_a_complete_feasible_selection_contract()
    {
        GameweekAdviceDocument? advice = await _client.GetFromJsonAsync<GameweekAdviceDocument>(
            "/api/v1/advice/demo",
            TestContext.Current.CancellationToken);

        Assert.NotNull(advice);
        Assert.True(advice.IsSynthetic);
        Assert.Equal("synthetic-persisted", advice.EvidenceStatus);
        Assert.NotNull(advice.SnapshotId);
        Assert.Equal(1, advice.SnapshotRevision);
        Assert.Equal(DateTimeOffset.Parse("2026-08-14T18:30:00Z"), advice.DecisionCutoffUtc);
        Assert.Equal(64, advice.SnapshotContentHash!.Length);
        Assert.Equal(15, advice.Selection.Players.Count);
        Assert.Equal(11, advice.Selection.Players.Count(player => player.LineupPlace == "starting"));
        Assert.Equal(4, advice.Selection.Players.Count(player => player.LineupPlace == "bench"));
        Assert.Single(advice.Selection.Players, player => player.Captaincy == "captain");
        Assert.Single(advice.Selection.Players, player => player.Captaincy == "vice-captain");
        Assert.Equal(
            [1, 2, 3, 4],
            advice.Selection.Players
                .Where(player => player.LineupPlace == "bench")
                .Select(player => player.BenchOrder)
                .Order()
                .ToArray());
        Assert.All(advice.Selection.Players, player =>
        {
            Assert.NotEmpty(player.Reasons);
            Assert.NotEmpty(player.Risks);
            Assert.True(player.Lower80 <= player.ExpectedPoints);
            Assert.True(player.ExpectedPoints <= player.Upper80);
        });
    }

    [Fact]
    public async Task Demo_advice_is_explicit_about_unavailable_ai_access()
    {
        GameweekAdviceDocument? advice = await _client.GetFromJsonAsync<GameweekAdviceDocument>(
            "/api/v1/advice/demo",
            TestContext.Current.CancellationToken);

        Assert.NotNull(advice);
        Assert.False(advice.AiAccess.Available);
        Assert.Contains("chatgpt-plugin", advice.AiAccess.PlannedModes);
        Assert.Contains("mcp", advice.AiAccess.PlannedModes);
        Assert.Contains("server-api-key", advice.AiAccess.PlannedModes);
    }
}
