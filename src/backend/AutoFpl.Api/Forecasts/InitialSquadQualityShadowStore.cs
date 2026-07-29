using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using AutoFpl.Api.Persistence;
using AutoFpl.Api.Selections;
using AutoFpl.Contracts.Advice;
using AutoFpl.Contracts.Forecasts;
using AutoFpl.Contracts.Selections;
using AutoFpl.Domain.Outcomes;
using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Forecasts;

public sealed class InitialSquadQualityShadowStore
{
    public const string ArtifactType =
        "current-initial-squad-quality-shadow";
    public const string ArtifactVersion =
        "current-initial-squad-quality-shadow-v1";
    public const string Status = "prospective-shadow-unscored";
    public const string OptimizerVersion =
        "scipy-highs-linear-squad-surrogate-v1";

    private const decimal NumericTolerance = 0.000001m;
    private const decimal BenchWeight = 0.08m;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public InitialSquadQualityShadowStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<InitialSquadQualityShadowDocument> ImportAsync(
        InitialSquadQualityShadowDocument document,
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
        SourceArtifacts source = await ReadSourceAsync(
            connection,
            transaction,
            document,
            cancellationToken);
        ValidateLineage(document, source);
        await ValidatePlayersAsync(
            connection,
            transaction,
            document,
            source.Scenario,
            cancellationToken);

        string documentJson = JsonSerializer.Serialize(document, JsonOptions);
        string contentSha256 = Sha256(documentJson);
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO initial_squad_quality_shadow_artifacts (
                    schema_version, artifact_type, artifact_version, status,
                    scenario_artifact_id, forecast_artifact_id,
                    optimizer_version, official_capture_id, season_code,
                    gameweek, decision_cutoff_utc, scenario_count,
                    candidate_pool_count, budget_tenths,
                    producer_run_identity_sha256, document_json,
                    content_sha256, created_at_utc
                )
                VALUES (
                    $schemaVersion, $artifactType, $artifactVersion, $status,
                    $scenarioArtifactId, $forecastArtifactId,
                    $optimizerVersion, $officialCaptureId, $seasonCode,
                    $gameweek, $decisionCutoffUtc, $scenarioCount,
                    $candidatePoolCount, $budgetTenths, $runIdentity,
                    $documentJson, $contentSha256, $createdAtUtc
                )
                ON CONFLICT (
                    scenario_artifact_id,
                    forecast_artifact_id,
                    optimizer_version
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
                source.ScenarioArtifactId);
            insert.Parameters.AddWithValue(
                "$forecastArtifactId",
                source.ForecastArtifactId);
            insert.Parameters.AddWithValue(
                "$optimizerVersion",
                document.Optimizer.OptimizerVersion);
            insert.Parameters.AddWithValue(
                "$officialCaptureId",
                document.OfficialCaptureId);
            insert.Parameters.AddWithValue("$seasonCode", document.SeasonCode);
            insert.Parameters.AddWithValue("$gameweek", document.Gameweek);
            insert.Parameters.AddWithValue(
                "$decisionCutoffUtc",
                document.DecisionCutoffUtc.UtcDateTime.ToString("O"));
            insert.Parameters.AddWithValue(
                "$scenarioCount",
                document.ScenarioCount);
            insert.Parameters.AddWithValue(
                "$candidatePoolCount",
                document.CandidatePoolCount);
            insert.Parameters.AddWithValue(
                "$budgetTenths",
                document.BudgetTenths);
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
            source.ScenarioArtifactId,
            source.ForecastArtifactId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The initial squad quality shadow was not persisted.");
        if (stored.ContentSha256 != contentSha256)
        {
            throw new InitialSquadQualityValidationException(
                "source-conflict",
                "dataIdentitySha256");
        }
        await transaction.CommitAsync(cancellationToken);
        return Materialize(stored);
    }

    public async Task<InitialSquadQualityShadowDocument?> GetCurrentAsync(
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
            )
            SELECT initial_squad_artifact_id, document_json, content_sha256
            FROM initial_squad_quality_shadow_artifacts
            WHERE scenario_artifact_id = (
                SELECT scenario_artifact_id FROM current_scenario
            )
              AND forecast_artifact_id = (
                SELECT artifact_id FROM current_forecast
            )
              AND optimizer_version = $optimizerVersion
            ORDER BY initial_squad_artifact_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$optimizerVersion", OptimizerVersion);
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
        InitialSquadQualityShadowDocument document)
    {
        Require(
            document.SchemaVersion == "1.0"
                && document.ArtifactType == ArtifactType
                && document.ArtifactVersion == ArtifactVersion
                && document.Status == Status
                && !document.IsPromoted
                && !document.InfluencesAdvice
                && document.InitialSquadArtifactId is null
                && document.InitialSquadArtifactContentSha256 is null,
            "identity",
            "artifactType");
        Require(
            document.SeasonCode == "2026-27"
                && document.Gameweek == 1
                && document.OfficialCaptureId > 0
                && document.DecisionCutoffUtc < document.DeadlineUtc
                && document.ScenarioCount is >= 1 and <= 512
                && document.CandidatePoolCount is >= 15 and <= 1024,
            "target",
            "officialCaptureId");
        Require(
            document.Objective
                == new InitialSquadObjectiveDocument(
                    "single-gameweek-linear-mean-surrogate",
                    1m,
                    BenchWeight,
                    1m)
                && document.Optimizer.OptimizerVersion == OptimizerVersion
                && document.Optimizer.Solver == "scipy.optimize.milp-highs"
                && document.Optimizer.Status
                    == "global-linear-surrogate-optimum"
                && document.Optimizer.MipGap == 0m
                && document.Optimizer.BenchWeight == BenchWeight,
            "optimizer",
            "optimizer");
        Require(
            document.ScenarioSource.ScenarioArtifactId > 0
                && document.ModelSource.ForecastArtifactId > 0
                && IsSha256(
                    document.ScenarioSource
                        .ScenarioArtifactContentSha256)
                && IsSha256(
                    document.ScenarioSource.ScenarioContentSha256)
                && IsSha256(
                    document.ScenarioSource.ScenarioRunIdentitySha256)
                && IsSha256(
                    document.ModelSource
                        .ForecastArtifactContentSha256)
                && IsSha256(
                    document.ModelSource.SelectionContentSha256),
            "source",
            "scenarioSource");
        Require(
            document.Players.Count == 15
                && document.Players.Select(value => value.PlayerId)
                    .Distinct()
                    .Count() == 15
                && document.BudgetTenths
                    == document.Players.Sum(value => value.PriceTenths)
                && document.BudgetTenths is > 0 and <= 1000,
            "squad",
            "players");
        Require(
            document.Limitations.Count is >= 1 and <= 32
                && document.Limitations.All(
                    value => !string.IsNullOrWhiteSpace(value)
                        && value.Length <= 1000)
                && IsSha256(document.DataIdentitySha256)
                && IsSha256(document.RunIdentitySha256)
                && document.DataIdentitySha256
                    == DataIdentitySha256(document)
                && document.RunIdentitySha256
                    == RunIdentitySha256(document),
            "boundary",
            "dataIdentitySha256");

        try
        {
            SelectionScenarioScoreShadowStore.ValidateResult(
                document.Model,
                document.ScenarioCount,
                "model");
            SelectionScenarioScoreShadowStore.ValidateResult(
                document.Candidate,
                document.ScenarioCount,
                "candidate");
            SelectionScenarioScoreShadowStore.ValidateComparison(
                document.Model.TotalPointRows,
                document.Candidate.TotalPointRows,
                document.CandidateVsModel,
                document.ScenarioCount,
                "candidateVsModel");
        }
        catch (SelectionScenarioScoreValidationException exception)
        {
            throw new InitialSquadQualityValidationException(
                $"scenario-{exception.Code}",
                exception.Field);
        }
        Require(
            document.ModelSource.SelectionContentSha256
                == SelectionScenarioScoreShadowStore
                    .SelectionContentSha256(document.Model.Selection),
            "model-selection-hash",
            "modelSource.selectionContentSha256");
    }

    private static async Task<SourceArtifacts> ReadSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        InitialSquadQualityShadowDocument document,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand scenarioCommand = connection.CreateCommand();
        scenarioCommand.Transaction = transaction;
        scenarioCommand.CommandText =
            """
            SELECT document_json, content_sha256
            FROM joint_scenario_shadow_artifacts
            WHERE scenario_artifact_id = $artifactId;
            """;
        scenarioCommand.Parameters.AddWithValue(
            "$artifactId",
            document.ScenarioSource.ScenarioArtifactId);
        await using SqliteDataReader scenarioReader =
            await scenarioCommand.ExecuteReaderAsync(cancellationToken);
        if (!await scenarioReader.ReadAsync(cancellationToken))
        {
            throw new InitialSquadQualityValidationException(
                "scenario-not-found",
                "scenarioSource.scenarioArtifactId");
        }
        string scenarioJson = scenarioReader.GetString(0);
        string scenarioHash = scenarioReader.GetString(1);
        Require(
            Sha256(scenarioJson) == scenarioHash,
            "scenario-content",
            "scenarioSource");
        JointScenarioShadowDocument scenario =
            JsonSerializer.Deserialize<JointScenarioShadowDocument>(
                scenarioJson,
                JsonOptions)
            ?? throw new JsonException(
                "The persisted joint scenario cannot be null.");
        await scenarioReader.DisposeAsync();

        await using SqliteCommand forecastCommand = connection.CreateCommand();
        forecastCommand.Transaction = transaction;
        forecastCommand.CommandText =
            """
            SELECT document_json, content_sha256
            FROM baseline_forecast_artifacts
            WHERE artifact_id = $artifactId;
            """;
        forecastCommand.Parameters.AddWithValue(
            "$artifactId",
            document.ModelSource.ForecastArtifactId);
        await using SqliteDataReader forecastReader =
            await forecastCommand.ExecuteReaderAsync(cancellationToken);
        if (!await forecastReader.ReadAsync(cancellationToken))
        {
            throw new InitialSquadQualityValidationException(
                "forecast-not-found",
                "modelSource.forecastArtifactId");
        }
        string forecastJson = forecastReader.GetString(0);
        string forecastHash = forecastReader.GetString(1);
        Require(
            Sha256(forecastJson) == forecastHash,
            "forecast-content",
            "modelSource");
        GameweekAdviceDocument forecast =
            JsonSerializer.Deserialize<GameweekAdviceDocument>(
                forecastJson,
                JsonOptions)
            ?? throw new JsonException(
                "The persisted model forecast cannot be null.");
        return new(
            document.ScenarioSource.ScenarioArtifactId,
            scenarioHash,
            scenario,
            document.ModelSource.ForecastArtifactId,
            forecastHash,
            forecast);
    }

    private static void ValidateLineage(
        InitialSquadQualityShadowDocument document,
        SourceArtifacts source)
    {
        Require(
            source.ScenarioContentSha256
                    == document.ScenarioSource
                        .ScenarioArtifactContentSha256
                && source.Scenario.ScenarioContentSha256
                    == document.ScenarioSource.ScenarioContentSha256
                && source.Scenario.RunIdentitySha256
                    == document.ScenarioSource.ScenarioRunIdentitySha256
                && source.Scenario.OfficialCaptureId
                    == document.OfficialCaptureId
                && source.Scenario.SeasonCode == document.SeasonCode
                && source.Scenario.Gameweek == document.Gameweek
                && source.Scenario.DeadlineUtc == document.DeadlineUtc
                && source.Scenario.DecisionCutoffUtc
                    == document.DecisionCutoffUtc
                && source.Scenario.ScenarioCount == document.ScenarioCount,
            "scenario-lineage",
            "scenarioSource");
        Require(
            source.ForecastContentSha256
                    == document.ModelSource
                        .ForecastArtifactContentSha256
                && source.Forecast.ModelLabel
                    == document.ModelSource.ModelLabel
                && source.Forecast.Gameweek == document.Gameweek
                && source.Forecast.DecisionCutoffUtc
                    == document.DecisionCutoffUtc
                && JsonSerializer.Serialize(
                    ForecastSelection(source.Forecast),
                    JsonOptions)
                    == JsonSerializer.Serialize(
                        document.Model.Selection,
                        JsonOptions),
            "forecast-lineage",
            "modelSource");
    }

    private static async Task ValidatePlayersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        InitialSquadQualityShadowDocument document,
        JointScenarioShadowDocument scenario,
        CancellationToken cancellationToken)
    {
        var scenarioByPlayer = scenario.Players.ToDictionary(
            player => player.PlayerId);
        var officialByPlayer = new Dictionary<int, OfficialPlayerState>();
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT player_id, team_id, position, price_tenths, status,
                       chance_next_round
                FROM official_fpl_players
                WHERE capture_id = $captureId;
                """;
            command.Parameters.AddWithValue(
                "$captureId",
                document.OfficialCaptureId);
            await using SqliteDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                officialByPlayer.Add(
                    reader.GetInt32(0),
                    new(
                        reader.GetInt32(1),
                        reader.GetString(2),
                        reader.GetInt32(3),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetInt32(5)));
            }
        }
        Require(
            scenario.Players.All(
                player => officialByPlayer.ContainsKey(player.PlayerId)),
            "official-player-pool",
            "candidatePoolCount");
        int eligibleScenarioPlayers = scenario.Players.Count(
            player => officialByPlayer[player.PlayerId].Status != "u");
        Require(
            eligibleScenarioPlayers == document.CandidatePoolCount,
            "candidate-pool",
            "candidatePoolCount");
        ValidateScenarioResult(
            document.Model,
            scenario,
            officialByPlayer,
            "model");
        ValidateScenarioResult(
            document.Candidate,
            scenario,
            officialByPlayer,
            "candidate");

        var selectedIds = document.Candidate.Selection.PlayerIds.ToHashSet();
        Require(
            selectedIds.SetEquals(
                document.Players.Select(player => player.PlayerId)),
            "candidate-player",
            "players");
        var positions = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var teams = new Dictionary<int, int>();
        foreach (InitialSquadPlayerDocument player in document.Players)
        {
            Require(
                scenarioByPlayer.TryGetValue(
                    player.PlayerId,
                    out JointScenarioPlayerDocument? scenarioPlayer)
                    && scenarioPlayer.TeamId == player.TeamId
                    && scenarioPlayer.TeamName == player.TeamName
                    && scenarioPlayer.Position == player.Position
                    && Close(
                        scenarioPlayer.PointMean,
                        player.PointModelMean)
                    && Close(
                        scenarioPlayer.AppearanceProbability,
                        player.AppearanceProbability)
                    && scenarioPlayer.PointHistoryIdentityStatus
                        == player.PointHistoryIdentityStatus
                    && scenarioPlayer.ParticipationHistoryIdentityStatus
                        == player.ParticipationHistoryIdentityStatus,
                "scenario-player",
                $"players.{player.PlayerId}");
            decimal scenarioMean = scenario.PointRows.Sum(
                row => (decimal)row[scenarioPlayer!.ColumnIndex])
                / scenario.ScenarioCount;
            Require(
                Close(scenarioMean, player.ScenarioMeanPoints),
                "scenario-mean",
                $"players.{player.PlayerId}.scenarioMeanPoints");

            Require(
                officialByPlayer.TryGetValue(
                    player.PlayerId,
                    out OfficialPlayerState? officialPlayer)
                    && officialPlayer.TeamId == player.TeamId
                    && officialPlayer.Position == player.Position
                    && officialPlayer.PriceTenths == player.PriceTenths
                    && officialPlayer.Status == player.OfficialStatus
                    && officialPlayer.ChanceNextRound
                        == player.OfficialChanceOfPlayingNextRound
                    && player.OfficialStatus != "u",
                "official-player",
                $"players.{player.PlayerId}");
            positions[player.Position] =
                positions.GetValueOrDefault(player.Position) + 1;
            teams[player.TeamId] =
                teams.GetValueOrDefault(player.TeamId) + 1;
        }
        Require(
            positions.GetValueOrDefault("goalkeeper") == 2
                && positions.GetValueOrDefault("defender") == 5
                && positions.GetValueOrDefault("midfielder") == 5
                && positions.GetValueOrDefault("forward") == 3
                && teams.Values.All(value => value <= 3),
            "squad-legality",
            "players");

    }

    internal static void ValidateScenarioResult(
        SelectionScenarioResultDocument result,
        JointScenarioShadowDocument scenario,
        IReadOnlyDictionary<int, OfficialPlayerState> officialByPlayer,
        string field)
    {
        var scenarioByPlayer = scenario.Players.ToDictionary(
            player => player.PlayerId);
        Require(
            result.Selection.PlayerIds.All(
                playerId => scenarioByPlayer.ContainsKey(playerId)
                    && officialByPlayer.ContainsKey(playerId)),
            "scenario-selection-player",
            $"{field}.selection");
        Squad squad;
        GameweekSelection selection;
        try
        {
            squad = Squad.Create(
                1000,
                result.Selection.PlayerIds
                    .Select(
                        playerId =>
                        {
                            OfficialPlayerState player =
                                officialByPlayer[playerId];
                            return SquadPlayer.Create(
                                playerId,
                                player.TeamId,
                                player.Position,
                                player.PriceTenths);
                        })
                    .ToArray());
            selection = GameweekSelection.Create(
                squad,
                result.Selection.StartingPlayerIds,
                result.Selection.CaptainPlayerId,
                result.Selection.ViceCaptainPlayerId,
                result.Selection.ReplacementGoalkeeperPlayerId,
                result.Selection.OutfieldSubstitutePlayerIds);
        }
        catch (Exception exception)
            when (exception is SquadValidationException
                or GameweekSelectionValidationException)
        {
            throw new InitialSquadQualityValidationException(
                "scenario-selection",
                $"{field}.selection");
        }

        var columnByPlayer = scenario.Players.ToDictionary(
            player => player.PlayerId,
            player => player.ColumnIndex);
        for (int rowIndex = 0;
            rowIndex < scenario.ScenarioCount;
            rowIndex++)
        {
            IReadOnlyList<int> pointRow = scenario.PointRows[rowIndex];
            IReadOnlyList<bool> playedRow = scenario.PlayedRows[rowIndex];
            int[] playedPlayerIds =
            [
                .. result.Selection.PlayerIds.Where(
                    playerId => playedRow[columnByPlayer[playerId]]),
            ];
            GameweekSubstitutionResolution substitutions =
                GameweekSubstitutionResolution.Resolve(
                    squad,
                    selection,
                    playedPlayerIds);
            GameweekCaptaincyResolution captaincy =
                GameweekCaptaincyResolution.Resolve(
                    squad,
                    selection,
                    playedPlayerIds);
            int basePoints = substitutions.EffectivePlayerIds.Sum(
                playerId => pointRow[columnByPlayer[playerId]]);
            int captainBonus = captaincy.EffectiveCaptainPlayerId is int captain
                ? pointRow[columnByPlayer[captain]]
                : 0;
            Require(
                result.CaptainBonusPointRows[rowIndex] == captainBonus
                    && result.TotalPointRows[rowIndex]
                        == basePoints + captainBonus,
                "scenario-score",
                $"{field}.totalPointRows");
        }
    }

    private static SelectionScenarioDefinitionDocument ForecastSelection(
        GameweekAdviceDocument forecast)
    {
        IReadOnlyList<AdvicePlayerDocument> players =
            forecast.Selection.Players;
        int[] starting =
        [
            .. players
                .Where(player => player.LineupPlace == "starting")
                .Select(player => player.PlayerId),
        ];
        AdvicePlayerDocument[] bench =
        [
            .. players.Where(player => player.LineupPlace == "bench"),
        ];
        int goalkeeper = bench.Single(
            player => player.Position == "goalkeeper").PlayerId;
        int[] substitutes =
        [
            .. bench
                .Where(player => player.Position != "goalkeeper")
                .OrderBy(player => player.BenchOrder)
                .Select(player => player.PlayerId),
        ];
        int captain = players.Single(
            player => player.Captaincy == "captain").PlayerId;
        int viceCaptain = players.Single(
            player => player.Captaincy == "vice-captain").PlayerId;
        int[] playerIds = [.. starting, goalkeeper, .. substitutes];
        var positionByPlayer = players.ToDictionary(
            player => player.PlayerId,
            player => player.Position);
        return new(
            playerIds,
            playerIds.Select(playerId => positionByPlayer[playerId]).ToArray(),
            starting,
            goalkeeper,
            substitutes,
            captain,
            viceCaptain);
    }

    private static async Task<StoredArtifact?> ReadExactAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long scenarioArtifactId,
        long forecastArtifactId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT initial_squad_artifact_id, document_json, content_sha256
            FROM initial_squad_quality_shadow_artifacts
            WHERE scenario_artifact_id = $scenarioArtifactId
              AND forecast_artifact_id = $forecastArtifactId
              AND optimizer_version = $optimizerVersion;
            """;
        command.Parameters.AddWithValue(
            "$scenarioArtifactId",
            scenarioArtifactId);
        command.Parameters.AddWithValue(
            "$forecastArtifactId",
            forecastArtifactId);
        command.Parameters.AddWithValue("$optimizerVersion", OptimizerVersion);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static InitialSquadQualityShadowDocument Materialize(
        StoredArtifact artifact)
    {
        if (Sha256(artifact.DocumentJson) != artifact.ContentSha256)
        {
            throw new InvalidDataException(
                "The initial squad quality artifact hash is invalid.");
        }
        InitialSquadQualityShadowDocument document =
            JsonSerializer.Deserialize<InitialSquadQualityShadowDocument>(
                artifact.DocumentJson,
                JsonOptions)
            ?? throw new JsonException(
                "The initial squad quality document cannot be null.");
        return document with
        {
            InitialSquadArtifactId = artifact.ArtifactId,
            InitialSquadArtifactContentSha256 = artifact.ContentSha256,
        };
    }

    private static string DataIdentitySha256(
        InitialSquadQualityShadowDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("benchWeight", BenchWeight);
            writer.WriteString(
                "forecastArtifactContentSha256",
                document.ModelSource.ForecastArtifactContentSha256);
            writer.WriteString(
                "optimizerVersion",
                document.Optimizer.OptimizerVersion);
            writer.WriteString(
                "scenarioArtifactContentSha256",
                document.ScenarioSource.ScenarioArtifactContentSha256);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static string RunIdentitySha256(
        InitialSquadQualityShadowDocument document)
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
            throw new InitialSquadQualityValidationException(code, field);
        }
    }

    private sealed record SourceArtifacts(
        long ScenarioArtifactId,
        string ScenarioContentSha256,
        JointScenarioShadowDocument Scenario,
        long ForecastArtifactId,
        string ForecastContentSha256,
        GameweekAdviceDocument Forecast);

    private sealed record StoredArtifact(
        long ArtifactId,
        string DocumentJson,
        string ContentSha256);

    internal sealed record OfficialPlayerState(
        int TeamId,
        string Position,
        int PriceTenths,
        string Status,
        int? ChanceNextRound);
}

public sealed class InitialSquadQualityShadowImporter
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

    private readonly InitialSquadQualityShadowStore _store;

    public InitialSquadQualityShadowImporter(
        InitialSquadQualityShadowStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<InitialSquadQualityShadowDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The initial squad quality file does not exist.",
                fullPath);
        }
        if (file.Length is <= 0 or > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Initial squad quality files must contain 1 to "
                + $"{MaximumInputBytes} bytes.");
        }
        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        InitialSquadQualityShadowDocument document =
            await JsonSerializer.DeserializeAsync<
                InitialSquadQualityShadowDocument>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new JsonException(
                "The initial squad quality document cannot be null.");
        return await _store.ImportAsync(document, cancellationToken);
    }
}

public sealed class InitialSquadQualityValidationException : Exception
{
    public InitialSquadQualityValidationException(
        string code,
        string field)
        : base($"Initial squad quality validation failed: {code}.")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
