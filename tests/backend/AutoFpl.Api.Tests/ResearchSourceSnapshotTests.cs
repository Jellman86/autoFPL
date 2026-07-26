using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Intelligence;
using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Intelligence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class ResearchSourceSnapshotTests
{
    private static readonly DateTimeOffset CaptureTime =
        new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RetrievalTime =
        new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Spider_capture_is_fixed_origin_immutable_deduplicated_and_shadow_only()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var handler = new SpiderMcpHandler(
            """
            ## Arsenal
            * Raya
            * Saliba
            Last Updated Sun 26th Jul
            """);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://spider-mcp:8080/mcp"),
        };
        var importer = new ResearchSourceSnapshotImporter(
            new SpiderMcpClient(httpClient),
            new ResearchSourceSnapshotStore(
                options,
                new FixedTimeProvider(RetrievalTime)));

        ResearchSourceSnapshotDocument first = await importer.ImportAsync(
            "ffscout-predicted-lineups",
            TestContext.Current.CancellationToken);
        ResearchSourceSnapshotDocument duplicate = await importer.ImportAsync(
            "ffscout-predicted-lineups",
            TestContext.Current.CancellationToken);

        Assert.Equal(first, duplicate);
        Assert.Equal("shadow-only", first.Status);
        Assert.Equal("specialist-predicted-lineup", first.SourceClass);
        Assert.Equal("ffscout-editorial-lineup", first.DependenceGroup);
        Assert.Equal("spider-mcp", first.TransportKey);
        Assert.Equal(1, first.SourceRevision);
        Assert.Equal(1, first.Gameweek);
        Assert.Equal(1, first.IdentityCaptureId);
        Assert.True(first.IsPreDeadline);
        Assert.Equal(64, first.ContentSha256.Length);
        Assert.True(first.ContentBytes > 0);
        Assert.Equal(2, handler.ScrapeCalls);
        Assert.Equal(2, handler.DeleteCalls);

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM research_source_snapshots;";
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync(
            TestContext.Current.CancellationToken))!);

        await using SqliteCommand readRaw = connection.CreateCommand();
        readRaw.CommandText =
            "SELECT content_brotli FROM research_source_snapshots WHERE snapshot_id = 1;";
        Assert.NotEmpty((byte[])(await readRaw.ExecuteScalarAsync(
            TestContext.Current.CancellationToken))!);

        await using SqliteCommand mutate = connection.CreateCommand();
        mutate.CommandText =
            "UPDATE research_source_snapshots SET status = 'changed' WHERE snapshot_id = 1;";
        SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
            async () => await mutate.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "research source snapshots are immutable",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inventory_api_exposes_diverse_registry_and_metadata_but_not_source_text()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var source = ResearchSourceRegistry.Get("premier-league-injuries");
        await new ResearchSourceSnapshotStore(
                options,
                new FixedTimeProvider(RetrievalTime))
            .PersistAsync(
                source,
                new SpiderScrapeResult(
                    source.CanonicalUri,
                    200,
                    "Private retained source text.",
                    "untrusted_remote_content"),
                TestContext.Current.CancellationToken);

        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(
                    builder =>
                    {
                        builder.UseSetting(
                            "AutoFpl:DatabasePath",
                            files.DatabasePath);
                        builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");
                    });
        using HttpClient client = factory.CreateClient();
        ResearchSourceInventoryDocument? inventory =
            await client.GetFromJsonAsync<ResearchSourceInventoryDocument>(
                "/api/v1/research/sources",
                TestContext.Current.CancellationToken);

        Assert.NotNull(inventory);
        Assert.Equal(3, inventory.Sources.Count);
        Assert.Contains(
            inventory.Sources,
            item => item.SourceClass == "official-availability-aggregation");
        Assert.Contains(
            inventory.Sources,
            item => item.SourceClass == "specialist-predicted-lineup");
        Assert.Contains(
            inventory.Sources,
            item => item.SourceClass == "derived-predicted-lineup-consensus");
        ResearchSourceSnapshotDocument latest =
            Assert.Single(inventory.LatestSnapshots);
        Assert.Equal("premier-league-injuries", latest.SourceKey);
        string json = JsonSerializer.Serialize(
            inventory,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(
            "Private retained source text.",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spider_capture_rejects_wrong_server_and_redirected_resource()
    {
        ResearchSourceDefinition source =
            ResearchSourceRegistry.Get("ffscout-predicted-lineups");
        using var wrongServerClient = new HttpClient(
            new SpiderMcpHandler("content", serverName: "unexpected"))
        {
            BaseAddress = new Uri("http://spider-mcp:8080/mcp"),
        };
        await Assert.ThrowsAsync<ResearchSourceSnapshotException>(
            async () => await new SpiderMcpClient(wrongServerClient).ScrapeAsync(
                source,
                TestContext.Current.CancellationToken));

        using var wrongResourceClient = new HttpClient(
            new SpiderMcpHandler(
                "content",
                finalUrl: "https://example.com/copied-page"))
        {
            BaseAddress = new Uri("http://spider-mcp:8080/mcp"),
        };
        await Assert.ThrowsAsync<ResearchSourceSnapshotException>(
            async () => await new SpiderMcpClient(wrongResourceClient).ScrapeAsync(
                source,
                TestContext.Current.CancellationToken));
    }

    private static async Task<DatabaseOptions> CreateDatabaseAsync(
        string databasePath)
    {
        DatabaseOptions options = CreateOptions(databasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        await new OfficialFplImporter(
                httpClient,
                new OfficialFplCaptureStore(options),
                TimeProvider.System)
            .ImportCapturedPayloadAsync(
                CreateBootstrap(),
                CreateFixtures(),
                CaptureTime,
                TestContext.Current.CancellationToken);
        return options;
    }

    private static byte[] CreateBootstrap() =>
        JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                events = new object[]
                {
                    new
                    {
                        id = 1,
                        name = "Gameweek 1",
                        deadline_time = "2026-08-01T12:00:00Z",
                        finished = false,
                        data_checked = false,
                        is_current = false,
                        is_next = true,
                    },
                },
                teams = new object[]
                {
                    new
                    {
                        id = 1,
                        code = 10,
                        name = "Home",
                        short_name = "HOM",
                    },
                    new
                    {
                        id = 2,
                        code = 20,
                        name = "Away",
                        short_name = "AWY",
                    },
                },
                elements = new object[]
                {
                    new
                    {
                        id = 101,
                        code = 1001,
                        team = 1,
                        element_type = 3,
                        first_name = "Test",
                        second_name = "Player",
                        web_name = "Player",
                        photo = "1001.png",
                        now_cost = 75,
                        status = "a",
                        news = string.Empty,
                        news_added = (string?)null,
                        chance_of_playing_next_round = (int?)null,
                        selected_by_percent = "12.5",
                        ep_next = "4.2",
                        total_points = 0,
                        minutes = 0,
                        starts = 0,
                    },
                },
            });

    private static byte[] CreateFixtures() =>
        JsonSerializer.SerializeToUtf8Bytes(
            new object[]
            {
                new
                {
                    id = 1,
                    @event = 1,
                    team_h = 1,
                    team_a = 2,
                    kickoff_time = "2026-08-02T14:00:00Z",
                    started = false,
                    finished = false,
                    finished_provisional = false,
                    team_h_score = (int?)null,
                    team_a_score = (int?)null,
                },
            });

    private static DatabaseOptions CreateOptions(string databasePath)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:DatabasePath"] = databasePath,
                })
            .Build();
        return DatabaseOptions.FromConfiguration(configuration);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class SpiderMcpHandler(
        string content,
        string serverName = "rmcp",
        string? finalUrl = null) : HttpMessageHandler
    {
        public int ScrapeCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete)
            {
                DeleteCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            using JsonDocument body = JsonDocument.Parse(
                await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            JsonElement root = body.RootElement;
            string method = root.GetProperty("method").GetString()!;
            if (method == "initialize")
            {
                var response = Json(
                    new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        result = new
                        {
                            protocolVersion = "2025-03-26",
                            capabilities = new { tools = new { listChanged = false } },
                            serverInfo = new { name = serverName, version = "test" },
                        },
                    });
                response.Headers.TryAddWithoutValidation(
                    "Mcp-Session-Id",
                    "test-session");
                return response;
            }

            if (method == "notifications/initialized")
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            Assert.Equal("tools/call", method);
            JsonElement parameters = root.GetProperty("params");
            Assert.Equal(
                "spider_scrape",
                parameters.GetProperty("name").GetString());
            JsonElement arguments = parameters.GetProperty("arguments");
            Assert.Equal(
                "https://cdn.fantasyfootballscout.co.uk/team-news",
                arguments.GetProperty("url").GetString());
            Assert.False(arguments.GetProperty("headless").GetBoolean());
            ScrapeCalls++;
            string scrape = JsonSerializer.Serialize(
                new
                {
                    url = finalUrl
                        ?? "https://cdn.fantasyfootballscout.co.uk/team-news",
                    status_code = 200,
                    content,
                    links = Array.Empty<string>(),
                    content_trust = "untrusted_remote_content",
                });
            return Json(
                new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    result = new
                    {
                        content = new[]
                        {
                            new { type = "text", text = scrape },
                        },
                        isError = false,
                    },
                });
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(value),
                    Encoding.UTF8,
                    "application/json"),
            };
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-research-source-tests-{Guid.NewGuid():N}");

        public TemporaryDatabaseFiles()
        {
            Directory.CreateDirectory(_directory);
            DatabasePath = Path.Combine(_directory, "autofpl.db");
        }

        public string DatabasePath { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
