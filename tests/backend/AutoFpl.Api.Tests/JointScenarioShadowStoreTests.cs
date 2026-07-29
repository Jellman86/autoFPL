using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class JointScenarioShadowStoreTests
{
    [Fact]
    public async Task Exact_matrix_is_immutable_idempotent_and_separate()
    {
        using var files = new TemporaryDatabaseFiles();
        (DatabaseOptions options, MultiSeasonPlayerForecastDocument point) =
            await MultiSeasonPlayerForecastStoreTests.CreateDatabaseAsync(
                files.DatabasePath);
        var pointStore = new MultiSeasonPlayerForecastStore(
            options,
            TimeProvider.System);
        MultiSeasonPlayerForecastDocument importedPoint =
            await pointStore.ImportAsync(
                point,
                TestContext.Current.CancellationToken);
        JointScenarioShadowDocument request = CreateRequest(importedPoint);
        var store = new JointScenarioShadowStore(
            options,
            TimeProvider.System);

        JointScenarioShadowDocument imported = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        JointScenarioShadowDocument repeated = await store.ImportAsync(
            request,
            TestContext.Current.CancellationToken);
        JointScenarioShadowDocument? latest = await store.GetLatestAsync(
            TestContext.Current.CancellationToken);

        Assert.NotNull(imported.ScenarioArtifactId);
        Assert.Equal(
            64,
            imported.ScenarioArtifactContentSha256?.Length);
        Assert.Equal(
            imported.ScenarioArtifactId,
            repeated.ScenarioArtifactId);
        Assert.Equal(
            imported.ScenarioArtifactId,
            latest?.ScenarioArtifactId);
        Assert.False(imported.IsPromoted);
        Assert.False(imported.InfluencesAdvice);
        Assert.All(
            imported.PlayedRows,
            row => Assert.All(row, Assert.True));

        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
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
        JointScenarioShadowDocument? response =
            await client.GetFromJsonAsync<JointScenarioShadowDocument>(
                "/api/v1/forecasts/joint-scenario-shadow/latest",
                TestContext.Current.CancellationToken);
        JointScenarioReadinessDocument? readiness =
            await client.GetFromJsonAsync<JointScenarioReadinessDocument>(
                "/api/v1/forecasts/joint-scenario-shadow/readiness",
                TestContext.Current.CancellationToken);

        Assert.Equal(
            imported.ScenarioArtifactId,
            response?.ScenarioArtifactId);
        Assert.Equal("current", readiness?.Status);
        Assert.Equal("official-capture-match", readiness?.ReasonCode);
        Assert.Equal(
            imported.ScenarioArtifactId,
            readiness?.LatestScenario?.ScenarioArtifactId);
        Assert.Equal(
            request.ScenarioContentSha256,
            readiness?.LatestScenario?.ScenarioContentSha256);
        Assert.False(readiness?.InfluencesAdvice);

        await using var connection = new SqliteConnection(
            options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText =
            """
            UPDATE joint_scenario_shadow_artifacts
            SET status = status;
            """;
        SqliteException immutable = await Assert.ThrowsAsync<SqliteException>(
            () => update.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken));
        Assert.Contains("immutable", immutable.Message);
    }

    [Fact]
    public async Task Matrix_content_can_repeat_for_a_new_official_capture()
    {
        using var files = new TemporaryDatabaseFiles();
        (DatabaseOptions options, MultiSeasonPlayerForecastDocument point) =
            await MultiSeasonPlayerForecastStoreTests.CreateDatabaseAsync(
                files.DatabasePath);
        var pointStore = new MultiSeasonPlayerForecastStore(
            options,
            TimeProvider.System);
        MultiSeasonPlayerForecastDocument importedPoint =
            await pointStore.ImportAsync(
                point,
                TestContext.Current.CancellationToken);
        var store = new JointScenarioShadowStore(
            options,
            TimeProvider.System);
        JointScenarioShadowDocument imported = await store.ImportAsync(
            CreateRequest(importedPoint),
            TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection(
            options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand allowRepeatedContent =
            connection.CreateCommand();
        allowRepeatedContent.CommandText =
            """
            INSERT INTO official_fpl_captures (
                capture_id, schema_version, source_key, season_code,
                bootstrap_url, fixtures_url, retrieved_at_utc,
                available_at_utc, bootstrap_sha256, fixtures_sha256,
                bootstrap_json, fixtures_json, event_count, team_count,
                player_count, fixture_count, next_gameweek_number,
                next_deadline_utc, latest_completed_gameweek, created_at_utc
            )
            SELECT
                capture_id + 1, schema_version, source_key, season_code,
                bootstrap_url, fixtures_url, '2026-07-29T00:00:00Z',
                '2026-07-29T00:00:00Z', $bootstrapHash, $fixturesHash,
                bootstrap_json, fixtures_json, event_count, team_count,
                player_count, fixture_count, next_gameweek_number,
                next_deadline_utc, latest_completed_gameweek,
                '2026-07-29T00:00:00Z'
            FROM official_fpl_captures
            WHERE capture_id = 15;

            INSERT INTO joint_scenario_shadow_artifacts (
                schema_version, artifact_type, artifact_version, status,
                scenario_model_key, official_capture_id,
                source_historical_capture_id, point_forecast_artifact_id,
                season_code, gameweek, decision_cutoff_utc, scenario_count,
                player_count, scenario_content_sha256,
                producer_run_identity_sha256, document_json, content_sha256,
                created_at_utc
            )
            SELECT
                schema_version, artifact_type, artifact_version, status,
                scenario_model_key, official_capture_id + 1,
                source_historical_capture_id, point_forecast_artifact_id,
                season_code, gameweek, decision_cutoff_utc, scenario_count,
                player_count, scenario_content_sha256,
                $runIdentity, document_json, $contentHash, created_at_utc
            FROM joint_scenario_shadow_artifacts
            WHERE scenario_artifact_id = $artifactId;
            """;
        allowRepeatedContent.Parameters.AddWithValue(
            "$artifactId",
            imported.ScenarioArtifactId);
        allowRepeatedContent.Parameters.AddWithValue(
            "$bootstrapHash",
            new string('3', 64));
        allowRepeatedContent.Parameters.AddWithValue(
            "$fixturesHash",
            new string('4', 64));
        allowRepeatedContent.Parameters.AddWithValue(
            "$runIdentity",
            new string('1', 64));
        allowRepeatedContent.Parameters.AddWithValue(
            "$contentHash",
            new string('2', 64));
        await allowRepeatedContent.ExecuteNonQueryAsync(
            TestContext.Current.CancellationToken);

        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText =
            """
            SELECT COUNT(*)
            FROM joint_scenario_shadow_artifacts
            WHERE scenario_content_sha256 = $matrixHash;
            """;
        count.Parameters.AddWithValue(
            "$matrixHash",
            imported.ScenarioContentSha256);

        Assert.Equal(
            2L,
            await count.ExecuteScalarAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Matrix_and_point_forecast_tampering_fail_closed()
    {
        using var files = new TemporaryDatabaseFiles();
        (DatabaseOptions options, MultiSeasonPlayerForecastDocument point) =
            await MultiSeasonPlayerForecastStoreTests.CreateDatabaseAsync(
                files.DatabasePath);
        var pointStore = new MultiSeasonPlayerForecastStore(
            options,
            TimeProvider.System);
        MultiSeasonPlayerForecastDocument importedPoint =
            await pointStore.ImportAsync(
                point,
                TestContext.Current.CancellationToken);
        JointScenarioShadowDocument request = CreateRequest(importedPoint);
        var store = new JointScenarioShadowStore(
            options,
            TimeProvider.System);

        JointScenarioValidationException content =
            await Assert.ThrowsAsync<JointScenarioValidationException>(
                () => store.ImportAsync(
                    request with
                    {
                        PointRows =
                        [
                            [99],
                            .. request.PointRows.Skip(1),
                        ],
                    },
                    TestContext.Current.CancellationToken));
        Assert.Equal("content-hash", content.Code);

        JointScenarioValidationException source =
            await Assert.ThrowsAsync<JointScenarioValidationException>(
                () => store.ImportAsync(
                    request with
                    {
                        Training = request.Training with
                        {
                            PointForecastRunIdentitySha256 =
                                new string('f', 64),
                        },
                    },
                    TestContext.Current.CancellationToken));
        Assert.Equal("point-forecast-identity", source.Code);
    }

    [Fact]
    public async Task Private_inbox_imports_valid_and_quarantines_unknown()
    {
        using var files = new TemporaryDatabaseFiles();
        (DatabaseOptions options, MultiSeasonPlayerForecastDocument point) =
            await MultiSeasonPlayerForecastStoreTests.CreateDatabaseAsync(
                files.DatabasePath);
        var pointStore = new MultiSeasonPlayerForecastStore(
            options,
            TimeProvider.System);
        MultiSeasonPlayerForecastDocument importedPoint =
            await pointStore.ImportAsync(
                point,
                TestContext.Current.CancellationToken);
        var store = new JointScenarioShadowStore(
            options,
            TimeProvider.System);
        var importer = new JointScenarioShadowImporter(store);
        string inbox = Path.Combine(files.DirectoryPath, "analytics-inbox");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [
                        "AutoFpl:Analytics:"
                        + "ShadowInboxPollIntervalMinutes"
                    ] = "1",
                    ["AutoFpl:Analytics:ShadowInboxPath"] = inbox,
                })
            .Build();
        var poller = new JointScenarioInboxPoller(
            importer,
            ShadowForecastInboxOptions.FromConfiguration(configuration),
            TimeProvider.System);
        Directory.CreateDirectory(inbox);
        string validPath = Path.Combine(
            inbox,
            "joint-scenario-shadow-capture-15.json");
        await File.WriteAllTextAsync(
            validPath,
            JsonSerializer.Serialize(
                CreateRequest(importedPoint),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "imported",
            await poller.ImportOnceAsync(
                TestContext.Current.CancellationToken));
        Assert.True(File.Exists(validPath + ".imported"));

        string invalidPath = Path.Combine(
            inbox,
            "joint-scenario-shadow-capture-16.json");
        await File.WriteAllTextAsync(
            invalidPath,
            "{\"unexpected\":true}",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "rejected",
            await poller.ImportOnceAsync(
                TestContext.Current.CancellationToken));
        Assert.True(File.Exists(invalidPath + ".rejected"));
    }

    private static JointScenarioShadowDocument CreateRequest(
        MultiSeasonPlayerForecastDocument point)
    {
        MultiSeasonPlayerForecastPlayerDocument source =
            Assert.Single(point.Players);
        JointScenarioPlayerDocument player = new(
            0,
            source.PlayerId,
            source.PlayerCode,
            source.WebName,
            source.TeamId,
            source.TeamName,
            source.Position,
            source.ExpectedPoints,
            source.ExpectedPoints,
            1m,
            1m,
            source.OfficialStatus,
            source.OfficialChanceOfPlayingNextRound,
            source.HistoricalIdentityStatus,
            "stable-code-match");
        IReadOnlyList<int> gameweeks = Enumerable.Range(1, 38).ToArray();
        IReadOnlyList<IReadOnlyList<int>> points =
            gameweeks.Select(_ => (IReadOnlyList<int>)[2]).ToArray();
        IReadOnlyList<IReadOnlyList<bool>> played =
            gameweeks.Select(_ => (IReadOnlyList<bool>)[true]).ToArray();
        string scenarioContentSha256 = ContentSha256(
            player.PlayerCode,
            gameweeks,
            points,
            played);
        MultiSeasonPlayerForecastCaptureDocument latest =
            point.Training.HistoricalCaptures.Single(
                capture => capture.SeasonCode == "2025-26");
        return new(
            "1.0",
            JointScenarioShadowStore.ArtifactType,
            JointScenarioShadowStore.ArtifactVersion,
            JointScenarioShadowStore.Status,
            false,
            false,
            point.SeasonCode,
            point.Gameweek,
            point.DeadlineUtc,
            point.DecisionCutoffUtc,
            point.OfficialCaptureId,
            JointScenarioShadowStore.ScenarioModelKey,
            JointScenarioShadowStore.AppearanceVariant,
            JointScenarioShadowStore.PointAvailabilityFusion,
            38,
            1,
            gameweeks,
            [player],
            points,
            played,
            scenarioContentSha256,
            new(0m, 0m, 0m, 0m, 0),
            new(
                latest.SeasonCode,
                latest.CaptureId,
                latest.PlayersSha256,
                latest.GameweeksSha256,
                point.RunIdentitySha256,
                new string('e', 64),
                new(
                    JointScenarioShadowStore.EvaluatorVersion,
                    JointScenarioShadowStore.EvaluationDataIdentity,
                    JointScenarioShadowStore.EvaluationRunIdentity,
                    "passes-retrospective-screen",
                    0.639806m,
                    0.071611m,
                    8,
                    8,
                    false),
                38,
                0),
            JointScenarioShadowStore.DistributionStatus,
            ["Research only."],
            new string('c', 64),
            new string('d', 64));
    }

    private static string ContentSha256(
        int playerCode,
        IReadOnlyList<int> gameweeks,
        IReadOnlyList<IReadOnlyList<int>> points,
        IReadOnlyList<IReadOnlyList<bool>> played)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("playedRows");
            JsonSerializer.Serialize(writer, played);
            writer.WritePropertyName("playerCodes");
            JsonSerializer.Serialize(writer, new[] { playerCode });
            writer.WritePropertyName("pointRows");
            JsonSerializer.Serialize(writer, points);
            writer.WritePropertyName("sourceGameweeks");
            JsonSerializer.Serialize(writer, gameweeks);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(
            SHA256.HashData(stream.ToArray()));
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        public TemporaryDatabaseFiles()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "autofpl-joint-scenario-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            DatabasePath = Path.Combine(DirectoryPath, "autofpl.db");
        }

        public string DirectoryPath { get; }

        public string DatabasePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
