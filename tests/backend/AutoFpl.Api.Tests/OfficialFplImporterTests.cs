using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
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

public sealed class OfficialFplImporterTests
{
    private static readonly DateTimeOffset RetrievedAtUtc =
        new(2026, 7, 25, 21, 45, 56, TimeSpan.Zero);

    [Fact]
    public async Task Import_is_fixed_origin_atomic_idempotent_and_queryable()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        var migrationStore = new DecisionSnapshotStore(options);
        await migrationStore.MigrateAsync(TestContext.Current.CancellationToken);

        byte[] bootstrap = CreateBootstrap(priceTenths: 60);
        byte[] fixtures = CreateFixtures();
        var handler = new StaticOfficialFplHandler(bootstrap, fixtures);
        using var httpClient = new HttpClient(handler);
        var captureStore = new OfficialFplCaptureStore(options);
        var importer = new OfficialFplImporter(
            httpClient,
            captureStore,
            new FixedTimeProvider(RetrievedAtUtc));

        OfficialFplCaptureDocument first =
            await importer.ImportLatestAsync(TestContext.Current.CancellationToken);
        await ClearPhotoIdentifierAsync(files.DatabasePath, first.CaptureId, playerId: 1);
        await ClearExpectedPointsNextAsync(
            files.DatabasePath,
            first.CaptureId,
            playerId: 1);
        OfficialFplCaptureDocument duplicate =
            await importer.ImportCapturedPayloadAsync(
                bootstrap,
                fixtures,
                RetrievedAtUtc.AddMinutes(30),
                TestContext.Current.CancellationToken);

        Assert.Equal(1, first.CaptureId);
        Assert.Equal(first, duplicate);
        Assert.Equal("official-fpl-api/v1", first.SourceKey);
        Assert.Equal("2026-27", first.SeasonCode);
        Assert.Null(first.PublishedAtUtc);
        Assert.Equal(RetrievedAtUtc, first.RetrievedAtUtc);
        Assert.Equal(RetrievedAtUtc, first.AvailableAtUtc);
        Assert.Equal(2, first.EventCount);
        Assert.Equal(2, first.TeamCount);
        Assert.Equal(4, first.PlayerCount);
        Assert.Equal(1, first.FixtureCount);
        Assert.Equal(2, first.NextGameweekNumber);
        Assert.Equal(new(2026, 8, 29, 14, 0, 0, TimeSpan.Zero), first.NextDeadlineUtc);
        Assert.Equal(1, first.LatestCompletedGameweek);
        Assert.Equal(64, first.BootstrapSha256.Length);
        Assert.Equal(64, first.FixturesSha256.Length);
        Assert.Equal(
            [OfficialFplImporter.BootstrapUri, OfficialFplImporter.FixturesUri],
            handler.Requests.OrderBy(uri => uri.AbsoluteUri).ToArray());

        OfficialFplCaptureDocument? latest =
            await captureStore.GetLatestAsync(TestContext.Current.CancellationToken);
        Assert.Equal(first, latest);
        Assert.Equal(
            RetrievedAtUtc,
            await captureStore.GetLatestCheckTimeAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(
            "101.jpg",
            await ReadPhotoIdentifierAsync(
                files.DatabasePath,
                first.CaptureId,
                playerId: 1));
        Assert.Equal(
            4.2m,
            await ReadExpectedPointsNextAsync(
                files.DatabasePath,
                first.CaptureId,
                playerId: 1));
        await AssertDatabaseShapeAsync(files.DatabasePath, expectedCaptures: 1);

        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(
                    builder =>
                    {
                        builder.UseSetting("AutoFpl:DatabasePath", files.DatabasePath);
                        builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");
                    });
        using HttpClient client = factory.CreateClient();
        OfficialFplCaptureDocument? served =
            await client.GetFromJsonAsync<OfficialFplCaptureDocument>(
                "/api/v1/data/official-fpl/latest",
                TestContext.Current.CancellationToken);
        Assert.Equal(first, served);
    }

    [Fact]
    public async Task Changed_content_creates_a_new_immutable_capture()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        var migrationStore = new DecisionSnapshotStore(options);
        await migrationStore.MigrateAsync(TestContext.Current.CancellationToken);
        var captureStore = new OfficialFplCaptureStore(options);
        using var httpClient = new HttpClient(new StaticOfficialFplHandler(
            CreateBootstrap(60),
            CreateFixtures()));
        var importer = new OfficialFplImporter(
            httpClient,
            captureStore,
            new FixedTimeProvider(RetrievedAtUtc));

        OfficialFplCaptureDocument first =
            await importer.ImportCapturedPayloadAsync(
                CreateBootstrap(60),
                CreateFixtures(),
                RetrievedAtUtc,
                TestContext.Current.CancellationToken);
        OfficialFplCaptureDocument corrected =
            await importer.ImportCapturedPayloadAsync(
                CreateBootstrap(61),
                CreateFixtures(),
                RetrievedAtUtc.AddHours(1),
                TestContext.Current.CancellationToken);

        Assert.Equal(1, first.CaptureId);
        Assert.Equal(2, corrected.CaptureId);
        Assert.NotEqual(first.BootstrapSha256, corrected.BootstrapSha256);
        Assert.Equal(first.FixturesSha256, corrected.FixturesSha256);
        Assert.Equal(
            corrected,
            await captureStore.GetLatestAsync(TestContext.Current.CancellationToken));
        await AssertDatabaseShapeAsync(files.DatabasePath, expectedCaptures: 2);
    }

    [Fact]
    public async Task Pre_deadline_replay_selects_the_latest_capture_that_was_available_in_time()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        var migrationStore = new DecisionSnapshotStore(options);
        await migrationStore.MigrateAsync(TestContext.Current.CancellationToken);
        var captureStore = new OfficialFplCaptureStore(options);
        using var httpClient = new HttpClient(new StaticOfficialFplHandler(
            CreateBootstrap(60),
            CreateFixtures()));
        var importer = new OfficialFplImporter(
            httpClient,
            captureStore,
            new FixedTimeProvider(RetrievedAtUtc));

        OfficialFplCaptureDocument beforeDeadline =
            await importer.ImportCapturedPayloadAsync(
                CreateBootstrap(60),
                CreateFixtures(),
                RetrievedAtUtc,
                TestContext.Current.CancellationToken);
        OfficialFplCaptureDocument afterDeadline =
            await importer.ImportCapturedPayloadAsync(
                CreateBootstrap(61),
                CreateFixtures(),
                new DateTimeOffset(2026, 8, 21, 17, 31, 0, TimeSpan.Zero),
                TestContext.Current.CancellationToken);

        OfficialFplReplayDocument? replay =
            await captureStore.GetLatestPreDeadlineReplayAsync(
                "2026-27",
                1,
                TestContext.Current.CancellationToken);

        Assert.NotNull(replay);
        Assert.Equal("1.0", replay.SchemaVersion);
        Assert.Equal("official-fpl-api/v1", replay.SourceKey);
        Assert.Equal("2026-27", replay.SeasonCode);
        Assert.Equal(1, replay.Gameweek);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 21, 17, 30, 0, TimeSpan.Zero),
            replay.DeadlineUtc);
        Assert.Equal(beforeDeadline.CaptureId, replay.SelectedCaptureId);
        Assert.NotEqual(afterDeadline.CaptureId, replay.SelectedCaptureId);
        Assert.Equal(beforeDeadline.AvailableAtUtc, replay.CaptureAvailableAtUtc);
        Assert.Equal(
            checked((long)(replay.DeadlineUtc - replay.CaptureAvailableAtUtc).TotalSeconds),
            replay.CaptureLeadTimeSeconds);
        Assert.Equal(beforeDeadline.BootstrapSha256, replay.BootstrapSha256);
        Assert.Equal(beforeDeadline.FixturesSha256, replay.FixturesSha256);
        Assert.Equal(2, replay.TeamCount);
        Assert.Equal(4, replay.PlayerCount);
        Assert.Equal(0, replay.GameweekFixtureCount);

        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(
                    builder =>
                    {
                        builder.UseSetting("AutoFpl:DatabasePath", files.DatabasePath);
                        builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");
                    });
        using HttpClient client = factory.CreateClient();
        OfficialFplReplayDocument? served =
            await client.GetFromJsonAsync<OfficialFplReplayDocument>(
                "/api/v1/data/official-fpl/replays/2026-27/1/pre-deadline",
                TestContext.Current.CancellationToken);
        Assert.Equal(replay, served);
    }

    [Fact]
    public async Task Pre_deadline_replay_returns_no_result_when_only_late_captures_exist()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        var migrationStore = new DecisionSnapshotStore(options);
        await migrationStore.MigrateAsync(TestContext.Current.CancellationToken);
        var captureStore = new OfficialFplCaptureStore(options);
        using var httpClient = new HttpClient(new StaticOfficialFplHandler(
            CreateBootstrap(60),
            CreateFixtures()));
        var importer = new OfficialFplImporter(
            httpClient,
            captureStore,
            new FixedTimeProvider(RetrievedAtUtc));

        await importer.ImportCapturedPayloadAsync(
            CreateBootstrap(60),
            CreateFixtures(),
            new DateTimeOffset(2026, 8, 21, 17, 30, 1, TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        Assert.Null(
            await captureStore.GetLatestPreDeadlineReplayAsync(
                "2026-27",
                1,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invalid_cross_reference_is_rejected_before_any_rows_are_written()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        var migrationStore = new DecisionSnapshotStore(options);
        await migrationStore.MigrateAsync(TestContext.Current.CancellationToken);
        var captureStore = new OfficialFplCaptureStore(options);
        using var httpClient = new HttpClient(new StaticOfficialFplHandler(
            CreateBootstrap(60, firstPlayerTeamId: 99),
            CreateFixtures()));
        var importer = new OfficialFplImporter(
            httpClient,
            captureStore,
            new FixedTimeProvider(RetrievedAtUtc));

        OfficialFplPayloadException exception =
            await Assert.ThrowsAsync<OfficialFplPayloadException>(
                () => importer.ImportLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("unknown team", exception.Message, StringComparison.Ordinal);
        Assert.Null(await captureStore.GetLatestAsync(TestContext.Current.CancellationToken));
        await AssertDatabaseShapeAsync(files.DatabasePath, expectedCaptures: 0);
    }

    [Fact]
    public async Task Arbitrary_photo_identifier_is_rejected_before_any_rows_are_written()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        var captureStore = new OfficialFplCaptureStore(options);
        using var httpClient = new HttpClient(new StaticOfficialFplHandler(
            CreateBootstrap(60, firstPlayerPhoto: "../portrait.jpg"),
            CreateFixtures()));
        var importer = new OfficialFplImporter(
            httpClient,
            captureStore,
            new FixedTimeProvider(RetrievedAtUtc));

        OfficialFplPayloadException exception =
            await Assert.ThrowsAsync<OfficialFplPayloadException>(
                () => importer.ImportLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("official asset identifier", exception.Message, StringComparison.Ordinal);
        Assert.Null(await captureStore.GetLatestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invalid_published_expected_points_is_rejected_before_any_rows_are_written()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        var captureStore = new OfficialFplCaptureStore(options);
        using var httpClient = new HttpClient(new StaticOfficialFplHandler(
            CreateBootstrap(60, firstPlayerExpectedPointsNext: "not-a-number"),
            CreateFixtures()));
        var importer = new OfficialFplImporter(
            httpClient,
            captureStore,
            new FixedTimeProvider(RetrievedAtUtc));

        OfficialFplPayloadException exception =
            await Assert.ThrowsAsync<OfficialFplPayloadException>(
                () => importer.ImportLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("ep_next", exception.Message, StringComparison.Ordinal);
        Assert.Null(await captureStore.GetLatestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Non_json_response_is_rejected()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        var migrationStore = new DecisionSnapshotStore(options);
        await migrationStore.MigrateAsync(TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient(new PlainTextHandler());
        var importer = new OfficialFplImporter(
            httpClient,
            new OfficialFplCaptureStore(options),
            new FixedTimeProvider(RetrievedAtUtc));

        OfficialFplPayloadException exception =
            await Assert.ThrowsAsync<OfficialFplPayloadException>(
                () => importer.ImportLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("content type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_provider_property_is_rejected()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        var migrationStore = new DecisionSnapshotStore(options);
        await migrationStore.MigrateAsync(TestContext.Current.CancellationToken);
        var captureStore = new OfficialFplCaptureStore(options);
        using var httpClient = new HttpClient(new StaticOfficialFplHandler(
            """{"events":[],"events":[],"teams":[],"elements":[]}"""u8.ToArray(),
            CreateFixtures()));
        var importer = new OfficialFplImporter(
            httpClient,
            captureStore,
            new FixedTimeProvider(RetrievedAtUtc));

        OfficialFplPayloadException exception =
            await Assert.ThrowsAsync<OfficialFplPayloadException>(
                () => importer.ImportLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("duplicate JSON property", exception.Message, StringComparison.Ordinal);
        Assert.Null(await captureStore.GetLatestAsync(TestContext.Current.CancellationToken));
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

    private static byte[] CreateBootstrap(
        int priceTenths,
        int firstPlayerTeamId = 1,
        string? firstPlayerPhoto = null,
        string? firstPlayerExpectedPointsNext = "4.2")
    {
        object[] players =
        [
            Player(
                1,
                101,
                firstPlayerTeamId,
                1,
                "Ada",
                "Keeper",
                "Keeper",
                priceTenths,
                firstPlayerPhoto,
                firstPlayerExpectedPointsNext),
            Player(2, 102, 1, 2, "Bea", "Back", "Back", 50),
            Player(3, 103, 2, 3, "Mia", "Middle", "Middle", 75),
            Player(4, 104, 2, 4, "Fran", "Forward", "Forward", 80),
        ];
        return JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                events = new object[]
                {
                    new
                    {
                        id = 1,
                        name = "Gameweek 1",
                        deadline_time = "2026-08-21T17:30:00Z",
                        finished = true,
                        data_checked = true,
                        is_current = false,
                        is_next = false,
                    },
                    new
                    {
                        id = 2,
                        name = "Gameweek 2",
                        deadline_time = "2026-08-29T14:00:00Z",
                        finished = false,
                        data_checked = false,
                        is_current = false,
                        is_next = true,
                    },
                },
                teams = new object[]
                {
                    new { id = 1, code = 10, name = "North Town", short_name = "NTH" },
                    new { id = 2, code = 20, name = "South City", short_name = "STH" },
                },
                elements = players,
            });
    }

    private static object Player(
        int id,
        int code,
        int team,
        int elementType,
        string firstName,
        string secondName,
        string webName,
        int priceTenths,
        string? photoIdentifier = null,
        string? expectedPointsNext = "3.0") =>
        new
        {
            id,
            code,
            team,
            element_type = elementType,
            first_name = firstName,
            second_name = secondName,
            web_name = webName,
            photo = photoIdentifier ?? $"{code}.jpg",
            now_cost = priceTenths,
            status = "a",
            news = string.Empty,
            news_added = (string?)null,
            chance_of_playing_next_round = (int?)null,
            selected_by_percent = "10.5",
            ep_next = expectedPointsNext,
            total_points = 10,
            minutes = 90,
            starts = 1,
        };

    private static byte[] CreateFixtures() =>
        JsonSerializer.SerializeToUtf8Bytes(
            new object[]
            {
                new
                {
                    id = 1,
                    @event = 2,
                    team_h = 1,
                    team_a = 2,
                    kickoff_time = "2026-08-29T14:00:00Z",
                    started = false,
                    finished = false,
                    finished_provisional = false,
                    team_h_score = (int?)null,
                    team_a_score = (int?)null,
                },
            });

    private static async Task ClearPhotoIdentifierAsync(
        string databasePath,
        long captureId,
        int playerId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE official_fpl_players
            SET photo_identifier = NULL
            WHERE capture_id = $captureId AND player_id = $playerId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$playerId", playerId);
        Assert.Equal(
            1,
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    private static async Task ClearExpectedPointsNextAsync(
        string databasePath,
        long captureId,
        int playerId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE official_fpl_players
            SET expected_points_next = NULL
            WHERE capture_id = $captureId AND player_id = $playerId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$playerId", playerId);
        Assert.Equal(
            1,
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<string?> ReadPhotoIdentifierAsync(
        string databasePath,
        long captureId,
        int playerId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT photo_identifier
            FROM official_fpl_players
            WHERE capture_id = $captureId AND player_id = $playerId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$playerId", playerId);
        return (string?)await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken);
    }

    private static async Task<decimal?> ReadExpectedPointsNextAsync(
        string databasePath,
        long captureId,
        int playerId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT expected_points_next
            FROM official_fpl_players
            WHERE capture_id = $captureId AND player_id = $playerId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$playerId", playerId);
        object? result = await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken);
        return result is string value
            ? decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static async Task AssertDatabaseShapeAsync(
        string databasePath,
        int expectedCaptures)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM official_fpl_captures),
                (SELECT COUNT(*) FROM official_fpl_events),
                (SELECT COUNT(*) FROM official_fpl_teams),
                (SELECT COUNT(*) FROM official_fpl_players),
                (SELECT COUNT(*) FROM official_fpl_fixtures);
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(expectedCaptures, reader.GetInt32(0));
        Assert.Equal(expectedCaptures * 2, reader.GetInt32(1));
        Assert.Equal(expectedCaptures * 2, reader.GetInt32(2));
        Assert.Equal(expectedCaptures * 4, reader.GetInt32(3));
        Assert.Equal(expectedCaptures, reader.GetInt32(4));
    }

    private sealed class StaticOfficialFplHandler : HttpMessageHandler
    {
        private readonly byte[] _bootstrap;
        private readonly byte[] _fixtures;

        public StaticOfficialFplHandler(byte[] bootstrap, byte[] fixtures)
        {
            _bootstrap = bootstrap;
            _fixtures = fixtures;
        }

        public ConcurrentBag<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Uri uri = Assert.IsType<Uri>(request.RequestUri);
            Requests.Add(uri);
            byte[] content = uri == OfficialFplImporter.BootstrapUri
                ? _bootstrap
                : uri == OfficialFplImporter.FixturesUri
                    ? _fixtures
                    : throw new InvalidOperationException($"Unexpected URI: {uri}");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }

    private sealed class PlainTextHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not json"),
                });
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-official-fpl-tests-{Guid.NewGuid():N}");

        public TemporaryDatabaseFiles()
        {
            Directory.CreateDirectory(_directory);
            DatabasePath = Path.Combine(_directory, "autofpl.db");
        }

        public string DatabasePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
