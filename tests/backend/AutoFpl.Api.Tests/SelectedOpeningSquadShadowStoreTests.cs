using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class SelectedOpeningSquadShadowStoreTests
{
    private static readonly DateTimeOffset Deadline =
        DateTimeOffset.Parse("2026-08-21T17:30:00Z");
    private static readonly DateTimeOffset Cutoff =
        DateTimeOffset.Parse("2026-07-29T16:38:42Z");

    [Fact]
    public async Task Import_is_immutable_current_and_policy_bound()
    {
        using var files = new TemporaryFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new SelectedOpeningSquadShadowStore(
            options,
            new FixedTimeProvider(
                DateTimeOffset.Parse("2026-07-29T18:00:00Z")));
        SelectedOpeningSquadShadowDocument request = CreateRequest();

        SelectedOpeningSquadShadowDocument first =
            await store.ImportAsync(
                request,
                TestContext.Current.CancellationToken);
        SelectedOpeningSquadShadowDocument second =
            await store.ImportAsync(
                request,
                TestContext.Current.CancellationToken);
        SelectedOpeningSquadShadowDocument? current =
            await store.GetCurrentAsync(
                TestContext.Current.CancellationToken);

        Assert.NotNull(first.SelectedOpeningSquadArtifactId);
        Assert.Equal(
            first.SelectedOpeningSquadArtifactId,
            second.SelectedOpeningSquadArtifactId);
        Assert.Equal(
            first.SelectedOpeningSquadArtifactId,
            current!.SelectedOpeningSquadArtifactId);
        Assert.Equal(8, current.Selection.Gameweeks.Count);
        Assert.False(current.IsPromoted);
        Assert.False(current.InfluencesAdvice);
        Assert.Equal(
            SelectedOpeningSquadShadowStore.EvaluationDataIdentity,
            current.SelectedPolicy.RetrospectiveEvaluationSource
                .DataIdentitySha256);

        await using var connection =
            new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText =
            "SELECT COUNT(*) FROM selected_opening_squad_shadow_artifacts;";
        Assert.Equal(
            1L,
            await count.ExecuteScalarAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Import_rejects_policy_drift_and_illegal_roles()
    {
        using var files = new TemporaryFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new SelectedOpeningSquadShadowStore(
            options,
            TimeProvider.System);
        SelectedOpeningSquadShadowDocument request = CreateRequest();

        SelectedOpeningSquadValidationException policy =
            await Assert.ThrowsAsync<
                SelectedOpeningSquadValidationException>(
                () => store.ImportAsync(
                    request with
                    {
                        SelectedPolicy = request.SelectedPolicy with
                        {
                            EvaluationPolicyKey = "3-downside-balanced",
                        },
                    },
                    TestContext.Current.CancellationToken));
        Assert.Equal("policy", policy.Code);

        SelectedOpeningGameweekDocument first =
            request.Selection.Gameweeks[0];
        SelectedOpeningSquadValidationException roles =
            await Assert.ThrowsAsync<
                SelectedOpeningSquadValidationException>(
                () => store.ImportAsync(
                    request with
                    {
                        Selection = request.Selection with
                        {
                            Gameweeks =
                            [
                                first with
                                {
                                    CaptainPlayerId = 2,
                                },
                                .. request.Selection.Gameweeks.Skip(1),
                            ],
                        },
                    },
                    TestContext.Current.CancellationToken));
        Assert.Equal("gameweek-roles", roles.Code);
    }

    [Fact]
    public async Task Best_supported_v2_coexists_with_and_precedes_v1()
    {
        using var files = new TemporaryFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new SelectedOpeningSquadShadowStore(
            options,
            new FixedTimeProvider(
                DateTimeOffset.Parse("2026-07-29T18:00:00Z")));
        SelectedOpeningSquadShadowDocument legacy = CreateRequest();
        SelectedOpeningSquadShadowDocument bestSupported =
            CreateBestSupportedRequest();

        SelectedOpeningSquadShadowDocument storedLegacy =
            await store.ImportAsync(
                legacy,
                TestContext.Current.CancellationToken);
        SelectedOpeningSquadShadowDocument storedBest =
            await store.ImportAsync(
                bestSupported,
                TestContext.Current.CancellationToken);
        SelectedOpeningSquadShadowDocument? current =
            await store.GetCurrentAsync(
                TestContext.Current.CancellationToken);

        Assert.NotEqual(
            storedLegacy.SelectedOpeningSquadArtifactId,
            storedBest.SelectedOpeningSquadArtifactId);
        Assert.Equal(
            SelectedOpeningSquadShadowStore.BestSupportedArtifactVersion,
            current!.ArtifactVersion);
        Assert.Equal(
            SelectedOpeningSquadShadowStore.ModelEvaluationDataIdentity,
            current.SelectedPolicy.ModelEvaluationSource!
                .DataIdentitySha256);
        Assert.All(
            current.Selection.Players,
            player =>
            {
                Assert.Equal(4.25m, player.ModelExpectedPoints);
                Assert.Equal(
                    0.90m,
                    player.ModelAppearanceProbability);
                Assert.Equal(
                    25.50m,
                    player.ModelSixGameweekExpectedPoints);
            });

        await using var connection =
            new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText =
            "SELECT COUNT(*) FROM selected_opening_squad_shadow_artifacts;";
        Assert.Equal(
            2L,
            await count.ExecuteScalarAsync(
                TestContext.Current.CancellationToken));
    }

    private static SelectedOpeningSquadShadowDocument CreateRequest()
    {
        string[] positions =
        [
            "goalkeeper", "goalkeeper",
            "defender", "defender", "defender", "defender", "defender",
            "midfielder", "midfielder", "midfielder", "midfielder",
            "midfielder",
            "forward", "forward", "forward",
        ];
        SelectedOpeningPlayerDocument[] players =
        [
            .. positions.Select(
                (position, index) =>
                    new SelectedOpeningPlayerDocument(
                        index + 1,
                        $"Player {index + 1}",
                        index / 3 + 1,
                        $"Team {index / 3 + 1}",
                        position,
                        50,
                        "a",
                        null)),
        ];
        int[] starting = [1, 3, 4, 5, 8, 9, 10, 11, 13, 14, 15];
        SelectedOpeningGameweekDocument[] gameweeks =
        [
            .. Enumerable.Range(1, 8).Select(
                gameweek =>
                    new SelectedOpeningGameweekDocument(
                        gameweek,
                        starting,
                        13,
                        9,
                        2,
                        [6, 12, 7])),
        ];
        return new(
            "1.0",
            SelectedOpeningSquadShadowStore.ArtifactType,
            SelectedOpeningSquadShadowStore.ArtifactVersion,
            SelectedOpeningSquadShadowStore.Status,
            false,
            false,
            "2026-27",
            1,
            Deadline,
            Cutoff,
            16,
            15,
            38,
            new(
                SelectedOpeningSquadShadowStore.EvaluationPolicyKey,
                6,
                "expected-points",
                new(
                    "scipy-highs-multi-horizon-mean-cvar-v1",
                    "scipy.optimize.milp-highs",
                    "global-linear-mean-cvar-surrogate-optimum",
                    0m,
                    0m,
                    0.000000000001m),
                new(
                    SelectedOpeningSquadShadowStore
                        .EvaluationArtifactVersion,
                    SelectedOpeningSquadShadowStore
                        .EvaluationDataIdentity,
                    SelectedOpeningSquadShadowStore
                        .EvaluationRunIdentity)),
            new(
                Enumerable.Range(1, 15).ToArray(),
                750,
                players,
                gameweeks),
            new(
                Enumerable.Range(1, 8).ToArray(),
                "fixed-opening-squad-no-transfers-for-all-eight-gameweeks",
                "all-eight-weeks-frozen-from-preseason-scenario-means",
                "exact-fpl-captain-fallback-and-ordered-auto-substitution",
                "waiting-for-official-2026-27-outcomes"),
            new(
                [
                    .. Enumerable.Range(1, 8).Select(
                        gameweek =>
                            new SelectedOpeningWeeklyScoreDocument(
                                "1.0",
                                "cpu-joint-scenario-reference-v1",
                                gameweek,
                                38,
                                50m,
                                0m,
                                50,
                                50m,
                                50m,
                                50m,
                                50,
                                0m,
                                0m)),
                ],
                new(
                    38,
                    400m,
                    0m,
                    400,
                    400m,
                    400m,
                    400m,
                    400),
                0.20m,
                400m,
                Enumerable.Repeat(400, 38).ToArray()),
            new(
                "current-multi-horizon-initial-squad-shadow-v2",
                new string('a', 64),
                new string('b', 64)),
            ["Prospective shadow only."],
            new string('c', 64),
            new string('d', 64));
    }

    private static SelectedOpeningSquadShadowDocument
        CreateBestSupportedRequest()
    {
        SelectedOpeningSquadShadowDocument request = CreateRequest();
        return request with
        {
            ArtifactVersion =
                SelectedOpeningSquadShadowStore
                    .BestSupportedArtifactVersion,
            Status =
                SelectedOpeningSquadShadowStore.BestSupportedStatus,
            InfluencesAdvice = true,
            SelectedPolicy = request.SelectedPolicy with
            {
                ModelEvaluationSource = new(
                    SelectedOpeningSquadShadowStore
                        .ModelEvaluationArtifactVersion,
                    SelectedOpeningSquadShadowStore
                        .ModelEvaluationDataIdentity,
                    SelectedOpeningSquadShadowStore
                        .ModelEvaluationRunIdentity),
            },
            Selection = request.Selection with
            {
                Players =
                [
                    .. request.Selection.Players.Select(
                        player => player with
                        {
                            ModelExpectedPoints = 4.25m,
                            ModelAppearanceProbability = 0.90m,
                            ModelSixGameweekExpectedPoints = 25.50m,
                        }),
                ],
            },
            DataIdentitySha256 = new string('e', 64),
            RunIdentitySha256 = new string('f', 64),
        };
    }

    private static async Task<DatabaseOptions> CreateDatabaseAsync(string path)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:DatabasePath"] = path,
                })
            .Build();
        DatabaseOptions options =
            DatabaseOptions.FromConfiguration(configuration);
        await using var connection =
            new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE official_fpl_captures (
                capture_id INTEGER PRIMARY KEY,
                season_code TEXT NOT NULL,
                next_gameweek_number INTEGER NOT NULL,
                next_deadline_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL
            );
            CREATE TABLE official_fpl_players (
                capture_id INTEGER NOT NULL,
                player_id INTEGER NOT NULL,
                web_name TEXT NOT NULL,
                team_id INTEGER NOT NULL,
                position TEXT NOT NULL,
                price_tenths INTEGER NOT NULL,
                status TEXT NOT NULL,
                chance_next_round INTEGER
            );
            CREATE TABLE selected_opening_squad_shadow_artifacts (
                selected_opening_squad_artifact_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL,
                artifact_type TEXT NOT NULL,
                artifact_version TEXT NOT NULL,
                status TEXT NOT NULL,
                official_capture_id INTEGER NOT NULL,
                season_code TEXT NOT NULL,
                opening_gameweek INTEGER NOT NULL,
                decision_cutoff_utc TEXT NOT NULL,
                evaluation_policy_key TEXT NOT NULL,
                evaluation_data_identity_sha256 TEXT NOT NULL,
                scenario_count INTEGER NOT NULL,
                candidate_pool_count INTEGER NOT NULL,
                budget_tenths INTEGER NOT NULL,
                producer_data_identity_sha256 TEXT NOT NULL,
                producer_run_identity_sha256 TEXT NOT NULL,
                document_json TEXT NOT NULL,
                content_sha256 TEXT NOT NULL UNIQUE,
                created_at_utc TEXT NOT NULL,
                UNIQUE (
                    official_capture_id,
                    evaluation_policy_key,
                    evaluation_data_identity_sha256
                )
            );
            INSERT INTO official_fpl_captures
                (capture_id, season_code, next_gameweek_number,
                 next_deadline_utc, available_at_utc)
            VALUES (16, '2026-27', 1, $deadline, $cutoff);
            """;
        command.Parameters.AddWithValue(
            "$deadline",
            Deadline.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue(
            "$cutoff",
            Cutoff.UtcDateTime.ToString("O"));
        await command.ExecuteNonQueryAsync(
            TestContext.Current.CancellationToken);
        for (int index = 0; index < 15; index++)
        {
            string position = index switch
            {
                < 2 => "goalkeeper",
                < 7 => "defender",
                < 12 => "midfielder",
                _ => "forward",
            };
            await using SqliteCommand player = connection.CreateCommand();
            player.CommandText =
                """
                INSERT INTO official_fpl_players
                    (capture_id, player_id, web_name, team_id, position,
                     price_tenths, status, chance_next_round)
                VALUES (16, $playerId, $webName, $teamId, $position,
                        50, 'a', NULL);
                """;
            player.Parameters.AddWithValue("$playerId", index + 1);
            player.Parameters.AddWithValue(
                "$webName",
                $"Player {index + 1}");
            player.Parameters.AddWithValue("$teamId", index / 3 + 1);
            player.Parameters.AddWithValue("$position", position);
            await player.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken);
        }
        return options;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-selected-squad-{Guid.NewGuid():N}");

        public TemporaryFiles() => Directory.CreateDirectory(_directory);

        public string DatabasePath => Path.Combine(_directory, "autofpl.db");

        public void Dispose() => Directory.Delete(_directory, true);
    }
}
