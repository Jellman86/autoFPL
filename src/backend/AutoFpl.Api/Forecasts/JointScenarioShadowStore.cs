using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Forecasts;

public sealed class JointScenarioShadowStore
{
    public const string ArtifactType =
        "current-joint-player-gameweek-scenario-shadow";
    public const string ArtifactVersion =
        "current-joint-scenario-shadow-v1";
    public const string Status = "prospective-shadow-unscored";
    public const string ScenarioModelKey =
        "joint-gameweek-residual-bootstrap";
    public const string AppearanceVariant =
        "officialCeilingFactorized";
    public const string PointAvailabilityFusion =
        "official-appearance-ceiling-ratio-v1";
    public const string DistributionStatus =
        "joint-scenario-shadow-prospective-current-season-unscored";
    public const string EvaluatorVersion =
        "historical-joint-scenario-evaluation-v1";
    public const string EvaluationDataIdentity =
        "265fc9e0457273ef51bc247f44943805058e64a99fc9f5178a1eff5a43e27d90";
    public const string EvaluationRunIdentity =
        "d48e8483dc0af0e85aab254e5919b43d98a1167b5c9956f5988b37e92ef87c78";
    public const string SourcePlayersSha256 =
        "412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b";
    public const string SourceGameweeksSha256 =
        "0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> PointHistoryStatuses =
    [
        "both-historical-seasons",
        "latest-historical-season-only",
        "older-historical-season-only",
        "no-historical-season-match",
    ];

    private static readonly HashSet<string> ParticipationHistoryStatuses =
    [
        "stable-code-match",
        "no-prior-season-match",
    ];

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public JointScenarioShadowStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<JointScenarioShadowDocument> ImportAsync(
        JointScenarioShadowDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateFixedIdentity(document);
        ValidateRows(document);

        await using var connection = new SqliteConnection(
            _options.ConnectionString);
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
        HistoricalCapture source = await ReadHistoricalAsync(
            connection,
            transaction,
            document.Training.SourceHistoricalCaptureId,
            cancellationToken);
        ValidateHistorical(document, source, target);
        PointForecastSource pointSource = await ReadPointForecastAsync(
            connection,
            transaction,
            document.OfficialCaptureId,
            cancellationToken);
        ValidatePlayers(document, pointSource.Document);

        string documentJson = JsonSerializer.Serialize(document, JsonOptions);
        string contentSha256 = Sha256(documentJson);
        await InsertAsync(
            connection,
            transaction,
            document,
            pointSource.ArtifactId,
            documentJson,
            contentSha256,
            cancellationToken);
        StoredArtifact stored = await ReadForCaptureAsync(
            connection,
            transaction,
            document.OfficialCaptureId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The joint scenario shadow artifact was not persisted.");
        if (!StringComparer.Ordinal.Equals(
            stored.ContentSha256,
            contentSha256))
        {
            throw new JointScenarioValidationException(
                "capture-conflict",
                "officialCaptureId");
        }

        await transaction.CommitAsync(cancellationToken);
        return Materialize(stored);
    }

    public async Task<JointScenarioShadowDocument?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(
            _options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT scenario_artifact_id, document_json, content_sha256
            FROM joint_scenario_shadow_artifacts
            ORDER BY
                julianday(decision_cutoff_utc) DESC,
                scenario_artifact_id DESC
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

    public async Task<JointScenarioReadinessDocument> GetReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(
            _options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        JointScenarioOfficialTargetDocument? official =
            await ReadLatestOfficialTargetAsync(
                connection,
                cancellationToken);
        JointScenarioArtifactIdentityDocument? scenario =
            await ReadLatestIdentityAsync(connection, cancellationToken);
        (string status, string reasonCode) = (official, scenario) switch
        {
            (null, _) => ("missing", "no-official-capture"),
            (_, null) => ("missing", "no-scenario-artifact"),
            _ when official.OfficialCaptureId == scenario.OfficialCaptureId
                => ("current", "official-capture-match"),
            _ => ("stale", "official-capture-mismatch"),
        };
        return new(
            "1.0",
            status,
            reasonCode,
            false,
            official,
            scenario);
    }

    private static void ValidateFixedIdentity(
        JointScenarioShadowDocument document)
    {
        Require(document.SchemaVersion == "1.0", "identity", "schemaVersion");
        Require(
            document.ArtifactType == ArtifactType,
            "identity",
            "artifactType");
        Require(
            document.ArtifactVersion == ArtifactVersion,
            "identity",
            "artifactVersion");
        Require(document.Status == Status, "identity", "status");
        Require(!document.IsPromoted, "must-be-false", "isPromoted");
        Require(
            !document.InfluencesAdvice,
            "must-be-false",
            "influencesAdvice");
        Require(
            document.ScenarioArtifactId is null
                && document.ScenarioArtifactContentSha256 is null,
            "must-be-null",
            "scenarioArtifactId");
        Require(document.SeasonCode == "2026-27", "identity", "seasonCode");
        Require(document.Gameweek == 1, "identity", "gameweek");
        Require(
            document.ScenarioModelKey == ScenarioModelKey,
            "identity",
            "scenarioModelKey");
        Require(
            document.AppearanceVariant == AppearanceVariant,
            "identity",
            "appearanceVariant");
        Require(
            document.PointAvailabilityFusion == PointAvailabilityFusion,
            "identity",
            "pointAvailabilityFusion");
        Require(
            document.DistributionStatus == DistributionStatus,
            "identity",
            "distributionStatus");
        Require(
            document.SourceGameweeks.SequenceEqual(
                Enumerable.Range(1, 38)),
            "identity",
            "sourceGameweeks");
        Require(
            document.ScenarioCount == 38,
            "identity",
            "scenarioCount");
        Require(
            document.Training.SourceSeasonCode == "2025-26"
                && document.Training.SourcePlayersSha256
                    == SourcePlayersSha256
                && document.Training.SourceGameweeksSha256
                    == SourceGameweeksSha256,
            "identity",
            "training");
        Require(
            IsSha256(
                document.Training.PointForecastRunIdentitySha256)
                && IsSha256(
                    document.Training
                        .ParticipationForecastRunIdentitySha256),
            "sha256",
            "training");
        JointScenarioScreenDocument screen =
            document.Training.RetrospectiveScreen;
        Require(
            screen.EvaluatorVersion == EvaluatorVersion
                && screen.DataIdentitySha256 == EvaluationDataIdentity
                && screen.RunIdentitySha256 == EvaluationRunIdentity
                && screen.Status == "passes-retrospective-screen"
                && screen.MeanCrps == 0.639806m
                && screen.AggregateCrpsImprovementFraction == 0.071611m
                && screen.FoldWins == 8
                && screen.FoldCount == 8
                && !screen.IsPromoted,
            "identity",
            "training.retrospectiveScreen");
        Require(
            IsSha256(document.ScenarioContentSha256),
            "sha256",
            "scenarioContentSha256");
        Require(
            IsSha256(document.DataIdentitySha256),
            "sha256",
            "dataIdentitySha256");
        Require(
            IsSha256(document.RunIdentitySha256),
            "sha256",
            "runIdentitySha256");
        Require(
            document.Limitations.Count is >= 1 and <= 32,
            "limitations",
            "limitations");
    }

    private static void ValidateRows(
        JointScenarioShadowDocument document)
    {
        Require(
            document.PlayerCount == document.Players.Count
                && document.PlayerCount is >= 1 and <= 1024,
            "player-coverage",
            "playerCount");
        Require(
            document.PointRows.Count == document.ScenarioCount
                && document.PlayedRows.Count == document.ScenarioCount,
            "row-count",
            "pointRows");
        Require(
            document.Training.SelfDonorAssignments >= 0
                && document.Training.FallbackDonorAssignments >= 0
                && document.Training.SelfDonorAssignments
                    + document.Training.FallbackDonorAssignments
                    == document.ScenarioCount * document.PlayerCount,
            "donor-count",
            "training");
        Require(
            document.ScenarioDiagnostics.NonPlayingNonZeroPointCount == 0
                && document.ScenarioDiagnostics
                    .MeanAbsolutePointMeanDelta is >= 0m and <= 100m
                && document.ScenarioDiagnostics
                    .MaximumAbsolutePointMeanDelta is >= 0m and <= 100m
                && document.ScenarioDiagnostics
                    .MeanAbsoluteAppearanceProbabilityDelta
                    is >= 0m and <= 1m
                && document.ScenarioDiagnostics
                    .MaximumAbsoluteAppearanceProbabilityDelta
                    is >= 0m and <= 1m,
            "diagnostics",
            "scenarioDiagnostics");
        for (int row = 0; row < document.ScenarioCount; row++)
        {
            IReadOnlyList<int> points = document.PointRows[row];
            IReadOnlyList<bool> played = document.PlayedRows[row];
            Require(
                points.Count == document.PlayerCount
                    && played.Count == document.PlayerCount,
                "row-width",
                "pointRows");
            for (int column = 0; column < document.PlayerCount; column++)
            {
                Require(
                    points[column] is >= -50 and <= 100,
                    "points-out-of-range",
                    "pointRows");
                Require(
                    played[column] || points[column] == 0,
                    "nonplayer-points",
                    "pointRows");
            }
        }
        string actual = ScenarioContentSha256(document);
        Require(
            actual == document.ScenarioContentSha256,
            "content-hash",
            "scenarioContentSha256");
    }

    private static void ValidateTarget(
        JointScenarioShadowDocument document,
        TargetCapture target)
    {
        Require(
            target.SeasonCode == document.SeasonCode
                && target.Gameweek == document.Gameweek
                && target.DeadlineUtc == document.DeadlineUtc
                && target.AvailableAtUtc == document.DecisionCutoffUtc
                && target.AvailableAtUtc < target.DeadlineUtc,
            "target-mismatch",
            "officialCaptureId");
    }

    private static void ValidateHistorical(
        JointScenarioShadowDocument document,
        HistoricalCapture source,
        TargetCapture target)
    {
        Require(
            source.SeasonCode == document.Training.SourceSeasonCode
                && source.AvailableAtUtc <= target.AvailableAtUtc
                && source.PlayersSha256
                    == document.Training.SourcePlayersSha256
                && source.GameweeksSha256
                    == document.Training.SourceGameweeksSha256,
            "archive-identity",
            "training.sourceHistoricalCaptureId");
    }

    private static void ValidatePlayers(
        JointScenarioShadowDocument document,
        MultiSeasonPlayerForecastDocument pointForecast)
    {
        Require(
            pointForecast.OfficialCaptureId == document.OfficialCaptureId
                && pointForecast.RunIdentitySha256
                    == document.Training
                        .PointForecastRunIdentitySha256
                && pointForecast.Players.Count == document.PlayerCount,
            "point-forecast-identity",
            "training.pointForecastRunIdentitySha256");
        IReadOnlyDictionary<int, MultiSeasonPlayerForecastPlayerDocument>
            pointPlayers = pointForecast.Players.ToDictionary(
                player => player.PlayerId);
        Require(
            document.Players
                .Select(player => player.ColumnIndex)
                .SequenceEqual(Enumerable.Range(0, document.PlayerCount))
                && document.Players
                    .Select(player => player.PlayerId)
                    .Distinct()
                    .Count() == document.PlayerCount
                && document.Players
                    .Select(player => player.PlayerCode)
                    .Distinct()
                    .Count() == document.PlayerCount,
            "player-order",
            "players");
        foreach (JointScenarioPlayerDocument player in document.Players)
        {
            if (!pointPlayers.TryGetValue(
                player.PlayerId,
                out MultiSeasonPlayerForecastPlayerDocument? point))
            {
                throw new JointScenarioValidationException(
                    "point-player-not-found",
                    "players");
            }
            Require(
                point.PlayerCode == player.PlayerCode
                    && point.WebName == player.WebName
                    && point.TeamId == player.TeamId
                    && point.TeamName == player.TeamName
                    && point.Position == player.Position
                    && point.OfficialStatus == player.OfficialStatus
                    && point.OfficialChanceOfPlayingNextRound
                        == player.OfficialChanceOfPlayingNextRound
                    && point.HistoricalIdentityStatus
                        == player.PointHistoryIdentityStatus,
                "player-identity",
                "players");
            Require(
                point.ExpectedPoints
                    == player.PointMeanBeforeAvailability,
                "point-mean-source",
                "players.pointMeanBeforeAvailability");
            Require(
                player.PointMean is >= -20m and <= 100m
                    && player.PointMeanBeforeAvailability
                        is >= -20m and <= 100m
                    && player.PointAvailabilityMultiplier
                        is >= 0m and <= 1m
                    && player.AppearanceProbability is >= 0m and <= 1m,
                "player-value-range",
                "players");
            Require(
                Math.Abs(
                    player.PointMeanBeforeAvailability
                    * player.PointAvailabilityMultiplier
                    - player.PointMean) <= 0.00001m,
                "point-availability-fusion",
                "players.pointMean");
            Require(
                PointHistoryStatuses.Contains(
                    player.PointHistoryIdentityStatus)
                    && ParticipationHistoryStatuses.Contains(
                        player.ParticipationHistoryIdentityStatus),
                "history-identity",
                "players");
        }
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
            SELECT season_code, next_gameweek_number, next_deadline_utc,
                   available_at_utc
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
            throw new JointScenarioValidationException(
                "target-not-found",
                "officialCaptureId");
        }
        return new(
            reader.GetString(0),
            reader.GetInt32(1),
            DateTimeOffset.Parse(reader.GetString(2)),
            DateTimeOffset.Parse(reader.GetString(3)));
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
            SELECT season_code, available_at_utc, players_sha256,
                   gameweeks_sha256
            FROM historical_fpl_season_captures
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new JointScenarioValidationException(
                "archive-not-found",
                "training.sourceHistoricalCaptureId");
        }
        return new(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3));
    }

    private static async Task<PointForecastSource> ReadPointForecastAsync(
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
            FROM multi_season_player_forecast_artifacts
            WHERE official_capture_id = $captureId
              AND model_key = $modelKey;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue(
            "$modelKey",
            MultiSeasonPlayerForecastStore.ModelKey);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new JointScenarioValidationException(
                "point-forecast-not-found",
                "training.pointForecastRunIdentitySha256");
        }
        long artifactId = reader.GetInt64(0);
        string json = reader.GetString(1);
        string contentSha256 = reader.GetString(2);
        Require(
            Sha256(json) == contentSha256,
            "point-forecast-content-hash",
            "training.pointForecastRunIdentitySha256");
        MultiSeasonPlayerForecastDocument document =
            JsonSerializer.Deserialize<MultiSeasonPlayerForecastDocument>(
                json,
                JsonOptions)
            ?? throw new JointScenarioValidationException(
                "point-forecast-invalid",
                "training.pointForecastRunIdentitySha256");
        return new(artifactId, document);
    }

    private async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JointScenarioShadowDocument document,
        long pointForecastArtifactId,
        string documentJson,
        string contentSha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO joint_scenario_shadow_artifacts (
                schema_version, artifact_type, artifact_version, status,
                scenario_model_key, official_capture_id,
                source_historical_capture_id, point_forecast_artifact_id,
                season_code, gameweek, decision_cutoff_utc,
                scenario_count, player_count, scenario_content_sha256,
                producer_run_identity_sha256, document_json,
                content_sha256, created_at_utc
            )
            VALUES (
                $schemaVersion, $artifactType, $artifactVersion, $status,
                $scenarioModelKey, $officialCaptureId,
                $sourceHistoricalCaptureId, $pointForecastArtifactId,
                $seasonCode, $gameweek, $decisionCutoffUtc,
                $scenarioCount, $playerCount, $scenarioContentSha256,
                $runIdentity, $documentJson, $contentSha256, $createdAtUtc
            )
            ON CONFLICT (official_capture_id, scenario_model_key)
            DO NOTHING;
            """;
        command.Parameters.AddWithValue(
            "$schemaVersion",
            document.SchemaVersion);
        command.Parameters.AddWithValue(
            "$artifactType",
            document.ArtifactType);
        command.Parameters.AddWithValue(
            "$artifactVersion",
            document.ArtifactVersion);
        command.Parameters.AddWithValue("$status", document.Status);
        command.Parameters.AddWithValue(
            "$scenarioModelKey",
            document.ScenarioModelKey);
        command.Parameters.AddWithValue(
            "$officialCaptureId",
            document.OfficialCaptureId);
        command.Parameters.AddWithValue(
            "$sourceHistoricalCaptureId",
            document.Training.SourceHistoricalCaptureId);
        command.Parameters.AddWithValue(
            "$pointForecastArtifactId",
            pointForecastArtifactId);
        command.Parameters.AddWithValue(
            "$seasonCode",
            document.SeasonCode);
        command.Parameters.AddWithValue("$gameweek", document.Gameweek);
        command.Parameters.AddWithValue(
            "$decisionCutoffUtc",
            document.DecisionCutoffUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue(
            "$scenarioCount",
            document.ScenarioCount);
        command.Parameters.AddWithValue(
            "$playerCount",
            document.PlayerCount);
        command.Parameters.AddWithValue(
            "$scenarioContentSha256",
            document.ScenarioContentSha256);
        command.Parameters.AddWithValue(
            "$runIdentity",
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
            SELECT scenario_artifact_id, document_json, content_sha256
            FROM joint_scenario_shadow_artifacts
            WHERE official_capture_id = $captureId
              AND scenario_model_key = $modelKey;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$modelKey", ScenarioModelKey);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2))
            : null;
    }

    private static async Task<JointScenarioOfficialTargetDocument?>
        ReadLatestOfficialTargetAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT capture_id, season_code, next_gameweek_number,
                   next_deadline_utc, available_at_utc
            FROM official_fpl_captures
            ORDER BY available_at_utc DESC, capture_id DESC
            LIMIT 1;
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3)
                ? null
                : DateTimeOffset.Parse(reader.GetString(3)),
            DateTimeOffset.Parse(reader.GetString(4)));
    }

    private static async Task<JointScenarioArtifactIdentityDocument?>
        ReadLatestIdentityAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT scenario_artifact_id, official_capture_id, season_code,
                   gameweek, decision_cutoff_utc, scenario_count,
                   player_count, scenario_content_sha256, content_sha256
            FROM joint_scenario_shadow_artifacts
            ORDER BY
                julianday(decision_cutoff_utc) DESC,
                scenario_artifact_id DESC
            LIMIT 1;
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetInt32(3),
            DateTimeOffset.Parse(reader.GetString(4)),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.GetString(8));
    }

    private static JointScenarioShadowDocument Materialize(
        StoredArtifact artifact)
    {
        if (Sha256(artifact.DocumentJson) != artifact.ContentSha256)
        {
            throw new InvalidOperationException(
                "The stored joint scenario content hash is invalid.");
        }
        JointScenarioShadowDocument document =
            JsonSerializer.Deserialize<JointScenarioShadowDocument>(
                artifact.DocumentJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The stored joint scenario document is invalid.");
        ValidateFixedIdentity(document);
        ValidateRows(document);
        return document with
        {
            ScenarioArtifactId = artifact.ArtifactId,
            ScenarioArtifactContentSha256 = artifact.ContentSha256,
        };
    }

    private static string ScenarioContentSha256(
        JointScenarioShadowDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("playedRows");
            writer.WriteStartArray();
            foreach (IReadOnlyList<bool> row in document.PlayedRows)
            {
                writer.WriteStartArray();
                foreach (bool value in row)
                {
                    writer.WriteBooleanValue(value);
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("playerCodes");
            writer.WriteStartArray();
            foreach (JointScenarioPlayerDocument player in document.Players)
            {
                writer.WriteNumberValue(player.PlayerCode);
            }
            writer.WriteEndArray();
            writer.WritePropertyName("pointRows");
            writer.WriteStartArray();
            foreach (IReadOnlyList<int> row in document.PointRows)
            {
                writer.WriteStartArray();
                foreach (int value in row)
                {
                    writer.WriteNumberValue(value);
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("sourceGameweeks");
            writer.WriteStartArray();
            foreach (int gameweek in document.SourceGameweeks)
            {
                writer.WriteNumberValue(gameweek);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(
            SHA256.HashData(stream.ToArray()));
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(
            character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static void Require(
        bool condition,
        string code,
        string field)
    {
        if (!condition)
        {
            throw new JointScenarioValidationException(code, field);
        }
    }

    private sealed record TargetCapture(
        string SeasonCode,
        int Gameweek,
        DateTimeOffset DeadlineUtc,
        DateTimeOffset AvailableAtUtc);

    private sealed record HistoricalCapture(
        string SeasonCode,
        DateTimeOffset AvailableAtUtc,
        string PlayersSha256,
        string GameweeksSha256);

    private sealed record PointForecastSource(
        long ArtifactId,
        MultiSeasonPlayerForecastDocument Document);

    private sealed record StoredArtifact(
        long ArtifactId,
        string DocumentJson,
        string ContentSha256);
}

public sealed class JointScenarioShadowImporter
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

    private readonly JointScenarioShadowStore _store;

    public JointScenarioShadowImporter(JointScenarioShadowStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<JointScenarioShadowDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The joint scenario shadow file does not exist.",
                fullPath);
        }
        if (file.Length is <= 0 or > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Joint scenario files must contain 1 to "
                + $"{MaximumInputBytes} bytes.");
        }
        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        JointScenarioShadowDocument document =
            await JsonSerializer.DeserializeAsync<
                JointScenarioShadowDocument>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new JsonException(
                "The joint scenario shadow document cannot be null.");
        return await _store.ImportAsync(document, cancellationToken);
    }
}

public sealed class JointScenarioValidationException : Exception
{
    public JointScenarioValidationException(
        string code,
        string field)
        : base($"Joint scenario shadow validation failed: {code}.")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
