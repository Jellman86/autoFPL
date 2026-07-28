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
    public void Parser_preserves_missing_legacy_defensive_metrics_as_null()
    {
        HistoricalFplSeasonPayload payload =
            HistoricalFplSeasonPayloadParser.Parse(
                PlayersCsv(),
                LegacyGameweeksCsv());

        HistoricalFplPlayerGameweek row = Assert.Single(payload.PlayerGameweeks);
        Assert.Null(row.ClearancesBlocksInterceptions);
        Assert.Null(row.DefensiveContribution);
        Assert.Null(row.Recoveries);
        Assert.Null(row.Tackles);
    }

    [Fact]
    public void Identity_coverage_uses_only_exact_codes_and_fails_closed()
    {
        HistoricalFplSeasonCaptureDocument from =
            Capture("2024-25", playerCount: 3);
        HistoricalFplSeasonCaptureDocument to =
            Capture("2025-26", playerCount: 3) with { CaptureId = 2 };

        HistoricalFplIdentityCoverageDocument coverage =
            HistoricalFplSeasonStore.CreateIdentityCoverage(
                from,
                [10, 20, 30],
                to,
                [20, 30, 40]);

        Assert.Equal("audited", coverage.Status);
        Assert.Equal(2, coverage.SharedPlayerCodeCount);
        Assert.Equal(1, coverage.DepartedPlayerCodeCount);
        Assert.Equal(1, coverage.IntroducedPlayerCodeCount);
        Assert.Equal(0.666667m, coverage.FromSeasonRetentionFraction);
        Assert.Equal(
            0.666667m,
            coverage.ToSeasonPriorIdentityCoverageFraction);
        Assert.False(coverage.UsesNameFallback);
        Assert.Equal(64, coverage.IdentitySetSha256.Length);

        Assert.Throws<HistoricalFplSeasonCoverageException>(
            () => HistoricalFplSeasonStore.CreateIdentityCoverage(
                from,
                [10, 10, 30],
                to,
                [20, 30, 40]));
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
        HttpResponseMessage missingCoverage = await client.GetAsync(
            "/api/v1/data/historical-fpl/identity-coverage/2024-25/2025-26",
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            missingCoverage.StatusCode);
    }

    [Fact]
    public async Task Migration_preserves_existing_archive_and_admits_second_season()
    {
        using var files = new TemporaryDatabaseFiles();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = files.DatabasePath,
            ForeignKeys = true,
        };
        await using (var connection = new SqliteConnection(builder.ToString()))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await ExecuteAsync(
                connection,
                """
                CREATE TABLE schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL UNIQUE,
                    applied_at_utc TEXT NOT NULL
                );
                """);
            foreach (DatabaseMigration migration in
                DatabaseMigrations.All.Where(item => item.Version < 24))
            {
                await ExecuteAsync(connection, migration.Sql);
                await using SqliteCommand record = connection.CreateCommand();
                record.CommandText =
                    """
                    INSERT INTO schema_migrations (version, name, applied_at_utc)
                    VALUES ($version, $name, $appliedAtUtc);
                    """;
                record.Parameters.AddWithValue("$version", migration.Version);
                record.Parameters.AddWithValue("$name", migration.Name);
                record.Parameters.AddWithValue("$appliedAtUtc", RetrievalTime.ToString("O"));
                await record.ExecuteNonQueryAsync(
                    TestContext.Current.CancellationToken);
            }

            await ExecuteAsync(
                connection,
                $"""
                INSERT INTO historical_fpl_season_captures VALUES (
                    1, '1.0', '{HistoricalFplSeasonImporter.SourceKey}', '2025-26',
                    '{HistoricalFplSeasonImporter.SourceRevision}',
                    'https://example.test/players.csv',
                    'https://example.test/gameweeks.csv',
                    '2026-06-17T12:19:44+00:00',
                    '2026-07-26T12:00:00+00:00',
                    '2026-07-26T12:00:00+00:00',
                    '{new string('a', 64)}', '{new string('b', 64)}',
                    X'01', X'02', 1, 1, 1, '2026-07-26T12:00:00+00:00'
                );
                INSERT INTO historical_fpl_players VALUES (
                    1, 1, 154561, 'David', 'Raya Martín', 'Raya', 'goalkeeper',
                    1, 'a', NULL, '{new string('c', 64)}', NULL
                );
                INSERT INTO historical_fpl_player_gameweeks VALUES (
                    1, 1, 154561, 38, 370, '2026-05-24T15:00:00+00:00',
                    'Arsenal', 20, 1, 90, 1, 6, 0, 0, 1, 0, 3, 1, 0, 0, 30,
                    '20.1', '0.2', '0.0', '2.1', '0.00', '0.00', '0.00', '0.30',
                    4, 6, 5, 1
                );
                INSERT INTO official_fpl_captures VALUES (
                    10, '1.0', 'official-fpl-api/v1', '2026-27',
                    'https://fantasy.premierleague.com/api/bootstrap-static/',
                    'https://fantasy.premierleague.com/api/fixtures/',
                    '2026-07-26T10:00:00+00:00',
                    '2026-07-26T10:00:00+00:00',
                    '{new string('f', 64)}', '{new string('0', 64)}',
                    X'7B7D', X'5B5D', 1, 1, 1, 0, 1,
                    '2026-08-01T12:00:00+00:00', NULL,
                    '2026-07-26T10:00:00+00:00'
                );
                INSERT INTO preseason_player_forecast_artifacts VALUES (
                    1, '1.0',
                    'historical-preseason-player-gameweek-forecast',
                    'provisional-preseason-challenger',
                    'historical-preseason-histogram-tree-v1',
                    10, 1, '2026-27', 1, '2026-07-26T10:00:00+00:00',
                    '{new string('1', 64)}', '[]', '{new string('2', 64)}',
                    '2026-07-26T10:00:00+00:00'
                );
                """);
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:DatabasePath"] = files.DatabasePath,
                })
            .Build();
        DatabaseOptions options = DatabaseOptions.FromConfiguration(configuration);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);

        await using var migrated = new SqliteConnection(options.ConnectionString);
        await migrated.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand read = migrated.CreateCommand();
        read.CommandText =
            """
            SELECT
                (SELECT MAX(version) FROM schema_migrations),
                (SELECT COUNT(*) FROM historical_fpl_season_captures),
                (SELECT COUNT(*) FROM historical_fpl_players),
                (SELECT COUNT(*) FROM historical_fpl_player_gameweeks),
                (SELECT COUNT(*) FROM preseason_player_forecast_artifacts),
                (
                    SELECT historical_capture_id
                    FROM preseason_player_forecast_artifacts
                    WHERE forecast_artifact_id = 1
                );
            """;
        await using SqliteDataReader reader =
            await read.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(24, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.Equal(1, reader.GetInt32(4));
        Assert.Equal(1, reader.GetInt32(5));

        await ExecuteAsync(
            migrated,
            $"""
            INSERT INTO historical_fpl_season_captures VALUES (
                2, '1.0', '{HistoricalFplSeasonImporter.SourceKey}', '2024-25',
                '{HistoricalFplSeasonImporter.SourceRevision}',
                'https://example.test/2024-25/players.csv',
                'https://example.test/2024-25/gameweeks.csv',
                '2026-06-17T12:19:44+00:00',
                '2026-07-26T12:00:00+00:00',
                '2026-07-26T12:00:00+00:00',
                '{new string('d', 64)}', '{new string('e', 64)}',
                X'03', X'04', 1, 1, 1, '2026-07-26T12:00:00+00:00'
            );
            """);
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

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string commandText)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
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

    private static byte[] LegacyGameweeksCsv() =>
        Encoding.UTF8.GetBytes(
            "element,GW,fixture,kickoff_time,team,opponent_team,was_home,minutes,"
            + "starts,total_points,goals_scored,assists,clean_sheets,goals_conceded,"
            + "saves,bonus,yellow_cards,red_cards,bps,influence,creativity,threat,"
            + "ict_index,expected_goals,expected_assists,expected_goal_involvements,"
            + "expected_goals_conceded,xP\n"
            + "1,38,370,2026-05-24T15:00:00Z,Arsenal,20,True,90,1,6,0,0,1,0,"
            + "3,1,0,0,30,20.1,0.2,0.0,2.1,0.00,0.00,0.00,0.30,5.5\n");

    private static string GameweekRow(int elementId = 1, int minutes = 90) =>
        $"{elementId},38,370,2026-05-24T15:00:00Z,Arsenal,20,True,{minutes},"
        + "1,6,0,0,1,0,3,1,0,0,30,20.1,0.2,0.0,2.1,0.00,0.00,0.00,0.30,"
        + "4,6,5,1,5.5";

    private static HistoricalFplSeasonCaptureDocument Capture(
        string seasonCode,
        int playerCount) =>
        new(
            1,
            "1.0",
            HistoricalFplSeasonImporter.SourceKey,
            seasonCode,
            HistoricalFplSeasonImporter.SourceRevision,
            $"https://example.test/{seasonCode}/players.csv",
            $"https://example.test/{seasonCode}/gameweeks.csv",
            RetrievalTime.AddDays(-1),
            RetrievalTime,
            RetrievalTime,
            new('a', 64),
            new('b', 64),
            playerCount,
            playerCount,
            playerCount);

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
