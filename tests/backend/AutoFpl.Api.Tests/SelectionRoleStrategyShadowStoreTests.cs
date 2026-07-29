using System.Security.Cryptography;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Api.Selections;
using AutoFpl.Contracts.Selections;

using Microsoft.Data.Sqlite;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class SelectionRoleStrategyShadowStoreTests
{
    [Fact]
    public async Task Exact_strategies_are_idempotent_current_and_derived()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "autofpl-selection-role-strategy-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "autofpl.db");
        try
        {
            DatabaseOptions options =
                await SelectionScenarioScoreShadowStoreTests
                    .CreateDatabaseAsync(databasePath);
            await CreateStrategyTableAsync(options);
            SelectionScenarioScoreShadowDocument scoreRequest =
                SelectionScenarioScoreShadowStoreTests.CreateRequest();
            var scoreStore = new SelectionScenarioScoreShadowStore(
                options,
                TimeProvider.System);
            SelectionScenarioScoreShadowDocument score =
                await scoreStore.ImportAsync(
                    scoreRequest,
                    TestContext.Current.CancellationToken);
            SelectionRoleStrategyShadowDocument request =
                CreateRequest(score);
            var store = new SelectionRoleStrategyShadowStore(
                options,
                TimeProvider.System);

            SelectionRoleStrategyShadowDocument first =
                await store.ImportAsync(
                    request,
                    TestContext.Current.CancellationToken);
            SelectionRoleStrategyShadowDocument repeated =
                await store.ImportAsync(
                    request,
                    TestContext.Current.CancellationToken);
            SelectionRoleStrategyShadowDocument? current =
                await store.GetCurrentAsync(
                    TestContext.Current.CancellationToken);

            Assert.NotNull(first.StrategyArtifactId);
            Assert.Equal(first.StrategyArtifactId, repeated.StrategyArtifactId);
            Assert.Equal(first.StrategyArtifactId, current?.StrategyArtifactId);
            Assert.Equal(15m, current?.Strategies.Balanced.Objective.ObjectiveValue);
            Assert.False(current?.InfluencesAdvice);

            SelectionRoleStrategyValidationException exception =
                await Assert.ThrowsAsync<
                    SelectionRoleStrategyValidationException>(
                    () => store.ImportAsync(
                        request with
                        {
                            Strategies = request.Strategies with
                            {
                                Safer = request.Strategies.Safer with
                                {
                                    Objective =
                                        request
                                            .Strategies
                                            .Safer
                                            .Objective with
                                        {
                                            ObjectiveValue = 99m,
                                        },
                                },
                            },
                        },
                        TestContext.Current.CancellationToken));
            Assert.Equal("objective", exception.Code);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static SelectionRoleStrategyShadowDocument CreateRequest(
        SelectionScenarioScoreShadowDocument score)
    {
        var comparison = new SelectionScenarioComparisonDocument(
            "1.0",
            SelectionRoleStrategyShadowStore.EngineVersion,
            score.ScenarioCount,
            0m,
            0m,
            0m,
            0m,
            0m,
            1m,
            0m);
        SelectionRoleStrategyDocument Strategy(
            string id,
            string objectiveKey,
            decimal? tailFraction,
            decimal objectiveValue) =>
            new(
                id,
                new(objectiveKey, tailFraction, objectiveValue),
                false,
                score.Model,
                comparison);
        var source = new SelectionRoleStrategyScoreSourceDocument(
            score.DataIdentitySha256,
            score.RunIdentitySha256,
            score.ScenarioSource,
            score.ModelSource);
        var search = new SelectionRoleStrategySearchDocument(
            SelectionRoleStrategyShadowStore.SearchVersion,
            12,
            3,
            0.20m,
            7,
            new Dictionary<string, int>
            {
                ["balanced"] = 2,
                ["safer"] = 2,
                ["higherCeiling"] = 2,
            },
            "bounded-heuristic-not-global-optimum");
        string dataIdentity = DataIdentitySha256(source.RunIdentitySha256);
        return new(
            "1.0",
            SelectionRoleStrategyShadowStore.ArtifactType,
            SelectionRoleStrategyShadowStore.ArtifactVersion,
            SelectionRoleStrategyShadowStore.Status,
            false,
            false,
            score.SeasonCode,
            score.Gameweek,
            score.DeadlineUtc,
            score.DecisionCutoffUtc,
            score.OfficialCaptureId,
            score.ScenarioCount,
            source,
            score.Model.Selection.PlayerIds,
            score.Model,
            new(
                Strategy("balanced", "mean", null, 15m),
                Strategy("safer", "lower-tail-mean", 0.20m, 10m),
                Strategy("higherCeiling", "upper-tail-mean", 0.20m, 20m)),
            search,
            ["Test-only bounded strategy artifact."],
            dataIdentity,
            RunIdentitySha256(dataIdentity));
    }

    private static async Task CreateStrategyTableAsync(
        DatabaseOptions options)
    {
        await using var connection =
            new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE selection_role_strategy_shadow_artifacts (
                strategy_artifact_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL,
                artifact_type TEXT NOT NULL,
                artifact_version TEXT NOT NULL,
                status TEXT NOT NULL,
                score_artifact_id INTEGER NOT NULL,
                search_version TEXT NOT NULL,
                season_code TEXT NOT NULL,
                gameweek INTEGER NOT NULL,
                decision_cutoff_utc TEXT NOT NULL,
                scenario_count INTEGER NOT NULL,
                producer_run_identity_sha256 TEXT NOT NULL,
                document_json TEXT NOT NULL,
                content_sha256 TEXT NOT NULL UNIQUE,
                created_at_utc TEXT NOT NULL,
                UNIQUE (score_artifact_id, search_version)
            );
            """;
        await command.ExecuteNonQueryAsync(
            TestContext.Current.CancellationToken);
    }

    private static string DataIdentitySha256(string scoreRunIdentity)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("beamWidth", 12);
            writer.WriteNumber("searchIterations", 3);
            writer.WriteString(
                "searchVersion",
                SelectionRoleStrategyShadowStore.SearchVersion);
            writer.WriteString(
                "selectionScoreRunIdentitySha256",
                scoreRunIdentity);
            writer.WriteNumber("tailFraction", 0.2);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static string RunIdentitySha256(string dataIdentity)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "artifactVersion",
                SelectionRoleStrategyShadowStore.ArtifactVersion);
            writer.WriteString("dataIdentitySha256", dataIdentity);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
}
