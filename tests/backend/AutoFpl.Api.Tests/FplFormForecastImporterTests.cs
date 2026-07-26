using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Sources;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class FplFormForecastImporterTests
{
    private static readonly DateTimeOffset RetrievedAtUtc =
        new(2026, 8, 14, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task Import_is_fixed_origin_idempotent_compressed_and_queryable()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        var migrationStore = new DecisionSnapshotStore(options);
        await migrationStore.MigrateAsync(TestContext.Current.CancellationToken);

        byte[] evidence = CreateForecastEvidence(6.25m);
        var handler = new PlaywrightMcpHandler(evidence);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://playwright-mcp:8931/mcp"),
        };
        var store = new FplFormForecastStore(options);
        var importer = new FplFormForecastImporter(
            new PlaywrightMcpFplFormCollector(httpClient),
            store,
            new FixedTimeProvider(RetrievedAtUtc));

        FplFormForecastCaptureDocument first =
            await importer.ImportLatestAsync(TestContext.Current.CancellationToken);
        FplFormForecastCaptureDocument duplicate =
            await importer.ImportExtractedEvidenceAsync(
                evidence,
                RetrievedAtUtc.AddMinutes(30),
                TestContext.Current.CancellationToken);

        Assert.Equal(first, duplicate);
        Assert.Equal(1, first.CaptureId);
        Assert.Equal("1.1", first.SchemaVersion);
        Assert.Equal("fpl-form-public-forecast/v1", first.SourceKey);
        Assert.Equal(
            "https://fplform.com/fpl-predicted-points",
            first.SourceUrl);
        Assert.Equal("2026-27", first.SeasonCode);
        Assert.Equal(1, first.Gameweek);
        Assert.Null(first.PublishedAtUtc);
        Assert.Equal(RetrievedAtUtc, first.RetrievedAtUtc);
        Assert.Equal(RetrievedAtUtc, first.AvailableAtUtc);
        Assert.Equal(64, first.ContentSha256.Length);
        Assert.Equal("playwright-mcp/v1", first.Transport);
        Assert.Equal("fpl-form-stream-extract/v2", first.ExtractionVersion);
        Assert.Equal(new string('a', 64), first.ProviderPayloadSha256);
        Assert.Equal(2, first.PlayerCount);
        Assert.Equal(2, first.FixturePredictionCount);
        Assert.Equal(1, first.AppearanceProbabilityCount);
        Assert.Equal(
            ["initialize", "notifications/initialized", "browser_run_code_unsafe",
                "browser_close", "DELETE"],
            handler.Operations);
        Assert.Equal(
            first,
            await store.GetLatestAsync(TestContext.Current.CancellationToken));
        FplFormForecastStatusDocument status =
            await store.GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.Equal("captured", status.Status);
        Assert.Equal(RetrievedAtUtc.AddMinutes(30), status.CheckedAtUtc);
        Assert.Null(status.ReasonCode);
        Assert.Equal(first, status.LatestCapture);

        await AssertDatabaseShapeAsync(files.DatabasePath, 1, 2);
        await AssertPredictionAsync(files.DatabasePath);

        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(
                    builder =>
                    {
                        builder.UseSetting("AutoFpl:DatabasePath", files.DatabasePath);
                        builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");
                    });
        using HttpClient client = factory.CreateClient();
        FplFormForecastCaptureDocument? served =
            await client.GetFromJsonAsync<FplFormForecastCaptureDocument>(
                "/api/v1/data/fpl-form-forecast/latest",
                TestContext.Current.CancellationToken);
        Assert.Equal(first, served);
        FplFormForecastStatusDocument? servedStatus =
            await client.GetFromJsonAsync<FplFormForecastStatusDocument>(
                "/api/v1/data/fpl-form-forecast/status",
                TestContext.Current.CancellationToken);
        Assert.Equal(status, servedStatus);
    }

    [Fact]
    public async Task Collector_accepts_direct_json_streamable_http_responses()
    {
        byte[] evidence = CreateForecastEvidence(6.25m);
        var handler = new PlaywrightMcpHandler(
            evidence,
            useJsonResponses: true);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://playwright-mcp:8931/mcp"),
        };
        var collector = new PlaywrightMcpFplFormCollector(httpClient);

        byte[] captured = await collector.CaptureAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(evidence, captured);
        Assert.Equal(
            ["initialize", "notifications/initialized", "browser_run_code_unsafe",
                "browser_close", "DELETE"],
            handler.Operations);
    }

    [Fact]
    public async Task Changed_forecast_creates_a_new_immutable_capture()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        var store = new FplFormForecastStore(options);
        var importer = new FplFormForecastImporter(
            CreateUnusedCollector(),
            store,
            new FixedTimeProvider(RetrievedAtUtc));

        FplFormForecastCaptureDocument first =
            await importer.ImportCapturedHtmlAsync(
                CreateForecastHtml(6.25m),
                RetrievedAtUtc,
                TestContext.Current.CancellationToken);
        FplFormForecastCaptureDocument changed =
            await importer.ImportCapturedHtmlAsync(
                CreateForecastHtml(6.75m),
                RetrievedAtUtc.AddHours(1),
                TestContext.Current.CancellationToken);

        Assert.Equal(1, first.CaptureId);
        Assert.Equal(2, changed.CaptureId);
        Assert.NotEqual(first.ContentSha256, changed.ContentSha256);
        Assert.Equal(
            changed,
            await store.GetLatestAsync(TestContext.Current.CancellationToken));
        await AssertDatabaseShapeAsync(files.DatabasePath, 2, 4);
    }

    [Fact]
    public async Task Offseason_page_is_rejected_without_writing_a_capture()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        var store = new FplFormForecastStore(options);
        var handler = new PlaywrightMcpHandler(
            CreateForecastEvidence(6.25m, nextGameweek: 99));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://playwright-mcp:8931/mcp"),
        };
        var importer = new FplFormForecastImporter(
            new PlaywrightMcpFplFormCollector(httpClient),
            store,
            new FixedTimeProvider(RetrievedAtUtc));

        FplFormForecastPayloadException exception =
            await Assert.ThrowsAsync<FplFormForecastPayloadException>(
                () => importer.ImportLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("no active next-Gameweek", exception.Message, StringComparison.Ordinal);
        Assert.Null(await store.GetLatestAsync(TestContext.Current.CancellationToken));
        FplFormForecastStatusDocument status =
            await store.GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.Equal("waiting", status.Status);
        Assert.Equal(RetrievedAtUtc, status.CheckedAtUtc);
        Assert.Equal("provider-no-active-gameweek", status.ReasonCode);
        Assert.Null(status.LatestCapture);
    }

    [Fact]
    public async Task Unexpected_mcp_server_is_rejected()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient(
            new PlaywrightMcpHandler(
                CreateForecastEvidence(6.25m),
                serverName: "Unexpected"))
        {
            BaseAddress = new Uri("http://playwright-mcp:8931/mcp"),
        };
        var store = new FplFormForecastStore(options);
        var importer = new FplFormForecastImporter(
            new PlaywrightMcpFplFormCollector(httpClient),
            store,
            new FixedTimeProvider(RetrievedAtUtc));

        FplFormForecastPayloadException exception =
            await Assert.ThrowsAsync<FplFormForecastPayloadException>(
                () => importer.ImportLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("expected Playwright", exception.Message, StringComparison.Ordinal);
        FplFormForecastStatusDocument status =
            await store.GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.Equal("failed", status.Status);
        Assert.Equal("collection-failed", status.ReasonCode);
        Assert.Null(status.LatestCapture);
    }

    [Fact]
    public async Task Migration_preserves_legacy_direct_http_evidence()
    {
        using var files = new TemporaryDatabaseFiles();
        await CreateLegacyForecastDatabaseAsync(files.DatabasePath);
        DatabaseOptions options = CreateOptions(files.DatabasePath);

        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = files.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                schema_version,
                transport,
                extraction_version,
                provider_payload_sha256,
                length(evidence_brotli),
                (SELECT COUNT(*) FROM fpl_form_fixture_predictions),
                (SELECT MAX(version) FROM schema_migrations)
            FROM fpl_form_forecast_captures;
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("1.0", reader.GetString(0));
        Assert.Equal("direct-http/v1", reader.GetString(1));
        Assert.Equal("fpl-form-full-html/v1", reader.GetString(2));
        Assert.True(reader.IsDBNull(3));
        Assert.Equal(2, reader.GetInt32(4));
        Assert.Equal(1, reader.GetInt64(5));
        Assert.Equal(14, reader.GetInt64(6));
    }

    private static DatabaseOptions CreateOptions(string databasePath)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:DatabasePath"] = databasePath,
                })
            .Build();
        return DatabaseOptions.FromConfiguration(configuration);
    }

    private static async Task CreateLegacyForecastDatabaseAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            CREATE TABLE schema_migrations (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                applied_at_utc TEXT NOT NULL
            );
            INSERT INTO schema_migrations (version, name, applied_at_utc) VALUES
                (1, 'season-gameweek-player', '2026-01-01T00:00:00Z'),
                (2, 'squad-selection', '2026-01-01T00:00:00Z'),
                (3, 'observation-snapshot', '2026-01-01T00:00:00Z'),
                (4, 'official-fpl-capture', '2026-01-01T00:00:00Z'),
                (5, 'official-fpl-gameweek-outcome', '2026-01-01T00:00:00Z'),
                (6, 'fpl-form-forecast-capture', '2026-01-01T00:00:00Z'),
                (7, 'official-fpl-player-photo', '2026-01-01T00:00:00Z');

            CREATE TABLE official_fpl_player_outcomes (
                outcome_capture_id INTEGER NOT NULL,
                reference_capture_id INTEGER NOT NULL,
                player_id INTEGER NOT NULL,
                minutes INTEGER NOT NULL,
                starts INTEGER NOT NULL,
                total_points INTEGER NOT NULL,
                goals_scored INTEGER NOT NULL,
                assists INTEGER NOT NULL,
                clean_sheets INTEGER NOT NULL,
                goals_conceded INTEGER NOT NULL,
                saves INTEGER NOT NULL,
                bonus INTEGER NOT NULL,
                yellow_cards INTEGER NOT NULL,
                red_cards INTEGER NOT NULL,
                PRIMARY KEY (outcome_capture_id, player_id)
            );

            CREATE TABLE official_fpl_players (
                capture_id INTEGER NOT NULL,
                player_id INTEGER NOT NULL,
                PRIMARY KEY (capture_id, player_id)
            );

            CREATE TABLE fpl_form_forecast_captures (
                capture_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                source_key TEXT NOT NULL,
                source_url TEXT NOT NULL,
                season_code TEXT NOT NULL,
                gameweek INTEGER NOT NULL,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                content_sha256 TEXT NOT NULL,
                html_brotli BLOB NOT NULL,
                player_count INTEGER NOT NULL,
                fixture_prediction_count INTEGER NOT NULL,
                appearance_probability_count INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                UNIQUE (content_sha256),
                UNIQUE (capture_id, gameweek)
            );
            CREATE INDEX fpl_form_forecast_captures_latest_idx
                ON fpl_form_forecast_captures (
                    season_code,
                    gameweek,
                    available_at_utc DESC,
                    capture_id DESC
                );
            CREATE TABLE fpl_form_fixture_predictions (
                capture_id INTEGER NOT NULL,
                source_player_id INTEGER NOT NULL,
                fixture_id INTEGER NOT NULL,
                gameweek INTEGER NOT NULL,
                player_name TEXT NOT NULL,
                team_name TEXT NOT NULL,
                position TEXT NOT NULL,
                kickoff_local TEXT NOT NULL,
                predicted_points TEXT NOT NULL,
                appearance_probability TEXT,
                PRIMARY KEY (capture_id, source_player_id, fixture_id),
                FOREIGN KEY (capture_id, gameweek)
                    REFERENCES fpl_form_forecast_captures(
                        capture_id,
                        gameweek
                    ) ON DELETE RESTRICT
            );
            INSERT INTO fpl_form_forecast_captures VALUES (
                1,
                '1.0',
                'fpl-form-public-forecast/v1',
                'https://www.fplform.com/fpl-predicted-points.php',
                '2026-27',
                1,
                '2026-08-14T09:15:00+00:00',
                '2026-08-14T09:15:00+00:00',
                'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
                X'0102',
                1,
                1,
                0,
                '2026-08-14T09:15:00+00:00'
            );
            INSERT INTO fpl_form_fixture_predictions VALUES (
                1,
                101,
                10,
                1,
                'Ada Forward',
                'North London',
                'forward',
                '2026-08-22 15:00:00',
                '6.25',
                NULL
            );
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static byte[] CreateForecastHtml(
        decimal firstPrediction,
        int nextGameweek = 1)
    {
        Dictionary<string, object> players = new()
        {
            ["101"] = Player(
                "Ada Forward",
                "North London",
                "Forward",
                fixtureId: 10,
                predictedPoints: firstPrediction,
                appearanceProbability: 0.95m),
            ["102"] = Player(
                "Bea Keeper",
                "South Coast",
                "Goalkeeper",
                fixtureId: 11,
                predictedPoints: 3.5m,
                appearanceProbability: null),
        };
        string json = JsonSerializer.Serialize(players);
        string encoded = WebUtility.HtmlEncode(json);
        return Encoding.UTF8.GetBytes(
            $"""
            <!doctype html>
            <html>
            <body>
              <div id="php-data"
                   data-count="2"
                   data-lw="0"
                   data-myteam="undefined"
                   data-nw="{nextGameweek}"
                   data-players="{encoded}">
              </div>
            </body>
            </html>
            """);
    }

    private static byte[] CreateForecastEvidence(
        decimal firstPrediction,
        int nextGameweek = 1)
    {
        object[] predictions =
        [
            new
            {
                sourcePlayerId = 101,
                fixtureId = 10,
                playerName = "Ada Forward",
                teamName = "North London",
                position = "Forward",
                kickoffLocal = "2026-08-22 15:00:00",
                predictedPoints = firstPrediction.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                appearanceProbability = "0.95",
            },
            new
            {
                sourcePlayerId = 102,
                fixtureId = 11,
                playerName = "Bea Keeper",
                teamName = "South Coast",
                position = "Goalkeeper",
                kickoffLocal = "2026-08-23 14:00:00",
                predictedPoints = "3.5",
                appearanceProbability = (string?)null,
            },
        ];
        return JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schemaVersion = "fpl-form-stream-extract/v2",
                sourceUrl = FplFormForecastImporter.ForecastUri.AbsoluteUri,
                season = 26,
                gameweek = nextGameweek,
                providerPayloadSha256 =
                    nextGameweek is >= 1 and <= 38 ? new string('a', 64) : null,
                predictions =
                    nextGameweek is >= 1 and <= 38 ? predictions : [],
            });
    }

    private static PlaywrightMcpFplFormCollector CreateUnusedCollector()
    {
        var client = new HttpClient(new UnusedHandler())
        {
            BaseAddress = new Uri("http://playwright-mcp:8931/mcp"),
        };
        return new PlaywrightMcpFplFormCollector(client);
    }

    private static object Player(
        string name,
        string teamName,
        string position,
        int fixtureId,
        decimal predictedPoints,
        decimal? appearanceProbability)
    {
        string kickoff = fixtureId == 10
            ? "2026-08-22 15:00:00"
            : "2026-08-23 14:00:00";
        return new
        {
            fixtures = new Dictionary<string, object>
            {
                ["26"] = new Dictionary<string, object>
                {
                    ["1"] = new Dictionary<string, object>
                    {
                        [kickoff] = new
                        {
                            season = "26",
                            fixture = fixtureId.ToString(),
                            @event = "1",
                            kickoff,
                            predicted_points = predictedPoints.ToString(
                                System.Globalization.CultureInfo.InvariantCulture),
                            probability_of_playing = appearanceProbability?.ToString(
                                System.Globalization.CultureInfo.InvariantCulture),
                        },
                    },
                },
            },
            name,
            team_name = teamName,
            position,
        };
    }

    private static async Task AssertDatabaseShapeAsync(
        string databasePath,
        long captures,
        long predictions)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM fpl_form_forecast_captures),
                (SELECT COUNT(*) FROM fpl_form_fixture_predictions),
                (SELECT MIN(length(evidence_brotli)) FROM fpl_form_forecast_captures),
                (SELECT COUNT(*)
                 FROM fpl_form_forecast_captures
                 WHERE transport IN ('direct-http/v1', 'playwright-mcp/v1'));
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(captures, reader.GetInt64(0));
        Assert.Equal(predictions, reader.GetInt64(1));
        Assert.True(reader.GetInt64(2) > 0);
        Assert.Equal(captures, reader.GetInt64(3));
    }

    private static async Task AssertPredictionAsync(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                fixture_id,
                player_name,
                team_name,
                position,
                kickoff_local,
                predicted_points,
                appearance_probability
            FROM fpl_form_fixture_predictions
            WHERE capture_id = 1 AND source_player_id = 101;
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(10, reader.GetInt32(0));
        Assert.Equal("Ada Forward", reader.GetString(1));
        Assert.Equal("North London", reader.GetString(2));
        Assert.Equal("forward", reader.GetString(3));
        Assert.Equal("2026-08-22 15:00:00", reader.GetString(4));
        Assert.Equal("6.25", reader.GetString(5));
        Assert.Equal("0.95", reader.GetString(6));
    }

    private sealed class PlaywrightMcpHandler(
        byte[] evidence,
        string serverName = "Playwright",
        bool useJsonResponses = false) : HttpMessageHandler
    {
        public List<string> Operations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete)
            {
                Operations.Add("DELETE");
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(body);
            string method = document.RootElement.GetProperty("method").GetString()!;
            if (StringComparer.Ordinal.Equals(method, "initialize"))
            {
                Operations.Add("initialize");
                return EventResponse(
                    1,
                    new
                    {
                        protocolVersion = "2025-03-26",
                        capabilities = new { tools = new { } },
                        serverInfo = new
                        {
                            name = serverName,
                            version = "test",
                        },
                    },
                    includeSession: true);
            }

            if (StringComparer.Ordinal.Equals(method, "notifications/initialized"))
            {
                Operations.Add("notifications/initialized");
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            JsonElement parameters = document.RootElement.GetProperty("params");
            string tool = parameters.GetProperty("name").GetString()!;
            Operations.Add(tool);
            if (StringComparer.Ordinal.Equals(tool, "browser_run_code_unsafe"))
            {
                string code = parameters
                    .GetProperty("arguments")
                    .GetProperty("code")
                    .GetString()!;
                Assert.Contains(
                    FplFormForecastImporter.ForecastUri.AbsoluteUri,
                    code,
                    StringComparison.Ordinal);
                Assert.Contains("page.request.get", code, StringComparison.Ordinal);
                Assert.Contains("response.dispose", code, StringComparison.Ordinal);
                Assert.Contains("visitPlayers", code, StringComparison.Ordinal);
                Assert.Contains("hashChunkSize", code, StringComparison.Ordinal);
                Assert.Contains("prediction.season", code, StringComparison.Ordinal);
                Assert.Contains("kickoffIdentity", code, StringComparison.Ordinal);
                string extracted = Encoding.UTF8.GetString(evidence);
                string literal = JsonSerializer.Serialize(
                    $"AUTOFPL_FPL_FORM_V1:{extracted}");
                return EventResponse(
                    2,
                    new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text =
                                    "### Result\n"
                                    + literal
                                    + "\n### Ran Playwright code\n```js\nprobe\n```",
                            },
                        },
                    });
            }

            Assert.Equal("browser_close", tool);
            return EventResponse(
                3,
                new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = "closed",
                        },
                    },
                });
        }

        private HttpResponseMessage EventResponse(
            int id,
            object result,
            bool includeSession = false)
        {
            string envelope = JsonSerializer.Serialize(
                new
                {
                    result,
                    jsonrpc = "2.0",
                    id,
                });
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    useJsonResponses
                        ? envelope
                        : $"event: message\ndata: {envelope}\n",
                    Encoding.UTF8,
                    useJsonResponses
                        ? "application/json"
                        : "text/event-stream"),
            };
            if (includeSession)
            {
                response.Headers.TryAddWithoutValidation(
                    "Mcp-Session-Id",
                    "test-session");
            }

            return response;
        }
    }

    private sealed class UnusedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The MCP collector should not be used.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-fplform-tests-{Guid.NewGuid():N}");

        public TemporaryDatabaseFiles()
        {
            Directory.CreateDirectory(_directory);
        }

        public string DatabasePath => Path.Combine(_directory, "autofpl.db");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
