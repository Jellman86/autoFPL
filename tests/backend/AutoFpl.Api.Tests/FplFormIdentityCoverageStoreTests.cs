using System.Net;
using System.Net.Http.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Sources;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class FplFormIdentityCoverageStoreTests
{
    [Fact]
    public async Task Direct_ids_require_matching_attributes_and_are_served_by_capture()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedAsync(files.DatabasePath);
        var store = new FplFormIdentityCoverageStore(options);

        FplFormIdentityCoverageDocument? coverage = await store.GetAsync(
            20,
            TestContext.Current.CancellationToken);

        Assert.NotNull(coverage);
        Assert.Equal("complete", coverage.Status);
        Assert.True(coverage.IsComplete);
        Assert.Equal(10, coverage.OfficialCaptureId);
        Assert.Equal(1, coverage.DirectPlayerMatchCount);
        Assert.Equal(0, coverage.FallbackPlayerMatchCount);
        Assert.Equal(1, coverage.DirectFixtureMatchCount);
        Assert.Equal(0, coverage.FallbackFixtureMatchCount);
        Assert.Empty(coverage.Issues);

        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(
                    builder =>
                    {
                        builder.UseSetting("AutoFpl:DatabasePath", files.DatabasePath);
                        builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");
                    });
        using HttpClient client = factory.CreateClient();
        FplFormIdentityCoverageDocument? served =
            await client.GetFromJsonAsync<FplFormIdentityCoverageDocument>(
                "/api/v1/data/fpl-form-forecast/20/identity-coverage",
                TestContext.Current.CancellationToken);
        Assert.Equivalent(coverage, served, strict: true);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(
                "/api/v1/data/fpl-form-forecast/999/identity-coverage",
                TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Absent_ids_may_use_only_unique_normalized_attributes()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedAsync(
            files.DatabasePath,
            sourcePlayerId: 999,
            sourceFixtureId: 999,
            playerName: "Áda--Forward",
            teamName: "NOR");
        var store = new FplFormIdentityCoverageStore(options);

        FplFormIdentityCoverageDocument? coverage = await store.GetAsync(
            20,
            TestContext.Current.CancellationToken);

        Assert.NotNull(coverage);
        Assert.True(coverage.IsComplete);
        Assert.Equal(1, coverage.FallbackPlayerMatchCount);
        Assert.Equal(1, coverage.FallbackFixtureMatchCount);
        Assert.Empty(coverage.Issues);
    }

    [Fact]
    public async Task Present_but_inconsistent_source_ids_fail_closed()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedAsync(files.DatabasePath, teamName: "Wrong Team");
        var store = new FplFormIdentityCoverageStore(options);

        FplFormIdentityCoverageDocument? coverage = await store.GetAsync(
            20,
            TestContext.Current.CancellationToken);

        Assert.NotNull(coverage);
        Assert.Equal("incomplete", coverage.Status);
        Assert.False(coverage.IsComplete);
        Assert.Equal(1, coverage.ConflictingPlayerCount);
        Assert.Equal(1, coverage.ConflictingPredictionCount);
        Assert.Contains(
            coverage.Issues,
            issue => issue.Reason == "source-player-id-attributes-disagree");
        Assert.Contains(
            coverage.Issues,
            issue => issue.Reason == "official-player-unresolved");
    }

    [Fact]
    public async Task Attribute_fallback_rejects_ambiguous_official_players()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedAsync(files.DatabasePath, sourcePlayerId: 999);
        await using (var connection = new SqliteConnection($"Data Source={files.DatabasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO official_fpl_players (
                    capture_id, player_id, code, team_id, position,
                    first_name, second_name, web_name, price_tenths, status,
                    news, news_added_utc, chance_next_round, selected_by_percent,
                    total_points, minutes, starts, photo_identifier
                ) VALUES (
                    10, 102, 10102, 1, 'forward',
                    'Ada', 'Forward', 'Forward', 75, 'a',
                    '', NULL, 100, '1.0', 0, 0, 0, '10102.jpg'
                );
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var store = new FplFormIdentityCoverageStore(options);
        FplFormIdentityCoverageDocument? coverage = await store.GetAsync(
            20,
            TestContext.Current.CancellationToken);

        Assert.NotNull(coverage);
        Assert.Equal("incomplete", coverage.Status);
        Assert.Equal(1, coverage.ConflictingPlayerCount);
        Assert.Contains(
            coverage.Issues,
            issue => issue.Reason == "ambiguous-official-player-match");
    }

    [Fact]
    public async Task Future_catalogues_are_not_used_and_late_forecasts_are_ineligible()
    {
        using var futureFiles = new TemporaryDatabaseFiles();
        DatabaseOptions futureOptions = await CreateDatabaseAsync(futureFiles.DatabasePath);
        await SeedAsync(
            futureFiles.DatabasePath,
            officialAvailableAtUtc: "2026-08-14T11:00:00+00:00");
        var futureStore = new FplFormIdentityCoverageStore(futureOptions);

        FplFormIdentityCoverageDocument? unavailable = await futureStore.GetAsync(
            20,
            TestContext.Current.CancellationToken);

        Assert.NotNull(unavailable);
        Assert.Equal("official-catalogue-unavailable", unavailable.Status);
        Assert.Null(unavailable.OfficialCaptureId);
        Assert.False(unavailable.IsComplete);

        using var lateFiles = new TemporaryDatabaseFiles();
        DatabaseOptions lateOptions = await CreateDatabaseAsync(lateFiles.DatabasePath);
        await SeedAsync(
            lateFiles.DatabasePath,
            forecastAvailableAtUtc: "2026-08-21T18:00:00+00:00");
        var lateStore = new FplFormIdentityCoverageStore(lateOptions);

        FplFormIdentityCoverageDocument? late = await lateStore.GetAsync(
            20,
            TestContext.Current.CancellationToken);

        Assert.NotNull(late);
        Assert.Equal("forecast-after-deadline", late.Status);
        Assert.Equal(1, late.MatchedPlayerCount);
        Assert.Equal(1, late.MatchedPredictionCount);
        Assert.False(late.IsComplete);
    }

    [Fact]
    public async Task External_evaluation_scores_conditional_and_adjusted_values_separately()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedAsync(files.DatabasePath);
        await SeedOutcomeAsync(files.DatabasePath, totalPoints: 4, minutes: 0);
        var identityStore = new FplFormIdentityCoverageStore(options);
        var evaluator = new FplFormForecastEvaluationStore(options, identityStore);

        FplFormForecastEvaluationDocument first = await evaluator.EvaluateAsync(
            "2026-27",
            TestContext.Current.CancellationToken);
        FplFormForecastEvaluationDocument second = await evaluator.EvaluateAsync(
            "2026-27",
            TestContext.Current.CancellationToken);

        Assert.Equivalent(first, second, strict: true);
        Assert.Equal("complete", first.Status);
        Assert.Null(first.Reason);
        Assert.Equal(1, first.CandidateCaptureCount);
        Assert.Equal(1, first.EligiblePairCount);
        Assert.Equal(64, first.DataIdentitySha256.Length);
        Assert.Equal(64, first.RunIdentitySha256.Length);
        Assert.Empty(first.ExcludedCaptures);
        FplFormForecastEvaluationFoldDocument fold = Assert.Single(first.Folds);
        Assert.Equal(1, fold.PlayerCount);
        Assert.Equal(1, fold.FixturePredictionCount);
        Assert.Equal(1, fold.ProbabilityAdjustedPlayerCount);
        Assert.Equal(2.25, fold.PublishedConditionalMetrics.Mae);
        Assert.Equal(1.9375, fold.ProbabilityAdjustedMetrics!.Mae);

        Assert.Equal(2, first.Models.Count);
        FplFormForecastEvaluationModelDocument published = first.Models.Single(
            model => model.Name == "fpl-form-published-conditional-points");
        Assert.Equal(2.25, published.Metrics!.Mae);
        Assert.Equal(2.25, published.Metrics.Bias);
        Assert.Equal(1, published.ZeroMinuteMetrics!.SampleCount);
        FplFormForecastEvaluationModelDocument adjusted = first.Models.Single(
            model => model.Name
                == "autofpl-appearance-probability-adjusted-points");
        Assert.Equal(1.9375, adjusted.Metrics!.Mae);
        Assert.Equal(0, adjusted.MissingPlayerCount);
    }

    [Fact]
    public async Task External_evaluation_reports_missing_appearance_probabilities()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedAsync(files.DatabasePath);
        await SeedOutcomeAsync(files.DatabasePath, totalPoints: 4, minutes: 90);
        await using (var connection = new SqliteConnection($"Data Source={files.DatabasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE fpl_form_fixture_predictions
                SET appearance_probability = NULL
                WHERE capture_id = 20;
                UPDATE fpl_form_forecast_captures
                SET appearance_probability_count = 0
                WHERE capture_id = 20;
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var identityStore = new FplFormIdentityCoverageStore(options);
        var evaluator = new FplFormForecastEvaluationStore(options, identityStore);
        FplFormForecastEvaluationDocument report = await evaluator.EvaluateAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("complete", report.Status);
        FplFormForecastEvaluationModelDocument adjusted = report.Models.Single(
            model => model.Name
                == "autofpl-appearance-probability-adjusted-points");
        Assert.Equal(0, adjusted.SampleCount);
        Assert.Equal(1, adjusted.MissingPlayerCount);
        Assert.Null(adjusted.Metrics);
        Assert.Empty(adjusted.PositionSlices);
    }

    [Fact]
    public async Task External_evaluation_reports_missing_outcome_without_promoting_source()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedAsync(files.DatabasePath);
        var identityStore = new FplFormIdentityCoverageStore(options);
        var evaluator = new FplFormForecastEvaluationStore(options, identityStore);

        FplFormForecastEvaluationDocument report = await evaluator.EvaluateAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("insufficient-data", report.Status);
        Assert.Equal("no-complete-forecast-outcome-pairs", report.Reason);
        Assert.Equal(1, report.CandidateCaptureCount);
        Assert.Equal(0, report.EligiblePairCount);
        Assert.Empty(report.Folds);
        Assert.Empty(report.Models);
        FplFormForecastEvaluationExclusionDocument exclusion =
            Assert.Single(report.ExcludedCaptures);
        Assert.Equal("official-outcome-unavailable", exclusion.Reason);
    }

    [Fact]
    public async Task External_evaluation_does_not_cherry_pick_older_complete_capture()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await SeedAsync(files.DatabasePath);
        await SeedOutcomeAsync(files.DatabasePath, totalPoints: 4, minutes: 90);
        await using (var connection = new SqliteConnection($"Data Source={files.DatabasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO fpl_form_forecast_captures (
                    capture_id, schema_version, source_key, source_url,
                    season_code, gameweek, retrieved_at_utc, available_at_utc,
                    content_sha256, transport, extraction_version,
                    provider_payload_sha256, evidence_brotli,
                    player_count, fixture_prediction_count,
                    appearance_probability_count, created_at_utc
                )
                SELECT
                    21, schema_version, source_key, source_url,
                    season_code, gameweek,
                    '2026-08-14T11:00:00+00:00',
                    '2026-08-14T11:00:00+00:00',
                    $contentSha, transport, extraction_version,
                    $providerSha, evidence_brotli,
                    player_count, fixture_prediction_count,
                    appearance_probability_count,
                    '2026-08-14T11:00:00+00:00'
                FROM fpl_form_forecast_captures
                WHERE capture_id = 20;
                INSERT INTO fpl_form_fixture_predictions
                SELECT
                    21, source_player_id, fixture_id, gameweek,
                    player_name, 'Wrong Team', position, kickoff_local,
                    predicted_points, appearance_probability
                FROM fpl_form_fixture_predictions
                WHERE capture_id = 20;
                """;
            command.Parameters.AddWithValue("$contentSha", new string('f', 64));
            command.Parameters.AddWithValue("$providerSha", new string('1', 64));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var identityStore = new FplFormIdentityCoverageStore(options);
        var evaluator = new FplFormForecastEvaluationStore(options, identityStore);
        FplFormForecastEvaluationDocument report = await evaluator.EvaluateAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("insufficient-data", report.Status);
        Assert.Empty(report.Folds);
        FplFormForecastEvaluationExclusionDocument exclusion =
            Assert.Single(report.ExcludedCaptures);
        Assert.Equal(21, exclusion.ForecastCaptureId);
        Assert.Equal("incomplete", exclusion.Reason);
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

    private static async Task SeedAsync(
        string databasePath,
        int sourcePlayerId = 101,
        int sourceFixtureId = 10,
        string playerName = "Ada Forward",
        string teamName = "North London",
        string officialAvailableAtUtc = "2026-08-14T09:00:00.0000000Z",
        string forecastAvailableAtUtc = "2026-08-14T10:00:00+00:00")
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true,
            }.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO official_fpl_captures (
                capture_id, schema_version, source_key, season_code,
                bootstrap_url, fixtures_url, retrieved_at_utc, available_at_utc,
                bootstrap_sha256, fixtures_sha256, bootstrap_json, fixtures_json,
                event_count, team_count, player_count, fixture_count,
                next_gameweek_number, next_deadline_utc,
                latest_completed_gameweek, created_at_utc
            ) VALUES (
                10, '1.0', 'official-fpl-api/v1', '2026-27',
                'https://fantasy.premierleague.com/api/bootstrap-static/',
                'https://fantasy.premierleague.com/api/fixtures/',
                $officialAvailableAtUtc, $officialAvailableAtUtc,
                $bootstrapSha, $fixturesSha, X'00', X'00',
                1, 2, 1, 1, 1, $deadlineUtc, NULL, $officialAvailableAtUtc
            );
            INSERT INTO official_fpl_events VALUES (
                10, 1, 'Gameweek 1', $deadlineUtc, 0, 0, 0, 1
            );
            INSERT INTO official_fpl_teams VALUES
                (10, 1, 1, 'North London', 'NOR'),
                (10, 2, 2, 'South Coast', 'SOU');
            INSERT INTO official_fpl_players (
                capture_id, player_id, code, team_id, position,
                first_name, second_name, web_name, price_tenths, status,
                news, news_added_utc, chance_next_round, selected_by_percent,
                total_points, minutes, starts, photo_identifier
            ) VALUES (
                10, 101, 10101, 1, 'forward',
                'Ada', 'Forward', 'Forward', 75, 'a',
                '', NULL, 100, '10.0', 0, 0, 0, '10101.jpg'
            );
            INSERT INTO official_fpl_fixtures VALUES (
                10, 10, 1, 1, 2, '2026-08-22T14:00:00+00:00',
                0, 0, 0, NULL, NULL
            );
            INSERT INTO fpl_form_forecast_captures (
                capture_id, schema_version, source_key, source_url,
                season_code, gameweek, retrieved_at_utc, available_at_utc,
                content_sha256, transport, extraction_version,
                provider_payload_sha256, evidence_brotli,
                player_count, fixture_prediction_count,
                appearance_probability_count, created_at_utc
            ) VALUES (
                20, '1.1', 'fpl-form-public-forecast/v1',
                'https://fplform.com/fpl-predicted-points',
                '2026-27', 1, $forecastAvailableAtUtc, $forecastAvailableAtUtc,
                $forecastSha, 'playwright-mcp/v1', 'fpl-form-dom/v1',
                $providerSha, X'00', 1, 1, 1, $forecastAvailableAtUtc
            );
            INSERT INTO fpl_form_fixture_predictions VALUES (
                20, $sourcePlayerId, $sourceFixtureId, 1,
                $playerName, $teamName, 'forward', '2026-08-22 15:00:00',
                '6.25', '0.95'
            );
            """;
        command.Parameters.AddWithValue("$officialAvailableAtUtc", officialAvailableAtUtc);
        command.Parameters.AddWithValue("$forecastAvailableAtUtc", forecastAvailableAtUtc);
        command.Parameters.AddWithValue(
            "$deadlineUtc",
            "2026-08-21T17:30:00.0000000Z");
        command.Parameters.AddWithValue("$bootstrapSha", new string('a', 64));
        command.Parameters.AddWithValue("$fixturesSha", new string('b', 64));
        command.Parameters.AddWithValue("$forecastSha", new string('c', 64));
        command.Parameters.AddWithValue("$providerSha", new string('d', 64));
        command.Parameters.AddWithValue("$sourcePlayerId", sourcePlayerId);
        command.Parameters.AddWithValue("$sourceFixtureId", sourceFixtureId);
        command.Parameters.AddWithValue("$playerName", playerName);
        command.Parameters.AddWithValue("$teamName", teamName);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SeedOutcomeAsync(
        string databasePath,
        int totalPoints,
        int minutes)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true,
            }.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO official_fpl_outcome_captures (
                outcome_capture_id, schema_version, source_key,
                season_code, gameweek, reference_capture_id, live_url,
                retrieved_at_utc, available_at_utc, live_sha256, live_json,
                player_count, gameweek_fixture_count, created_at_utc
            ) VALUES (
                30, '1.0', 'official-fpl-api-event-live/v1',
                '2026-27', 1, 10,
                'https://fantasy.premierleague.com/api/event/1/live/',
                '2026-08-24T12:00:00+00:00',
                '2026-08-24T12:00:00+00:00',
                $liveSha, X'00', 1, 1, '2026-08-24T12:00:00+00:00'
            );
            INSERT INTO official_fpl_player_outcomes (
                outcome_capture_id, reference_capture_id, player_id,
                minutes, starts, total_points, goals_scored, assists,
                clean_sheets, goals_conceded, saves, bonus,
                yellow_cards, red_cards
            ) VALUES (
                30, 10, 101, $minutes, 0, $totalPoints,
                0, 0, 0, 0, 0, 0, 0, 0
            );
            """;
        command.Parameters.AddWithValue("$liveSha", new string('e', 64));
        command.Parameters.AddWithValue("$totalPoints", totalPoints);
        command.Parameters.AddWithValue("$minutes", minutes);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-fpl-form-identity-tests-{Guid.NewGuid():N}");

        public TemporaryDatabaseFiles()
        {
            Directory.CreateDirectory(_directory);
            DatabasePath = Path.Combine(_directory, "autofpl.db");
        }

        public string DatabasePath { get; }

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
