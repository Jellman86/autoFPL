using System.Security.Cryptography;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Sources;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class OfficialFplExpectedPointsEvaluationStoreTests
{
    [Fact]
    public async Task Complete_pair_is_scored_deterministically_and_read_only()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new OfficialFplExpectedPointsEvaluationStore(options);
        byte[] before = await File.ReadAllBytesAsync(
            files.DatabasePath,
            TestContext.Current.CancellationToken);

        OfficialFplExpectedPointsEvaluationDocument first =
            await store.EvaluateAsync(
                "2026-27",
                TestContext.Current.CancellationToken);
        OfficialFplExpectedPointsEvaluationDocument repeated =
            await store.EvaluateAsync(
                "2026-27",
                TestContext.Current.CancellationToken);
        byte[] after = await File.ReadAllBytesAsync(
            files.DatabasePath,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(repeated));
        Assert.Equal(SHA256.HashData(before), SHA256.HashData(after));
        Assert.Equal("complete", first.Status);
        Assert.Null(first.Reason);
        Assert.Equal(
            OfficialFplExpectedPointsEvaluationStore.EvaluatorVersion,
            first.EvaluatorVersion);
        Assert.Equal("exploratory-external-baseline-not-promoted", first.ResearchStatus);
        Assert.Equal(1, first.CandidateGameweekCount);
        Assert.Equal(1, first.EligiblePairCount);
        Assert.Equal(64, first.DataIdentitySha256.Length);
        Assert.Equal(64, first.RunIdentitySha256.Length);
        Assert.Empty(first.ExcludedGameweeks);

        Assert.NotNull(first.Metrics);
        Assert.Equal(2, first.Metrics.SampleCount);
        Assert.Equal(2, first.Metrics.Mae);
        Assert.Equal(2, first.Metrics.Rmse);
        Assert.Equal(0, first.Metrics.Bias);
        Assert.NotNull(first.ZeroMinuteMetrics);
        Assert.Equal(1, first.ZeroMinuteMetrics.SampleCount);
        Assert.Equal(2, first.ZeroMinuteMetrics.Mae);
        Assert.Equal(2, first.ZeroMinuteMetrics.Bias);
        Assert.Equal(
            ["forward", "goalkeeper"],
            first.PositionSlices.Select(slice => slice.Position).ToArray());

        OfficialFplExpectedPointsFoldDocument fold = Assert.Single(first.Folds);
        Assert.Equal("2026-27", fold.SeasonCode);
        Assert.Equal(1, fold.Gameweek);
        Assert.Equal(1, fold.ForecastCaptureId);
        Assert.Equal(1, fold.OutcomeCaptureId);
        Assert.Equal(2, fold.PlayerCount);
        Assert.Equal(first.Metrics, fold.Metrics);
    }

    [Fact]
    public async Task Missing_published_value_excludes_the_entire_gameweek()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(
            files.DatabasePath,
            missingSecondForecast: true);
        var store = new OfficialFplExpectedPointsEvaluationStore(options);

        OfficialFplExpectedPointsEvaluationDocument report =
            await store.EvaluateAsync(
                "2026-27",
                TestContext.Current.CancellationToken);

        Assert.Equal("insufficient-data", report.Status);
        Assert.Equal("no-complete-forecast-outcome-pairs", report.Reason);
        Assert.Equal(1, report.CandidateGameweekCount);
        Assert.Equal(0, report.EligiblePairCount);
        Assert.Null(report.Metrics);
        Assert.Empty(report.Folds);
        OfficialFplExpectedPointsExclusionDocument exclusion =
            Assert.Single(report.ExcludedGameweeks);
        Assert.Equal(1, exclusion.ForecastCaptureId);
        Assert.Equal("published-expected-points-incomplete", exclusion.Reason);
    }

    [Fact]
    public async Task Database_without_outcomes_reports_insufficient_data()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(
            files.DatabasePath,
            includeOutcome: false);
        var store = new OfficialFplExpectedPointsEvaluationStore(options);

        OfficialFplExpectedPointsEvaluationDocument report =
            await store.EvaluateAsync(
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("insufficient-data", report.Status);
        Assert.Equal(0, report.CandidateGameweekCount);
        Assert.Equal(0, report.EligiblePairCount);
        Assert.Empty(report.ExcludedGameweeks);
    }

    private static async Task<DatabaseOptions> CreateDatabaseAsync(
        string databasePath,
        bool missingSecondForecast = false,
        bool includeOutcome = true)
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
                1,
                '1.0',
                'official-fpl-api/v1',
                '2026-27',
                'https://fantasy.premierleague.com/api/bootstrap-static/',
                'https://fantasy.premierleague.com/api/fixtures/',
                '2026-08-20T12:00:00Z',
                '2026-08-20T12:00:00Z',
                '{{new string('a', 64)}}',
                '{{new string('b', 64)}}',
                X'7B7D',
                X'5B5D',
                1,
                1,
                2,
                1,
                1,
                '2026-08-21T17:30:00Z',
                NULL,
                '2026-08-20T12:00:00Z'
            );

            INSERT INTO official_fpl_events VALUES (
                1,
                1,
                'Gameweek 1',
                '2026-08-21T17:30:00Z',
                0,
                0,
                0,
                1
            );

            INSERT INTO official_fpl_teams VALUES (
                1,
                1,
                10,
                'North Town',
                'NTH'
            );

            INSERT INTO official_fpl_players (
                capture_id,
                player_id,
                code,
                team_id,
                position,
                first_name,
                second_name,
                web_name,
                price_tenths,
                status,
                news,
                news_added_utc,
                chance_next_round,
                selected_by_percent,
                total_points,
                minutes,
                starts,
                photo_identifier,
                expected_points_next
            )
            VALUES
                (
                    1, 1, 101, 1, 'goalkeeper', 'Ada', 'Keeper', 'Keeper',
                    50, 'a', '', NULL, NULL, '10.0', 0, 0, 0, '101.jpg', '4.0'
                ),
                (
                    1, 2, 102, 1, 'forward', 'Fran', 'Forward', 'Forward',
                    60, 'a', '', NULL, NULL, '5.0', 0, 0, 0, '102.jpg',
                    {{(missingSecondForecast ? "NULL" : "'2.0'")}}
                );
            """;
        if (includeOutcome)
        {
            command.CommandText +=
                $$"""

                INSERT INTO official_fpl_outcome_captures (
                    outcome_capture_id,
                    schema_version,
                    source_key,
                    season_code,
                    gameweek,
                    reference_capture_id,
                    live_url,
                    retrieved_at_utc,
                    available_at_utc,
                    live_sha256,
                    live_json,
                    player_count,
                    gameweek_fixture_count,
                    created_at_utc
                )
                VALUES (
                    1,
                    '1.0',
                    'official-fpl-api-event-live/v1',
                    '2026-27',
                    1,
                    1,
                    'https://fantasy.premierleague.com/api/event/1/live/',
                    '2026-08-24T22:00:00Z',
                    '2026-08-24T22:00:00Z',
                    '{{new string('c', 64)}}',
                    X'7B7D',
                    2,
                    1,
                    '2026-08-24T22:00:00Z'
                );

                INSERT INTO official_fpl_player_outcomes (
                    outcome_capture_id,
                    reference_capture_id,
                    player_id,
                    minutes,
                    starts,
                    total_points,
                    goals_scored,
                    assists,
                    clean_sheets,
                    goals_conceded,
                    saves,
                    bonus,
                    yellow_cards,
                    red_cards
                )
                VALUES
                    (1, 1, 1, 90, 1, 6, 0, 0, 1, 0, 4, 2, 0, 0),
                    (1, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                """;
        }

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return options;
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-official-xpts-evaluation-tests-{Guid.NewGuid():N}");

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
