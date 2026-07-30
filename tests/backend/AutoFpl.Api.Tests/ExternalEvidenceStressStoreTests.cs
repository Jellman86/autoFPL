using System.Text.Json;

using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class ExternalEvidenceStressStoreTests
{
    [Fact]
    public async Task Exact_current_stress_is_immutable_and_readable()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new ExternalEvidenceStressStore(
            options,
            TimeProvider.System);
        ExternalEvidenceStressDocument request = CreateRequest();

        ExternalEvidenceStressDocument first = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        ExternalEvidenceStressDocument second = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        ExternalEvidenceStressDocument? current = await store.GetCurrentAsync(
            TestContext.Current.CancellationToken);

        Assert.NotNull(first.ExternalEvidenceStressArtifactId);
        Assert.Equal(
            first.ExternalEvidenceStressArtifactId,
            second.ExternalEvidenceStressArtifactId);
        Assert.Equal(
            first.ExternalEvidenceStressArtifactId,
            current!.ExternalEvidenceStressArtifactId);
        Assert.Equal(
            request.DataIdentitySha256,
            current.DataIdentitySha256);
        Assert.Equal(1, current.Coverage.DecisionRelevantScenarioCount);
        Assert.Equal(
            "consider-alternative-if-source-trusted",
            Assert.Single(current.StressScenarios).StressDecision);
    }

    [Fact]
    public async Task Stress_cannot_assign_an_external_source_probability()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new ExternalEvidenceStressStore(
            options,
            TimeProvider.System);
        ExternalEvidenceStressDocument invalid = CreateRequest() with
        {
            StressMethod = CreateRequest().StressMethod with
            {
                AssignsSourceProbability = true,
            },
        };

        ExternalEvidenceStressValidationException exception =
            await Assert.ThrowsAsync<
                ExternalEvidenceStressValidationException>(
                () => store.ImportAsync(
                    invalid,
                    TestContext.Current.CancellationToken));

        Assert.Equal("method", exception.Code);
    }

    [Fact]
    public async Task Imported_claim_content_must_match_the_evidence_tape()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new ExternalEvidenceStressStore(
            options,
            TimeProvider.System);
        ExternalEvidenceStressDocument request = CreateRequest();
        ExternalEvidenceStressDocument invalid = request with
        {
            AdverseClaims =
            [
                request.AdverseClaims[0] with
                {
                    StartStatus = "starts",
                },
            ],
        };

        ExternalEvidenceStressValidationException exception =
            await Assert.ThrowsAsync<
                ExternalEvidenceStressValidationException>(
                () => store.ImportAsync(
                    invalid,
                    TestContext.Current.CancellationToken));

        Assert.Equal("claim-content", exception.Code);
    }

    private static ExternalEvidenceStressDocument CreateRequest()
    {
        object[] players = Enumerable.Range(1, 15)
            .Select(Player)
            .ToArray();
        object removed = Player(1);
        object added = Player(16);
        string json = JsonSerializer.Serialize(
            new
            {
                schemaVersion = "1.0",
                artifactType = ExternalEvidenceStressStore.ArtifactType,
                artifactVersion =
                    ExternalEvidenceStressStore.ArtifactVersion,
                status = ExternalEvidenceStressStore.Status,
                isPromoted = false,
                influencesAdvice = false,
                seasonCode = "2026-27",
                openingGameweek = 1,
                deadlineUtc = "2026-08-21T17:30:00Z",
                forecastDecisionCutoffUtc = "2026-07-30T10:00:00Z",
                evidenceDecisionCutoffUtc = "2026-07-30T12:00:00Z",
                officialCaptureId = 1,
                scenarioCount = 5,
                candidatePoolCount = 22,
                stressMethod = new
                {
                    method = ExternalEvidenceStressStore.Method,
                    affectedGameweeks = new[] { 1 },
                    laterGameweeksUnchanged = true,
                    assignsSourceProbability = false,
                    interpretation = "Explicit zero-minute extreme.",
                },
                policy = new
                {
                    evaluationPolicyKey = "6-expected-points",
                    horizonGameweeks = 6,
                    optimizerPolicyKey = "expected-points",
                    optimizerVersion =
                        "scipy-highs-multi-horizon-mean-cvar-v1",
                    benchWeight = 0.15m,
                    cvarWeight = 0m,
                },
                incumbent = new
                {
                    playerIds = Enumerable.Range(1, 15).ToArray(),
                    players,
                    budgetTenths = 750,
                    solverStatus =
                        "global-linear-mean-cvar-surrogate-optimum",
                    reportedMipGap = 0m,
                    exactScenarioMeanPoints = 300m,
                },
                coverage = new
                {
                    latestClaimCount = 1,
                    adverseClaimCount = 1,
                    selectedAdversePlayerCount = 1,
                    stressScenarioCount = 1,
                    squadChangingScenarioCount = 1,
                    decisionRelevantScenarioCount = 1,
                },
                selectedAdversePlayers = new[] { removed },
                adverseClaims = new[] { Claim() },
                stressScenarios = new[]
                {
                    new
                    {
                        scenarioKey = "selected-player-1",
                        scenarioType = "selected-player-extreme",
                        sourceKeys = new[]
                        {
                            "ffscout-predicted-lineups",
                        },
                        affectedPlayerIds = new[] { 1 },
                        claimIds = new[] { 1L },
                        affectedPlayerCount = 1,
                        affectedSelectedPlayers = new[] { removed },
                        selectionPlayerIds = Enumerable.Range(2, 15).ToArray(),
                        selectionBudgetTenths = 750,
                        solverStatus =
                            "global-linear-mean-cvar-surrogate-optimum",
                        reportedMipGap = 0m,
                        squadChanged = true,
                        gameweekOneRolesChanged = true,
                        removedPlayers = new[] { removed },
                        addedPlayers = new[] { added },
                        unappliedAdverseEvidenceForAddedPlayers =
                            Array.Empty<object>(),
                        isSourceConsistentAlternative = true,
                        exactScenarioMeanDifferenceIfStressTruePoints = 0.7m,
                        exactScenarioMeanDifferenceIfStressFalsePoints = -0.5m,
                        stressDecision =
                            "consider-alternative-if-source-trusted",
                    },
                },
                decision = "review-evidence-sensitive-squad",
                limitations = new[] { "This is a stress, not a probability." },
                source = new
                {
                    scenarioArtifactVersion =
                        "current-multi-horizon-joint-scenario-shadow-v1",
                    scenarioContentSha256 = new string('a', 64),
                    scenarioRunIdentitySha256 = new string('b', 64),
                },
                dataIdentitySha256 = new string('c', 64),
                runIdentitySha256 = new string('d', 64),
            });
        return JsonSerializer.Deserialize<ExternalEvidenceStressDocument>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static object Player(int playerId) =>
        new
        {
            playerId,
            webName = $"Player {playerId}",
            teamId = (playerId - 1) % 10 + 1,
            teamName = $"Team {(playerId - 1) % 10 + 1}",
            position = Position(playerId),
            priceTenths = 50,
        };

    private static object Claim() =>
        new
        {
            claimId = 1,
            sourceKey = "ffscout-predicted-lineups",
            claimType = "start",
            availableAtUtc = "2026-07-30T12:00:00Z",
            playerId = 1,
            webName = "Player 1",
            teamId = 1,
            teamName = "Team 1",
            position = "goalkeeper",
            startStatus = "does-not-start",
            availabilityStatus = (string?)null,
            forecastProbability = (decimal?)null,
            claimContentSha256 = new string('e', 64),
            duplicateClusterKey = new string('f', 64),
        };

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
                '{{new string('a', 64)}}', '{{new string('b', 64)}}',
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

            INSERT INTO evidence_claims (
                schema_version, status, source_key, canonical_url, author,
                published_at_utc, retrieved_at_utc, available_at_utc,
                content_sha256, source_revision, season_code, gameweek,
                deadline_utc, player_id, identity_capture_id, claim_type,
                availability_status, start_status, forecast_probability,
                expected_minutes, role, directness, source_span,
                extraction_method, extraction_version, extraction_confidence,
                duplicate_cluster_key, claim_content_sha256, created_at_utc
            )
            VALUES (
                '1.0', 'quarantined', 'ffscout-predicted-lineups',
                'https://example.test/lineups', 'FFScout', NULL,
                '2026-07-30T12:00:00Z', '2026-07-30T12:00:00Z',
                '{{new string('e', 64)}}', 1, '2026-27', 1,
                '2026-08-21T17:30:00Z', 1, 1, 'start', NULL,
                'does-not-start', NULL, NULL, NULL, 'reported',
                'Player 1 omitted', 'deterministic', 'test/v1', '1',
                '{{new string('f', 64)}}', '{{new string('e', 64)}}',
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
            $"autofpl-evidence-stress-tests-{Guid.NewGuid():N}");

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
