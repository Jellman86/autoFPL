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

        byte[] html = CreateForecastHtml(6.25m);
        var handler = new StaticHtmlHandler(html);
        using var httpClient = new HttpClient(handler);
        var store = new FplFormForecastStore(options);
        var importer = new FplFormForecastImporter(
            httpClient,
            store,
            new FixedTimeProvider(RetrievedAtUtc));

        FplFormForecastCaptureDocument first =
            await importer.ImportLatestAsync(TestContext.Current.CancellationToken);
        FplFormForecastCaptureDocument duplicate =
            await importer.ImportCapturedHtmlAsync(
                html,
                RetrievedAtUtc.AddMinutes(30),
                TestContext.Current.CancellationToken);

        Assert.Equal(first, duplicate);
        Assert.Equal(1, first.CaptureId);
        Assert.Equal("1.0", first.SchemaVersion);
        Assert.Equal("fpl-form-public-forecast/v1", first.SourceKey);
        Assert.Equal(
            "https://www.fplform.com/fpl-predicted-points.php",
            first.SourceUrl);
        Assert.Equal("2026-27", first.SeasonCode);
        Assert.Equal(1, first.Gameweek);
        Assert.Null(first.PublishedAtUtc);
        Assert.Equal(RetrievedAtUtc, first.RetrievedAtUtc);
        Assert.Equal(RetrievedAtUtc, first.AvailableAtUtc);
        Assert.Equal(64, first.ContentSha256.Length);
        Assert.Equal(2, first.PlayerCount);
        Assert.Equal(2, first.FixturePredictionCount);
        Assert.Equal(1, first.AppearanceProbabilityCount);
        Assert.Equal([FplFormForecastImporter.ForecastUri], handler.Requests);
        Assert.Equal(
            first,
            await store.GetLatestAsync(TestContext.Current.CancellationToken));

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
    }

    [Fact]
    public async Task Changed_forecast_creates_a_new_immutable_capture()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        var store = new FplFormForecastStore(options);
        using var httpClient = new HttpClient(
            new StaticHtmlHandler(CreateForecastHtml(6.25m)));
        var importer = new FplFormForecastImporter(
            httpClient,
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
        using var httpClient = new HttpClient(
            new StaticHtmlHandler(CreateForecastHtml(6.25m, nextGameweek: 99)));
        var importer = new FplFormForecastImporter(
            httpClient,
            store,
            new FixedTimeProvider(RetrievedAtUtc));

        FplFormForecastPayloadException exception =
            await Assert.ThrowsAsync<FplFormForecastPayloadException>(
                () => importer.ImportLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("no active next-Gameweek", exception.Message, StringComparison.Ordinal);
        Assert.Null(await store.GetLatestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Non_html_response_is_rejected()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient(new JsonHandler());
        var importer = new FplFormForecastImporter(
            httpClient,
            new FplFormForecastStore(options),
            new FixedTimeProvider(RetrievedAtUtc));

        FplFormForecastPayloadException exception =
            await Assert.ThrowsAsync<FplFormForecastPayloadException>(
                () => importer.ImportLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("content type", exception.Message, StringComparison.Ordinal);
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
                (SELECT MIN(length(html_brotli)) FROM fpl_form_forecast_captures);
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(captures, reader.GetInt64(0));
        Assert.Equal(predictions, reader.GetInt64(1));
        Assert.True(reader.GetInt64(2) > 0);
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

    private sealed class StaticHtmlHandler(byte[] html) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(html),
                RequestMessage = request,
            };
            response.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
            return Task.FromResult(response);
        }
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { message = "not HTML" }),
                    RequestMessage = request,
                });
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
