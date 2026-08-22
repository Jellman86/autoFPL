using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Api.Selections;
using AutoFpl.Contracts.Advice;
using AutoFpl.Contracts.Forecasts;
using AutoFpl.Contracts.Selections;
using AutoFpl.Domain.Lineups;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class SelectionRevisionStoreTests
{
    private static readonly DateTimeOffset BeforeDeadline =
        DateTimeOffset.Parse("2026-08-20T12:00:00Z");
    private static readonly DateTimeOffset Deadline =
        DateTimeOffset.Parse("2026-08-21T17:30:00Z");

    [Fact]
    public async Task Forecast_draft_is_immutable_idempotent_and_lockable_once()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedForecastAsync(options, artifactId: 10);
        var store = new SelectionRevisionStore(
            options,
            new FixedTimeProvider(BeforeDeadline));

        SelectionRevisionDocument first =
            (await store.CreateDraftFromForecastAsync(
                10,
                TestContext.Current.CancellationToken))!;
        SelectionRevisionDocument repeated =
            (await store.CreateDraftFromForecastAsync(
                10,
                TestContext.Current.CancellationToken))!;

        Assert.Equal(
            JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(repeated));
        Assert.Equal(1, first.Revision);
        Assert.Null(first.SupersedesSelectionRevisionId);
        Assert.Equal("draft", first.Status);
        Assert.True(first.CanLock);
        Assert.Null(first.LockedAtUtc);
        Assert.Equal(11, first.Selection.StartingPlayerIds.Count);
        Assert.Equal(3, first.Selection.OutfieldSubstitutePlayerIds.Count);
        Assert.Equal(64, first.SelectionContentHash.Length);

        SelectionRevisionDocument locked =
            (await store.LockAsync(
                first.SelectionRevisionId,
                TestContext.Current.CancellationToken))!;
        SelectionRevisionDocument repeatedLock =
            (await store.LockAsync(
                first.SelectionRevisionId,
                TestContext.Current.CancellationToken))!;

        Assert.Equal(
            JsonSerializer.Serialize(locked),
            JsonSerializer.Serialize(repeatedLock));
        Assert.Equal("locked", locked.Status);
        Assert.False(locked.CanLock);
        Assert.Equal(BeforeDeadline, locked.LockedAtUtc);

        var afterDeadlineStore = new SelectionRevisionStore(
            options,
            new FixedTimeProvider(Deadline.AddSeconds(1)));
        SelectionRevisionDocument frozen =
            (await afterDeadlineStore.GetAsync(
                first.SelectionRevisionId,
                TestContext.Current.CancellationToken))!;
        Assert.Equal("frozen", frozen.Status);
        Assert.False(frozen.CanLock);
    }

    [Fact]
    public async Task New_forecast_creates_a_revision_and_stale_draft_cannot_lock()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedForecastAsync(options, artifactId: 10);
        await SeedForecastAsync(
            options,
            artifactId: 11,
            captureId: 2,
            captainPlayerId: 9);
        var store = new SelectionRevisionStore(
            options,
            new FixedTimeProvider(BeforeDeadline));

        SelectionRevisionDocument first =
            (await store.CreateDraftFromForecastAsync(
                10,
                TestContext.Current.CancellationToken))!;
        SelectionRevisionDocument second =
            (await store.CreateDraftFromForecastAsync(
                11,
                TestContext.Current.CancellationToken))!;

        Assert.Equal(2, second.Revision);
        Assert.Equal(first.SelectionRevisionId, second.SupersedesSelectionRevisionId);
        Assert.NotEqual(first.SelectionContentHash, second.SelectionContentHash);
        SelectionWorkflowException exception =
            await Assert.ThrowsAsync<SelectionWorkflowException>(
                () => store.LockAsync(
                    first.SelectionRevisionId,
                    TestContext.Current.CancellationToken));
        Assert.Equal("selection.revision.stale", exception.Code);

        SelectionRevisionDocument current =
            (await store.GetCurrentAsync(
                TestContext.Current.CancellationToken))!;
        Assert.Equal(second.SelectionRevisionId, current.SelectionRevisionId);
    }

    [Fact]
    public async Task Editing_latest_selection_creates_an_unlocked_superseding_revision()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedForecastAsync(options, artifactId: 10);
        var store = new SelectionRevisionStore(
            options,
            new FixedTimeProvider(BeforeDeadline));
        SelectionRevisionDocument first =
            (await store.CreateDraftFromForecastAsync(
                10,
                TestContext.Current.CancellationToken))!;
        await store.LockAsync(
            first.SelectionRevisionId,
            TestContext.Current.CancellationToken);

        var editedSelection = new LockedSelectionDocument(
            [1, 3, 4, 6, 8, 9, 10, 11, 12, 13, 14],
            8,
            13,
            2,
            [5, 7, 15]);
        SelectionRevisionDocument edited =
            (await store.CreateEditedRevisionAsync(
                first.SelectionRevisionId,
                editedSelection,
                TestContext.Current.CancellationToken))!;
        SelectionRevisionDocument repeated =
            (await store.CreateEditedRevisionAsync(
                edited.SelectionRevisionId,
                editedSelection,
                TestContext.Current.CancellationToken))!;

        Assert.Equal(2, edited.Revision);
        Assert.Equal(first.SelectionRevisionId, edited.SupersedesSelectionRevisionId);
        Assert.Equal(first.ForecastArtifactId, edited.ForecastArtifactId);
        Assert.Equal("draft", edited.Status);
        Assert.True(edited.CanLock);
        Assert.Null(edited.LockedAtUtc);
        Assert.Equal([5, 7, 15], edited.Selection.OutfieldSubstitutePlayerIds);
        Assert.Equal(
            JsonSerializer.Serialize(edited),
            JsonSerializer.Serialize(repeated));

        SelectionRevisionDocument preserved =
            (await store.GetAsync(
                first.SelectionRevisionId,
                TestContext.Current.CancellationToken))!;
        Assert.Equal("locked", preserved.Status);
        SelectionWorkflowException stale =
            await Assert.ThrowsAsync<SelectionWorkflowException>(
                () => store.CreateEditedRevisionAsync(
                    first.SelectionRevisionId,
                    editedSelection,
                    TestContext.Current.CancellationToken));
        Assert.Equal("selection.revision.stale", stale.Code);
    }

    [Fact]
    public async Task Editing_can_replace_a_model_player_and_compare_on_the_same_forecast()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedForecastAsync(options, artifactId: 10);
        var store = new SelectionRevisionStore(
            options,
            new FixedTimeProvider(BeforeDeadline));
        SelectionRevisionDocument draft =
            (await store.CreateDraftFromForecastAsync(
                10,
                TestContext.Current.CancellationToken))!;
        var changedSquad = new LockedSelectionDocument(
            [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14],
            8,
            13,
            2,
            [6, 16, 15]);

        SelectionRevisionDocument edited =
            (await store.CreateEditedRevisionAsync(
                draft.SelectionRevisionId,
                changedSquad,
                TestContext.Current.CancellationToken))!;
        SelectionComparisonDocument comparison =
            (await store.GetComparisonAsync(
                edited.SelectionRevisionId,
                TestContext.Current.CancellationToken))!;

        Assert.Contains(16, edited.Selection.OutfieldSubstitutePlayerIds);
        Assert.Equal([16], comparison.PlayersAdded);
        Assert.Equal([7], comparison.PlayersRemoved);
        Assert.Equal(10, comparison.ForecastArtifactId);
        Assert.Equal(110, comparison.PlayerForecastArtifactId);
        Assert.Equal(
            comparison.User.ProjectedPoints - comparison.Model.ProjectedPoints,
            comparison.ProjectedPointsDelta);
        Assert.Equal(15, comparison.User.SquadPlayerIds.Count);
        Assert.Equal(250, comparison.User.RemainingBudgetTenths);
    }

    [Fact]
    public async Task Editing_rejects_an_infeasible_formation()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedForecastAsync(options, artifactId: 10);
        var store = new SelectionRevisionStore(
            options,
            new FixedTimeProvider(BeforeDeadline));
        SelectionRevisionDocument draft =
            (await store.CreateDraftFromForecastAsync(
                10,
                TestContext.Current.CancellationToken))!;

        var invalid = new LockedSelectionDocument(
            [1, 4, 5, 8, 9, 10, 11, 12, 13, 14, 15],
            8,
            13,
            2,
            [3, 6, 7]);
        LineupValidationException exception =
            await Assert.ThrowsAsync<LineupValidationException>(
                () => store.CreateEditedRevisionAsync(
                    draft.SelectionRevisionId,
                    invalid,
                    TestContext.Current.CancellationToken));
        Assert.Equal("lineup.formation.invalid", exception.Code);
    }

    [Fact]
    public async Task Editing_fails_closed_when_player_pool_cutoff_does_not_match()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedForecastAsync(
            options,
            artifactId: 10,
            playerForecastCutoffUtc: BeforeDeadline);
        var store = new SelectionRevisionStore(
            options,
            new FixedTimeProvider(BeforeDeadline));
        SelectionRevisionDocument draft =
            (await store.CreateDraftFromForecastAsync(
                10,
                TestContext.Current.CancellationToken))!;
        var selection = new LockedSelectionDocument(
            [1, 3, 4, 6, 8, 9, 10, 11, 12, 13, 14],
            8,
            13,
            2,
            [5, 7, 15]);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.CreateEditedRevisionAsync(
                    draft.SelectionRevisionId,
                    selection,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            "The player forecast artifact does not match the selection forecast.",
            exception.Message);
    }

    [Fact]
    public async Task Deadline_rejects_new_drafts_and_expires_unlocked_revisions()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedForecastAsync(options, artifactId: 10);
        var before = new SelectionRevisionStore(
            options,
            new FixedTimeProvider(BeforeDeadline));
        SelectionRevisionDocument draft =
            (await before.CreateDraftFromForecastAsync(
                10,
                TestContext.Current.CancellationToken))!;
        var after = new SelectionRevisionStore(
            options,
            new FixedTimeProvider(Deadline));

        SelectionRevisionDocument expired =
            (await after.GetAsync(
                draft.SelectionRevisionId,
                TestContext.Current.CancellationToken))!;
        Assert.Equal("expired", expired.Status);
        Assert.False(expired.CanLock);
        SelectionWorkflowException lockException =
            await Assert.ThrowsAsync<SelectionWorkflowException>(
                () => after.LockAsync(
                    draft.SelectionRevisionId,
                    TestContext.Current.CancellationToken));
        Assert.Equal("selection.deadline.passed", lockException.Code);
        SelectionWorkflowException createException =
            await Assert.ThrowsAsync<SelectionWorkflowException>(
                () => after.CreateDraftFromForecastAsync(
                    10,
                    TestContext.Current.CancellationToken));
        Assert.Equal("selection.deadline.passed", createException.Code);
    }

    [Fact]
    public async Task Http_workflow_creates_reads_and_locks_the_current_revision()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedForecastAsync(options, artifactId: 10);
        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting(
                        "AutoFpl:DatabasePath",
                        files.DatabasePath);
                    builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");

                    // The seeded forecast carries a fixed 2026-08-21T17:30Z deadline.
                    // Without a fixed clock this workflow passes only until that real
                    // instant, then fails as selection.deadline.passed.
                    builder.ConfigureServices(services =>
                        services.AddSingleton<TimeProvider>(
                            new FixedTimeProvider(BeforeDeadline)));
                });
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage missing = await client.GetAsync(
            "/api/v1/selections/current",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/v1/selections/drafts",
            new { forecastArtifactId = 10 },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        SelectionRevisionDocument draft =
            (await created.Content.ReadFromJsonAsync<SelectionRevisionDocument>(
                TestContext.Current.CancellationToken))!;
        Assert.Equal("draft", draft.Status);
        Assert.Equal(
            $"/api/v1/selections/{draft.SelectionRevisionId}",
            created.Headers.Location?.OriginalString);

        SelectionRevisionDocument current =
            (await client.GetFromJsonAsync<SelectionRevisionDocument>(
                "/api/v1/selections/current",
                TestContext.Current.CancellationToken))!;
        Assert.Equal(draft.SelectionRevisionId, current.SelectionRevisionId);

        using HttpResponseMessage lockedResponse = await client.PutAsync(
            $"/api/v1/selections/{draft.SelectionRevisionId}/lock",
            content: null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, lockedResponse.StatusCode);
        SelectionRevisionDocument locked =
            (await lockedResponse.Content.ReadFromJsonAsync<SelectionRevisionDocument>(
                TestContext.Current.CancellationToken))!;
        Assert.Equal("locked", locked.Status);
        Assert.NotNull(locked.LockedAtUtc);

        using HttpResponseMessage editedResponse = await client.PostAsJsonAsync(
            $"/api/v1/selections/{draft.SelectionRevisionId}/revisions",
            new
            {
                startingPlayerIds = new[] { 1, 3, 4, 6, 8, 9, 10, 11, 12, 13, 14 },
                captainPlayerId = 8,
                viceCaptainPlayerId = 13,
                replacementGoalkeeperPlayerId = 2,
                outfieldSubstitutePlayerIds = new[] { 5, 16, 15 },
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, editedResponse.StatusCode);
        SelectionRevisionDocument edited =
            (await editedResponse.Content.ReadFromJsonAsync<SelectionRevisionDocument>(
                TestContext.Current.CancellationToken))!;
        Assert.Equal(2, edited.Revision);
        Assert.Equal("draft", edited.Status);
        Assert.Equal(
            draft.SelectionRevisionId,
            edited.SupersedesSelectionRevisionId);
        Assert.Contains(16, edited.Selection.OutfieldSubstitutePlayerIds);

        PlayerGameweekForecastDocument pool =
            (await client.GetFromJsonAsync<PlayerGameweekForecastDocument>(
                "/api/v1/forecasts/10/player-pool",
                TestContext.Current.CancellationToken))!;
        Assert.Equal(110, pool.ForecastArtifactId);
        Assert.Equal(20, pool.Players.Count);

        SelectionComparisonDocument comparison =
            (await client.GetFromJsonAsync<SelectionComparisonDocument>(
                $"/api/v1/selections/{edited.SelectionRevisionId}/comparison",
                TestContext.Current.CancellationToken))!;
        Assert.Equal([16], comparison.PlayersAdded);
        Assert.Equal([7], comparison.PlayersRemoved);
    }

    [Fact]
    public async Task Database_trigger_rejects_selection_content_rewrites()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedForecastAsync(options, artifactId: 10);
        var store = new SelectionRevisionStore(
            options,
            new FixedTimeProvider(BeforeDeadline));
        SelectionRevisionDocument draft =
            (await store.CreateDraftFromForecastAsync(
                10,
                TestContext.Current.CancellationToken))!;

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE selection_revisions
            SET selection_json = '{}'
            WHERE selection_revision_id = $selectionRevisionId;
            """;
        command.Parameters.AddWithValue(
            "$selectionRevisionId",
            draft.SelectionRevisionId);
        SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
            () => command.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "selection revisions are immutable",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static async Task<DatabaseOptions> CreateDatabaseAsync(
        string databasePath)
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

    private static async Task SeedForecastAsync(
        DatabaseOptions options,
        long artifactId,
        long captureId = 1,
        int captainPlayerId = 8,
        DateTimeOffset? playerForecastCutoffUtc = null)
    {
        DateTimeOffset captureAvailableAt = BeforeDeadline.AddHours(captureId);
        GameweekAdviceDocument advice = CreateAdvice(
            captainPlayerId,
            captureAvailableAt);
        PlayerGameweekForecastDocument playerForecast =
            CreatePlayerForecast(
                captureId,
                playerForecastCutoffUtc ?? captureAvailableAt);
        string documentJson = JsonSerializer.Serialize(
            advice,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string playerForecastJson = JsonSerializer.Serialize(
            playerForecast,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string artifactHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(documentJson)));
        string playerForecastHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(playerForecastJson)));
        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO official_fpl_captures (
                capture_id,
                schema_version,
                source_key,
                season_code,
                bootstrap_url,
                fixtures_url,
                retrieved_at_utc,
                available_at_utc,
                bootstrap_sha256,
                fixtures_sha256,
                bootstrap_json,
                fixtures_json,
                event_count,
                team_count,
                player_count,
                fixture_count,
                next_gameweek_number,
                next_deadline_utc,
                latest_completed_gameweek,
                created_at_utc
            )
            VALUES (
                $captureId,
                '1.0',
                'official-fpl-api/v1',
                '2026-27',
                'https://fantasy.premierleague.com/api/bootstrap-static/',
                'https://fantasy.premierleague.com/api/fixtures/',
                $availableAtUtc,
                $availableAtUtc,
                $bootstrapHash,
                $fixturesHash,
                X'7B7D',
                X'5B5D',
                1,
                1,
                20,
                0,
                1,
                $deadlineUtc,
                NULL,
                $availableAtUtc
            );

            INSERT INTO baseline_forecast_artifacts (
                artifact_id,
                schema_version,
                model_key,
                capture_id,
                document_json,
                content_sha256,
                created_at_utc
            )
            VALUES (
                $artifactId,
                '1.0',
                'official-market-baseline-v0',
                $captureId,
                $documentJson,
                $artifactHash,
                $availableAtUtc
            );

            INSERT INTO player_gameweek_forecast_artifacts (
                forecast_artifact_id,
                schema_version,
                model_key,
                official_capture_id,
                season_code,
                gameweek,
                decision_cutoff_utc,
                document_json,
                content_sha256,
                created_at_utc
            )
            VALUES (
                $playerForecastArtifactId,
                '1.0',
                'official-market-baseline-v0-player-table',
                $captureId,
                '2026-27',
                1,
                $availableAtUtc,
                $playerForecastJson,
                $playerForecastHash,
                $availableAtUtc
            );
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$artifactId", artifactId);
        command.Parameters.AddWithValue(
            "$availableAtUtc",
            FormatDatabaseUtc(BeforeDeadline.AddHours(captureId)));
        command.Parameters.AddWithValue(
            "$deadlineUtc",
            FormatDatabaseUtc(Deadline));
        command.Parameters.AddWithValue(
            "$bootstrapHash",
            new string(captureId == 1 ? 'a' : 'c', 64));
        command.Parameters.AddWithValue(
            "$fixturesHash",
            new string(captureId == 1 ? 'b' : 'd', 64));
        command.Parameters.AddWithValue("$documentJson", documentJson);
        command.Parameters.AddWithValue("$artifactHash", artifactHash);
        command.Parameters.AddWithValue(
            "$playerForecastArtifactId",
            artifactId + 100);
        command.Parameters.AddWithValue(
            "$playerForecastJson",
            playerForecastJson);
        command.Parameters.AddWithValue(
            "$playerForecastHash",
            playerForecastHash);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static PlayerGameweekForecastDocument CreatePlayerForecast(
        long captureId,
        DateTimeOffset decisionCutoffUtc)
    {
        string[] positions =
        [
            "goalkeeper", "goalkeeper",
            "defender", "defender", "defender", "defender", "defender",
            "midfielder", "midfielder", "midfielder", "midfielder", "midfielder",
            "forward", "forward", "forward",
            "defender", "midfielder", "forward", "goalkeeper", "defender",
        ];
        PlayerGameweekForecastPlayerDocument[] players =
        [
            .. positions.Select(
                (position, index) =>
                {
                    int playerId = index + 1;
                    return new PlayerGameweekForecastPlayerDocument(
                        playerId,
                        $"Player {playerId}",
                        playerId <= 15 ? $"T{playerId % 5}" : $"X{playerId}",
                        position,
                        50,
                        "a",
                        null,
                        1,
                        "OPP",
                        true,
                        playerId,
                        Math.Max(0, playerId - 2),
                        playerId + 2,
                        80,
                        null,
                        null,
                        ["Forecast reason."],
                        ["Forecast risk."],
                        null,
                        $"/players/{playerId}");
                }),
        ];
        return new(
            "1.0",
            "provisional-unvalidated",
            "official-market-baseline-v0-player-table",
            "2026-27",
            1,
            Deadline,
            decisionCutoffUtc,
            captureId,
            "interval-only-uncalibrated",
            players,
            ["Test limitation."]);
    }

    private static GameweekAdviceDocument CreateAdvice(
        int captainPlayerId,
        DateTimeOffset decisionCutoffUtc)
    {
        string[] positions =
        [
            "goalkeeper", "goalkeeper",
            "defender", "defender", "defender", "defender", "defender",
            "midfielder", "midfielder", "midfielder", "midfielder", "midfielder",
            "forward", "forward", "forward",
        ];
        int[] starters = [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14];
        Dictionary<int, int> benchOrder = new()
        {
            [2] = 1,
            [6] = 2,
            [7] = 3,
            [15] = 4,
        };
        AdvicePlayerDocument[] players =
        [
            .. Enumerable.Range(1, 15).Select(playerId =>
                new AdvicePlayerDocument(
                    playerId,
                    $"Player {playerId}",
                    $"T{playerId % 5}",
                    positions[playerId - 1],
                    starters.Contains(playerId) ? "starting" : "bench",
                    benchOrder.GetValueOrDefault(playerId) is 0
                        ? null
                        : benchOrder[playerId],
                    playerId == captainPlayerId
                        ? "captain"
                        : playerId == 13
                            ? "vice-captain"
                            : null,
                    "OPP",
                    true,
                    5,
                    1,
                    10,
                    80,
                    ["Forecast reason."],
                    ["Forecast risk."])),
        ];
        return new(
            "1.0",
            "official-market-baseline-v0",
            false,
            null,
            null,
            decisionCutoffUtc,
            null,
            1,
            Deadline,
            decisionCutoffUtc,
            "Baseline v0",
            "A persisted forecast.",
            new("Recommended", "Maximum expected points", 55, players),
            [],
            new(false, "Unavailable.", []));
    }

    private static string FormatDatabaseUtc(DateTimeOffset value) =>
        value
            .ToUniversalTime()
            .ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                System.Globalization.CultureInfo.InvariantCulture);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-selection-tests-{Guid.NewGuid():N}");

        public TemporaryDatabaseFiles()
        {
            Directory.CreateDirectory(_directory);
        }

        public string DatabasePath => Path.Combine(_directory, "autofpl.db");

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
