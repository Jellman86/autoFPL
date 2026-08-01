using System.Text.Json;

using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class PublicProjectionOpeningSquadStoreTests
{
    [Fact]
    public async Task Exact_source_challenger_is_immutable_and_current()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new PublicProjectionOpeningSquadStore(
            options,
            TimeProvider.System);
        JsonElement request = CreateRequest();

        JsonElement first = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        JsonElement second = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        JsonElement? current = await store.GetCurrentAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            first.GetProperty("runIdentitySha256").GetString(),
            second.GetProperty("runIdentitySha256").GetString());
        Assert.Equal(
            PublicProjectionOpeningSquadStore.ArtifactVersion,
            current!.Value.GetProperty("artifactVersion").GetString());
        Assert.False(current.Value.GetProperty("influencesAdvice").GetBoolean());

        await using var connection =
            new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText =
            "SELECT COUNT(*) FROM public_projection_opening_squad_artifacts;";
        Assert.Equal(
            1L,
            await count.ExecuteScalarAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Challenger_rejects_an_illegal_squad()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new PublicProjectionOpeningSquadStore(
            options,
            TimeProvider.System);
        JsonElement request = CreateRequest(
            Enumerable.Repeat(1, 15).ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.ImportAsync(
                request,
                TestContext.Current.CancellationToken));
    }

    private static JsonElement CreateRequest(int[]? playerIds = null)
    {
        playerIds ??= Enumerable.Range(1, 15).ToArray();
        int[] starters = [1, 3, 4, 5, 8, 9, 10, 11, 13, 14, 15];
        object[] players = playerIds.Select(playerId => (object)new
        {
            playerId,
            webName = $"Player {playerId}",
            teamId = (playerId - 1) % 10 + 1,
            position = Position(playerId),
            priceTenths = 50,
        }).ToArray();
        object[] Gameweeks(int count) =>
            Enumerable.Range(1, count).Select(gameweek => (object)new
            {
                gameweek,
                startingPlayerIds = starters,
                captainPlayerId = 13,
                viceCaptainPlayerId = 14,
                replacementGoalkeeperPlayerId = 2,
                outfieldSubstitutePlayerIds = new[] { 6, 7, 12 },
            }).ToArray();
        object incumbentSelection = new
        {
            playerIds,
            budgetTenths = 750,
            players,
            gameweeks = Gameweeks(8),
        };
        object challengerSelection = new
        {
            playerIds,
            budgetTenths = 750,
            players,
            gameweeks = Gameweeks(8),
        };
        string json = JsonSerializer.Serialize(
            new
            {
                schemaVersion = "1.0",
                artifactType = PublicProjectionOpeningSquadStore.ArtifactType,
                artifactVersion =
                    PublicProjectionOpeningSquadStore.ArtifactVersion,
                status = PublicProjectionOpeningSquadStore.Status,
                isPromoted = false,
                influencesAdvice = false,
                seasonCode = "2026-27",
                openingGameweek = 1,
                deadlineUtc = "2026-08-21T17:30:00Z",
                forecastDecisionCutoffUtc = "2026-07-30T10:00:00Z",
                evidenceDecisionCutoffUtc = "2026-07-30T12:00:00Z",
                officialCaptureId = 1,
                scenarioCount = 64,
                candidatePoolCount = 22,
                source = new
                {
                    sourceKey = PublicProjectionOpeningSquadStore.SourceKey,
                    snapshotId = 1,
                    sourceRevision = 1,
                    availableAtUtc = "2026-07-30T12:00:00Z",
                    contentSha256 = new string('a', 64),
                    generatedAtUtc = "2026-07-30T11:30:00Z",
                    deadlineUtc = "2026-08-21T17:30:00Z",
                    declaredSource =
                        "https://fpl.solioanalytics.com/api/data/latest",
                    publishedPlayerCount = 20,
                    matchedPlayerCount = 20,
                    unmatchedPlayerCount = 0,
                    matchFraction = 1m,
                },
                projections = Enumerable.Range(1, 20)
                    .Select(playerId => new { playerId })
                    .ToArray(),
                method = new
                {
                    methodKey =
                        "solio-gw1-mean-overlay-on-retained-six-week-policy-v1",
                    horizonGameweeks = 6,
                    laterGameweeksUnchanged = true,
                    unpublishedPlayersUnchanged = true,
                    evaluationPolicyKey = "6-expected-points",
                },
                prospectiveScoreRegistration = new
                {
                    outcomeGameweeks = Enumerable.Range(1, 8).ToArray(),
                    squadMembership =
                        "fixed-opening-squad-no-transfers-for-all-eight-gameweeks",
                    roles =
                        "all-eight-weeks-frozen-from-preseason-scenario-means",
                    realisedScorer =
                        "exact-fpl-captain-fallback-and-ordered-auto-substitution",
                    outcomeStatus =
                        "waiting-for-official-2026-27-outcomes",
                },
                incumbent = new { selection = incumbentSelection },
                challenger = new
                {
                    selection = challengerSelection,
                    solver = new
                    {
                        optimizerVersion =
                            "scipy-highs-multi-horizon-mean-cvar-v1",
                        solver = "scipy.optimize.milp-highs",
                        status =
                            "global-linear-mean-cvar-surrogate-optimum",
                        mipGap = 0m,
                        reportedMipGap = 0m,
                    },
                },
                selectionChange = new
                {
                    overlapPlayerCount = 15,
                    removedPlayers = Array.Empty<object>(),
                    addedPlayers = Array.Empty<object>(),
                },
                decision =
                    "retain-as-prospective-external-challenger-only",
                dataIdentitySha256 = new string('b', 64),
                runIdentitySha256 = new string('c', 64),
            });
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Position(int playerId) =>
        playerId switch
        {
            <= 2 => "goalkeeper",
            <= 7 => "defender",
            <= 12 => "midfielder",
            _ => "forward",
        };

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
        DatabaseOptions options =
            DatabaseOptions.FromConfiguration(configuration);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);

        await using var connection =
            new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $$"""
            INSERT INTO official_fpl_captures (
                capture_id, schema_version, source_key, season_code,
                bootstrap_url, fixtures_url, retrieved_at_utc, available_at_utc,
                bootstrap_sha256, fixtures_sha256, bootstrap_json, fixtures_json,
                event_count, team_count, player_count, fixture_count,
                next_gameweek_number, next_deadline_utc,
                latest_completed_gameweek, created_at_utc
            )
            VALUES (
                1, '1.0', 'official-fpl-api/v1', '2026-27',
                'https://fantasy.premierleague.com/api/bootstrap-static/',
                'https://fantasy.premierleague.com/api/fixtures/',
                '2026-07-30T10:00:00Z', '2026-07-30T10:00:00Z',
                '{{new string('d', 64)}}', '{{new string('e', 64)}}',
                X'7B7D', X'5B5D', 1, 10, 22, 1, 1,
                '2026-08-21T17:30:00Z', NULL, '2026-07-30T10:00:00Z'
            );

            INSERT INTO official_fpl_events VALUES (
                1, 1, 'Gameweek 1', '2026-08-21T17:30:00Z', 0, 0, 0, 1
            );
            """;
        for (int teamId = 1; teamId <= 10; teamId++)
        {
            command.CommandText +=
                $"""

                INSERT INTO official_fpl_teams VALUES (
                    1, {teamId}, {teamId * 10}, 'Team {teamId}', 'T{teamId}'
                );
                """;
        }
        for (int playerId = 1; playerId <= 22; playerId++)
        {
            int teamId = (playerId - 1) % 10 + 1;
            command.CommandText +=
                $"""

                INSERT INTO official_fpl_players (
                    capture_id, player_id, code, team_id, position,
                    first_name, second_name, web_name, price_tenths, status,
                    news, news_added_utc, chance_next_round,
                    selected_by_percent, total_points, minutes, starts,
                    photo_identifier, expected_points_next
                )
                VALUES (
                    1, {playerId}, {10_000 + playerId}, {teamId},
                    '{Position(playerId)}', 'Player', '{playerId}',
                    'Player {playerId}', 50, 'a', '', NULL, NULL,
                    '1.0', 0, 0, 0, '{playerId}.jpg', NULL
                );
                """;
        }
        command.CommandText +=
            $$"""

            INSERT INTO research_source_snapshots (
                schema_version, status, source_key, source_class,
                canonical_url, final_url, dependence_group, transport_key,
                transport_version, season_code, gameweek, deadline_utc,
                identity_capture_id, retrieved_at_utc, available_at_utc,
                source_revision, content_sha256, content_bytes,
                content_brotli, created_at_utc
            )
            VALUES (
                '1.0', 'shadow-only', 'solio-public-projections',
                'public-quantitative-market-projection',
                'https://fpl.solioanalytics.com/api/data/latest.json',
                'https://fpl.solioanalytics.com/api/data/latest.json',
                'solio-sports-market-model', 'spider-mcp', 'test-v1',
                '2026-27', 1, '2026-08-21T17:30:00Z', 1,
                '2026-07-30T12:00:00Z', '2026-07-30T12:00:00Z', 1,
                '{{new string('a', 64)}}', 2, X'0102',
                '2026-07-30T12:00:00Z'
            );
            """;
        await command.ExecuteNonQueryAsync(
            TestContext.Current.CancellationToken);
        return options;
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-public-projection-tests-{Guid.NewGuid():N}");

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
