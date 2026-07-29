using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Selections;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Selections;

public sealed class SelectionRoleStrategyShadowStore
{
    public const string ArtifactType =
        "current-selection-role-strategy-shadow";
    public const string ArtifactVersion =
        "current-selection-role-strategies-v1";
    public const string Status = "prospective-shadow-unscored";
    public const string SearchVersion = "deterministic-role-beam-v1";
    public const string EngineVersion = "cpu-joint-scenario-reference-v1";

    private const decimal NumericTolerance = 0.000000001m;
    private const decimal TailFraction = 0.20m;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public SelectionRoleStrategyShadowStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<SelectionRoleStrategyShadowDocument> ImportAsync(
        SelectionRoleStrategyShadowDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);

        await using var connection =
            new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(
                cancellationToken);
        ScoreSource score = await ReadScoreAsync(
            connection,
            transaction,
            document.SelectionScoreSource.RunIdentitySha256,
            cancellationToken);
        ValidateLineage(document, score);

        string documentJson = JsonSerializer.Serialize(document, JsonOptions);
        string contentSha256 = Sha256(documentJson);
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO selection_role_strategy_shadow_artifacts (
                    schema_version, artifact_type, artifact_version, status,
                    score_artifact_id, search_version, season_code, gameweek,
                    decision_cutoff_utc, scenario_count,
                    producer_run_identity_sha256, document_json,
                    content_sha256, created_at_utc
                )
                VALUES (
                    $schemaVersion, $artifactType, $artifactVersion, $status,
                    $scoreArtifactId, $searchVersion, $seasonCode, $gameweek,
                    $decisionCutoffUtc, $scenarioCount, $runIdentity,
                    $documentJson, $contentSha256, $createdAtUtc
                )
                ON CONFLICT (score_artifact_id, search_version) DO NOTHING;
                """;
            insert.Parameters.AddWithValue(
                "$schemaVersion",
                document.SchemaVersion);
            insert.Parameters.AddWithValue("$artifactType", document.ArtifactType);
            insert.Parameters.AddWithValue(
                "$artifactVersion",
                document.ArtifactVersion);
            insert.Parameters.AddWithValue("$status", document.Status);
            insert.Parameters.AddWithValue("$scoreArtifactId", score.ArtifactId);
            insert.Parameters.AddWithValue(
                "$searchVersion",
                document.Search.SearchVersion);
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
            score.ArtifactId,
            document.Search.SearchVersion,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The selection role strategy shadow was not persisted.");
        if (!StringComparer.Ordinal.Equals(
            stored.ContentSha256,
            contentSha256))
        {
            throw new SelectionRoleStrategyValidationException(
                "source-conflict",
                "dataIdentitySha256");
        }
        await transaction.CommitAsync(cancellationToken);
        return Materialize(stored);
    }

    public async Task<SelectionRoleStrategyShadowDocument?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqliteConnection(_options.ConnectionString);
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
            ),
            current_score AS (
                SELECT score_artifact_id
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
                LIMIT 1
            )
            SELECT strategy_artifact_id, document_json, content_sha256
            FROM selection_role_strategy_shadow_artifacts
            WHERE score_artifact_id = (
                SELECT score_artifact_id FROM current_score
            )
              AND search_version = $searchVersion
            ORDER BY strategy_artifact_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$searchVersion", SearchVersion);
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
        SelectionRoleStrategyShadowDocument document)
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
            document.StrategyArtifactId is null
                && document.StrategyArtifactContentSha256 is null,
            "must-be-null",
            "strategyArtifactId");
        Require(
            document.SeasonCode == "2026-27"
                && document.Gameweek == 1
                && document.OfficialCaptureId > 0
                && document.DecisionCutoffUtc < document.DeadlineUtc
                && document.ScenarioCount is >= 1 and <= 512,
            "target",
            "officialCaptureId");
        Require(
            IsSha256(document.SelectionScoreSource.DataIdentitySha256)
                && IsSha256(
                    document.SelectionScoreSource.RunIdentitySha256),
            "sha256",
            "selectionScoreSource");
        Require(
            document.FixedSquadPlayerIds.Count == 15
                && document.FixedSquadPlayerIds.Distinct().Count() == 15
                && document.FixedSquadPlayerIds.All(value => value > 0),
            "fixed-squad",
            "fixedSquadPlayerIds");
        ValidateResult(document.Model, document.ScenarioCount, "model");
        Require(
            document.FixedSquadPlayerIds.SequenceEqual(
                document.Model.Selection.PlayerIds),
            "fixed-squad",
            "model.selection.playerIds");
        ValidateStrategy(
            document.Strategies.Balanced,
            "balanced",
            "mean",
            document);
        ValidateStrategy(
            document.Strategies.Safer,
            "safer",
            "lower-tail-mean",
            document);
        ValidateStrategy(
            document.Strategies.HigherCeiling,
            "higherCeiling",
            "upper-tail-mean",
            document);
        Require(
            document.Search.SearchVersion == SearchVersion
                && document.Search.BeamWidth == 12
                && document.Search.Iterations == 3
                && document.Search.TailFraction == TailFraction
                && document.Search.UniqueCandidatesEvaluated >= 1
                && document.Search.NewCandidatesByStrategy.Count == 3
                && document.Search.NewCandidatesByStrategy.Keys.ToHashSet()
                    .SetEquals(["balanced", "safer", "higherCeiling"])
                && document.Search.NewCandidatesByStrategy.Values.All(
                    value => value >= 0)
                && document.Search.SearchStatus
                    == "bounded-heuristic-not-global-optimum",
            "search",
            "search");
        Require(
            document.Limitations.Count is >= 1 and <= 32
                && document.Limitations.All(
                    value => !string.IsNullOrWhiteSpace(value)
                        && value.Length <= 1000),
            "boundary",
            "limitations");
        Require(
            IsSha256(document.DataIdentitySha256)
                && IsSha256(document.RunIdentitySha256)
                && document.DataIdentitySha256 == DataIdentitySha256(document)
                && document.RunIdentitySha256 == RunIdentitySha256(document),
            "identity-hash",
            "dataIdentitySha256");
    }

    private static void ValidateStrategy(
        SelectionRoleStrategyDocument strategy,
        string strategyId,
        string objectiveKey,
        SelectionRoleStrategyShadowDocument document)
    {
        ValidateResult(
            strategy.Result,
            document.ScenarioCount,
            $"strategies.{strategyId}.result");
        Require(
            strategy.StrategyId == strategyId
                && strategy.Objective.ObjectiveKey == objectiveKey
                && strategy.Objective.TailFraction
                    == (objectiveKey == "mean" ? null : TailFraction)
                && strategy.IsDistinctFromModel
                    == !SelectionKey(strategy.Result.Selection).SequenceEqual(
                        SelectionKey(document.Model.Selection))
                && strategy.Result.Selection.PlayerIds.ToHashSet().SetEquals(
                    document.FixedSquadPlayerIds),
            "strategy",
            $"strategies.{strategyId}");
        decimal objective = ObjectiveValue(
            strategy.Result.TotalPointRows,
            objectiveKey);
        Require(
            Close(strategy.Objective.ObjectiveValue, objective),
            "objective",
            $"strategies.{strategyId}.objective");
        ValidateComparison(
            document.Model.TotalPointRows,
            strategy.Result.TotalPointRows,
            strategy.VsModel,
            document.ScenarioCount,
            $"strategies.{strategyId}.vsModel");
    }

    private static void ValidateResult(
        SelectionScenarioResultDocument result,
        int scenarioCount,
        string field)
    {
        SelectionScenarioDefinitionDocument selection = result.Selection;
        Require(
            selection.PlayerIds.Count == 15
                && selection.Positions.Count == 15
                && selection.PlayerIds.Distinct().Count() == 15
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
        Require(
            selection.Positions.All(
                value => value is "goalkeeper"
                    or "defender"
                    or "midfielder"
                    or "forward")
                && starting.IsSubsetOf(players)
                && substitutes.Count == 4
                && substitutes.IsSubsetOf(players)
                && !starting.Overlaps(substitutes)
                && selection.CaptainPlayerId
                    != selection.ViceCaptainPlayerId
                && starting.Contains(selection.CaptainPlayerId)
                && starting.Contains(selection.ViceCaptainPlayerId)
                && positionByPlayer[
                    selection.ReplacementGoalkeeperPlayerId] == "goalkeeper"
                && selection.OutfieldSubstitutePlayerIds.All(
                    playerId => positionByPlayer[playerId] != "goalkeeper")
                && selection.StartingPlayerIds.Count(
                    playerId => positionByPlayer[playerId] == "goalkeeper") == 1
                && selection.StartingPlayerIds.Count(
                    playerId => positionByPlayer[playerId] == "defender") >= 3
                && selection.StartingPlayerIds.Count(
                    playerId => positionByPlayer[playerId] == "midfielder") >= 2
                && selection.StartingPlayerIds.Count(
                    playerId => positionByPlayer[playerId] == "forward") >= 1,
            "selection-role",
            $"{field}.selection");
        Require(
            result.TotalPointRows.Count == scenarioCount
                && result.CaptainBonusPointRows.Count == scenarioCount
                && result.TotalPointRows.All(
                    value => value is >= -200 and <= 2000)
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
        Require(
            summary.SchemaVersion == "1.0"
                && summary.EngineVersion == EngineVersion
                && summary.ScenarioCount == scenarioCount
                && Close(summary.MeanPoints, mean)
                && Close(
                    summary.StandardDeviationPoints,
                    (decimal)Math.Sqrt((double)variance))
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

    private static void ValidateComparison(
        IReadOnlyList<int> model,
        IReadOnlyList<int> candidate,
        SelectionScenarioComparisonDocument comparison,
        int scenarioCount,
        string field)
    {
        int[] differences = model
            .Zip(candidate, (reference, value) => value - reference)
            .ToArray();
        int[] sorted = [.. differences.Order()];
        Require(
            comparison.SchemaVersion == "1.0"
                && comparison.EngineVersion == EngineVersion
                && comparison.ScenarioCount == scenarioCount
                && Close(
                    comparison.MeanPointsDelta,
                    differences.Sum(value => (decimal)value) / scenarioCount)
                && Close(comparison.P10PointsDelta, Quantile(sorted, 0.10m))
                && Close(
                    comparison.MedianPointsDelta,
                    Quantile(sorted, 0.50m))
                && Close(comparison.P90PointsDelta, Quantile(sorted, 0.90m))
                && Close(
                    comparison.ProbabilityCandidateWins,
                    differences.Count(value => value > 0)
                        / (decimal)scenarioCount)
                && Close(
                    comparison.ProbabilityTie,
                    differences.Count(value => value == 0)
                        / (decimal)scenarioCount)
                && Close(
                    comparison.ProbabilityCandidateLoses,
                    differences.Count(value => value < 0)
                        / (decimal)scenarioCount),
            "comparison",
            field);
    }

    private static void ValidateLineage(
        SelectionRoleStrategyShadowDocument document,
        ScoreSource source)
    {
        SelectionScenarioScoreShadowDocument score = source.Document;
        Require(
            source.ContentSha256 == Sha256(source.DocumentJson)
                && score.DataIdentitySha256
                    == document.SelectionScoreSource.DataIdentitySha256
                && score.RunIdentitySha256
                    == document.SelectionScoreSource.RunIdentitySha256
                && score.OfficialCaptureId == document.OfficialCaptureId
                && score.SeasonCode == document.SeasonCode
                && score.Gameweek == document.Gameweek
                && score.DeadlineUtc == document.DeadlineUtc
                && score.DecisionCutoffUtc == document.DecisionCutoffUtc
                && score.ScenarioCount == document.ScenarioCount
                && score.ScenarioSource
                    == document.SelectionScoreSource.ScenarioSource
                && score.ModelSource
                    == document.SelectionScoreSource.ModelSource
                && JsonSerializer.Serialize(score.Model, JsonOptions)
                    == JsonSerializer.Serialize(document.Model, JsonOptions),
            "score-lineage",
            "selectionScoreSource");
    }

    private static async Task<ScoreSource> ReadScoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runIdentitySha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT score_artifact_id, document_json, content_sha256
            FROM selection_scenario_score_shadow_artifacts
            WHERE producer_run_identity_sha256 = $runIdentity
            ORDER BY score_artifact_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runIdentity", runIdentitySha256);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new SelectionRoleStrategyValidationException(
                "score-not-found",
                "selectionScoreSource.runIdentitySha256");
        }
        string documentJson = reader.GetString(1);
        return new(
            reader.GetInt64(0),
            documentJson,
            reader.GetString(2),
            JsonSerializer.Deserialize<SelectionScenarioScoreShadowDocument>(
                documentJson,
                JsonOptions)
                ?? throw new JsonException(
                    "The persisted scenario score cannot be null."));
    }

    private static async Task<StoredArtifact?> ReadExactAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long scoreArtifactId,
        string searchVersion,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT strategy_artifact_id, document_json, content_sha256
            FROM selection_role_strategy_shadow_artifacts
            WHERE score_artifact_id = $scoreArtifactId
              AND search_version = $searchVersion;
            """;
        command.Parameters.AddWithValue("$scoreArtifactId", scoreArtifactId);
        command.Parameters.AddWithValue("$searchVersion", searchVersion);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static SelectionRoleStrategyShadowDocument Materialize(
        StoredArtifact stored)
    {
        if (Sha256(stored.DocumentJson) != stored.ContentSha256)
        {
            throw new InvalidDataException(
                "The persisted selection role strategy hash is invalid.");
        }
        SelectionRoleStrategyShadowDocument document =
            JsonSerializer.Deserialize<SelectionRoleStrategyShadowDocument>(
                stored.DocumentJson,
                JsonOptions)
            ?? throw new JsonException(
                "The selection role strategy document cannot be null.");
        return document with
        {
            StrategyArtifactId = stored.ArtifactId,
            StrategyArtifactContentSha256 = stored.ContentSha256,
        };
    }

    private static decimal ObjectiveValue(
        IReadOnlyList<int> rows,
        string objectiveKey)
    {
        int[] sorted = [.. rows.Order()];
        int tailCount = Math.Max(
            1,
            (int)Math.Ceiling(sorted.Length * (double)TailFraction));
        return objectiveKey switch
        {
            "mean" => rows.Sum(value => (decimal)value) / rows.Count,
            "lower-tail-mean" =>
                sorted.Take(tailCount).Sum(value => (decimal)value) / tailCount,
            "upper-tail-mean" =>
                sorted.TakeLast(tailCount).Sum(value => (decimal)value)
                    / tailCount,
            _ => throw new SelectionRoleStrategyValidationException(
                "objective-key",
                "objective.objectiveKey"),
        };
    }

    private static IEnumerable<object> SelectionKey(
        SelectionScenarioDefinitionDocument selection) =>
        selection.StartingPlayerIds.Cast<object>()
            .Append(selection.ReplacementGoalkeeperPlayerId)
            .Concat(selection.OutfieldSubstitutePlayerIds.Cast<object>())
            .Append(selection.CaptainPlayerId)
            .Append(selection.ViceCaptainPlayerId);

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

    private static string DataIdentitySha256(
        SelectionRoleStrategyShadowDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("beamWidth", 12);
            writer.WriteNumber("searchIterations", 3);
            writer.WriteString("searchVersion", SearchVersion);
            writer.WriteString(
                "selectionScoreRunIdentitySha256",
                document.SelectionScoreSource.RunIdentitySha256);
            writer.WriteNumber("tailFraction", 0.2);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static string RunIdentitySha256(
        SelectionRoleStrategyShadowDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("artifactVersion", ArtifactVersion);
            writer.WriteString(
                "dataIdentitySha256",
                document.DataIdentitySha256);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static bool Close(decimal actual, decimal expected) =>
        Math.Abs(actual - expected) <= NumericTolerance;

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
            throw new SelectionRoleStrategyValidationException(code, field);
        }
    }

    private sealed record ScoreSource(
        long ArtifactId,
        string DocumentJson,
        string ContentSha256,
        SelectionScenarioScoreShadowDocument Document);

    private sealed record StoredArtifact(
        long ArtifactId,
        string DocumentJson,
        string ContentSha256);
}

public sealed class SelectionRoleStrategyShadowImporter
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

    private readonly SelectionRoleStrategyShadowStore _store;

    public SelectionRoleStrategyShadowImporter(
        SelectionRoleStrategyShadowStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<SelectionRoleStrategyShadowDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The selection role strategy file does not exist.",
                fullPath);
        }
        if (file.Length is <= 0 or > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Selection role strategy files must contain 1 to "
                + $"{MaximumInputBytes} bytes.");
        }
        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        SelectionRoleStrategyShadowDocument document =
            await JsonSerializer.DeserializeAsync<
                SelectionRoleStrategyShadowDocument>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new JsonException(
                "The selection role strategy document cannot be null.");
        return await _store.ImportAsync(document, cancellationToken);
    }
}

public sealed class SelectionRoleStrategyValidationException : Exception
{
    public SelectionRoleStrategyValidationException(
        string code,
        string field)
        : base($"Selection role strategy validation failed: {code}.")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
