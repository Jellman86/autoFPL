using System.Text.Json;

using AutoFpl.Api.Intelligence;
using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Intelligence;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class EvidenceClaimEvaluationStoreTests
{
    [Fact]
    public async Task Start_claims_are_scored_by_source_and_lead_time()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await ImportClaimsAsync(options);
        var store = new EvidenceClaimEvaluationStore(options);

        EvidenceClaimEvaluationDocument first = await store.EvaluateAsync(
            "2026-27",
            TestContext.Current.CancellationToken);
        EvidenceClaimEvaluationDocument repeated = await store.EvaluateAsync(
            "2026-27",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(repeated));
        Assert.Equal("complete", first.Status);
        Assert.Null(first.Reason);
        Assert.Equal(EvidenceClaimEvaluationStore.EvaluatorVersion, first.EvaluatorVersion);
        Assert.Equal(
            EvidenceClaimEvaluationStore.ResearchStatus,
            first.ResearchStatus);
        Assert.Equal(1, first.CandidateOutcomeCount);
        Assert.Equal(1, first.EvaluatedGameweekCount);
        Assert.Equal(3, first.EvaluatedClaimCount);
        Assert.Equal(64, first.DataIdentitySha256.Length);
        Assert.Equal(64, first.RunIdentitySha256.Length);
        Assert.Empty(first.Exclusions);

        EvidenceClaimEvaluationFoldDocument fold = Assert.Single(first.Folds);
        Assert.Equal(3, fold.ClaimCount);
        Assert.Equal(2, fold.Slices.Count);

        EvidenceClaimEvaluationSliceDocument categorical = Assert.Single(
            first.Slices,
            slice => slice.SourceKey == "ffscout-predicted-lineups");
        Assert.Equal("0-6h", categorical.LeadTimeBucket);
        Assert.Equal(1, categorical.SampleCount);
        Assert.Equal(1, categorical.CorrectCount);
        Assert.Equal(1, categorical.Accuracy);
        Assert.Equal(0, categorical.ProbabilisticSampleCount);
        Assert.Null(categorical.BrierScore);
        Assert.Null(categorical.LogLoss);

        EvidenceClaimEvaluationSliceDocument probabilistic = Assert.Single(
            first.Slices,
            slice => slice.SourceKey == "straightred-lineup-consensus");
        Assert.Equal("24-72h", probabilistic.LeadTimeBucket);
        Assert.Equal(2, probabilistic.SampleCount);
        Assert.Equal(1, probabilistic.PositiveOutcomeCount);
        Assert.Equal(2, probabilistic.CorrectCount);
        Assert.Equal(1, probabilistic.Accuracy);
        Assert.Equal(2, probabilistic.ProbabilisticSampleCount);
        Assert.Equal(0.0625, probabilistic.BrierScore);
        Assert.Equal(0.287682, probabilistic.LogLoss);
    }

    [Fact]
    public async Task Availability_claims_are_not_scored_against_appearance()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await ImportAvailabilityClaimAsync(options);

        EvidenceClaimEvaluationDocument report =
            await new EvidenceClaimEvaluationStore(options).EvaluateAsync(
                "2026-27",
                TestContext.Current.CancellationToken);

        Assert.Equal("insufficient-data", report.Status);
        Assert.Equal("no-scorable-claim-outcome-pairs", report.Reason);
        Assert.Equal(1, report.CandidateOutcomeCount);
        Assert.Equal(0, report.EvaluatedClaimCount);
        EvidenceClaimEvaluationExclusionDocument exclusion =
            Assert.Single(report.Exclusions);
        Assert.Equal("no-pre-deadline-start-claims", exclusion.Reason);
    }

    [Fact]
    public async Task Missing_official_start_identity_excludes_the_whole_fold()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(
            files.DatabasePath,
            includeSecondOutcome: false);
        await ImportClaimsAsync(options);

        EvidenceClaimEvaluationDocument report =
            await new EvidenceClaimEvaluationStore(options).EvaluateAsync(
                "2026-27",
                TestContext.Current.CancellationToken);

        Assert.Equal("insufficient-data", report.Status);
        Assert.Equal(0, report.EvaluatedClaimCount);
        EvidenceClaimEvaluationExclusionDocument exclusion =
            Assert.Single(report.Exclusions);
        Assert.Equal(
            "official-outcome-player-identity-incomplete",
            exclusion.Reason);
    }

    private static async Task ImportClaimsAsync(DatabaseOptions options)
    {
        var store = new EvidenceClaimStore(options, TimeProvider.System);
        await store.ImportAsync(
            CreateStartRequest(
                "ffscout-predicted-lineups",
                1,
                new DateTimeOffset(2026, 8, 21, 16, 0, 0, TimeSpan.Zero),
                null,
                "FFScout player one"),
            TestContext.Current.CancellationToken);
        await store.ImportAsync(
            CreateStartRequest(
                "straightred-lineup-consensus",
                1,
                new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
                0.75m,
                "Consensus player one"),
            TestContext.Current.CancellationToken);
        await store.ImportAsync(
            CreateStartRequest(
                "straightred-lineup-consensus",
                2,
                new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
                0.25m,
                "Consensus player two"),
            TestContext.Current.CancellationToken);
    }

    private static async Task ImportAvailabilityClaimAsync(DatabaseOptions options)
    {
        var store = new EvidenceClaimStore(options, TimeProvider.System);
        DateTimeOffset availableAt =
            new(2026, 8, 21, 16, 0, 0, TimeSpan.Zero);
        await store.ImportAsync(
            new(
                "1.0",
                "ffscout-predicted-lineups",
                "https://example.test/availability",
                "FFScout",
                null,
                availableAt,
                availableAt,
                new string('d', 64),
                1,
                "2026-27",
                1,
                1,
                "availability",
                "doubtful",
                null,
                0.25m,
                null,
                null,
                "reported",
                "Player one doubtful",
                "deterministic",
                "test/v1",
                1m,
                null),
            TestContext.Current.CancellationToken);
    }

    private static EvidenceClaimImportRequest CreateStartRequest(
        string sourceKey,
        int playerId,
        DateTimeOffset availableAt,
        decimal? probability,
        string sourceSpan) =>
        new(
            "1.0",
            sourceKey,
            $"https://example.test/{sourceKey}",
            sourceKey,
            null,
            availableAt,
            availableAt,
            new string(
                sourceKey == "straightred-lineup-consensus" ? 'e' : 'f',
                64),
            1,
            "2026-27",
            1,
            playerId,
            "start",
            null,
            "starts",
            probability,
            null,
            null,
            probability is null ? "reported" : "model-forecast",
            sourceSpan,
            "deterministic",
            "test/v1",
            1m,
            null);

    private static async Task<DatabaseOptions> CreateDatabaseAsync(
        string databasePath,
        bool includeSecondOutcome = true)
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

        await using var connection = new SqliteConnection(options.ConnectionString);
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
                '2026-08-20T10:00:00Z', '2026-08-20T10:00:00Z',
                '{{new string('a', 64)}}', '{{new string('b', 64)}}',
                X'7B7D', X'5B5D', 1, 1, 2, 1, 1,
                '2026-08-21T17:30:00Z', NULL, '2026-08-20T10:00:00Z'
            );

            INSERT INTO official_fpl_events VALUES (
                1, 1, 'Gameweek 1', '2026-08-21T17:30:00Z', 0, 0, 0, 1
            );

            INSERT INTO official_fpl_teams VALUES (
                1, 1, 10, 'North Town', 'NTH'
            );

            INSERT INTO official_fpl_players (
                capture_id, player_id, code, team_id, position,
                first_name, second_name, web_name, price_tenths, status,
                news, news_added_utc, chance_next_round, selected_by_percent,
                total_points, minutes, starts, photo_identifier,
                expected_points_next
            )
            VALUES
                (
                    1, 1, 101, 1, 'goalkeeper', 'Ada', 'Keeper', 'Keeper',
                    50, 'a', '', NULL, NULL, '10.0', 0, 0, 0, '101.jpg', NULL
                ),
                (
                    1, 2, 102, 1, 'forward', 'Fran', 'Forward', 'Forward',
                    60, 'a', '', NULL, NULL, '5.0', 0, 0, 0, '102.jpg', NULL
                );

            INSERT INTO official_fpl_outcome_captures (
                outcome_capture_id, schema_version, source_key, season_code,
                gameweek, reference_capture_id, live_url, retrieved_at_utc,
                available_at_utc, live_sha256, live_json, player_count,
                gameweek_fixture_count, created_at_utc
            )
            VALUES (
                1, '1.0', 'official-fpl-api-event-live/v1', '2026-27', 1, 1,
                'https://fantasy.premierleague.com/api/event/1/live/',
                '2026-08-24T22:00:00Z', '2026-08-24T22:00:00Z',
                '{{new string('c', 64)}}', X'7B7D', 2, 1,
                '2026-08-24T22:00:00Z'
            );

            INSERT INTO official_fpl_player_outcomes (
                outcome_capture_id, reference_capture_id, player_id,
                minutes, starts, total_points, goals_scored, assists,
                clean_sheets, goals_conceded, saves, bonus,
                yellow_cards, red_cards
            )
            VALUES
                (1, 1, 1, 90, 1, 6, 0, 0, 1, 0, 4, 2, 0, 0)
                {{(includeSecondOutcome
                    ? ", (1, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)"
                    : string.Empty)}};
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return options;
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-evidence-evaluation-tests-{Guid.NewGuid():N}");

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
