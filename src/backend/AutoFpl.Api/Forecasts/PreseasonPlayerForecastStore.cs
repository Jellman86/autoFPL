using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Forecasts;

public sealed class PreseasonPlayerForecastStore
{
    public const string ArtifactType =
        "historical-preseason-player-gameweek-forecast";
    public const string Status = "provisional-preseason-challenger";
    public const string ModelKey = "historical-preseason-histogram-tree-v1";
    public const string BaselineModelKey =
        "official-market-baseline-v0-player-table";
    public const string EvaluationDataIdentity =
        "628bd4aae195dc26cfaaba1d692d2e91cb486a0c8f8ad8f737eb7ceb42a52503";
    public const string EvaluationRunIdentity =
        "5ed600cf5ad5ebf830d814d615a1bd6648ba74db25d8939831bdb40116f2ca2b";
    public const string SourceRevision =
        "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a";
    public const string PlayersSha256 =
        "412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b";
    public const string GameweeksSha256 =
        "0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public PreseasonPlayerForecastStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<PreseasonPlayerForecastDocument> ImportAsync(
        PreseasonPlayerForecastDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateFixedIdentity(document);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        TargetCapture target = await ReadTargetAsync(
            connection,
            transaction,
            document.OfficialCaptureId,
            cancellationToken);
        ValidateTarget(document, target);
        HistoricalCapture historical = await ReadHistoricalAsync(
            connection,
            transaction,
            document.Training.HistoricalCaptureId,
            cancellationToken);
        ValidateHistorical(document, historical);

        IReadOnlyDictionary<int, CurrentPlayer> currentPlayers =
            await ReadCurrentPlayersAsync(
                connection,
                transaction,
                document.OfficialCaptureId,
                cancellationToken);
        IReadOnlyDictionary<int, decimal> baseline = await ReadBaselineAsync(
            connection,
            transaction,
            document.OfficialCaptureId,
            cancellationToken);
        IReadOnlyDictionary<int, int> priorHistory =
            await ReadPriorHistoryAsync(
                connection,
                transaction,
                document.Training.HistoricalCaptureId,
                cancellationToken);
        ValidatePlayers(
            document,
            target,
            currentPlayers,
            baseline,
            priorHistory);

        string documentJson = JsonSerializer.Serialize(document, JsonOptions);
        string contentSha256 = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(documentJson)));
        await InsertAsync(
            connection,
            transaction,
            document,
            documentJson,
            contentSha256,
            cancellationToken);
        StoredArtifact artifact = await ReadForCaptureAsync(
            connection,
            transaction,
            document.OfficialCaptureId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The preseason player forecast artifact was not persisted.");
        if (!StringComparer.Ordinal.Equals(
            artifact.ContentSha256,
            contentSha256))
        {
            throw new PreseasonPlayerForecastValidationException(
                "capture-conflict",
                "officialCaptureId");
        }

        await transaction.CommitAsync(cancellationToken);
        return Materialize(artifact);
    }

    public async Task<PreseasonPlayerForecastDocument?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                forecast_artifact_id,
                document_json,
                content_sha256
            FROM preseason_player_forecast_artifacts
            ORDER BY
                julianday(decision_cutoff_utc) DESC,
                forecast_artifact_id DESC
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

    private static void ValidateFixedIdentity(
        PreseasonPlayerForecastDocument document)
    {
        Require(document.SchemaVersion == "1.0", "identity", "schemaVersion");
        Require(document.ArtifactType == ArtifactType, "identity", "artifactType");
        Require(document.Status == Status, "identity", "status");
        Require(document.ModelKey == ModelKey, "identity", "modelKey");
        Require(!document.IsPromoted, "must-be-false", "isPromoted");
        Require(!document.InfluencesAdvice, "must-be-false", "influencesAdvice");
        Require(document.SeasonCode == "2026-27", "identity", "seasonCode");
        Require(document.Gameweek == 1, "identity", "gameweek");
        Require(
            document.DistributionStatus
                == "point-mean-only-no-calibrated-distribution",
            "identity",
            "distributionStatus");
        Require(document.Training.SeasonCode == "2025-26", "identity", "training");
        Require(
            document.Training.SourceRevision == SourceRevision
                && document.Training.PlayersSha256 == PlayersSha256
                && document.Training.GameweeksSha256 == GameweeksSha256,
            "archive-not-evaluated",
            "training");
        Require(
            document.Training.SelectedModel
                == "historical-preseason-histogram-tree",
            "identity",
            "training.selectedModel");
        Require(
            document.Training.EvaluationDataIdentitySha256
                == EvaluationDataIdentity
                && document.Training.EvaluationRunIdentitySha256
                    == EvaluationRunIdentity,
            "evaluation-not-supported",
            "training");
        Require(
            document.Training.ModelConfiguration.ValueKind
                == JsonValueKind.Object
                && document.Training.ModelDiagnostics.ValueKind
                    == JsonValueKind.Object,
            "must-be-object",
            "training");
        ValidateModelIdentity(document.Training);
        Require(
            document.Comparison.BaselineModelKey == BaselineModelKey
                && document.Comparison.SelectionMetric
                    == "locked-holdout-mae"
                && document.Comparison.BaselineStillDrivesAdvice
                && document.Comparison.LockedHoldoutMaeImprovementFraction
                    == 0.088702m,
            "identity",
            "comparison");
        Require(
            IsSha256(document.DataIdentitySha256)
                && IsSha256(document.RunIdentitySha256),
            "must-be-sha256",
            "runIdentitySha256");
        Require(
            document.ForecastArtifactId is null
                && document.ForecastArtifactContentSha256 is null,
            "must-be-absent",
            "forecastArtifactId");
        Require(
            document.Limitations.Count is >= 1 and <= 20
                && document.Limitations.All(
                    item => !string.IsNullOrWhiteSpace(item)
                        && item.Length <= 500),
            "invalid-limitations",
            "limitations");
    }

    private static void ValidateTarget(
        PreseasonPlayerForecastDocument document,
        TargetCapture target)
    {
        Require(target.SeasonCode == document.SeasonCode, "target-mismatch", "seasonCode");
        Require(target.Gameweek == document.Gameweek, "target-mismatch", "gameweek");
        Require(
            target.DeadlineUtc == document.DeadlineUtc.ToUniversalTime(),
            "target-mismatch",
            "deadlineUtc");
        Require(
            target.AvailableAtUtc == document.DecisionCutoffUtc.ToUniversalTime(),
            "target-mismatch",
            "decisionCutoffUtc");
        Require(
            target.AvailableAtUtc <= target.DeadlineUtc,
            "post-deadline",
            "decisionCutoffUtc");
    }

    private static void ValidateModelIdentity(
        PreseasonPlayerForecastTrainingDocument training)
    {
        JsonElement configuration = training.ModelConfiguration;
        Require(
            ReadString(configuration, "loss") == "squared_error"
                && ReadDecimal(configuration, "learningRate") == 0.05m
                && ReadInt(configuration, "maximumIterations") == 100
                && ReadInt(configuration, "maximumLeafNodes") == 7
                && ReadInt(configuration, "minimumSamplesPerLeaf") == 20
                && ReadDecimal(configuration, "l2Regularisation") == 10m
                && ReadInt(configuration, "maximumBins") == 63
                && ReadBoolean(configuration, "earlyStopping") == false
                && ReadInt(configuration, "randomSeed") == 20_260_726
                && ReadString(configuration, "missingValues")
                    == "native-learned-branch",
            "model-configuration-mismatch",
            "training.modelConfiguration");
        JsonElement diagnostics = training.ModelDiagnostics;
        Require(
            ReadString(diagnostics, "implementation")
                == "sklearn.ensemble.HistGradientBoostingRegressor"
                && ReadString(diagnostics, "libraryVersion") == "1.9.0"
                && ReadInt(diagnostics, "trainingRows")
                    == training.TrainingRowCount
                && ReadInt(diagnostics, "candidateFeatureCount") == 45
                && ReadInt(diagnostics, "completedIterations") == 100,
            "model-diagnostics-mismatch",
            "training.modelDiagnostics");
    }

    private static void ValidateHistorical(
        PreseasonPlayerForecastDocument document,
        HistoricalCapture historical)
    {
        Require(
            historical.SourceRevision == SourceRevision
                && historical.PlayersSha256 == PlayersSha256
                && historical.GameweeksSha256 == GameweeksSha256,
            "archive-not-evaluated",
            "training");
        Require(
            historical.AvailableAtUtc
                <= document.DecisionCutoffUtc.ToUniversalTime(),
            "archive-after-cutoff",
            "training");
        Require(
            historical.GameweekCount == 38
                && document.Training.TrainingGameweekCount
                    == historical.GameweekCount
                && document.Training.TrainingRowCount
                    == historical.PlayerGameweekSampleCount,
            "training-coverage",
            "training");
    }

    private static void ValidatePlayers(
        PreseasonPlayerForecastDocument document,
        TargetCapture target,
        IReadOnlyDictionary<int, CurrentPlayer> currentPlayers,
        IReadOnlyDictionary<int, decimal> baseline,
        IReadOnlyDictionary<int, int> priorHistory)
    {
        Require(
            document.OfficialPlayerCount == target.PlayerCount
                && document.IneligiblePlayerCount
                    == target.PlayerCount - currentPlayers.Count
                && document.PlayerCount == currentPlayers.Count
                && document.Players.Count == currentPlayers.Count,
            "player-coverage",
            "playerCount");
        Require(
            baseline.Keys.ToHashSet().SetEquals(currentPlayers.Keys),
            "baseline-coverage",
            "players");
        Require(
            document.Players.Select(player => player.PlayerId).Distinct().Count()
                == document.Players.Count
                && document.Players.Select(player => player.PlayerCode)
                    .Distinct()
                    .Count()
                    == document.Players.Count,
            "duplicate-player",
            "players");

        int matched = 0;
        foreach (PreseasonPlayerForecastPlayerDocument player in document.Players)
        {
            Require(
                currentPlayers.TryGetValue(player.PlayerId, out CurrentPlayer? current),
                "unknown-player",
                "players");
            Require(
                current!.PlayerCode == player.PlayerCode
                    && current.WebName == player.WebName
                    && current.Position == player.Position
                    && current.TeamId == player.TeamId
                    && current.TeamName == player.TeamName
                    && current.Status == player.OfficialStatus
                    && current.ChanceNextRound
                        == player.OfficialChanceOfPlayingNextRound,
                "current-identity-mismatch",
                "players");
            Require(
                player.AvailabilityStatus
                    == "authoritative-current-official-not-modelled",
                "identity",
                "players.availabilityStatus");
            bool hasPrior = priorHistory.TryGetValue(
                player.PlayerCode,
                out int priorGameweekCount);
            Require(
                player.PriorSeasonIdentityStatus
                    == (hasPrior
                        ? "stable-code-match"
                        : "no-prior-season-match")
                    && player.PriorSeasonGameweekCount
                        == (hasPrior ? priorGameweekCount : 0),
                "prior-identity-mismatch",
                "players");
            matched += hasPrior ? 1 : 0;
            Require(
                player.ExpectedPoints is >= -20m and <= 100m
                    && player.BaselineV0ExpectedPoints is >= -20m and <= 100m,
                "points-out-of-range",
                "players");
            Require(
                baseline[player.PlayerId]
                    == player.BaselineV0ExpectedPoints,
                "baseline-mismatch",
                "players.baselineV0ExpectedPoints");
            Require(
                Math.Abs(
                    player.ExpectedPoints
                    - player.BaselineV0ExpectedPoints
                    - player.DifferenceFromBaselineV0)
                    <= 0.000001m,
                "difference-mismatch",
                "players.differenceFromBaselineV0");
        }
        Require(
            document.PriorSeasonIdentityMatchCount == matched
                && document.PriorSeasonIdentityMissingCount
                    == document.PlayerCount - matched,
            "prior-coverage",
            "priorSeasonIdentityMatchCount");
    }

    private static async Task<TargetCapture> ReadTargetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                season_code,
                next_gameweek_number,
                next_deadline_utc,
                available_at_utc,
                player_count
            FROM official_fpl_captures
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || reader.IsDBNull(1)
            || reader.IsDBNull(2))
        {
            throw new PreseasonPlayerForecastValidationException(
                "target-not-found",
                "officialCaptureId");
        }
        return new(
            reader.GetString(0),
            reader.GetInt32(1),
            DateTimeOffset.Parse(
                reader.GetString(2),
                System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(
                reader.GetString(3),
                System.Globalization.CultureInfo.InvariantCulture),
            reader.GetInt32(4));
    }

    private static async Task<HistoricalCapture> ReadHistoricalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                source_revision,
                available_at_utc,
                players_sha256,
                gameweeks_sha256,
                (
                    SELECT COUNT(*)
                    FROM (
                        SELECT player_code, gameweek
                        FROM historical_fpl_player_gameweeks
                        WHERE capture_id = capture.capture_id
                        GROUP BY player_code, gameweek
                    )
                ),
                (
                    SELECT COUNT(DISTINCT gameweek)
                    FROM historical_fpl_player_gameweeks
                    WHERE capture_id = capture.capture_id
                )
            FROM historical_fpl_season_captures AS capture
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new PreseasonPlayerForecastValidationException(
                "archive-not-found",
                "training.historicalCaptureId");
        }
        return new(
            reader.GetString(0),
            DateTimeOffset.Parse(
                reader.GetString(1),
                System.Globalization.CultureInfo.InvariantCulture),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5));
    }

    private static async Task<IReadOnlyDictionary<int, CurrentPlayer>>
        ReadCurrentPlayersAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long captureId,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                player.player_id,
                player.code,
                player.web_name,
                player.position,
                player.team_id,
                team.name,
                player.status,
                player.chance_next_round
            FROM official_fpl_players AS player
            INNER JOIN official_fpl_teams AS team
                ON team.capture_id = player.capture_id
               AND team.team_id = player.team_id
            WHERE player.capture_id = $captureId
              AND player.status <> 'u'
            ORDER BY player.player_id;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        var players = new Dictionary<int, CurrentPlayer>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            players.Add(
                reader.GetInt32(0),
                new(
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }
        return players;
    }

    private static async Task<IReadOnlyDictionary<int, decimal>>
        ReadBaselineAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long captureId,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT document_json, content_sha256
            FROM player_gameweek_forecast_artifacts
            WHERE official_capture_id = $captureId
              AND model_key = $modelKey;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$modelKey", BaselineModelKey);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new PreseasonPlayerForecastValidationException(
                "baseline-not-found",
                "comparison.baselineModelKey");
        }
        string json = reader.GetString(0);
        string contentSha256 = reader.GetString(1);
        string actualHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        if (!StringComparer.Ordinal.Equals(actualHash, contentSha256))
        {
            throw new PreseasonPlayerForecastValidationException(
                "baseline-content-hash-invalid",
                "comparison.baselineModelKey");
        }
        PlayerGameweekForecastDocument baseline =
            JsonSerializer.Deserialize<PlayerGameweekForecastDocument>(
                json,
                JsonOptions)
            ?? throw new PreseasonPlayerForecastValidationException(
                "baseline-invalid",
                "comparison.baselineModelKey");
        if (baseline.OfficialCaptureId != captureId
            || baseline.ModelKey != BaselineModelKey)
        {
            throw new PreseasonPlayerForecastValidationException(
                "baseline-identity-mismatch",
                "comparison.baselineModelKey");
        }
        return baseline.Players.ToDictionary(
            player => player.PlayerId,
            player => player.ExpectedPoints);
    }

    private static async Task<IReadOnlyDictionary<int, int>>
        ReadPriorHistoryAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long captureId,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT player_code, COUNT(DISTINCT gameweek)
            FROM historical_fpl_player_gameweeks
            WHERE capture_id = $captureId
            GROUP BY player_code;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        var history = new Dictionary<int, int>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(reader.GetInt32(0), reader.GetInt32(1));
        }
        return history;
    }

    private async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PreseasonPlayerForecastDocument document,
        string documentJson,
        string contentSha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO preseason_player_forecast_artifacts (
                schema_version,
                artifact_type,
                status,
                model_key,
                official_capture_id,
                historical_capture_id,
                season_code,
                gameweek,
                decision_cutoff_utc,
                producer_run_identity_sha256,
                document_json,
                content_sha256,
                created_at_utc
            )
            VALUES (
                $schemaVersion,
                $artifactType,
                $status,
                $modelKey,
                $officialCaptureId,
                $historicalCaptureId,
                $seasonCode,
                $gameweek,
                $decisionCutoffUtc,
                $producerRunIdentitySha256,
                $documentJson,
                $contentSha256,
                $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$schemaVersion", document.SchemaVersion);
        command.Parameters.AddWithValue("$artifactType", document.ArtifactType);
        command.Parameters.AddWithValue("$status", document.Status);
        command.Parameters.AddWithValue("$modelKey", document.ModelKey);
        command.Parameters.AddWithValue(
            "$officialCaptureId",
            document.OfficialCaptureId);
        command.Parameters.AddWithValue(
            "$historicalCaptureId",
            document.Training.HistoricalCaptureId);
        command.Parameters.AddWithValue("$seasonCode", document.SeasonCode);
        command.Parameters.AddWithValue("$gameweek", document.Gameweek);
        command.Parameters.AddWithValue(
            "$decisionCutoffUtc",
            document.DecisionCutoffUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue(
            "$producerRunIdentitySha256",
            document.RunIdentitySha256);
        command.Parameters.AddWithValue("$documentJson", documentJson);
        command.Parameters.AddWithValue("$contentSha256", contentSha256);
        command.Parameters.AddWithValue(
            "$createdAtUtc",
            _timeProvider.GetUtcNow().ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<StoredArtifact?> ReadForCaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT forecast_artifact_id, document_json, content_sha256
            FROM preseason_player_forecast_artifacts
            WHERE official_capture_id = $captureId
              AND model_key = $modelKey;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$modelKey", ModelKey);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static PreseasonPlayerForecastDocument Materialize(
        StoredArtifact artifact)
    {
        string actualHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(artifact.DocumentJson)));
        if (!StringComparer.Ordinal.Equals(
            actualHash,
            artifact.ContentSha256))
        {
            throw new InvalidOperationException(
                "The stored preseason forecast content hash is invalid.");
        }
        PreseasonPlayerForecastDocument document =
            JsonSerializer.Deserialize<PreseasonPlayerForecastDocument>(
                artifact.DocumentJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The stored preseason forecast document is invalid.");
        ValidateFixedIdentity(document);
        return document with
        {
            ForecastArtifactId = artifact.ArtifactId,
            ForecastArtifactContentSha256 = artifact.ContentSha256,
        };
    }

    private static void Require(bool condition, string code, string field)
    {
        if (!condition)
        {
            throw new PreseasonPlayerForecastValidationException(code, field);
        }
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            && value.TryGetInt32(out int result)
            ? result
            : int.MinValue;

    private static decimal ReadDecimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            && value.TryGetDecimal(out decimal result)
            ? result
            : decimal.MinValue;

    private static bool? ReadBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(
            character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private sealed record TargetCapture(
        string SeasonCode,
        int Gameweek,
        DateTimeOffset DeadlineUtc,
        DateTimeOffset AvailableAtUtc,
        int PlayerCount);

    private sealed record HistoricalCapture(
        string SourceRevision,
        DateTimeOffset AvailableAtUtc,
        string PlayersSha256,
        string GameweeksSha256,
        int PlayerGameweekSampleCount,
        int GameweekCount);

    private sealed record CurrentPlayer(
        int PlayerCode,
        string WebName,
        string Position,
        int TeamId,
        string TeamName,
        string Status,
        int? ChanceNextRound);

    private sealed record StoredArtifact(
        long ArtifactId,
        string DocumentJson,
        string ContentSha256);
}

public sealed class PreseasonPlayerForecastImporter
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

    private readonly PreseasonPlayerForecastStore _store;

    public PreseasonPlayerForecastImporter(PreseasonPlayerForecastStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PreseasonPlayerForecastDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The preseason player forecast file does not exist.",
                fullPath);
        }
        if (file.Length is <= 0 or > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Preseason forecast files must contain 1 to "
                + $"{MaximumInputBytes} bytes.");
        }
        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        PreseasonPlayerForecastDocument document =
            await JsonSerializer
                .DeserializeAsync<PreseasonPlayerForecastDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken)
            ?? throw new JsonException(
                "The preseason player forecast document cannot be null.");
        return await _store.ImportAsync(document, cancellationToken);
    }
}

public sealed class PreseasonPlayerForecastValidationException : Exception
{
    public PreseasonPlayerForecastValidationException(
        string code,
        string field)
        : base($"Preseason player forecast validation failed: {code}.")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
