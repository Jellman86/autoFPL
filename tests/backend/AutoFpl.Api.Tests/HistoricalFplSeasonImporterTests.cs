using System.Net;
using System.Net.Http.Json;
using System.Text;

using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Sources;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class HistoricalFplSeasonImporterTests
{
    private static readonly DateTimeOffset RetrievalTime =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Parser_maps_stable_code_deduplicates_exact_rows_and_store_is_immutable()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        byte[] playersCsv = PlayersCsv();
        byte[] gameweeksCsv = GameweeksCsv(GameweekRow(), GameweekRow());
        HistoricalFplSeasonPayload payload =
            HistoricalFplSeasonPayloadParser.Parse(playersCsv, gameweeksCsv);
        var store = new HistoricalFplSeasonStore(options);

        HistoricalFplSeasonCaptureDocument first = await store.SaveAsync(
            payload,
            RetrievalTime,
            TestContext.Current.CancellationToken);
        HistoricalFplSeasonCaptureDocument duplicate = await store.SaveAsync(
            payload,
            RetrievalTime.AddMinutes(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(first, duplicate);
        Assert.Equal(1, first.PlayerCount);
        Assert.Equal(1, first.PlayerGameweekCount);
        Assert.Equal(1, first.StableCodeCount);
        Assert.Equal(154561, Assert.Single(payload.PlayerGameweeks).PlayerCode);

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand read = connection.CreateCommand();
        read.CommandText =
            """
            SELECT
                player.player_code,
                gameweek.minutes,
                length(capture.players_csv_brotli),
                length(capture.gameweeks_csv_brotli)
            FROM historical_fpl_season_captures AS capture
            INNER JOIN historical_fpl_players AS player
                ON player.capture_id = capture.capture_id
            INNER JOIN historical_fpl_player_gameweeks AS gameweek
                ON gameweek.capture_id = player.capture_id
               AND gameweek.season_element_id = player.season_element_id;
            """;
        await using SqliteDataReader reader =
            await read.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(154561, reader.GetInt32(0));
        Assert.Equal(90, reader.GetInt32(1));
        Assert.True(reader.GetInt32(2) > 0);
        Assert.True(reader.GetInt32(3) > 0);

        await using SqliteCommand mutate = connection.CreateCommand();
        mutate.CommandText =
            """
            UPDATE historical_fpl_players
            SET final_status = 'i'
            WHERE capture_id = 1 AND season_element_id = 1;
            """;
        SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
            async () => await mutate.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "historical FPL players are immutable",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_rejects_unknown_player_and_conflicting_duplicate_atomically()
    {
        byte[] playersCsv = PlayersCsv();
        string unknown = GameweekRow(elementId: 999);
        Assert.Throws<HistoricalFplSeasonPayloadException>(
            () => HistoricalFplSeasonPayloadParser.Parse(
                playersCsv,
                GameweeksCsv(unknown)));

        string conflicting = GameweekRow(minutes: 10);
        Assert.Throws<HistoricalFplSeasonPayloadException>(
            () => HistoricalFplSeasonPayloadParser.Parse(
                playersCsv,
                GameweeksCsv(GameweekRow(), conflicting)));
    }

    [Fact]
    public async Task Importer_rejects_unpinned_files_and_api_exposes_metadata_not_raw_csv()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        byte[] playersCsv = PlayersCsv();
        byte[] gameweeksCsv = GameweeksCsv(GameweekRow());
        var store = new HistoricalFplSeasonStore(options);
        using var httpClient = new HttpClient();
        var importer = new HistoricalFplSeasonImporter(
            httpClient,
            store,
            new FixedTimeProvider(RetrievalTime));
        await Assert.ThrowsAsync<HistoricalFplSeasonPayloadException>(
            async () => await importer.ImportCapturedPayloadAsync(
                playersCsv,
                gameweeksCsv,
                RetrievalTime,
                TestContext.Current.CancellationToken));

        HistoricalFplSeasonPayload payload =
            HistoricalFplSeasonPayloadParser.Parse(playersCsv, gameweeksCsv);
        await store.SaveAsync(
            payload,
            RetrievalTime,
            TestContext.Current.CancellationToken);

        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(
                    builder =>
                    {
                        builder.UseSetting("AutoFpl:DatabasePath", files.DatabasePath);
                        builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");
                    });
        using HttpClient client = factory.CreateClient();
        HistoricalFplSeasonCaptureDocument? capture =
            await client.GetFromJsonAsync<HistoricalFplSeasonCaptureDocument>(
                "/api/v1/data/historical-fpl/2025-26",
                TestContext.Current.CancellationToken);

        Assert.NotNull(capture);
        Assert.Equal(HistoricalFplSeasonImporter.SourceRevision, capture.SourceRevision);
        string response = await client.GetStringAsync(
            "/api/v1/data/historical-fpl/2025-26",
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain("playersCsv", response, StringComparison.Ordinal);
        Assert.DoesNotContain("gameweeksCsv", response, StringComparison.Ordinal);
        Assert.DoesNotContain("\"xP\"", response, StringComparison.Ordinal);
        HttpResponseMessage missing = await client.GetAsync(
            "/api/v1/data/historical-fpl/2024-25",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static async Task<DatabaseOptions> CreateDatabaseAsync(string databasePath)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:DatabasePath"] = databasePath,
                })
            .Build();
        DatabaseOptions options = DatabaseOptions.FromConfiguration(configuration);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        return options;
    }

    private static byte[] PlayersCsv() =>
        Encoding.UTF8.GetBytes(
            """
            id,code,first_name,second_name,web_name,element_type,team,status,chance_of_playing_next_round,news,news_added
            1,154561,David,Raya Martín,Raya,1,1,a,None,"Fit, available",2026-05-24T17:00:00Z

            """);

    private static byte[] GameweeksCsv(params string[] rows) =>
        Encoding.UTF8.GetBytes(
            "element,GW,fixture,kickoff_time,team,opponent_team,was_home,minutes,"
            + "starts,total_points,goals_scored,assists,clean_sheets,goals_conceded,"
            + "saves,bonus,yellow_cards,red_cards,bps,influence,creativity,threat,"
            + "ict_index,expected_goals,expected_assists,expected_goal_involvements,"
            + "expected_goals_conceded,clearances_blocks_interceptions,"
            + "defensive_contribution,recoveries,tackles,xP\n"
            + string.Join('\n', rows)
            + "\n");

    private static string GameweekRow(int elementId = 1, int minutes = 90) =>
        $"{elementId},38,370,2026-05-24T15:00:00Z,Arsenal,20,True,{minutes},"
        + "1,6,0,0,1,0,3,1,0,0,30,20.1,0.2,0.0,2.1,0.00,0.00,0.00,0.30,"
        + "4,6,5,1,5.5";

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), $"autofpl-tests-{Guid.NewGuid():N}");

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
