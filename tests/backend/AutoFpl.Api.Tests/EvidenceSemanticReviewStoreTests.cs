using System.Net.Http.Json;
using System.Text.Json;

using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Intelligence;
using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;
using AutoFpl.Contracts.Intelligence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class EvidenceSemanticReviewStoreTests
{
    [Fact]
    public async Task Context_missing_results_in_validation_error()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var stressStore = new CurrentEvidenceReviewContextStore(
            new ExternalEvidenceStressStore(options, TimeProvider.System),
            new EvidenceClaimStore(options, TimeProvider.System));
        var store = new EvidenceSemanticReviewStore(
            options,
            stressStore,
            TimeProvider.System);

        await Assert.ThrowsAsync<EvidenceSemanticReviewValidationException>(
            () => store.ImportAsync(
                BuildReview(
                    new EvidenceReviewContextDocument(
                        "1.0",
                        CurrentEvidenceReviewContextStore.EmptyStatus,
                        CurrentEvidenceReviewContextStore.ReviewMode,
                        IsPromoted: false,
                        InfluencesForecast: false,
                        "2026-27",
                        1,
                        new DateTimeOffset(2026, 8, 21, 17, 30, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.Zero),
                        1,
                        1,
                        new string('e', 64),
                        new string('f', 64),
                        new(
                            CurrentEvidenceReviewContextStore.PromptVersion,
                            CurrentEvidenceReviewContextStore.OutputSchemaVersion,
                            ["supports-adverse-interpretation"],
                            [],
                            []),
                        new(0, 0, 0, 0, 0),
                        Array.Empty<EvidenceReviewTargetDocument>(),
                        []),
                    TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Import_is_identity_checked_immutable_and_current()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var stressStore = new ExternalEvidenceStressStore(options, TimeProvider.System);
        var claimStore = new EvidenceClaimStore(options, TimeProvider.System);
        EvidenceClaimDocument claim = await claimStore.ImportAsync(
            CreateEvidenceClaimRequest(),
            TestContext.Current.CancellationToken);
        ExternalEvidenceStressDocument stressTemplate =
            ExternalEvidenceStressStoreTests.CreateRequest();
        ExternalEvidenceStressDocument stress =
            await stressStore.ImportAsync(
                RebindStressClaimsAndScenarios(stressTemplate, claim),
                TestContext.Current.CancellationToken);
        var contextStore = new CurrentEvidenceReviewContextStore(
            stressStore,
            claimStore);
        EvidenceReviewContextDocument context = await contextStore.GetCurrentAsync(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException(
                "Expected a current evidence review context.");

        var store = new EvidenceSemanticReviewStore(
            options,
            contextStore,
            TimeProvider.System);
        EvidenceSemanticReviewDocument request = BuildReview(context, TestContext.Current.CancellationToken);
        EvidenceSemanticReviewDocument first = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        EvidenceSemanticReviewDocument second = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        EvidenceSemanticReviewDocument? current = await store.GetCurrentAsync(
            TestContext.Current.CancellationToken);

        Assert.NotNull(first.EvidenceSemanticReviewArtifactId);
        Assert.Equal(first.EvidenceSemanticReviewArtifactId, second.EvidenceSemanticReviewArtifactId);
        Assert.Equal(stress.ExternalEvidenceStressArtifactId, first.StressArtifactId);
        Assert.Equal(first.EvidenceSemanticReviewArtifactId, current?.EvidenceSemanticReviewArtifactId);
        Assert.Equal("complete", first.Status);
        Assert.False(first.InfluencesForecast);
    }

    [Fact]
    public async Task Current_review_requires_the_exact_current_context()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var stressStore = new ExternalEvidenceStressStore(options, TimeProvider.System);
        var claimStore = new EvidenceClaimStore(options, TimeProvider.System);
        EvidenceClaimDocument claim = await claimStore.ImportAsync(
            CreateEvidenceClaimRequest(),
            TestContext.Current.CancellationToken);
        ExternalEvidenceStressDocument stress = await stressStore.ImportAsync(
            RebindStressClaimsAndScenarios(
                ExternalEvidenceStressStoreTests.CreateRequest(),
                claim),
            TestContext.Current.CancellationToken);
        var contextStore = new CurrentEvidenceReviewContextStore(
            stressStore,
            claimStore);
        EvidenceReviewContextDocument context = await contextStore.GetCurrentAsync(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException(
                "Expected a current evidence review context.");
        var store = new EvidenceSemanticReviewStore(
            options,
            contextStore,
            TimeProvider.System);
        await store.ImportAsync(
            BuildReview(context, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        await claimStore.ImportAsync(
            CreateEvidenceClaimRequest() with
            {
                ContentSha256 = new string('a', 64),
                SourceRevision = 2,
                StartStatus = "starts",
                SourceSpan = "Player 1 is now expected to start.",
            },
            TestContext.Current.CancellationToken);

        EvidenceReviewContextDocument changedContext =
            await contextStore.GetCurrentAsync(TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException(
                "Expected the updated evidence review context.");
        EvidenceSemanticReviewDocument? current = await store.GetCurrentAsync(
            TestContext.Current.CancellationToken);

        Assert.NotEqual(
            context.ContextIdentitySha256,
            changedContext.ContextIdentitySha256);
        Assert.Equal(stress.ExternalEvidenceStressArtifactId, changedContext.StressArtifactId);
        Assert.Null(current);
    }

    [Fact]
    public async Task Import_rejects_source_keys_not_present_in_cited_claims()
    {
        ReviewFixture fixture = await CreateReviewFixtureAsync();
        await using (fixture)
        {
            EvidenceSemanticReviewDocument review = BuildReview(
                fixture.Context,
                TestContext.Current.CancellationToken);
            EvidenceSemanticReviewResultDocument result = review.Results[0] with
            {
                CorroboratingSourceKeys = ["invented-source"],
            };
            review = review with
            {
                Results = [result],
            };

            EvidenceSemanticReviewValidationException exception =
                await Assert.ThrowsAsync<EvidenceSemanticReviewValidationException>(
                    () => fixture.Store.ImportAsync(
                        review,
                        TestContext.Current.CancellationToken));

            Assert.Equal("result-corroborating-source", exception.Code);
            Assert.Equal("unknown-source", exception.Field);
        }
    }

    [Fact]
    public async Task Import_rejects_a_different_output_schema_version()
    {
        ReviewFixture fixture = await CreateReviewFixtureAsync();
        await using (fixture)
        {
            EvidenceSemanticReviewDocument review = BuildReview(
                fixture.Context,
                TestContext.Current.CancellationToken) with
            {
                OutputSchemaVersion = "evidence-semantic-review-v2",
            };

            EvidenceSemanticReviewValidationException exception =
                await Assert.ThrowsAsync<EvidenceSemanticReviewValidationException>(
                    () => fixture.Store.ImportAsync(
                        review,
                        TestContext.Current.CancellationToken));

            Assert.Equal("identity", exception.Code);
            Assert.Equal("outputSchemaVersion", exception.Field);
        }
    }

    [Fact]
    public async Task Insufficient_evidence_requires_missing_or_abstained_results()
    {
        ReviewFixture fixture = await CreateReviewFixtureAsync();
        await using (fixture)
        {
            EvidenceSemanticReviewDocument review = BuildReview(
                fixture.Context,
                TestContext.Current.CancellationToken) with
            {
                Status = EvidenceSemanticReviewStore.StatusInsufficientEvidence,
                Reason = "The provider reported insufficient evidence.",
            };

            EvidenceSemanticReviewValidationException exception =
                await Assert.ThrowsAsync<EvidenceSemanticReviewValidationException>(
                    () => fixture.Store.ImportAsync(
                        review,
                        TestContext.Current.CancellationToken));

            Assert.Equal("coverage", exception.Code);
            Assert.Equal("insufficient-evidence-state", exception.Field);
        }
    }

    [Fact]
    public async Task Api_route_returns_current_review()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var stressStore = new ExternalEvidenceStressStore(options, TimeProvider.System);
        var claimStore = new EvidenceClaimStore(options, TimeProvider.System);
        EvidenceClaimDocument claim = await claimStore.ImportAsync(
            CreateEvidenceClaimRequest(),
            TestContext.Current.CancellationToken);
        ExternalEvidenceStressDocument stressTemplate =
            ExternalEvidenceStressStoreTests.CreateRequest();
        ExternalEvidenceStressDocument stress =
            await stressStore.ImportAsync(
                RebindStressClaimsAndScenarios(stressTemplate, claim),
                TestContext.Current.CancellationToken);
        var contextStore = new CurrentEvidenceReviewContextStore(
            stressStore,
            claimStore);
        EvidenceReviewContextDocument context = await contextStore.GetCurrentAsync(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException(
                "Expected a current evidence review context.");

        var store = new EvidenceSemanticReviewStore(
            options,
            contextStore,
            TimeProvider.System);
        await store.ImportAsync(
            BuildReview(context, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        await using var factory = new WebApplicationFactory<Program>()
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
        EvidenceSemanticReviewDocument? response = await client.GetFromJsonAsync<EvidenceSemanticReviewDocument>(
            "/api/v1/evidence/review/current",
            TestContext.Current.CancellationToken);
        Assert.NotNull(response);
        Assert.NotNull(response.EvidenceSemanticReviewArtifactId);
        Assert.NotNull(response.EvidenceSemanticReviewArtifactContentSha256);
    }

    private static EvidenceClaimImportRequest CreateEvidenceClaimRequest() =>
        new(
            SchemaVersion: "1.0",
            SourceKey: "ffscout-predicted-lineups",
            CanonicalUrl: "https://example.test/lineups",
            Author: "FFScout",
            PublishedAtUtc: null,
            RetrievedAtUtc: new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero),
            AvailableAtUtc: new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero),
            ContentSha256: new string('e', 64),
            SourceRevision: 1,
            SeasonCode: "2026-27",
            Gameweek: 1,
            PlayerId: 1,
            ClaimType: "start",
            AvailabilityStatus: null,
            StartStatus: "does-not-start",
            ForecastProbability: null,
            ExpectedMinutes: null,
            Role: null,
            Directness: "reported",
            SourceSpan: "Player 1 is likely to miss.",
            ExtractionMethod: "deterministic",
            ExtractionVersion: "fixture-v1",
            ExtractionConfidence: 1m,
            DuplicateClusterKey: new string('f', 64));

    private static async Task<ReviewFixture> CreateReviewFixtureAsync()
    {
        var files = new TemporaryDatabaseFiles();
        try
        {
            DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
            var stressStore = new ExternalEvidenceStressStore(
                options,
                TimeProvider.System);
            var claimStore = new EvidenceClaimStore(options, TimeProvider.System);
            EvidenceClaimDocument claim = await claimStore.ImportAsync(
                CreateEvidenceClaimRequest(),
                TestContext.Current.CancellationToken);
            await stressStore.ImportAsync(
                RebindStressClaimsAndScenarios(
                    ExternalEvidenceStressStoreTests.CreateRequest(),
                    claim),
                TestContext.Current.CancellationToken);
            var contextStore = new CurrentEvidenceReviewContextStore(
                stressStore,
                claimStore);
            EvidenceReviewContextDocument context =
                await contextStore.GetCurrentAsync(
                    TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException(
                    "Expected a current evidence review context.");
            return new(
                files,
                context,
                new EvidenceSemanticReviewStore(
                    options,
                    contextStore,
                    TimeProvider.System));
        }
        catch
        {
            files.Dispose();
            throw;
        }
    }

    private static ExternalEvidenceStressDocument RebindStressClaimsAndScenarios(
        ExternalEvidenceStressDocument stressTemplate,
        EvidenceClaimDocument claim)
    {
        ExternalEvidenceStressClaimDocument adjustedClaim = stressTemplate.AdverseClaims[0] with
        {
            ClaimId = claim.ClaimId,
            ClaimContentSha256 = claim.ClaimContentSha256,
            DuplicateClusterKey = claim.DuplicateClusterKey,
        };
        ExternalEvidenceStressScenarioDocument[] adjustedScenarios =
            [.. stressTemplate.StressScenarios.Select(
                row => row with
                {
                    ClaimIds = new[] { claim.ClaimId },
                })];
        return stressTemplate with
        {
            AdverseClaims = [adjustedClaim],
            StressScenarios = adjustedScenarios,
        };
    }

    private static EvidenceSemanticReviewDocument BuildReview(
        EvidenceReviewContextDocument context,
        CancellationToken cancellationToken)
    {
        var results = context.Targets
            .Select(CreateResult)
            .ToArray();
        return new(
            "1.0",
            EvidenceSemanticReviewStore.ArtifactType,
            EvidenceSemanticReviewStore.ArtifactVersion,
            EvidenceSemanticReviewStore.StatusComplete,
            null,
            false,
            false,
            context.SeasonCode,
            context.Gameweek,
            context.DeadlineUtc,
            context.DecisionCutoffUtc,
            context.OfficialCaptureId,
            context.StressArtifactId,
            context.StressArtifactContentSha256,
            context.ContextIdentitySha256,
            "openai",
            "gpt-test-model",
            context.ReviewPolicy.PromptVersion,
            context.ReviewPolicy.OutputSchemaVersion,
            context.DecisionCutoffUtc,
            context.DeadlineUtc,
            new string('d', 64),
            new string('e', 64),
            BuildCoverage(context, results),
            results,
            null,
            null);
    }

    private static EvidenceSemanticReviewResultDocument CreateResult(
        EvidenceReviewTargetDocument target)
    {
        return new(
            target.Player,
            "supports-adverse-interpretation",
            "high",
            "next 2 gameweeks",
            target.ScenarioKeys,
            target.ReferencedClaimIds,
            [],
            [],
            target.SourceKeys is [] ? ["ffscout-predicted-lineups"] : target.SourceKeys,
            [],
            ["Potential temporary injury concern."],
            ["Unclear long-run role risk."],
            "Public report scope and prior lineup context.",
            "Claim evidence and corroborating context suggest a conservative interpretation.",
            false,
            false,
            null);
    }

    private static EvidenceSemanticReviewCoverageDocument BuildCoverage(
        EvidenceReviewContextDocument context,
        IReadOnlyList<EvidenceSemanticReviewResultDocument> results)
    {
        int citedClaims = results.SelectMany(
                row =>
                    row.SupportingClaimIds
                        .Concat(row.ContradictingClaimIds)
                        .Concat(row.DependentClaimIds))
            .Distinct()
            .Count();
        int sourceCount = results.SelectMany(
                row =>
                    row.CorroboratingSourceKeys.Concat(row.ContradictingSourceKeys))
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new(
            context.Coverage.TargetPlayerCount,
            results.Count,
            results.Count(result => result.IsAbstained),
            results.Count(result => result.IsUnavailable),
            citedClaims,
            sourceCount);
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
                '2026-07-30T10:00:00.0000000Z', '2026-07-30T10:00:00.0000000Z',
                '{{new string('a', 64)}}', '{{new string('b', 64)}}',
                X'7B7D', X'5B5D', 1, 10, 22, 1, 1,
                '2026-08-21T17:30:00.0000000Z', NULL, '2026-07-30T10:00:00.0000000Z'
            );

            INSERT INTO official_fpl_events VALUES (
                1, 1, 'Gameweek 1', '2026-08-21T17:30:00.0000000Z', 0, 0, 0, 1
            );
            """;
        for (int teamId = 1; teamId <= 10; teamId++)
        {
            command.CommandText +=
                $"""

                INSERT INTO official_fpl_teams (
                    capture_id, team_id, code, name, short_name
                ) VALUES (
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
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return options;
    }

    private static string Position(int playerId) =>
        playerId switch
        {
            <= 2 => "goalkeeper",
            <= 7 => "defender",
            <= 12 => "midfielder",
            _ => "forward",
        };

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        public TemporaryDatabaseFiles()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "autofpl-evidence-semantic-review-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            DatabasePath = Path.Combine(DirectoryPath, "autofpl.db");
        }

        public string DirectoryPath { get; }

        public string DatabasePath { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }

    private sealed record ReviewFixture(
        TemporaryDatabaseFiles Files,
        EvidenceReviewContextDocument Context,
        EvidenceSemanticReviewStore Store) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Files.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
