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

public sealed class PreseasonPlayerForecastStoreTests
{
    private static readonly DateTimeOffset CaptureTime =
        new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Exact_evaluated_artifact_is_immutable_idempotent_and_readable()
    {
        using var files = new TemporaryDatabaseFiles();
        (
            DatabaseOptions options,
            PreseasonPlayerForecastDocument request) =
            await CreateDatabaseAsync(files.DatabasePath);
        var store = new PreseasonPlayerForecastStore(
            options,
            TimeProvider.System);

        PreseasonPlayerForecastDocument imported = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        PreseasonPlayerForecastDocument repeated = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        PreseasonPlayerForecastDocument? latest = await store.GetLatestAsync(
            TestContext.Current.CancellationToken);

        Assert.NotNull(imported.ForecastArtifactId);
        Assert.Equal(64, imported.ForecastArtifactContentSha256?.Length);
        Assert.Equal(
            imported.ForecastArtifactId,
            repeated.ForecastArtifactId);
        Assert.Equal(
            imported.ForecastArtifactContentSha256,
            repeated.ForecastArtifactContentSha256);
        Assert.NotNull(latest);
        Assert.Equal(
            imported.ForecastArtifactId,
            latest.ForecastArtifactId);
        Assert.Equal(
            imported.ForecastArtifactContentSha256,
            latest.ForecastArtifactContentSha256);
        Assert.Equal(imported.Players, latest.Players);
        Assert.False(imported.IsPromoted);
        Assert.False(imported.InfluencesAdvice);
        Assert.True(imported.Comparison.BaselineStillDrivesAdvice);
        Assert.Equal(20, imported.PlayerCount);
        Assert.Equal(18, imported.PriorSeasonIdentityMatchCount);
        Assert.Equal(2, imported.PriorSeasonIdentityMissingCount);

        var dossierStore = new OfficialFplPlayerDossierStore(
            options,
            new(options));
        OfficialFplPlayerDossierDocument? dossier = await dossierStore.GetAsync(
            "2026-27",
            1,
            1,
            TestContext.Current.CancellationToken);
        Assert.NotNull(dossier);
        Assert.Equal("1.3", dossier.SchemaVersion);
        Assert.NotNull(dossier.PreseasonChallenger);
        Assert.Equal(
            imported.ModelKey,
            dossier.PreseasonChallenger.ModelKey);
        Assert.Equal(
            imported.Players[0].ExpectedPoints,
            dossier.PreseasonChallenger.ExpectedPoints);
        Assert.Equal(
            imported.ForecastArtifactId,
            dossier.PreseasonChallenger.ForecastArtifactId);
        Assert.False(dossier.PreseasonChallenger.InfluencesAdvice);

        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(
                    builder =>
                    {
                        builder.UseSetting(
                            "AutoFpl:DatabasePath",
                            files.DatabasePath);
                        builder.UseSetting(
                            "AutoFpl:SeedDemoSnapshot",
                            "false");
                    });
        using HttpClient client = factory.CreateClient();
        PreseasonPlayerForecastDocument? response =
            await client.GetFromJsonAsync<PreseasonPlayerForecastDocument>(
                "/api/v1/forecasts/preseason-challenger/latest",
                TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(
            imported.ForecastArtifactId,
            response.ForecastArtifactId);
        Assert.Equal(
            imported.ForecastArtifactContentSha256,
            response.ForecastArtifactContentSha256);
        Assert.Equal(imported.Players, response.Players);
        OfficialFplPlayerDossierDocument? servedDossier =
            await client.GetFromJsonAsync<OfficialFplPlayerDossierDocument>(
                "/api/v1/data/official-fpl/replays/2026-27/1/players/1",
                TestContext.Current.CancellationToken);
        Assert.NotNull(servedDossier?.PreseasonChallenger);
        Assert.Equal(
            imported.ForecastArtifactId,
            servedDossier.PreseasonChallenger.ForecastArtifactId);
    }

    [Fact]
    public async Task Changed_or_incomplete_artifact_fails_before_persistence()
    {
        using var files = new TemporaryDatabaseFiles();
        (
            DatabaseOptions options,
            PreseasonPlayerForecastDocument request) =
            await CreateDatabaseAsync(files.DatabasePath);
        var store = new PreseasonPlayerForecastStore(
            options,
            TimeProvider.System);
        PreseasonPlayerForecastPlayerDocument first = request.Players[0];
        PreseasonPlayerForecastDocument invalid = request with
        {
            Players =
            [
                first with { DifferenceFromBaselineV0 = 99m },
                .. request.Players.Skip(1),
            ],
        };

        PreseasonPlayerForecastValidationException exception =
            await Assert.ThrowsAsync<PreseasonPlayerForecastValidationException>(
                () => store.ImportAsync(
                    invalid,
                    TestContext.Current.CancellationToken));

        Assert.Equal("difference-mismatch", exception.Code);
        await using var connection = new SqliteConnection(
            options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM preseason_player_forecast_artifacts;";
        Assert.Equal(
            0L,
            (long)(await command.ExecuteScalarAsync(
                TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task Importer_rejects_unknown_contract_fields()
    {
        using var files = new TemporaryDatabaseFiles();
        (
            DatabaseOptions options,
            PreseasonPlayerForecastDocument request) =
            await CreateDatabaseAsync(files.DatabasePath);
        string json = JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string changed = json[..^1] + ",\"unexpected\":true}";
        string path = Path.Combine(files.DirectoryPath, "forecast.json");
        await File.WriteAllTextAsync(
            path,
            changed,
            TestContext.Current.CancellationToken);
        var importer = new PreseasonPlayerForecastImporter(
            new(options, TimeProvider.System));

        await Assert.ThrowsAsync<JsonException>(
            () => importer.ImportFileAsync(
                path,
                TestContext.Current.CancellationToken));
    }

    private static async Task<(
        DatabaseOptions Options,
        PreseasonPlayerForecastDocument Request)> CreateDatabaseAsync(
        string databasePath)
    {
        DatabaseOptions options = CreateOptions(databasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        var captureStore = new OfficialFplCaptureStore(options);
        using var httpClient = new HttpClient();
        var officialImporter = new OfficialFplImporter(
            httpClient,
            captureStore,
            TimeProvider.System);
        await officialImporter.ImportCapturedPayloadAsync(
            CreateBootstrap(),
            CreateFixtures(),
            CaptureTime,
            TestContext.Current.CancellationToken);
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
        await SeedHistoricalAsync(options);
        return (options, CreateRequest(baseline));
    }

    private static PreseasonPlayerForecastDocument CreateRequest(
        PlayerGameweekForecastDocument baseline)
    {
        PreseasonPlayerForecastPlayerDocument[] players = baseline.Players
            .Select(
                player =>
                {
                    decimal expected = player.ExpectedPoints + 0.1m;
                    return new PreseasonPlayerForecastPlayerDocument(
                        player.PlayerId,
                        1000 + player.PlayerId,
                        $"Official{player.PlayerId}",
                        player.Position,
                        ((player.PlayerId - 1) % 10) + 1,
                        $"Team {((player.PlayerId - 1) % 10) + 1}",
                        "a",
                        null,
                        "authoritative-current-official-not-modelled",
                        player.PlayerId <= 18
                            ? "stable-code-match"
                            : "no-prior-season-match",
                        player.PlayerId <= 18 ? 38 : 0,
                        expected,
                        player.ExpectedPoints,
                        0.1m);
                })
            .OrderBy(player => player.PlayerId)
            .ToArray();
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
                trainingRows = 18 * 38,
                candidateFeatureCount = 45,
                modelFeatureCount = 45,
                completedIterations = 100,
            });
        return new(
            "1.0",
            PreseasonPlayerForecastStore.ArtifactType,
            PreseasonPlayerForecastStore.Status,
            PreseasonPlayerForecastStore.ModelKey,
            IsPromoted: false,
            InfluencesAdvice: false,
            "2026-27",
            1,
            Deadline,
            CaptureTime,
            baseline.OfficialCaptureId,
            new(
                "2025-26",
                90,
                38,
                18 * 38,
                PreseasonPlayerForecastStore.SourceRevision,
                PreseasonPlayerForecastStore.PlayersSha256,
                PreseasonPlayerForecastStore.GameweeksSha256,
                "historical-preseason-histogram-tree",
                configuration,
                diagnostics,
                PreseasonPlayerForecastStore.EvaluationDataIdentity,
                PreseasonPlayerForecastStore.EvaluationRunIdentity),
            new(
                PreseasonPlayerForecastStore.BaselineModelKey,
                "locked-holdout-mae",
                0.088702m,
                BaselineStillDrivesAdvice: true),
            "point-mean-only-no-calibrated-distribution",
            players.Length,
            20,
            0,
            18,
            2,
            players,
            ["Comparison only; Baseline v0 still drives advice."],
            "a" + new string('0', 63),
            "b" + new string('0', 63));
    }

    private static async Task SeedHistoricalAsync(DatabaseOptions options)
    {
        await using var connection = new SqliteConnection(
            options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(
                TestContext.Current.CancellationToken);
        await using (SqliteCommand capture = connection.CreateCommand())
        {
            capture.Transaction = transaction;
            capture.CommandText =
                """
                INSERT INTO historical_fpl_season_captures (
                    capture_id,
                    schema_version,
                    source_key,
                    season_code,
                    source_revision,
                    players_url,
                    gameweeks_url,
                    published_at_utc,
                    retrieved_at_utc,
                    available_at_utc,
                    players_sha256,
                    gameweeks_sha256,
                    players_csv_brotli,
                    gameweeks_csv_brotli,
                    player_count,
                    player_gameweek_count,
                    stable_code_count,
                    created_at_utc
                )
                VALUES (
                    90, '1.0', 'vaastav-fpl-historical/v1', '2025-26',
                    $revision, 'https://example.test/players',
                    'https://example.test/gameweeks',
                    '2026-06-01T00:00:00Z',
                    '2026-07-26T00:00:00Z',
                    '2026-07-26T00:00:00Z',
                    $playersHash, $gameweeksHash, X'01', X'02',
                    18, 684, 18, '2026-07-26T00:00:00Z'
                );
                """;
            capture.Parameters.AddWithValue(
                "$revision",
                PreseasonPlayerForecastStore.SourceRevision);
            capture.Parameters.AddWithValue(
                "$playersHash",
                PreseasonPlayerForecastStore.PlayersSha256);
            capture.Parameters.AddWithValue(
                "$gameweeksHash",
                PreseasonPlayerForecastStore.GameweeksSha256);
            await capture.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken);
        }
        for (int playerId = 1; playerId <= 18; playerId++)
        {
            await InsertHistoricalPlayerAsync(
                connection,
                transaction,
                playerId);
            for (int gameweek = 1; gameweek <= 38; gameweek++)
            {
                await InsertHistoricalGameweekAsync(
                    connection,
                    transaction,
                    playerId,
                    gameweek);
            }
        }
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    private static async Task InsertHistoricalPlayerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int playerId)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO historical_fpl_players VALUES (
                90, $playerId, $code, $firstName, $secondName, $webName,
                $position, 1, 'a', NULL, $newsHash, NULL
            );
            """;
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$code", 1000 + playerId);
        command.Parameters.AddWithValue("$firstName", $"Player{playerId}");
        command.Parameters.AddWithValue("$secondName", $"Prior{playerId}");
        command.Parameters.AddWithValue("$webName", $"Prior{playerId}");
        command.Parameters.AddWithValue(
            "$position",
            PositionFor(playerId));
        command.Parameters.AddWithValue("$newsHash", new string('c', 64));
        await command.ExecuteNonQueryAsync(
            TestContext.Current.CancellationToken);
    }

    private static async Task InsertHistoricalGameweekAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int playerId,
        int gameweek)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO historical_fpl_player_gameweeks VALUES (
                90, $playerId, $code, $gameweek, $fixtureId,
                '2026-01-01T15:00:00Z', 'Prior Team', 2, 1,
                90, 1, 2, 0, 0, 0, 1, 0, 0, 0, 0, 10,
                '1.0', '1.0', '1.0', '1.0',
                '0.1', '0.1', '0.2', '0.5',
                1, 1, 1, 1
            );
            """;
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$code", 1000 + playerId);
        command.Parameters.AddWithValue("$gameweek", gameweek);
        command.Parameters.AddWithValue(
            "$fixtureId",
            gameweek * 1000 + playerId);
        await command.ExecuteNonQueryAsync(
            TestContext.Current.CancellationToken);
    }

    private static byte[] CreateBootstrap()
    {
        object[] teams = Enumerable.Range(1, 10)
            .Select(
                id => (object)new
                {
                    id,
                    code = id * 10,
                    name = $"Team {id}",
                    short_name = $"T{id:00}",
                })
            .ToArray();
        object[] players = Enumerable.Range(1, 20)
            .Select(
                id => (object)new
                {
                    id,
                    code = 1000 + id,
                    team = ((id - 1) % 10) + 1,
                    element_type = PositionIdFor(id),
                    first_name = $"Player{id}",
                    second_name = $"Official{id}",
                    web_name = $"Official{id}",
                    photo = $"{1000 + id}.png",
                    now_cost = 45 + id,
                    status = "a",
                    news = string.Empty,
                    news_added = (string?)null,
                    chance_of_playing_next_round = (int?)null,
                    selected_by_percent = (30 - id).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    total_points = id,
                    minutes = 90,
                    starts = 1,
                    ep_next = "3.0",
                })
            .ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(
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
                teams,
                elements = players,
            });
    }

    private static byte[] CreateFixtures() =>
        JsonSerializer.SerializeToUtf8Bytes(
            Enumerable.Range(1, 5)
                .Select(
                    id => new
                    {
                        id,
                        @event = 1,
                        team_h = id,
                        team_a = id + 5,
                        kickoff_time = $"2026-08-0{id}T15:00:00Z",
                        started = false,
                        finished = false,
                        finished_provisional = false,
                        team_h_score = (int?)null,
                        team_a_score = (int?)null,
                    }));

    private static string PositionFor(int playerId) =>
        PositionIdFor(playerId) switch
        {
            1 => "goalkeeper",
            2 => "defender",
            3 => "midfielder",
            _ => "forward",
        };

    private static int PositionIdFor(int playerId) =>
        playerId switch
        {
            <= 2 => 1,
            <= 8 => 2,
            <= 15 => 3,
            _ => 4,
        };

    private static DatabaseOptions CreateOptions(string databasePath) =>
        DatabaseOptions.FromConfiguration(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["AutoFpl:DatabasePath"] = databasePath,
                    })
                .Build());

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        public TemporaryDatabaseFiles()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"autofpl-preseason-{Guid.NewGuid():N}");
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
