using System.Net;
using System.Net.Http.Json;
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

public sealed class EvidenceClaimStoreTests
{
    private static readonly DateTimeOffset CaptureTime =
        new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClaimTime =
        new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Import_is_identity_checked_immutable_idempotent_and_cutoff_safe()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new EvidenceClaimStore(
            options,
            new FixedTimeProvider(ClaimTime.AddMinutes(1)));
        EvidenceClaimImportRequest request = CreateRequest();

        EvidenceClaimDocument first = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        EvidenceClaimDocument duplicate = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        EvidenceClaimSetDocument before = await store.GetForGameweekAsync(
            "2026-27",
            1,
            ClaimTime.AddTicks(-1),
            TestContext.Current.CancellationToken);
        EvidenceClaimSetDocument atCutoff = await store.GetForGameweekAsync(
            "2026-27",
            1,
            ClaimTime,
            TestContext.Current.CancellationToken);

        Assert.Equal(first, duplicate);
        Assert.Equal("quarantined", first.Status);
        Assert.Equal(1, first.IdentityCaptureId);
        Assert.True(first.IsPreDeadline);
        Assert.Equal("start", first.ClaimType);
        Assert.Equal("starts", first.StartStatus);
        Assert.Equal(0.7m, first.ForecastProbability);
        Assert.Equal(64, first.ClaimContentSha256.Length);
        Assert.Empty(before.Claims);
        Assert.Equal(first, Assert.Single(atCutoff.Claims));

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "UPDATE evidence_claims SET source_span = 'changed' WHERE claim_id = 1;";
        SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
            async () => await command.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "evidence claims are immutable",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_rejects_unknown_player_and_cross_type_values()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new EvidenceClaimStore(options, TimeProvider.System);

        EvidenceClaimValidationException identityException =
            await Assert.ThrowsAsync<EvidenceClaimValidationException>(
                async () => await store.ImportAsync(
                    CreateRequest() with { PlayerId = 999 },
                    TestContext.Current.CancellationToken));
        Assert.Equal("player.identity-unavailable", identityException.Code);
        Assert.Equal("playerId", identityException.Field);

        EvidenceClaimValidationException shapeException =
            await Assert.ThrowsAsync<EvidenceClaimValidationException>(
                async () => await store.ImportAsync(
                    CreateRequest() with { ExpectedMinutes = 75 },
                    TestContext.Current.CancellationToken));
        Assert.Equal("claim-value.invalid", shapeException.Code);
        Assert.Equal("claimType", shapeException.Field);
    }

    [Fact]
    public async Task Read_api_exposes_only_claims_available_by_requested_cutoff()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new EvidenceClaimStore(options, TimeProvider.System);
        EvidenceClaimDocument imported = await store.ImportAsync(
            CreateRequest(),
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

        HttpResponseMessage missingCutoff = await client.GetAsync(
            "/api/v1/evidence/claims/2026-27/1",
            TestContext.Current.CancellationToken);
        EvidenceClaimSetDocument? served =
            await client.GetFromJsonAsync<EvidenceClaimSetDocument>(
                "/api/v1/evidence/claims/2026-27/1"
                + "?decisionCutoffUtc=2026-07-26T10%3A00%3A00Z",
                TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, missingCutoff.StatusCode);
        Assert.NotNull(served);
        Assert.Equal(imported, Assert.Single(served.Claims));
    }

    [Fact]
    public async Task File_import_is_strict_and_bounded()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var importer = new EvidenceClaimImporter(
            new EvidenceClaimStore(options, TimeProvider.System));
        string validPath = Path.Combine(files.DirectoryPath, "claim.json");
        string unknownPath = Path.Combine(files.DirectoryPath, "unknown.json");
        string oversizedPath = Path.Combine(files.DirectoryPath, "oversized.json");
        await File.WriteAllBytesAsync(
            validPath,
            JsonSerializer.SerializeToUtf8Bytes(
                CreateRequest(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            unknownPath,
            "{\"schemaVersion\":\"1.0\",\"unknown\":true}",
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            oversizedPath,
            new byte[EvidenceClaimImporter.MaximumInputBytes + 1],
            TestContext.Current.CancellationToken);

        EvidenceClaimDocument imported = await importer.ImportFileAsync(
            validPath,
            TestContext.Current.CancellationToken);

        Assert.Equal("start", imported.ClaimType);
        await Assert.ThrowsAsync<JsonException>(
            async () => await importer.ImportFileAsync(
                unknownPath,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await importer.ImportFileAsync(
                oversizedPath,
                TestContext.Current.CancellationToken));
    }

    private static EvidenceClaimImportRequest CreateRequest() =>
        new(
            SchemaVersion: "1.0",
            SourceKey: "example-club-reporter/v1",
            CanonicalUrl: "https://example.com/reports/player-101",
            Author: "Example Reporter",
            PublishedAtUtc: ClaimTime.AddMinutes(-10),
            RetrievedAtUtc: ClaimTime,
            AvailableAtUtc: ClaimTime,
            ContentSha256: new string('a', 64),
            SourceRevision: 1,
            SeasonCode: "2026-27",
            Gameweek: 1,
            PlayerId: 101,
            ClaimType: "start",
            AvailabilityStatus: null,
            StartStatus: "starts",
            ForecastProbability: 0.7m,
            ExpectedMinutes: null,
            Role: null,
            Directness: "opinion",
            SourceSpan: "Player 101 is expected to start this weekend.",
            ExtractionMethod: "llm",
            ExtractionVersion: "test-extractor/v1",
            ExtractionConfidence: 0.9m,
            DuplicateClusterKey: new string('b', 64));

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

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-evidence-claim-tests-{Guid.NewGuid():N}");

        public TemporaryDatabaseFiles()
        {
            Directory.CreateDirectory(_directory);
            DatabasePath = Path.Combine(_directory, "autofpl.db");
        }

        public string DatabasePath { get; }

        public string DirectoryPath => _directory;

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
