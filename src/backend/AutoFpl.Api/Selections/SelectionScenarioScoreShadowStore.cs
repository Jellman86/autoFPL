using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Selections;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Selections;

public sealed class SelectionScenarioScoreShadowStore
{
    public const string ArtifactType =
        "current-selection-joint-scenario-score-shadow";
    public const string ArtifactVersion =
        "current-selection-scenario-score-v1";
    public const string Status = "prospective-shadow-unscored";
    public const string EngineVersion = "cpu-joint-scenario-reference-v1";

    private const decimal NumericTolerance = 0.000000001m;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public SelectionScenarioScoreShadowStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<SelectionScenarioScoreShadowDocument> ImportAsync(
        SelectionScenarioScoreShadowDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);

        await using var connection = new SqliteConnection(
            _options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        ScenarioSource scenario = await ReadScenarioAsync(
            connection,
            transaction,
            document.ScenarioSource.ScenarioArtifactId,
            cancellationToken);
        ForecastSource forecast = await ReadForecastAsync(
            connection,
            transaction,
            document.ModelSource.ForecastArtifactId,
            cancellationToken);
        SelectionSource? selection = await ReadLatestSelectionAsync(
            connection,
            transaction,
            forecast.ArtifactId,
            cancellationToken);
        ValidateLineage(document, scenario, forecast, selection);

        string documentJson = JsonSerializer.Serialize(document, JsonOptions);
        string contentSha256 = Sha256(documentJson);
        string userSelectionKey =
            document.UserSource.SelectionContentSha256 ?? "missing";
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO selection_scenario_score_shadow_artifacts (
                    schema_version, artifact_type, artifact_version, status,
                    scenario_artifact_id, forecast_artifact_id,
                    selection_revision_id, user_selection_key, season_code,
                    gameweek, decision_cutoff_utc, scenario_count,
                    producer_run_identity_sha256, document_json,
                    content_sha256, created_at_utc
                )
                VALUES (
                    $schemaVersion, $artifactType, $artifactVersion, $status,
                    $scenarioArtifactId, $forecastArtifactId,
                    $selectionRevisionId, $userSelectionKey, $seasonCode,
                    $gameweek, $decisionCutoffUtc, $scenarioCount,
                    $runIdentity, $documentJson, $contentSha256, $createdAtUtc
                )
                ON CONFLICT (
                    scenario_artifact_id,
                    forecast_artifact_id,
                    user_selection_key
                ) DO NOTHING;
                """;
            insert.Parameters.AddWithValue(
                "$schemaVersion",
                document.SchemaVersion);
            insert.Parameters.AddWithValue("$artifactType", document.ArtifactType);
            insert.Parameters.AddWithValue(
                "$artifactVersion",
                document.ArtifactVersion);
            insert.Parameters.AddWithValue("$status", document.Status);
            insert.Parameters.AddWithValue(
                "$scenarioArtifactId",
                scenario.ArtifactId);
            insert.Parameters.AddWithValue(
                "$forecastArtifactId",
                forecast.ArtifactId);
            insert.Parameters.AddWithValue(
                "$selectionRevisionId",
                selection is null ? DBNull.Value : selection.RevisionId);
            insert.Parameters.AddWithValue(
                "$userSelectionKey",
                userSelectionKey);
            insert.Parameters.AddWithValue("$seasonCode", document.SeasonCode);
            insert.Parameters.AddWithValue("$gameweek", document.Gameweek);
            insert.Parameters.AddWithValue(
                "$decisionCutoffUtc",
                document.DecisionCutoffUtc.UtcDateTime.ToString("O"));
            insert.Parameters.AddWithValue(
                "$scenarioCount",
                document.ScenarioCount);
            insert.Parameters.AddWithValue(
                "$runIdentity",
                document.RunIdentitySha256);
            insert.Parameters.AddWithValue("$documentJson", documentJson);
            insert.Parameters.AddWithValue("$contentSha256", contentSha256);
            insert.Parameters.AddWithValue(
                "$createdAtUtc",
                _timeProvider.GetUtcNow().UtcDateTime.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        StoredArtifact stored = await ReadExactAsync(
            connection,
            transaction,
            scenario.ArtifactId,
            forecast.ArtifactId,
            userSelectionKey,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The selection scenario score shadow was not persisted.");
        if (!StringComparer.Ordinal.Equals(
            stored.ContentSha256,
            contentSha256))
        {
            throw new SelectionScenarioScoreValidationException(
                "source-conflict",
                "dataIdentitySha256");
        }

        await transaction.CommitAsync(cancellationToken);
        return Materialize(stored);
    }

    public async Task<SelectionScenarioScoreShadowDocument?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(
            _options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH latest_official AS (
                SELECT capture_id
                FROM official_fpl_captures
                ORDER BY available_at_utc DESC, capture_id DESC
                LIMIT 1
            ),
            current_scenario AS (
                SELECT scenario_artifact_id, official_capture_id
                FROM joint_scenario_shadow_artifacts
                WHERE official_capture_id = (
                    SELECT capture_id FROM latest_official
                )
                ORDER BY scenario_artifact_id DESC
                LIMIT 1
            ),
            current_forecast AS (
                SELECT artifact_id
                FROM baseline_forecast_artifacts
                WHERE capture_id = (
                    SELECT official_capture_id FROM current_scenario
                )
                ORDER BY artifact_id DESC
                LIMIT 1
            ),
            current_selection AS (
                SELECT selection_revision_id
                FROM selection_revisions
                WHERE forecast_artifact_id = (
                    SELECT artifact_id FROM current_forecast
                )
                ORDER BY revision DESC, selection_revision_id DESC
                LIMIT 1
            )
            SELECT score_artifact_id, document_json, content_sha256
            FROM selection_scenario_score_shadow_artifacts
            WHERE scenario_artifact_id = (
                SELECT scenario_artifact_id FROM current_scenario
            )
            AND forecast_artifact_id = (
                SELECT artifact_id FROM current_forecast
            )
            AND selection_revision_id IS (
                SELECT selection_revision_id FROM current_selection
            )
            ORDER BY score_artifact_id DESC
            LIMIT 1;
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? Materialize(
                new(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2)))
            : null;
    }

    private static void ValidateDocument(
        SelectionScenarioScoreShadowDocument document)
    {
        Require(document.SchemaVersion == "1.0", "identity", "schemaVersion");
        Require(document.ArtifactType == ArtifactType, "identity", "artifactType");
        Require(
            document.ArtifactVersion == ArtifactVersion,
            "identity",
            "artifactVersion");
        Require(document.Status == Status, "identity", "status");
        Require(!document.IsPromoted, "must-be-false", "isPromoted");
        Require(!document.InfluencesAdvice, "must-be-false", "influencesAdvice");
        Require(
            document.ScoreArtifactId is null
                && document.ScoreArtifactContentSha256 is null,
            "must-be-null",
            "scoreArtifactId");
        Require(
            document.SeasonCode == "2026-27"
                && document.Gameweek == 1
                && document.OfficialCaptureId > 0
                && document.DecisionCutoffUtc < document.DeadlineUtc,
            "target",
            "officialCaptureId");
        Require(
            document.EngineVersion == EngineVersion
                && document.ScenarioCount is >= 1 and <= 512,
            "identity",
            "engineVersion");
        Require(
            document.ScenarioSource.ScenarioArtifactId > 0
                && IsSha256(
                    document
                        .ScenarioSource
                        .ScenarioArtifactContentSha256)
                && IsSha256(
                    document.ScenarioSource.ScenarioContentSha256)
                && IsSha256(
                    document.ScenarioSource.ScenarioRunIdentitySha256),
            "sha256",
            "scenarioSource");
        Require(
            document.ModelSource.ForecastArtifactId > 0
                && IsSha256(
                    document
                        .ModelSource
                        .ForecastArtifactContentSha256)
                && IsSha256(
                    document.ModelSource.SelectionContentSha256)
                && !string.IsNullOrWhiteSpace(
                    document.ModelSource.ModelLabel),
            "model-source",
            "modelSource");
        Require(
            document.Limitations.Count is >= 1 and <= 32
                && document.Limitations.All(
                    value => !string.IsNullOrWhiteSpace(value)
                        && value.Length <= 1000)
                && IsSha256(document.DataIdentitySha256)
                && IsSha256(document.RunIdentitySha256),
            "boundary",
            "limitations");
        Require(
            document.ModelSource.SelectionContentSha256
                == SelectionContentSha256(document.Model.Selection),
            "model-selection-hash",
            "modelSource.selectionContentSha256");
        Require(
            document.DataIdentitySha256 == DataIdentitySha256(document)
                && document.RunIdentitySha256
                    == RunIdentitySha256(document),
            "identity-hash",
            "dataIdentitySha256");

        ValidateResult(
            document.Model,
            document.ScenarioCount,
            "model");
        bool hasUser = document.UserSource.Status == "available";
        Require(
            hasUser
                ? document.UserSource.SelectionRevisionId is > 0
                    && document.UserSource.Revision is > 0
                    && document.UserSource.SelectionContentSha256 is not null
                    && IsSha256(
                        document.UserSource.SelectionContentSha256)
                    && document.User is not null
                    && document.UserVsModel is not null
                : document.UserSource.Status == "missing"
                    && document.UserSource.SelectionRevisionId is null
                    && document.UserSource.Revision is null
                    && document.UserSource.SelectionContentSha256 is null
                    && document.UserSource.LockedAtUtc is null
                    && document.User is null
                    && document.UserVsModel is null,
            "user-source",
            "userSource");
        if (document.User is not null
            && document.UserVsModel is not null)
        {
            ValidateResult(
                document.User,
                document.ScenarioCount,
                "user");
            ValidateComparison(
                document.Model.TotalPointRows,
                document.User.TotalPointRows,
                document.UserVsModel,
                document.ScenarioCount);
        }
    }

    internal static void ValidateResult(
        SelectionScenarioResultDocument result,
        int scenarioCount,
        string field)
    {
        SelectionScenarioDefinitionDocument selection = result.Selection;
        Require(
            selection.PlayerIds.Count == 15
                && selection.Positions.Count == 15
                && selection.PlayerIds.Distinct().Count() == 15
                && selection.PlayerIds.All(value => value > 0)
                && selection.Positions.All(
                    value => value is "goalkeeper"
                        or "defender"
                        or "midfielder"
                        or "forward")
                && selection.StartingPlayerIds.Count == 11
                && selection.StartingPlayerIds.Distinct().Count() == 11
                && selection.OutfieldSubstitutePlayerIds.Count == 3
                && selection.OutfieldSubstitutePlayerIds.Distinct().Count() == 3,
            "selection",
            $"{field}.selection");
        var players = selection.PlayerIds.ToHashSet();
        var starting = selection.StartingPlayerIds.ToHashSet();
        var substitutes = selection.OutfieldSubstitutePlayerIds
            .Append(selection.ReplacementGoalkeeperPlayerId)
            .ToHashSet();
        var positionByPlayer = selection.PlayerIds
            .Zip(selection.Positions)
            .ToDictionary(pair => pair.First, pair => pair.Second);
        int goalkeeperCount = selection.StartingPlayerIds.Count(
            playerId => positionByPlayer[playerId] == "goalkeeper");
        int defenderCount = selection.StartingPlayerIds.Count(
            playerId => positionByPlayer[playerId] == "defender");
        int midfielderCount = selection.StartingPlayerIds.Count(
            playerId => positionByPlayer[playerId] == "midfielder");
        int forwardCount = selection.StartingPlayerIds.Count(
            playerId => positionByPlayer[playerId] == "forward");
        Require(
            starting.IsSubsetOf(players)
                && substitutes.Count == 4
                && substitutes.IsSubsetOf(players)
                && !starting.Overlaps(substitutes)
                && starting.Count + substitutes.Count == players.Count
                && selection.CaptainPlayerId
                    != selection.ViceCaptainPlayerId
                && starting.Contains(selection.CaptainPlayerId)
                && starting.Contains(selection.ViceCaptainPlayerId)
                && positionByPlayer[
                    selection.ReplacementGoalkeeperPlayerId]
                    == "goalkeeper"
                && selection.OutfieldSubstitutePlayerIds.All(
                    playerId => positionByPlayer[playerId] != "goalkeeper")
                && goalkeeperCount == 1
                && defenderCount >= 3
                && midfielderCount >= 2
                && forwardCount >= 1,
            "selection-role",
            $"{field}.selection");
        Require(
            result.TotalPointRows.Count == scenarioCount
                && result.CaptainBonusPointRows.Count == scenarioCount
                && result.TotalPointRows.All(value => value is >= -200 and <= 2000)
                && result.CaptainBonusPointRows.All(
                    value => value is >= -100 and <= 200),
            "row-count",
            $"{field}.totalPointRows");
        ValidateSummary(result.TotalPointRows, result.Summary, scenarioCount, field);
    }

    private static void ValidateSummary(
        IReadOnlyList<int> rows,
        SelectionScenarioSummaryDocument summary,
        int scenarioCount,
        string field)
    {
        int[] sorted = [.. rows.Order()];
        decimal mean = rows.Sum(value => (decimal)value) / scenarioCount;
        decimal variance = rows.Sum(
            value =>
            {
                decimal difference = value - mean;
                return difference * difference;
            }) / scenarioCount;
        decimal standardDeviation = (decimal)Math.Sqrt((double)variance);
        Require(
            summary.SchemaVersion == "1.0"
                && summary.EngineVersion == EngineVersion
                && summary.ScenarioCount == scenarioCount
                && Close(summary.MeanPoints, mean)
                && Close(
                    summary.StandardDeviationPoints,
                    standardDeviation)
                && summary.MinimumPoints == sorted[0]
                && Close(summary.P10Points, Quantile(sorted, 0.10m))
                && Close(summary.MedianPoints, Quantile(sorted, 0.50m))
                && Close(summary.P90Points, Quantile(sorted, 0.90m))
                && summary.MaximumPoints == sorted[^1]
                && summary.MeanActivatedSubstitutes is >= 0m and <= 4m
                && summary.ProbabilityOfUnreplacedStarter is >= 0m and <= 1m,
            "summary",
            $"{field}.summary");
    }

    internal static void ValidateComparison(
        IReadOnlyList<int> model,
        IReadOnlyList<int> user,
        SelectionScenarioComparisonDocument comparison,
        int scenarioCount,
        string field = "userVsModel")
    {
        int[] differences = model
            .Zip(user, (reference, candidate) => candidate - reference)
            .ToArray();
        int[] sorted = [.. differences.Order()];
        decimal mean =
            differences.Sum(value => (decimal)value) / scenarioCount;
        decimal wins =
            differences.Count(value => value > 0) / (decimal)scenarioCount;
        decimal ties =
            differences.Count(value => value == 0) / (decimal)scenarioCount;
        decimal losses =
            differences.Count(value => value < 0) / (decimal)scenarioCount;
        Require(
            comparison.SchemaVersion == "1.0"
                && comparison.EngineVersion == EngineVersion
                && comparison.ScenarioCount == scenarioCount
                && Close(comparison.MeanPointsDelta, mean)
                && Close(
                    comparison.P10PointsDelta,
                    Quantile(sorted, 0.10m))
                && Close(
                    comparison.MedianPointsDelta,
                    Quantile(sorted, 0.50m))
                && Close(
                    comparison.P90PointsDelta,
                    Quantile(sorted, 0.90m))
                && Close(comparison.ProbabilityCandidateWins, wins)
                && Close(comparison.ProbabilityTie, ties)
                && Close(comparison.ProbabilityCandidateLoses, losses),
            "comparison",
            field);
    }

    private static void ValidateLineage(
        SelectionScenarioScoreShadowDocument document,
        ScenarioSource scenario,
        ForecastSource forecast,
        SelectionSource? selection)
    {
        Require(
            scenario.OfficialCaptureId == document.OfficialCaptureId
                && scenario.SeasonCode == document.SeasonCode
                && scenario.Gameweek == document.Gameweek
                && scenario.DecisionCutoffUtc
                    == document.DecisionCutoffUtc
                && scenario.ScenarioCount == document.ScenarioCount
                && scenario.ContentSha256
                    == document
                        .ScenarioSource
                        .ScenarioArtifactContentSha256
                && scenario.ScenarioContentSha256
                    == document.ScenarioSource.ScenarioContentSha256
                && scenario.RunIdentitySha256
                    == document
                        .ScenarioSource
                        .ScenarioRunIdentitySha256,
            "scenario-lineage",
            "scenarioSource");
        Require(
            forecast.CaptureId == document.OfficialCaptureId
                && forecast.ContentSha256
                    == document
                        .ModelSource
                        .ForecastArtifactContentSha256
                && forecast.ModelLabel == document.ModelSource.ModelLabel,
            "forecast-lineage",
            "modelSource");
        ValidateSelectionAgainstSource(
            document.Model.Selection,
            forecast.Selection,
            "model.selection");
        if (selection is null)
        {
            Require(
                document.UserSource.Status == "missing",
                "selection-lineage",
                "userSource");
            return;
        }
        Require(
            document.UserSource.Status == "available"
                && document.UserSource.SelectionRevisionId
                    == selection.RevisionId
                && document.UserSource.Revision == selection.Revision
                && document.UserSource.SelectionContentSha256
                    == selection.ContentSha256
                && document.UserSource.LockedAtUtc == selection.LockedAtUtc
                && document.User is not null,
            "selection-lineage",
            "userSource");
        ValidateSelectionAgainstSource(
            document.User!.Selection,
            selection.Selection,
            "user.selection");
    }

    private static void ValidateSelectionAgainstSource(
        SelectionScenarioDefinitionDocument actual,
        LockedSelectionDocument expected,
        string field)
    {
        Require(
            actual.StartingPlayerIds.SequenceEqual(
                expected.StartingPlayerIds)
                && actual.CaptainPlayerId == expected.CaptainPlayerId
                && actual.ViceCaptainPlayerId
                    == expected.ViceCaptainPlayerId
                && actual.ReplacementGoalkeeperPlayerId
                    == expected.ReplacementGoalkeeperPlayerId
                && actual.OutfieldSubstitutePlayerIds.SequenceEqual(
                    expected.OutfieldSubstitutePlayerIds),
            "selection-definition",
            field);
    }

    private static async Task<ScenarioSource> ReadScenarioAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long artifactId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT official_capture_id, season_code, gameweek,
                   decision_cutoff_utc, scenario_count, document_json,
                   content_sha256
            FROM joint_scenario_shadow_artifacts
            WHERE scenario_artifact_id = $artifactId;
            """;
        command.Parameters.AddWithValue("$artifactId", artifactId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new SelectionScenarioScoreValidationException(
                "scenario-not-found",
                "scenarioSource.scenarioArtifactId");
        }
        string documentJson = reader.GetString(5);
        string contentSha256 = reader.GetString(6);
        Require(
            Sha256(documentJson) == contentSha256,
            "scenario-content",
            "scenarioSource");
        using JsonDocument source = JsonDocument.Parse(documentJson);
        JsonElement root = source.RootElement;
        return new(
            artifactId,
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt32(2),
            DateTimeOffset.Parse(reader.GetString(3)),
            reader.GetInt32(4),
            root.GetProperty("scenarioContentSha256").GetString()!,
            root.GetProperty("runIdentitySha256").GetString()!,
            contentSha256);
    }

    private static async Task<ForecastSource> ReadForecastAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long artifactId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT capture_id, document_json, content_sha256
            FROM baseline_forecast_artifacts
            WHERE artifact_id = $artifactId;
            """;
        command.Parameters.AddWithValue("$artifactId", artifactId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new SelectionScenarioScoreValidationException(
                "forecast-not-found",
                "modelSource.forecastArtifactId");
        }
        string documentJson = reader.GetString(1);
        string contentSha256 = reader.GetString(2);
        Require(
            Sha256(documentJson) == contentSha256,
            "forecast-content",
            "modelSource");
        using JsonDocument source = JsonDocument.Parse(documentJson);
        JsonElement root = source.RootElement;
        return new(
            artifactId,
            reader.GetInt64(0),
            root.GetProperty("modelLabel").GetString()!,
            ReadForecastSelection(root),
            contentSha256);
    }

    private static LockedSelectionDocument ReadForecastSelection(
        JsonElement root)
    {
        JsonElement.ArrayEnumerator players =
            root.GetProperty("selection").GetProperty("players").EnumerateArray();
        var starting = new List<int>();
        int replacementGoalkeeper = 0;
        var outfield = new SortedDictionary<int, int>();
        int captain = 0;
        int viceCaptain = 0;
        foreach (JsonElement player in players)
        {
            int playerId = player.GetProperty("playerId").GetInt32();
            string position = player.GetProperty("position").GetString()!;
            if (player.GetProperty("lineupPlace").GetString() == "starting")
            {
                starting.Add(playerId);
            }
            else if (position == "goalkeeper")
            {
                replacementGoalkeeper = playerId;
            }
            else
            {
                outfield.Add(
                    player.GetProperty("benchOrder").GetInt32(),
                    playerId);
            }
            if (player.TryGetProperty("captaincy", out JsonElement captaincy)
                && captaincy.ValueKind == JsonValueKind.String)
            {
                if (captaincy.GetString() == "captain")
                {
                    captain = playerId;
                }
                else if (captaincy.GetString() == "vice-captain")
                {
                    viceCaptain = playerId;
                }
            }
        }
        return new(
            starting,
            captain,
            viceCaptain,
            replacementGoalkeeper,
            [.. outfield.Values]);
    }

    private static async Task<SelectionSource?> ReadLatestSelectionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long forecastArtifactId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT selection_revision_id, revision, selection_json,
                   selection_content_sha256, locked_at_utc
            FROM selection_revisions
            WHERE forecast_artifact_id = $forecastArtifactId
            ORDER BY revision DESC, selection_revision_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$forecastArtifactId",
            forecastArtifactId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        string selectionJson = reader.GetString(2);
        string contentSha256 = reader.GetString(3);
        Require(
            Sha256(selectionJson) == contentSha256,
            "selection-content",
            "userSource");
        LockedSelectionDocument selection =
            JsonSerializer.Deserialize<LockedSelectionDocument>(
                selectionJson,
                JsonOptions)
            ?? throw new JsonException(
                "The stored selection document cannot be null.");
        return new(
            reader.GetInt64(0),
            reader.GetInt32(1),
            selection,
            contentSha256,
            reader.IsDBNull(4)
                ? null
                : DateTimeOffset.Parse(reader.GetString(4)));
    }

    private static async Task<StoredArtifact?> ReadExactAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long scenarioArtifactId,
        long forecastArtifactId,
        string userSelectionKey,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT score_artifact_id, document_json, content_sha256
            FROM selection_scenario_score_shadow_artifacts
            WHERE scenario_artifact_id = $scenarioArtifactId
              AND forecast_artifact_id = $forecastArtifactId
              AND user_selection_key = $userSelectionKey;
            """;
        command.Parameters.AddWithValue(
            "$scenarioArtifactId",
            scenarioArtifactId);
        command.Parameters.AddWithValue(
            "$forecastArtifactId",
            forecastArtifactId);
        command.Parameters.AddWithValue(
            "$userSelectionKey",
            userSelectionKey);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2))
            : null;
    }

    private static SelectionScenarioScoreShadowDocument Materialize(
        StoredArtifact stored)
    {
        if (!StringComparer.Ordinal.Equals(
            Sha256(stored.DocumentJson),
            stored.ContentSha256))
        {
            throw new InvalidDataException(
                "The persisted selection scenario score content hash is invalid.");
        }
        SelectionScenarioScoreShadowDocument document =
            JsonSerializer.Deserialize<SelectionScenarioScoreShadowDocument>(
                stored.DocumentJson,
                JsonOptions)
            ?? throw new JsonException(
                "The selection scenario score document cannot be null.");
        return document with
        {
            ScoreArtifactId = stored.ArtifactId,
            ScoreArtifactContentSha256 = stored.ContentSha256,
        };
    }

    private static decimal Quantile(int[] sorted, decimal probability)
    {
        decimal index = (sorted.Length - 1) * probability;
        int lower = (int)decimal.Floor(index);
        int upper = (int)decimal.Ceiling(index);
        if (lower == upper)
        {
            return sorted[lower];
        }
        decimal fraction = index - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static bool Close(decimal actual, decimal expected) =>
        Math.Abs(actual - expected) <= NumericTolerance;

    internal static string SelectionContentSha256(
        SelectionScenarioDefinitionDocument selection)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber(
                "captainPlayerId",
                selection.CaptainPlayerId);
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
                document
                    .ScenarioSource
                    .ScenarioArtifactContentSha256);
            writer.WriteNumber(
                "scenarioArtifactId",
                document.ScenarioSource.ScenarioArtifactId);
            writer.WriteString(
                "scenarioContentSha256",
                document.ScenarioSource.ScenarioContentSha256);
            if (document.UserSource.SelectionContentSha256 is null)
            {
                writer.WriteNull("userSelectionContentSha256");
            }
            else
            {
                writer.WriteString(
                    "userSelectionContentSha256",
                    document.UserSource.SelectionContentSha256);
            }
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static string RunIdentitySha256(
        SelectionScenarioScoreShadowDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("artifactVersion", ArtifactVersion);
            writer.WriteString(
                "dataIdentitySha256",
                document.DataIdentitySha256);
            writer.WriteString("engineVersion", EngineVersion);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(
            character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static void Require(bool condition, string code, string field)
    {
        if (!condition)
        {
            throw new SelectionScenarioScoreValidationException(code, field);
        }
    }

    private sealed record ScenarioSource(
        long ArtifactId,
        long OfficialCaptureId,
        string SeasonCode,
        int Gameweek,
        DateTimeOffset DecisionCutoffUtc,
        int ScenarioCount,
        string ScenarioContentSha256,
        string RunIdentitySha256,
        string ContentSha256);

    private sealed record ForecastSource(
        long ArtifactId,
        long CaptureId,
        string ModelLabel,
        LockedSelectionDocument Selection,
        string ContentSha256);

    private sealed record SelectionSource(
        long RevisionId,
        int Revision,
        LockedSelectionDocument Selection,
        string ContentSha256,
        DateTimeOffset? LockedAtUtc);

    private sealed record StoredArtifact(
        long ArtifactId,
        string DocumentJson,
        string ContentSha256);
}

public sealed class SelectionScenarioScoreShadowImporter
{
    public const int MaximumInputBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly SelectionScenarioScoreShadowStore _store;

    public SelectionScenarioScoreShadowImporter(
        SelectionScenarioScoreShadowStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<SelectionScenarioScoreShadowDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The selection scenario score file does not exist.",
                fullPath);
        }
        if (file.Length is <= 0 or > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Selection scenario score files must contain 1 to "
                + $"{MaximumInputBytes} bytes.");
        }
        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        SelectionScenarioScoreShadowDocument document =
            await JsonSerializer.DeserializeAsync<
                SelectionScenarioScoreShadowDocument>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new JsonException(
                "The selection scenario score document cannot be null.");
        return await _store.ImportAsync(document, cancellationToken);
    }
}

public sealed class SelectionScenarioScoreValidationException : Exception
{
    public SelectionScenarioScoreValidationException(
        string code,
        string field)
        : base($"Selection scenario score validation failed: {code}.")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
