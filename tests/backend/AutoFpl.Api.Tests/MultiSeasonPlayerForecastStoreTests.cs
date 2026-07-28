using System.Net.Http.Json;
using System.Text.Json;

using AutoFpl.Api.Advice;
using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Forecasts;
using AutoFpl.Contracts.Sources;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class MultiSeasonPlayerForecastStoreTests
{
    private static readonly DateTimeOffset CaptureTime =
        new(2026, 7, 28, 22, 38, 41, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline =
        new(2026, 8, 21, 17, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Exact_shadow_is_immutable_idempotent_and_served_separately()
    {
        using var files = new TemporaryDatabaseFiles();
        (DatabaseOptions options, MultiSeasonPlayerForecastDocument request) =
            await CreateDatabaseAsync(files.DatabasePath);
        var store = new MultiSeasonPlayerForecastStore(
            options,
            TimeProvider.System);

        MultiSeasonPlayerForecastDocument imported = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        MultiSeasonPlayerForecastDocument repeated = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        MultiSeasonPlayerForecastDocument? latest = await store.GetLatestAsync(
            TestContext.Current.CancellationToken);

        Assert.NotNull(imported.ForecastArtifactId);
        Assert.Equal(64, imported.ForecastArtifactContentSha256?.Length);
        Assert.Equal(imported.ForecastArtifactId, repeated.ForecastArtifactId);
        Assert.Equal(imported.ForecastArtifactId, latest?.ForecastArtifactId);
        Assert.False(imported.InfluencesAdvice);
        Assert.False(imported.IsPromoted);
        Assert.True(imported.Comparison.BaselineStillDrivesAdvice);

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
        MultiSeasonPlayerForecastDocument? response =
            await client.GetFromJsonAsync<MultiSeasonPlayerForecastDocument>(
                "/api/v1/forecasts/multi-season-shadow/latest",
                TestContext.Current.CancellationToken);
        OfficialFplPlayerDossierDocument? dossier =
            await client.GetFromJsonAsync<OfficialFplPlayerDossierDocument>(
                "/api/v1/data/official-fpl/replays/2026-27/1/players/1",
                TestContext.Current.CancellationToken);

        Assert.Equal(imported.ForecastArtifactId, response?.ForecastArtifactId);
        Assert.Equal("1.4", dossier?.SchemaVersion);
        Assert.NotNull(dossier?.MultiSeasonShadow);
        Assert.Equal(
            imported.ForecastArtifactId,
            dossier.MultiSeasonShadow.ForecastArtifactId);
        Assert.Equal(
            "both-historical-seasons",
            dossier.MultiSeasonShadow.HistoricalIdentityStatus);
        Assert.False(dossier.MultiSeasonShadow.InfluencesAdvice);
        Assert.Null(dossier.PreseasonChallenger);

        await using var connection = new SqliteConnection(
            options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText =
            """
            UPDATE multi_season_player_forecast_artifacts
            SET status = status;
            """;
        SqliteException immutable = await Assert.ThrowsAsync<SqliteException>(
            () => update.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken));
        Assert.Contains("immutable", immutable.Message);
    }

    [Fact]
    public async Task Changed_player_or_unknown_field_fails_closed()
    {
        using var files = new TemporaryDatabaseFiles();
        (DatabaseOptions options, MultiSeasonPlayerForecastDocument request) =
            await CreateDatabaseAsync(files.DatabasePath);
        var store = new MultiSeasonPlayerForecastStore(
            options,
            TimeProvider.System);
        MultiSeasonPlayerForecastDocument changedModel = request with
        {
            Training = request.Training with
            {
                ModelConfiguration = JsonSerializer.SerializeToElement(
                    new { randomSeed = 1 }),
            },
        };
        MultiSeasonPlayerForecastValidationException modelException =
            await Assert.ThrowsAsync<MultiSeasonPlayerForecastValidationException>(
                () => store.ImportAsync(
                    changedModel,
                    TestContext.Current.CancellationToken));
        Assert.Equal("identity", modelException.Code);

        MultiSeasonPlayerForecastDocument changed = request with
        {
            Players =
            [
                request.Players[0] with
                {
                    DifferenceFromBaselineV0 = 99m,
                },
            ],
        };

        MultiSeasonPlayerForecastValidationException exception =
            await Assert.ThrowsAsync<MultiSeasonPlayerForecastValidationException>(
                () => store.ImportAsync(
                    changed,
                    TestContext.Current.CancellationToken));
        Assert.Equal("difference-mismatch", exception.Code);

        string json = JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string path = Path.Combine(files.DirectoryPath, "shadow.json");
        await File.WriteAllTextAsync(
            path,
            json[..^1] + ",\"unexpected\":true}",
            TestContext.Current.CancellationToken);
        var importer = new MultiSeasonPlayerForecastImporter(store);
        await Assert.ThrowsAsync<JsonException>(
            () => importer.ImportFileAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    private static async Task<(
        DatabaseOptions Options,
        MultiSeasonPlayerForecastDocument Request)> CreateDatabaseAsync(
        string databasePath)
    {
        DatabaseOptions options = CreateOptions(databasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        await using var connection = new SqliteConnection(
            options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await SeedOfficialAsync(connection);
        await SeedHistoricalAsync(connection);
        decimal baselinePoints = await SeedBaselineAsync(options);
        return (options, CreateRequest(baselinePoints));
    }

    private static async Task SeedOfficialAsync(SqliteConnection connection)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO official_fpl_captures (
                capture_id, schema_version, source_key, season_code,
                bootstrap_url, fixtures_url, retrieved_at_utc,
                available_at_utc, bootstrap_sha256, fixtures_sha256,
                bootstrap_json, fixtures_json, event_count, team_count,
                player_count, fixture_count, next_gameweek_number,
                next_deadline_utc, latest_completed_gameweek, created_at_utc
            )
            VALUES (
                15, '1.0', 'official-fpl-api/v1', '2026-27',
                'https://example.test/bootstrap',
                'https://example.test/fixtures',
                $captureTime, $captureTime, $bootstrapHash, $fixturesHash,
                X'01', X'02', 1, 1, 1, 0, 1, $deadline, NULL, $captureTime
            );
            INSERT INTO official_fpl_events VALUES (
                15, 1, 'Gameweek 1', $deadline, 0, 0, 0, 1
            );
            INSERT INTO official_fpl_teams VALUES (
                15, 1, 100, 'Team One', 'ONE'
            );
            INSERT INTO official_fpl_players (
                capture_id, player_id, code, team_id, position,
                first_name, second_name, web_name, price_tenths, status,
                news, news_added_utc, chance_next_round,
                selected_by_percent, total_points, minutes, starts
            )
            VALUES (
                15, 1, 1001, 1, 'midfielder', 'Test', 'Player',
                'Player', 75, 'a', '', NULL, NULL, '10.0', 0, 0, 0
            );
            """;
        command.Parameters.AddWithValue(
            "$captureTime",
            CaptureTime.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue(
            "$deadline",
            Deadline.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$bootstrapHash", new string('a', 64));
        command.Parameters.AddWithValue("$fixturesHash", new string('b', 64));
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SeedHistoricalAsync(SqliteConnection connection)
    {
        (long Id, string Season, string PlayersHash, string GameweeksHash,
            int PlayerGameweeks, string Available)[] captures =
        [
            (
                2,
                "2024-25",
                "75686051b265cbe7755ac71213ecaad21b26ee1cc46a8bafbba19c39ce894b05",
                "5bbbcba6353b4c72ad273adcc8e3aa451946a826564679788f45b1cb3325b84e",
                27_283,
                "2026-07-28T21:55:42+00:00"
            ),
            (
                1,
                "2025-26",
                "412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b",
                "0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d",
                29_747,
                "2026-07-26T18:17:21+00:00"
            ),
        ];
        foreach (var capture in captures)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO historical_fpl_season_captures (
                    capture_id, schema_version, source_key, season_code,
                    source_revision, players_url, gameweeks_url,
                    published_at_utc, retrieved_at_utc, available_at_utc,
                    players_sha256, gameweeks_sha256, players_csv_brotli,
                    gameweeks_csv_brotli, player_count,
                    player_gameweek_count, stable_code_count, created_at_utc
                )
                VALUES (
                    $id, '1.0', 'vaastav-fpl-historical/v1', $season,
                    $revision, 'https://example.test/players',
                    'https://example.test/gameweeks',
                    '2026-06-01T00:00:00Z',
                    $available, $available, $playersHash, $gameweeksHash,
                    X'01', X'02', 1, $playerGameweeks, 1, $available
                );
                INSERT INTO historical_fpl_players VALUES (
                    $id, 1, 1001, 'Test', 'Player', 'Player',
                    'midfielder', 1, 'a', NULL, $newsHash, NULL
                );
                INSERT INTO historical_fpl_player_gameweeks VALUES (
                    $id, 1, 1001, 1, $fixtureId,
                    '2026-01-01T15:00:00Z', 'Team One', 2, 1,
                    90, 1, 5, 0, 0, 0, 1, 0, 0, 0, 0, 10,
                    '1.0', '1.0', '1.0', '1.0',
                    '0.1', '0.1', '0.2', '0.5',
                    1, 1, 1, 1
                );
                """;
            command.Parameters.AddWithValue("$id", capture.Id);
            command.Parameters.AddWithValue("$season", capture.Season);
            command.Parameters.AddWithValue(
                "$revision",
                MultiSeasonPlayerForecastStore.SourceRevision);
            command.Parameters.AddWithValue("$available", capture.Available);
            command.Parameters.AddWithValue(
                "$playersHash",
                capture.PlayersHash);
            command.Parameters.AddWithValue(
                "$gameweeksHash",
                capture.GameweeksHash);
            command.Parameters.AddWithValue(
                "$playerGameweeks",
                capture.PlayerGameweeks);
            command.Parameters.AddWithValue("$newsHash", new string('c', 64));
            command.Parameters.AddWithValue(
                "$fixtureId",
                1000 + capture.Id);
            await command.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken);
        }
    }

    private static async Task<decimal> SeedBaselineAsync(
        DatabaseOptions options)
    {
        var captureStore = new OfficialFplCaptureStore(options);
        var previewStore = new OfficialDecisionRoomPreviewStore(
            options,
            captureStore);
        var baselineStore = new PlayerGameweekForecastArtifactStore(
            options,
            previewStore,
            TimeProvider.System);
        PlayerGameweekForecastDocument baseline =
            await baselineStore.RefreshLatestAsync(
                TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException(
                "The test baseline was not created.");
        return Assert.Single(baseline.Players).ExpectedPoints;
    }

    private static MultiSeasonPlayerForecastDocument CreateRequest(
        decimal baselinePoints)
    {
        JsonElement configuration = JsonSerializer.SerializeToElement(
            new
            {
                loss = "squared_error",
                learningRate = 0.05m,
                maximumIterations = 100,
                maximumLeafNodes = 7,
                minimumSamplesPerLeaf = 20,
                l2Regularisation = 10m,
                maximumBins = 63,
                earlyStopping = false,
                randomSeed = 20_260_726,
                missingValues = "native-learned-branch",
            });
        JsonElement diagnostics = JsonSerializer.SerializeToElement(
            new
            {
                implementation =
                    "sklearn.ensemble.HistGradientBoostingRegressor",
                libraryVersion = "1.9.0",
                trainingRows = 56_257,
                candidateFeatureCount = 56,
                modelFeatureCount = 56,
                completedIterations = 100,
            });
        return new(
            "1.0",
            MultiSeasonPlayerForecastStore.ArtifactType,
            MultiSeasonPlayerForecastStore.Status,
            MultiSeasonPlayerForecastStore.ModelKey,
            false,
            false,
            "2026-27",
            1,
            Deadline,
            CaptureTime,
            15,
            new(
                ["2024-25", "2025-26"],
                [
                    new(
                        2,
                        "2024-25",
                        MultiSeasonPlayerForecastStore.SourceRevision,
                        DateTimeOffset.Parse("2026-07-28T21:55:42+00:00"),
                        "75686051b265cbe7755ac71213ecaad21b26ee1cc46a8bafbba19c39ce894b05",
                        "5bbbcba6353b4c72ad273adcc8e3aa451946a826564679788f45b1cb3325b84e",
                        1,
                        27_283,
                        1),
                    new(
                        1,
                        "2025-26",
                        MultiSeasonPlayerForecastStore.SourceRevision,
                        DateTimeOffset.Parse("2026-07-26T18:17:21+00:00"),
                        "412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b",
                        "0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d",
                        1,
                        29_747,
                        1),
                ],
                76,
                56_257,
                "multi-season-histogram-tree",
                configuration,
                diagnostics,
                MultiSeasonPlayerForecastStore.EvaluationRunIdentity),
            new(
                MultiSeasonPlayerForecastStore.BaselineModelKey,
                0.091511m,
                0.002607m,
                3,
                8,
                false,
                true),
            "point-mean-only-no-calibrated-distribution",
            1,
            1,
            0,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["both-historical-seasons"] = 1,
                ["latest-historical-season-only"] = 0,
                ["older-historical-season-only"] = 0,
                ["no-historical-season-match"] = 0,
            },
            [
                new(
                    1,
                    1001,
                    "Player",
                    "midfielder",
                    1,
                    "Team One",
                    "a",
                    null,
                    "authoritative-current-official-not-modelled",
                    "both-historical-seasons",
                    ["2024-25", "2025-26"],
                    2,
                    baselinePoints + 0.1m,
                    baselinePoints,
                    0.1m),
            ],
            ["Comparison only; Baseline v0 still drives advice."],
            new string('d', 64),
            new string('e', 64));
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

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        public TemporaryDatabaseFiles()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"autofpl-multi-season-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            DatabasePath = Path.Combine(DirectoryPath, "autofpl.db");
        }

        public string DirectoryPath { get; }

        public string DatabasePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
