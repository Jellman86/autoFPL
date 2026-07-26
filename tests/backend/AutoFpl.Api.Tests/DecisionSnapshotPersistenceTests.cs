using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Advice;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class DecisionSnapshotPersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset DeadlineUtc =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CutoffUtc =
        new(2026, 8, 15, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Snapshot_is_cutoff_correct_revisioned_restart_safe_and_backed_up()
    {
        using var files = new TemporaryDatabaseFiles();
        long firstSnapshotId;
        long firstObservationId;

        await using (WebApplicationFactory<Program> factory = CreateFactory(files.DatabasePath))
        {
            using HttpClient client = factory.CreateClient();
            object request = CreateRequest(
                observations:
                [
                    Observation(
                        sourceKey: "synthetic-forecast",
                        playerId: 8,
                        metric: "expected-points",
                        value: 7.2m,
                        availableAtUtc: CutoffUtc.AddMinutes(-15)),
                    Observation(
                        sourceKey: "late-team-news",
                        playerId: 9,
                        metric: "expected-minutes",
                        value: 25m,
                        availableAtUtc: CutoffUtc.AddMinutes(15)),
                ]);

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/v1/decision-snapshots",
                request,
                JsonOptions,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using JsonDocument created = await ReadJsonAsync(response);
            JsonElement root = created.RootElement;
            firstSnapshotId = root.GetProperty("snapshotId").GetInt64();
            Assert.Equal(1, root.GetProperty("revision").GetInt32());
            Assert.Equal(15, root.GetProperty("squad").GetProperty("players").GetArrayLength());
            JsonElement persistedObservation = Assert.Single(
                root.GetProperty("observations").EnumerateArray());
            firstObservationId = persistedObservation.GetProperty("observationId").GetInt64();
            Assert.Equal(8, persistedObservation.GetProperty("playerId").GetInt32());
            Assert.Equal(7.2m, persistedObservation.GetProperty("value").GetDecimal());
            Assert.Equal(64, root.GetProperty("contentHash").GetString()!.Length);
        }

        await using (WebApplicationFactory<Program> restarted = CreateFactory(files.DatabasePath))
        {
            using HttpClient client = restarted.CreateClient();
            using HttpResponseMessage readResponse = await client.GetAsync(
                $"/api/v1/decision-snapshots/{firstSnapshotId}",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
            using JsonDocument persisted = await ReadJsonAsync(readResponse);
            Assert.Equal(firstSnapshotId, persisted.RootElement.GetProperty("snapshotId").GetInt64());
            Assert.Equal(
                7.2m,
                persisted.RootElement
                    .GetProperty("observations")[0]
                    .GetProperty("value")
                    .GetDecimal());

            object correction = CreateRequest(
                supersedesSnapshotId: firstSnapshotId,
                observations:
                [
                    Observation(
                        sourceKey: "synthetic-forecast",
                        playerId: 8,
                        metric: "expected-points",
                        value: 6.8m,
                        availableAtUtc: CutoffUtc.AddMinutes(-5),
                        supersedesObservationId: firstObservationId),
                ]);
            using HttpResponseMessage correctionResponse = await client.PostAsJsonAsync(
                "/api/v1/decision-snapshots",
                correction,
                JsonOptions,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Created, correctionResponse.StatusCode);
            using JsonDocument corrected = await ReadJsonAsync(correctionResponse);
            Assert.Equal(2, corrected.RootElement.GetProperty("revision").GetInt32());
            Assert.Equal(
                firstSnapshotId,
                corrected.RootElement.GetProperty("supersedesSnapshotId").GetInt64());
            JsonElement correctedObservation = Assert.Single(
                corrected.RootElement.GetProperty("observations").EnumerateArray());
            Assert.Equal(2, correctedObservation.GetProperty("revision").GetInt32());
            Assert.Equal(
                firstObservationId,
                correctedObservation.GetProperty("supersedesObservationId").GetInt64());
            Assert.Equal(6.8m, correctedObservation.GetProperty("value").GetDecimal());

            using HttpResponseMessage originalResponse = await client.GetAsync(
                $"/api/v1/decision-snapshots/{firstSnapshotId}",
                TestContext.Current.CancellationToken);
            using JsonDocument original = await ReadJsonAsync(originalResponse);
            Assert.Equal(
                7.2m,
                original.RootElement
                    .GetProperty("observations")[0]
                    .GetProperty("value")
                    .GetDecimal());

            DecisionSnapshotStore store =
                restarted.Services.GetRequiredService<DecisionSnapshotStore>();
            Assert.Equal(
                "ok",
                await store.IntegrityCheckAsync(TestContext.Current.CancellationToken));
            await store.BackupAsync(
                files.BackupPath,
                TestContext.Current.CancellationToken);
        }

        await AssertDatabaseShapeAsync(files.DatabasePath);

        await using WebApplicationFactory<Program> backupFactory =
            CreateFactory(files.BackupPath);
        using HttpClient backupClient = backupFactory.CreateClient();
        using HttpResponseMessage backupResponse = await backupClient.GetAsync(
            $"/api/v1/decision-snapshots/{firstSnapshotId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, backupResponse.StatusCode);
    }

    [Fact]
    public async Task Existing_evidence_and_snapshot_require_explicit_supersedes_links()
    {
        using var files = new TemporaryDatabaseFiles();
        await using WebApplicationFactory<Program> factory = CreateFactory(files.DatabasePath);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage first = await client.PostAsJsonAsync(
            "/api/v1/decision-snapshots",
            CreateRequest(
                observations:
                [
                    Observation(
                        "synthetic-forecast",
                        8,
                        "expected-points",
                        7.2m,
                        CutoffUtc.AddMinutes(-15)),
                ]),
            JsonOptions,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using HttpResponseMessage duplicate = await client.PostAsJsonAsync(
            "/api/v1/decision-snapshots",
            CreateRequest(
                observations:
                [
                    Observation(
                        "synthetic-forecast",
                        8,
                        "expected-points",
                        6.8m,
                        CutoffUtc.AddMinutes(-5)),
                ]),
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);
        using JsonDocument problem = await ReadJsonAsync(duplicate);
        Assert.Equal(
            "observation.supersedes.required",
            problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Migration_refuses_a_database_created_by_newer_application_code()
    {
        using var files = new TemporaryDatabaseFiles();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = files.DatabasePath,
        }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL UNIQUE,
                    applied_at_utc TEXT NOT NULL
                );
                INSERT INTO schema_migrations (version, name, applied_at_utc)
                VALUES (99, 'future-schema', '2026-08-15T10:00:00.0000000Z');
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:DatabasePath"] = files.DatabasePath,
                })
            .Build();
        var store = new DecisionSnapshotStore(
            DatabaseOptions.FromConfiguration(configuration));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.MigrateAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            "Database schema is newer than this application.",
            exception.Message);
    }

    [Fact]
    public async Task Synthetic_decision_room_keeps_the_same_snapshot_after_restart()
    {
        using var files = new TemporaryDatabaseFiles();
        long snapshotId;

        await using (WebApplicationFactory<Program> first = CreateFactory(
            files.DatabasePath,
            seedDemoSnapshot: true))
        {
            using HttpClient client = first.CreateClient();
            GameweekAdviceDocument advice =
                (await client.GetFromJsonAsync<GameweekAdviceDocument>(
                    "/api/v1/advice/demo",
                    TestContext.Current.CancellationToken))!;
            snapshotId = advice.SnapshotId!.Value;
            Assert.Equal("synthetic-persisted", advice.EvidenceStatus);
        }

        await using WebApplicationFactory<Program> restarted = CreateFactory(
            files.DatabasePath,
            seedDemoSnapshot: true);
        using HttpClient restartedClient = restarted.CreateClient();
        GameweekAdviceDocument restartedAdvice =
            (await restartedClient.GetFromJsonAsync<GameweekAdviceDocument>(
                "/api/v1/advice/demo",
                TestContext.Current.CancellationToken))!;
        Assert.Equal(snapshotId, restartedAdvice.SnapshotId);
        Assert.Equal(1, restartedAdvice.SnapshotRevision);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string databasePath,
        bool seedDemoSnapshot = false) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("AutoFpl:DatabasePath", databasePath);
                builder.UseSetting(
                    "AutoFpl:SeedDemoSnapshot",
                    seedDemoSnapshot ? "true" : "false");
            });

    private static object CreateRequest(
        IReadOnlyList<object> observations,
        long? supersedesSnapshotId = null)
    {
        object[] players =
        [
            Player(1, "Keeper One", 1, "goalkeeper", 45),
            Player(2, "Keeper Two", 2, "goalkeeper", 45),
            Player(3, "Defender Three", 1, "defender", 45),
            Player(4, "Defender Four", 2, "defender", 45),
            Player(5, "Defender Five", 3, "defender", 45),
            Player(6, "Defender Six", 4, "defender", 45),
            Player(7, "Defender Seven", 5, "defender", 45),
            Player(8, "Midfielder Eight", 1, "midfielder", 50),
            Player(9, "Midfielder Nine", 2, "midfielder", 50),
            Player(10, "Midfielder Ten", 3, "midfielder", 50),
            Player(11, "Midfielder Eleven", 4, "midfielder", 50),
            Player(12, "Midfielder Twelve", 5, "midfielder", 50),
            Player(13, "Forward Thirteen", 3, "forward", 60),
            Player(14, "Forward Fourteen", 4, "forward", 60),
            Player(15, "Forward Fifteen", 5, "forward", 60),
        ];

        return new
        {
            schemaVersion = "1.0",
            seasonCode = "2026-27",
            gameweek = 1,
            deadlineUtc = DeadlineUtc,
            decisionCutoffUtc = CutoffUtc,
            budgetTenths = 1_000,
            players,
            startingPlayerIds = new[] { 1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14 },
            captainPlayerId = 8,
            viceCaptainPlayerId = 13,
            replacementGoalkeeperPlayerId = 2,
            outfieldSubstitutePlayerIds = new[] { 6, 7, 15 },
            observations,
            supersedesSnapshotId,
        };
    }

    private static object Player(
        int playerId,
        string displayName,
        int clubId,
        string position,
        int priceTenths) => new
        {
            playerId,
            displayName,
            clubId,
            position,
            priceTenths,
        };

    private static object Observation(
        string sourceKey,
        int playerId,
        string metric,
        decimal value,
        DateTimeOffset availableAtUtc,
        long? supersedesObservationId = null) => new
        {
            sourceKey,
            playerId,
            metric,
            value,
            observedAtUtc = availableAtUtc.AddMinutes(-10),
            retrievedAtUtc = availableAtUtc.AddMinutes(-5),
            availableAtUtc,
            supersedesObservationId,
        };

    private static async Task AssertDatabaseShapeAsync(string databasePath)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:DatabasePath"] = databasePath,
                })
            .Build();
        DatabaseOptions options = DatabaseOptions.FromConfiguration(configuration);
        var configuredConnection = new SqliteConnectionStringBuilder(options.ConnectionString);
        Assert.Equal(5, configuredConnection.DefaultTimeout);
        var connectionString = new SqliteConnectionStringBuilder(options.ConnectionString)
        {
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using SqliteCommand migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        Assert.Equal(14L, await migrationCommand.ExecuteScalarAsync(
            TestContext.Current.CancellationToken));

        await using SqliteCommand journalCommand = connection.CreateCommand();
        journalCommand.CommandText = "PRAGMA journal_mode;";
        Assert.Equal(
            "wal",
            await journalCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        await using SqliteCommand foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(
            1L,
            await foreignKeyCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        await using SqliteCommand rowCommand = connection.CreateCommand();
        rowCommand.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM decision_snapshots),
                (SELECT COUNT(*) FROM source_observations);
            """;
        await using SqliteDataReader reader =
            await rowCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(3L, reader.GetInt64(1));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-tests-{Guid.NewGuid():N}");

        public TemporaryDatabaseFiles()
        {
            Directory.CreateDirectory(_directory);
        }

        public string DatabasePath => Path.Combine(_directory, "autofpl.db");

        public string BackupPath => Path.Combine(_directory, "autofpl.backup.db");

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
