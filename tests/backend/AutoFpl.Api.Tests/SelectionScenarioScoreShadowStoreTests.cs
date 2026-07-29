using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Persistence;
using AutoFpl.Api.Selections;
using AutoFpl.Contracts.Selections;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class SelectionScenarioScoreShadowStoreTests
{
    private static readonly DateTimeOffset Cutoff =
        DateTimeOffset.Parse("2026-07-29T04:38:41Z");
    private static readonly DateTimeOffset Deadline =
        DateTimeOffset.Parse("2026-08-21T17:30:00Z");

    [Fact]
    public async Task Exact_score_is_idempotent_current_and_strictly_derived()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        SelectionScenarioScoreShadowDocument request = CreateRequest();
        var store = new SelectionScenarioScoreShadowStore(
            options,
            TimeProvider.System);

        SelectionScenarioScoreShadowDocument imported =
            await store.ImportAsync(
                request,
                TestContext.Current.CancellationToken);
        SelectionScenarioScoreShadowDocument repeated =
            await store.ImportAsync(
                request,
                TestContext.Current.CancellationToken);
        SelectionScenarioScoreShadowDocument? current =
            await store.GetCurrentAsync(
                TestContext.Current.CancellationToken);

        Assert.NotNull(imported.ScoreArtifactId);
        Assert.Equal(
            imported.ScoreArtifactId,
            repeated.ScoreArtifactId);
        Assert.Equal(
            imported.ScoreArtifactId,
            current?.ScoreArtifactId);
        Assert.Equal(15m, current?.Model.Summary.MeanPoints);
        Assert.Equal(0m, current?.UserVsModel?.MeanPointsDelta);
        Assert.False(current?.InfluencesAdvice);

        SelectionScenarioScoreValidationException exception =
            await Assert.ThrowsAsync<
                SelectionScenarioScoreValidationException>(
                () => store.ImportAsync(
                    request with
                    {
                        Model = request.Model with
                        {
                            Summary = request.Model.Summary with
                            {
                                MeanPoints = 99m,
                            },
                        },
                    },
                    TestContext.Current.CancellationToken));
        Assert.Equal("summary", exception.Code);
    }

    [Fact]
    public async Task Private_inbox_imports_only_the_current_score()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var store = new SelectionScenarioScoreShadowStore(
            options,
            TimeProvider.System);
        var importer = new SelectionScenarioScoreShadowImporter(store);
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
        var poller = new SelectionScenarioScoreInboxPoller(
            importer,
            ShadowForecastInboxOptions.FromConfiguration(configuration),
            TimeProvider.System);
        Directory.CreateDirectory(inbox);
        string path = Path.Combine(
            inbox,
            "selection-scenario-score-capture-16-selection-1.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                CreateRequest(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "imported",
            await poller.ImportOnceAsync(
                TestContext.Current.CancellationToken));
        Assert.True(File.Exists(path + ".imported"));

        SelectionScenarioScoreShadowDocument? response =
            await store.GetCurrentAsync(
                TestContext.Current.CancellationToken);
        Assert.NotNull(response?.ScoreArtifactId);
        Assert.Equal(2, response.ScenarioCount);
        Assert.Equal("available", response.UserSource.Status);
    }

    private static SelectionScenarioScoreShadowDocument CreateRequest()
    {
        SelectionScenarioDefinitionDocument modelSelection =
            CreateSelection(
                starting: [1, 3, 4, 5, 6, 8, 9, 10, 11, 13, 14],
                captain: 13,
                viceCaptain: 8,
                outfield: [7, 12, 15]);
        SelectionScenarioDefinitionDocument userSelection =
            CreateSelection(
                starting: [1, 3, 4, 5, 6, 8, 9, 10, 11, 13, 15],
                captain: 15,
                viceCaptain: 8,
                outfield: [7, 12, 14]);
        var model = new SelectionScenarioResultDocument(
            modelSelection,
            new(
                "1.0",
                SelectionScenarioScoreShadowStore.EngineVersion,
                2,
                15m,
                5m,
                10,
                11m,
                15m,
                19m,
                20,
                0.5m,
                0m),
            [10, 20],
            [2, 4]);
        var user = new SelectionScenarioResultDocument(
            userSelection,
            new(
                "1.0",
                SelectionScenarioScoreShadowStore.EngineVersion,
                2,
                15m,
                3m,
                12,
                12.6m,
                15m,
                17.4m,
                18,
                1m,
                0.5m),
            [12, 18],
            [3, 2]);
        string scenarioContentSha256 = new string('b', 64);
        string scenarioRunIdentitySha256 = new string('c', 64);
        string scenarioJson = BuildScenarioJson(
            scenarioContentSha256,
            scenarioRunIdentitySha256);
        string forecastJson = BuildForecastJson(
            modelSelection,
            "Official market baseline v0");
        string modelSelectionSha256 =
            SelectionContentSha256(modelSelection);
        string userSelectionSha256 = Sha256(
            JsonSerializer.Serialize(
                new LockedSelectionDocument(
                    userSelection.StartingPlayerIds,
                    userSelection.CaptainPlayerId,
                    userSelection.ViceCaptainPlayerId,
                    userSelection.ReplacementGoalkeeperPlayerId,
                    userSelection.OutfieldSubstitutePlayerIds),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var document = new SelectionScenarioScoreShadowDocument(
            "1.0",
            SelectionScenarioScoreShadowStore.ArtifactType,
            SelectionScenarioScoreShadowStore.ArtifactVersion,
            SelectionScenarioScoreShadowStore.Status,
            false,
            false,
            "2026-27",
            1,
            Deadline,
            Cutoff,
            16,
            SelectionScenarioScoreShadowStore.EngineVersion,
            2,
            new(
                1,
                Sha256(scenarioJson),
                scenarioContentSha256,
                scenarioRunIdentitySha256),
            new(
                13,
                Sha256(forecastJson),
                "Official market baseline v0",
                modelSelectionSha256),
            new(
                "available",
                1,
                1,
                userSelectionSha256,
                null),
            model,
            user,
            new(
                "1.0",
                SelectionScenarioScoreShadowStore.EngineVersion,
                2,
                0m,
                -1.6m,
                0m,
                1.6m,
                0.5m,
                0m,
                0.5m),
            ["Prospective shadow only."],
            string.Empty,
            string.Empty);
        string dataIdentity = DataIdentitySha256(document);
        return document with
        {
            DataIdentitySha256 = dataIdentity,
            RunIdentitySha256 = RunIdentitySha256(dataIdentity),
        };
    }

    private static SelectionScenarioDefinitionDocument CreateSelection(
        IReadOnlyList<int> starting,
        int captain,
        int viceCaptain,
        IReadOnlyList<int> outfield)
    {
        string[] positions =
        [
            "goalkeeper",
            "goalkeeper",
            "defender",
            "defender",
            "defender",
            "defender",
            "defender",
            "midfielder",
            "midfielder",
            "midfielder",
            "midfielder",
            "midfielder",
            "forward",
            "forward",
            "forward",
        ];
        return new(
            Enumerable.Range(1, 15).ToArray(),
            positions,
            starting,
            2,
            outfield,
            captain,
            viceCaptain);
    }

    private static async Task<DatabaseOptions> CreateDatabaseAsync(
        string path)
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
        await using var connection = new SqliteConnection(
            options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE official_fpl_captures (
                capture_id INTEGER PRIMARY KEY,
                available_at_utc TEXT NOT NULL
            );
            CREATE TABLE joint_scenario_shadow_artifacts (
                scenario_artifact_id INTEGER PRIMARY KEY,
                official_capture_id INTEGER NOT NULL,
                season_code TEXT NOT NULL,
                gameweek INTEGER NOT NULL,
                decision_cutoff_utc TEXT NOT NULL,
                scenario_count INTEGER NOT NULL,
                document_json TEXT NOT NULL,
                content_sha256 TEXT NOT NULL
            );
            CREATE TABLE baseline_forecast_artifacts (
                artifact_id INTEGER PRIMARY KEY,
                capture_id INTEGER NOT NULL,
                document_json TEXT NOT NULL,
                content_sha256 TEXT NOT NULL
            );
            CREATE TABLE selection_revisions (
                selection_revision_id INTEGER PRIMARY KEY,
                revision INTEGER NOT NULL,
                forecast_artifact_id INTEGER NOT NULL,
                selection_json TEXT NOT NULL,
                selection_content_sha256 TEXT NOT NULL,
                locked_at_utc TEXT
            );
            CREATE TABLE selection_scenario_score_shadow_artifacts (
                score_artifact_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL,
                artifact_type TEXT NOT NULL,
                artifact_version TEXT NOT NULL,
                status TEXT NOT NULL,
                scenario_artifact_id INTEGER NOT NULL,
                forecast_artifact_id INTEGER NOT NULL,
                selection_revision_id INTEGER,
                user_selection_key TEXT NOT NULL,
                season_code TEXT NOT NULL,
                gameweek INTEGER NOT NULL,
                decision_cutoff_utc TEXT NOT NULL,
                scenario_count INTEGER NOT NULL,
                producer_run_identity_sha256 TEXT NOT NULL,
                document_json TEXT NOT NULL,
                content_sha256 TEXT NOT NULL UNIQUE,
                created_at_utc TEXT NOT NULL,
                UNIQUE (
                    scenario_artifact_id,
                    forecast_artifact_id,
                    user_selection_key
                )
            );
            """;
        await command.ExecuteNonQueryAsync(
            TestContext.Current.CancellationToken);

        SelectionScenarioScoreShadowDocument request = CreateRequest();
        string scenarioJson = BuildScenarioJson(
            request.ScenarioSource.ScenarioContentSha256,
            request.ScenarioSource.ScenarioRunIdentitySha256);
        string forecastJson = BuildForecastJson(
            request.Model.Selection,
            request.ModelSource.ModelLabel);
        var userSelection = new LockedSelectionDocument(
            request.User!.Selection.StartingPlayerIds,
            request.User.Selection.CaptainPlayerId,
            request.User.Selection.ViceCaptainPlayerId,
            request.User.Selection.ReplacementGoalkeeperPlayerId,
            request.User.Selection.OutfieldSubstitutePlayerIds);
        await using SqliteCommand seed = connection.CreateCommand();
        seed.CommandText =
            """
            INSERT INTO official_fpl_captures
                (capture_id, available_at_utc)
            VALUES (16, $cutoff);
            INSERT INTO joint_scenario_shadow_artifacts
                (scenario_artifact_id, official_capture_id, season_code,
                 gameweek, decision_cutoff_utc, scenario_count,
                 document_json, content_sha256)
            VALUES (
                1, 16, '2026-27', 1, $cutoff, 2,
                $scenarioJson, $scenarioHash
            );
            INSERT INTO baseline_forecast_artifacts
                (artifact_id, capture_id, document_json, content_sha256)
            VALUES (13, 16, $forecastJson, $forecastHash);
            INSERT INTO selection_revisions
                (selection_revision_id, revision, forecast_artifact_id,
                 selection_json, selection_content_sha256, locked_at_utc)
            VALUES (1, 1, 13, $selectionJson, $selectionHash, NULL);
            """;
        seed.Parameters.AddWithValue("$cutoff", Cutoff.UtcDateTime.ToString("O"));
        seed.Parameters.AddWithValue("$scenarioJson", scenarioJson);
        seed.Parameters.AddWithValue(
            "$scenarioHash",
            request.ScenarioSource.ScenarioArtifactContentSha256);
        seed.Parameters.AddWithValue("$forecastJson", forecastJson);
        seed.Parameters.AddWithValue(
            "$forecastHash",
            request.ModelSource.ForecastArtifactContentSha256);
        seed.Parameters.AddWithValue(
            "$selectionJson",
            JsonSerializer.Serialize(
                userSelection,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        seed.Parameters.AddWithValue(
            "$selectionHash",
            request.UserSource.SelectionContentSha256!);
        await seed.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return options;
    }

    private static string BuildScenarioJson(
        string scenarioContentSha256,
        string runIdentitySha256) =>
        JsonSerializer.Serialize(
            new
            {
                scenarioContentSha256,
                runIdentitySha256,
            });

    private static string BuildForecastJson(
        SelectionScenarioDefinitionDocument selection,
        string modelLabel)
    {
        object[] players = selection.PlayerIds
            .Select(
                (playerId, index) =>
                {
                    string lineupPlace =
                        selection.StartingPlayerIds.Contains(playerId)
                            ? "starting"
                            : "bench";
                    int? benchOrder = lineupPlace == "starting"
                        ? null
                            : playerId == 2
                            ? 1
                            : selection
                                .OutfieldSubstitutePlayerIds
                                .ToList()
                                .IndexOf(playerId) + 1;
                    string? captaincy =
                        playerId
                            == selection.CaptainPlayerId
                            ? "captain"
                            : playerId
                                == selection.ViceCaptainPlayerId
                                ? "vice-captain"
                                : null;
                    return (object)new
                    {
                        playerId,
                        position =
                            selection.Positions[index],
                        lineupPlace,
                        benchOrder,
                        captaincy,
                    };
                })
            .ToArray();
        return JsonSerializer.Serialize(
            new
            {
                modelLabel,
                selection = new { players },
            });
    }

    private static string SelectionContentSha256(
        SelectionScenarioDefinitionDocument selection)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("captainPlayerId", selection.CaptainPlayerId);
            writer.WritePropertyName("outfieldSubstitutePlayerIds");
            JsonSerializer.Serialize(
                writer,
                selection.OutfieldSubstitutePlayerIds);
            writer.WritePropertyName("playerIds");
            JsonSerializer.Serialize(writer, selection.PlayerIds);
            writer.WritePropertyName("positions");
            JsonSerializer.Serialize(writer, selection.Positions);
            writer.WriteNumber(
                "replacementGoalkeeperPlayerId",
                selection.ReplacementGoalkeeperPlayerId);
            writer.WritePropertyName("startingPlayerIds");
            JsonSerializer.Serialize(writer, selection.StartingPlayerIds);
            writer.WriteNumber(
                "viceCaptainPlayerId",
                selection.ViceCaptainPlayerId);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static string DataIdentitySha256(
        SelectionScenarioScoreShadowDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "forecastArtifactContentSha256",
                document.ModelSource.ForecastArtifactContentSha256);
            writer.WriteNumber(
                "forecastArtifactId",
                document.ModelSource.ForecastArtifactId);
            writer.WriteString(
                "modelSelectionContentSha256",
                document.ModelSource.SelectionContentSha256);
            writer.WriteString(
                "scenarioArtifactContentSha256",
                document.ScenarioSource.ScenarioArtifactContentSha256);
            writer.WriteNumber(
                "scenarioArtifactId",
                document.ScenarioSource.ScenarioArtifactId);
            writer.WriteString(
                "scenarioContentSha256",
                document.ScenarioSource.ScenarioContentSha256);
            writer.WriteString(
                "userSelectionContentSha256",
                document.UserSource.SelectionContentSha256);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static string RunIdentitySha256(string dataIdentitySha256)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "artifactVersion",
                SelectionScenarioScoreShadowStore.ArtifactVersion);
            writer.WriteString("dataIdentitySha256", dataIdentitySha256);
            writer.WriteString(
                "engineVersion",
                SelectionScenarioScoreShadowStore.EngineVersion);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        public TemporaryDatabaseFiles()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "autofpl-selection-scenario-score-tests",
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
}
