using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Forecasts;

public sealed class MultiSeasonPlayerForecastStore
{
    public const string ArtifactType =
        "multi-season-preseason-shadow-player-gameweek-forecast";
    public const string Status = "retrospective-screen-shadow-challenger";
    public const string ModelKey = "multi-season-histogram-tree-v1";
    public const string BaselineModelKey =
        "official-market-baseline-v0-player-table";
    public const string EvaluationRunIdentity =
        "577ff6fc3d4283670bae47deb53acad683111c6ed1155c9715442688f4602cfd";
    public const string SourceRevision =
        "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a";

    private static readonly IReadOnlyDictionary<string, ArchiveIdentity>
        Archives = new Dictionary<string, ArchiveIdentity>(
            StringComparer.Ordinal)
        {
            ["2024-25"] = new(
                "75686051b265cbe7755ac71213ecaad21b26ee1cc46a8bafbba19c39ce894b05",
                "5bbbcba6353b4c72ad273adcc8e3aa451946a826564679788f45b1cb3325b84e"),
            ["2025-26"] = new(
                "412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b",
                "0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d"),
        };

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public MultiSeasonPlayerForecastStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<MultiSeasonPlayerForecastDocument> ImportAsync(
        MultiSeasonPlayerForecastDocument document,
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
        foreach (MultiSeasonPlayerForecastCaptureDocument capture
            in document.Training.HistoricalCaptures)
        {
            HistoricalCapture persisted = await ReadHistoricalAsync(
                connection,
                transaction,
                capture.CaptureId,
                cancellationToken);
            ValidateHistorical(capture, persisted, target);
        }

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
        IReadOnlyDictionary<int, HistorySummary> history =
            await ReadHistoryAsync(
                connection,
                transaction,
                document.Training.HistoricalCaptures.Select(item => item.CaptureId),
                cancellationToken);
        ValidatePlayers(document, target, currentPlayers, baseline, history);

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
        StoredArtifact stored = await ReadForCaptureAsync(
            connection,
            transaction,
            document.OfficialCaptureId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The multi-season player forecast artifact was not persisted.");
        if (!StringComparer.Ordinal.Equals(stored.ContentSha256, contentSha256))
        {
            throw new MultiSeasonPlayerForecastValidationException(
                "capture-conflict",
                "officialCaptureId");
        }

        await transaction.CommitAsync(cancellationToken);
        return Materialize(stored);
    }

    public async Task<MultiSeasonPlayerForecastDocument?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT forecast_artifact_id, document_json, content_sha256
            FROM multi_season_player_forecast_artifacts
            ORDER BY
                julianday(decision_cutoff_utc) DESC,
                forecast_artifact_id DESC
            LIMIT 1;
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? Materialize(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)))
            : null;
    }

    public async Task<MultiSeasonPlayerForecastReadinessDocument> GetReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        MultiSeasonPlayerForecastOfficialTargetDocument? official =
            await ReadLatestOfficialTargetAsync(connection, cancellationToken);
        MultiSeasonPlayerForecastArtifactIdentityDocument? shadow =
            await ReadLatestArtifactIdentityAsync(connection, cancellationToken);

        (string status, string reasonCode) = (official, shadow) switch
        {
            (null, _) => ("missing", "no-official-capture"),
            (_, null) => ("missing", "no-shadow-artifact"),
            _ when official.OfficialCaptureId == shadow.OfficialCaptureId
                => ("current", "official-capture-match"),
            _ => ("stale", "official-capture-mismatch"),
        };
        return new(
            "1.0",
            status,
            reasonCode,
            false,
            official,
            shadow);
    }

    private static async Task<MultiSeasonPlayerForecastOfficialTargetDocument?>
        ReadLatestOfficialTargetAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                capture_id,
                season_code,
                next_gameweek_number,
                next_deadline_utc,
                available_at_utc
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
                : DateTimeOffset.Parse(
                    reader.GetString(3),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(
                reader.GetString(4),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind));
    }

    private static async Task<MultiSeasonPlayerForecastArtifactIdentityDocument?>
        ReadLatestArtifactIdentityAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                forecast_artifact_id,
                official_capture_id,
                season_code,
                gameweek,
                decision_cutoff_utc,
                content_sha256
            FROM multi_season_player_forecast_artifacts
            ORDER BY
                julianday(decision_cutoff_utc) DESC,
                forecast_artifact_id DESC
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
            DateTimeOffset.Parse(
                reader.GetString(4),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetString(5));
    }

    private static void ValidateFixedIdentity(
        MultiSeasonPlayerForecastDocument document)
    {
        Require(document.SchemaVersion == "1.0", "identity", "schemaVersion");
        Require(document.ArtifactType == ArtifactType, "identity", "artifactType");
        Require(document.Status == Status, "identity", "status");
        Require(document.ModelKey == ModelKey, "identity", "modelKey");
        Require(!document.IsPromoted, "must-be-false", "isPromoted");
        Require(!document.InfluencesAdvice, "must-be-false", "influencesAdvice");
        Require(
            document.ForecastArtifactId is null
                && document.ForecastArtifactContentSha256 is null,
            "must-be-null",
            "forecastArtifactId");
        Require(document.SeasonCode == "2026-27", "identity", "seasonCode");
        Require(document.Gameweek == 1, "identity", "gameweek");
        Require(
            document.DistributionStatus
                == "point-mean-only-no-calibrated-distribution",
            "identity",
            "distributionStatus");
        Require(
            document.Training.SeasonCodes.SequenceEqual(["2024-25", "2025-26"]),
            "identity",
            "training.seasonCodes");
        Require(
            document.Training.HistoricalCaptures.Count == 2
                && document.Training.HistoricalCaptures
                    .Select(item => item.SeasonCode)
                    .SequenceEqual(document.Training.SeasonCodes),
            "identity",
            "training.historicalCaptures");
        Require(
            document.Training.TrainingOriginCount == 76,
            "identity",
            "training.trainingOriginCount");
        Require(
            document.Training.TrainingRowCount == 56_257,
            "identity",
            "training.trainingRowCount");
        Require(
            document.Training.SelectedModel == "multi-season-histogram-tree",
            "identity",
            "training.selectedModel");
        JsonElement configuration = document.Training.ModelConfiguration;
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
            "identity",
            "training.modelConfiguration");
        JsonElement diagnostics = document.Training.ModelDiagnostics;
        Require(
            ReadString(diagnostics, "implementation")
                == "sklearn.ensemble.HistGradientBoostingRegressor"
                && ReadString(diagnostics, "libraryVersion") == "1.9.0"
                && ReadInt(diagnostics, "trainingRows") == 56_257
                && ReadInt(diagnostics, "candidateFeatureCount") == 56
                && ReadInt(diagnostics, "modelFeatureCount") == 56
                && ReadInt(diagnostics, "completedIterations") == 100,
            "identity",
            "training.modelDiagnostics");
        Require(
            document.Training.EvaluationRunIdentitySha256
                == EvaluationRunIdentity,
            "identity",
            "training.evaluationRunIdentitySha256");
        Require(
            document.Comparison.BaselineModelKey == BaselineModelKey
                && document.Comparison.RetrospectiveMaeImprovementOverBaselineFraction
                    == 0.091511m
                && document.Comparison.MatchedCurrentSeasonTreeMaeImprovementFraction
                    == 0.002607m
                && document.Comparison.MatchedCurrentSeasonTreeFoldWins == 3
                && document.Comparison.MatchedCurrentSeasonTreeFoldCount == 8
                && !document.Comparison.AllPositionMaeNonWorse
                && document.Comparison.BaselineStillDrivesAdvice,
            "identity",
            "comparison");
        Require(IsSha256(document.DataIdentitySha256), "sha256", "dataIdentitySha256");
        Require(IsSha256(document.RunIdentitySha256), "sha256", "runIdentitySha256");
    }

    private static void ValidateTarget(
        MultiSeasonPlayerForecastDocument document,
        TargetCapture target)
    {
        Require(target.SeasonCode == document.SeasonCode, "target-mismatch", "seasonCode");
        Require(target.Gameweek == document.Gameweek, "target-mismatch", "gameweek");
        Require(target.DeadlineUtc == document.DeadlineUtc, "target-mismatch", "deadlineUtc");
        Require(
            target.AvailableAtUtc == document.DecisionCutoffUtc
                && target.AvailableAtUtc < target.DeadlineUtc,
            "target-mismatch",
            "decisionCutoffUtc");
        Require(
            target.PlayerCount == document.OfficialPlayerCount,
            "target-mismatch",
            "officialPlayerCount");
    }

    private static void ValidateHistorical(
        MultiSeasonPlayerForecastCaptureDocument document,
        HistoricalCapture persisted,
        TargetCapture target)
    {
        if (!Archives.TryGetValue(
            document.SeasonCode,
            out ArchiveIdentity? expected))
        {
            throw new MultiSeasonPlayerForecastValidationException(
                "archive-identity",
                "training.historicalCaptures");
        }
        Require(
            persisted.SeasonCode == document.SeasonCode
                && persisted.SourceRevision == SourceRevision
                && persisted.SourceRevision == document.SourceRevision
                && persisted.AvailableAtUtc == document.AvailableAtUtc
                && persisted.AvailableAtUtc <= target.AvailableAtUtc
                && persisted.PlayersSha256 == expected.PlayersSha256
                && persisted.PlayersSha256 == document.PlayersSha256
                && persisted.GameweeksSha256 == expected.GameweeksSha256
                && persisted.GameweeksSha256 == document.GameweeksSha256
                && persisted.PlayerCount == document.PlayerCount
                && persisted.PlayerGameweekCount == document.PlayerGameweekCount
                && persisted.StableCodeCount == document.StableCodeCount,
            "archive-identity",
            "training.historicalCaptures");
    }

    private static void ValidatePlayers(
        MultiSeasonPlayerForecastDocument document,
        TargetCapture target,
        IReadOnlyDictionary<int, CurrentPlayer> currentPlayers,
        IReadOnlyDictionary<int, decimal> baseline,
        IReadOnlyDictionary<int, HistorySummary> history)
    {
        Require(
            document.PlayerCount == document.Players.Count
                && document.PlayerCount == currentPlayers.Count
                && document.IneligiblePlayerCount
                    == target.PlayerCount - currentPlayers.Count,
            "player-coverage",
            "playerCount");
        Require(
            baseline.Count == currentPlayers.Count
                && baseline.Keys.All(currentPlayers.ContainsKey),
            "baseline-coverage",
            "comparison.baselineModelKey");
        Require(
            document.Players.Select(item => item.PlayerId).Distinct().Count()
                == document.PlayerCount,
            "duplicate-player",
            "players");

        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["both-historical-seasons"] = 0,
            ["latest-historical-season-only"] = 0,
            ["older-historical-season-only"] = 0,
            ["no-historical-season-match"] = 0,
        };
        foreach (MultiSeasonPlayerForecastPlayerDocument player in document.Players)
        {
            if (!currentPlayers.TryGetValue(
                player.PlayerId,
                out CurrentPlayer? current))
            {
                throw new MultiSeasonPlayerForecastValidationException(
                    "current-player-not-found",
                    "players");
            }
            Require(
                current.PlayerCode == player.PlayerCode
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
            HistorySummary playerHistory = history.TryGetValue(
                player.PlayerCode,
                out HistorySummary? found)
                ? found
                : new(new(StringComparer.Ordinal), 0);
            HashSet<string> seasons = playerHistory.Seasons;
            string status = seasons.SetEquals(["2024-25", "2025-26"])
                ? "both-historical-seasons"
                : seasons.SetEquals(["2025-26"])
                    ? "latest-historical-season-only"
                    : seasons.SetEquals(["2024-25"])
                        ? "older-historical-season-only"
                        : "no-historical-season-match";
            Require(
                player.HistoricalIdentityStatus == status
                    && player.HistoricalSeasonCodes.SequenceEqual(
                        seasons.Order(StringComparer.Ordinal)),
                "historical-identity-mismatch",
                "players");
            Require(
                player.HistoricalGameweekCount == playerHistory.GameweekCount,
                "historical-gameweek-count",
                "players.historicalGameweekCount");
            counts[status]++;
            Require(
                player.ExpectedPoints is >= -20m and <= 100m
                    && player.BaselineV0ExpectedPoints is >= -20m and <= 100m,
                "points-out-of-range",
                "players");
            Require(
                baseline[player.PlayerId] == player.BaselineV0ExpectedPoints,
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
            counts.Count == document.HistoricalIdentityCounts.Count
                && counts.All(
                    pair => document.HistoricalIdentityCounts.TryGetValue(
                        pair.Key,
                        out int value)
                        && value == pair.Value),
            "historical-identity-counts",
            "historicalIdentityCounts");
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
                   available_at_utc, player_count
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
            throw new MultiSeasonPlayerForecastValidationException(
                "target-not-found",
                "officialCaptureId");
        }
        return new(
            reader.GetString(0),
            reader.GetInt32(1),
            DateTimeOffset.Parse(reader.GetString(2)),
            DateTimeOffset.Parse(reader.GetString(3)),
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
            SELECT season_code, source_revision, available_at_utc,
                   players_sha256, gameweeks_sha256, player_count,
                   player_gameweek_count, stable_code_count
            FROM historical_fpl_season_captures
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new MultiSeasonPlayerForecastValidationException(
                "archive-not-found",
                "training.historicalCaptures");
        }
        return new(
            reader.GetString(0),
            reader.GetString(1),
            DateTimeOffset.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7));
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
            SELECT player.player_id, player.code, player.web_name,
                   player.position, player.team_id, team.name,
                   player.status, player.chance_next_round
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
            throw new MultiSeasonPlayerForecastValidationException(
                "baseline-not-found",
                "comparison.baselineModelKey");
        }
        string json = reader.GetString(0);
        string contentSha256 = reader.GetString(1);
        string actual = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Require(actual == contentSha256, "baseline-content-hash-invalid",
            "comparison.baselineModelKey");
        PlayerGameweekForecastDocument baseline =
            JsonSerializer.Deserialize<PlayerGameweekForecastDocument>(
                json,
                JsonOptions)
            ?? throw new MultiSeasonPlayerForecastValidationException(
                "baseline-invalid",
                "comparison.baselineModelKey");
        Require(
            baseline.OfficialCaptureId == captureId
                && baseline.ModelKey == BaselineModelKey,
            "baseline-identity-mismatch",
            "comparison.baselineModelKey");
        return baseline.Players.ToDictionary(
            player => player.PlayerId,
            player => player.ExpectedPoints);
    }

    private static async Task<IReadOnlyDictionary<int, HistorySummary>>
        ReadHistoryAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            IEnumerable<long> captureIds,
            CancellationToken cancellationToken)
    {
        long[] ids = captureIds.ToArray();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT gameweek.player_code, capture.season_code,
                   COUNT(DISTINCT gameweek.gameweek)
            FROM historical_fpl_player_gameweeks AS gameweek
            INNER JOIN historical_fpl_season_captures AS capture
                ON capture.capture_id = gameweek.capture_id
            WHERE gameweek.capture_id IN ($older, $latest)
            GROUP BY gameweek.player_code, capture.season_code;
            """;
        command.Parameters.AddWithValue("$older", ids[0]);
        command.Parameters.AddWithValue("$latest", ids[1]);
        var history = new Dictionary<int, HistorySummary>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int code = reader.GetInt32(0);
            if (!history.TryGetValue(code, out HistorySummary? summary))
            {
                summary = new(new(StringComparer.Ordinal), 0);
                history.Add(code, summary);
            }
            summary.Seasons.Add(reader.GetString(1));
            history[code] = summary with
            {
                GameweekCount = summary.GameweekCount + reader.GetInt32(2),
            };
        }
        return history;
    }

    private async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MultiSeasonPlayerForecastDocument document,
        string documentJson,
        string contentSha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO multi_season_player_forecast_artifacts (
                schema_version, artifact_type, status, model_key,
                official_capture_id, older_historical_capture_id,
                latest_historical_capture_id, season_code, gameweek,
                decision_cutoff_utc, producer_run_identity_sha256,
                document_json, content_sha256, created_at_utc
            )
            VALUES (
                $schemaVersion, $artifactType, $status, $modelKey,
                $officialCaptureId, $olderCaptureId, $latestCaptureId,
                $seasonCode, $gameweek, $decisionCutoffUtc,
                $runIdentity, $documentJson, $contentSha256, $createdAtUtc
            );
            """;
        MultiSeasonPlayerForecastCaptureDocument[] captures =
            document.Training.HistoricalCaptures.ToArray();
        command.Parameters.AddWithValue("$schemaVersion", document.SchemaVersion);
        command.Parameters.AddWithValue("$artifactType", document.ArtifactType);
        command.Parameters.AddWithValue("$status", document.Status);
        command.Parameters.AddWithValue("$modelKey", document.ModelKey);
        command.Parameters.AddWithValue("$officialCaptureId", document.OfficialCaptureId);
        command.Parameters.AddWithValue("$olderCaptureId", captures[0].CaptureId);
        command.Parameters.AddWithValue("$latestCaptureId", captures[1].CaptureId);
        command.Parameters.AddWithValue("$seasonCode", document.SeasonCode);
        command.Parameters.AddWithValue("$gameweek", document.Gameweek);
        command.Parameters.AddWithValue(
            "$decisionCutoffUtc",
            document.DecisionCutoffUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$runIdentity", document.RunIdentitySha256);
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
            FROM multi_season_player_forecast_artifacts
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

    private static MultiSeasonPlayerForecastDocument Materialize(
        StoredArtifact artifact)
    {
        string actual = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(artifact.DocumentJson)));
        if (!StringComparer.Ordinal.Equals(actual, artifact.ContentSha256))
        {
            throw new InvalidOperationException(
                "The stored multi-season forecast content hash is invalid.");
        }
        MultiSeasonPlayerForecastDocument document =
            JsonSerializer.Deserialize<MultiSeasonPlayerForecastDocument>(
                artifact.DocumentJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The stored multi-season forecast document is invalid.");
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
            throw new MultiSeasonPlayerForecastValidationException(code, field);
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(
            character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

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

    private sealed record ArchiveIdentity(
        string PlayersSha256,
        string GameweeksSha256);

    private sealed record HistorySummary(
        HashSet<string> Seasons,
        int GameweekCount);

    private sealed record TargetCapture(
        string SeasonCode,
        int Gameweek,
        DateTimeOffset DeadlineUtc,
        DateTimeOffset AvailableAtUtc,
        int PlayerCount);

    private sealed record HistoricalCapture(
        string SeasonCode,
        string SourceRevision,
        DateTimeOffset AvailableAtUtc,
        string PlayersSha256,
        string GameweeksSha256,
        int PlayerCount,
        int PlayerGameweekCount,
        int StableCodeCount);

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

public sealed class MultiSeasonPlayerForecastImporter
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

    private readonly MultiSeasonPlayerForecastStore _store;

    public MultiSeasonPlayerForecastImporter(
        MultiSeasonPlayerForecastStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<MultiSeasonPlayerForecastDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The multi-season player forecast file does not exist.",
                fullPath);
        }
        if (file.Length is <= 0 or > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Multi-season forecast files must contain 1 to "
                + $"{MaximumInputBytes} bytes.");
        }
        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        MultiSeasonPlayerForecastDocument document =
            await JsonSerializer
                .DeserializeAsync<MultiSeasonPlayerForecastDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken)
            ?? throw new JsonException(
                "The multi-season player forecast document cannot be null.");
        return await _store.ImportAsync(document, cancellationToken);
    }
}

public sealed class MultiSeasonPlayerForecastValidationException : Exception
{
    public MultiSeasonPlayerForecastValidationException(
        string code,
        string field)
        : base($"Multi-season player forecast validation failed: {code}.")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
