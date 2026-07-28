using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Advice;
using AutoFpl.Contracts.Forecasts;
using AutoFpl.Contracts.Selections;
using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Selections;

public sealed class SelectionRevisionStore
{
    private const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public SelectionRevisionStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<SelectionRevisionDocument?> CreateDraftFromForecastAsync(
        long forecastArtifactId,
        CancellationToken cancellationToken = default)
    {
        if (forecastArtifactId <= 0)
        {
            return null;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using SqliteConnection connection =
            new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            connection.BeginTransaction(deferred: false);

        ForecastSelectionSource? source = await ReadForecastSourceAsync(
            connection,
            transaction,
            forecastArtifactId,
            cancellationToken);
        if (source is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        if (now >= source.DeadlineUtc)
        {
            throw new SelectionWorkflowException(
                "selection.deadline.passed",
                "forecastArtifactId");
        }

        SelectionRevisionRow? latest = await ReadLatestRowAsync(
            connection,
            transaction,
            source.SeasonCode,
            source.Gameweek,
            cancellationToken);
        if (latest is not null
            && latest.ForecastArtifactId == source.ForecastArtifactId
            && StringComparer.Ordinal.Equals(
                latest.SelectionContentHash,
                source.SelectionContentHash))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Materialize(latest, now);
        }

        int revision = (latest?.Revision ?? 0) + 1;
        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO selection_revisions (
                schema_version,
                revision,
                supersedes_selection_revision_id,
                season_code,
                gameweek,
                deadline_utc,
                forecast_artifact_id,
                forecast_artifact_content_sha256,
                selection_json,
                selection_content_sha256,
                created_at_utc,
                locked_at_utc
            )
            VALUES (
                $schemaVersion,
                $revision,
                $supersedesSelectionRevisionId,
                $seasonCode,
                $gameweek,
                $deadlineUtc,
                $forecastArtifactId,
                $forecastArtifactContentHash,
                $selectionJson,
                $selectionContentHash,
                $createdAtUtc,
                NULL
            );
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$schemaVersion", SchemaVersion);
        insert.Parameters.AddWithValue("$revision", revision);
        insert.Parameters.AddWithValue(
            "$supersedesSelectionRevisionId",
            (object?)latest?.SelectionRevisionId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$seasonCode", source.SeasonCode);
        insert.Parameters.AddWithValue("$gameweek", source.Gameweek);
        insert.Parameters.AddWithValue(
            "$deadlineUtc",
            source.DeadlineUtc.ToString("O"));
        insert.Parameters.AddWithValue(
            "$forecastArtifactId",
            source.ForecastArtifactId);
        insert.Parameters.AddWithValue(
            "$forecastArtifactContentHash",
            source.ForecastArtifactContentHash);
        insert.Parameters.AddWithValue("$selectionJson", source.SelectionJson);
        insert.Parameters.AddWithValue(
            "$selectionContentHash",
            source.SelectionContentHash);
        insert.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
        long selectionRevisionId =
            (long)(await insert.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException(
                    "SQLite did not return a selection revision ID."));

        SelectionRevisionRow row = await ReadRowAsync(
            connection,
            transaction,
            selectionRevisionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The new selection revision could not be read back.");
        await transaction.CommitAsync(cancellationToken);
        return Materialize(row, now);
    }

    public async Task<SelectionRevisionDocument?> LockAsync(
        long selectionRevisionId,
        CancellationToken cancellationToken = default)
    {
        if (selectionRevisionId <= 0)
        {
            return null;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using SqliteConnection connection =
            new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            connection.BeginTransaction(deferred: false);
        SelectionRevisionRow? row = await ReadRowAsync(
            connection,
            transaction,
            selectionRevisionId,
            cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        if (row.LockedAtUtc is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Materialize(row, now);
        }
        if (now >= row.DeadlineUtc)
        {
            throw new SelectionWorkflowException(
                "selection.deadline.passed",
                "selectionRevisionId");
        }

        SelectionRevisionRow latest = await ReadLatestRowAsync(
            connection,
            transaction,
            row.SeasonCode,
            row.Gameweek,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The selection revision disappeared during locking.");
        if (latest.SelectionRevisionId != selectionRevisionId)
        {
            throw new SelectionWorkflowException(
                "selection.revision.stale",
                "selectionRevisionId");
        }

        await using SqliteCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            """
            UPDATE selection_revisions
            SET locked_at_utc = $lockedAtUtc
            WHERE selection_revision_id = $selectionRevisionId
              AND locked_at_utc IS NULL;
            """;
        update.Parameters.AddWithValue("$lockedAtUtc", now.ToString("O"));
        update.Parameters.AddWithValue(
            "$selectionRevisionId",
            selectionRevisionId);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The selection revision was not locked exactly once.");
        }

        SelectionRevisionRow locked = await ReadRowAsync(
            connection,
            transaction,
            selectionRevisionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The locked selection revision could not be read back.");
        await transaction.CommitAsync(cancellationToken);
        return Materialize(locked, now);
    }

    public async Task<SelectionRevisionDocument?> CreateEditedRevisionAsync(
        long supersedesSelectionRevisionId,
        LockedSelectionDocument requestedSelection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedSelection);
        if (supersedesSelectionRevisionId <= 0)
        {
            return null;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using SqliteConnection connection =
            new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            connection.BeginTransaction(deferred: false);

        SelectionRevisionRow? superseded = await ReadRowAsync(
            connection,
            transaction,
            supersedesSelectionRevisionId,
            cancellationToken);
        if (superseded is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        if (now >= superseded.DeadlineUtc)
        {
            throw new SelectionWorkflowException(
                "selection.deadline.passed",
                "selectionRevisionId");
        }

        SelectionRevisionRow latest = await ReadLatestRowAsync(
            connection,
            transaction,
            superseded.SeasonCode,
            superseded.Gameweek,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The selection revision disappeared during editing.");
        if (latest.SelectionRevisionId != supersedesSelectionRevisionId)
        {
            throw new SelectionWorkflowException(
                "selection.revision.stale",
                "selectionRevisionId");
        }

        ForecastSelectionSource source = await ReadForecastSourceAsync(
            connection,
            transaction,
            superseded.ForecastArtifactId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The selection revision forecast artifact is unavailable.");
        if (!StringComparer.Ordinal.Equals(
                source.ForecastArtifactContentHash,
                superseded.ForecastArtifactContentHash)
            || !StringComparer.Ordinal.Equals(
                source.SeasonCode,
                superseded.SeasonCode)
            || source.Gameweek != superseded.Gameweek
            || source.DeadlineUtc != superseded.DeadlineUtc)
        {
            throw new InvalidOperationException(
                "The selection revision forecast lineage is invalid.");
        }

        ForecastPlayerPool playerPool = await ReadPlayerPoolAsync(
            connection,
            transaction,
            source.ForecastArtifactId,
            cancellationToken)
            ?? throw new SelectionWorkflowException(
                "selection.player_forecast.unavailable",
                "selectionRevisionId");
        EnsurePlayerPoolLineage(source, playerPool);
        LockedSelectionDocument selection = ValidateSelection(
            playerPool.Forecast,
            requestedSelection);
        string selectionJson = JsonSerializer.Serialize(selection, JsonOptions);
        string selectionHash = Hash(selectionJson);
        if (StringComparer.Ordinal.Equals(
            selectionHash,
            latest.SelectionContentHash))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Materialize(latest, now);
        }

        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO selection_revisions (
                schema_version,
                revision,
                supersedes_selection_revision_id,
                season_code,
                gameweek,
                deadline_utc,
                forecast_artifact_id,
                forecast_artifact_content_sha256,
                selection_json,
                selection_content_sha256,
                created_at_utc,
                locked_at_utc
            )
            VALUES (
                $schemaVersion,
                $revision,
                $supersedesSelectionRevisionId,
                $seasonCode,
                $gameweek,
                $deadlineUtc,
                $forecastArtifactId,
                $forecastArtifactContentHash,
                $selectionJson,
                $selectionContentHash,
                $createdAtUtc,
                NULL
            );
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$schemaVersion", SchemaVersion);
        insert.Parameters.AddWithValue("$revision", latest.Revision + 1);
        insert.Parameters.AddWithValue(
            "$supersedesSelectionRevisionId",
            latest.SelectionRevisionId);
        insert.Parameters.AddWithValue("$seasonCode", latest.SeasonCode);
        insert.Parameters.AddWithValue("$gameweek", latest.Gameweek);
        insert.Parameters.AddWithValue(
            "$deadlineUtc",
            latest.DeadlineUtc.ToString("O"));
        insert.Parameters.AddWithValue(
            "$forecastArtifactId",
            latest.ForecastArtifactId);
        insert.Parameters.AddWithValue(
            "$forecastArtifactContentHash",
            latest.ForecastArtifactContentHash);
        insert.Parameters.AddWithValue("$selectionJson", selectionJson);
        insert.Parameters.AddWithValue("$selectionContentHash", selectionHash);
        insert.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
        long selectionRevisionId =
            (long)(await insert.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException(
                    "SQLite did not return an edited selection revision ID."));

        SelectionRevisionRow edited = await ReadRowAsync(
            connection,
            transaction,
            selectionRevisionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The edited selection revision could not be read back.");
        await transaction.CommitAsync(cancellationToken);
        return Materialize(edited, now);
    }

    public async Task<SelectionComparisonDocument?> GetComparisonAsync(
        long selectionRevisionId,
        CancellationToken cancellationToken = default)
    {
        if (selectionRevisionId <= 0)
        {
            return null;
        }

        await using SqliteConnection connection =
            new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            connection.BeginTransaction(deferred: true);
        SelectionRevisionRow? row = await ReadRowAsync(
            connection,
            transaction,
            selectionRevisionId,
            cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        ForecastSelectionSource source = await ReadForecastSourceAsync(
            connection,
            transaction,
            row.ForecastArtifactId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The selection revision forecast artifact is unavailable.");
        if (!StringComparer.Ordinal.Equals(
                source.ForecastArtifactContentHash,
                row.ForecastArtifactContentHash)
            || !StringComparer.Ordinal.Equals(source.SeasonCode, row.SeasonCode)
            || source.Gameweek != row.Gameweek
            || source.DeadlineUtc != row.DeadlineUtc)
        {
            throw new InvalidOperationException(
                "The selection revision forecast lineage is invalid.");
        }
        ForecastPlayerPool playerPool = await ReadPlayerPoolAsync(
            connection,
            transaction,
            row.ForecastArtifactId,
            cancellationToken)
            ?? throw new SelectionWorkflowException(
                "selection.player_forecast.unavailable",
                "selectionRevisionId");
        EnsurePlayerPoolLineage(source, playerPool);
        if (!StringComparer.Ordinal.Equals(
                Hash(row.SelectionJson),
                row.SelectionContentHash))
        {
            throw new InvalidOperationException(
                "The stored selection revision content hash is invalid.");
        }
        LockedSelectionDocument userSelection =
            JsonSerializer.Deserialize<LockedSelectionDocument>(
                row.SelectionJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The stored selection revision is invalid.");
        userSelection = ValidateSelection(playerPool.Forecast, userSelection);
        LockedSelectionDocument modelSelection =
            ValidateSelection(playerPool.Forecast, ToLockedSelection(source.Advice));
        SelectionProjectionDocument model =
            Project(modelSelection, playerPool.Forecast);
        SelectionProjectionDocument user =
            Project(userSelection, playerPool.Forecast);
        HashSet<int> modelIds = [.. model.SquadPlayerIds];
        HashSet<int> userIds = [.. user.SquadPlayerIds];

        await transaction.CommitAsync(cancellationToken);
        return new(
            "1.0",
            row.SelectionRevisionId,
            row.ForecastArtifactId,
            playerPool.ArtifactId,
            playerPool.ContentHash,
            playerPool.Forecast.DistributionStatus,
            model,
            user,
            user.ProjectedPoints - model.ProjectedPoints,
            [.. userIds.Except(modelIds).Order()],
            [.. modelIds.Except(userIds).Order()],
            [
                "Projected points are a point-estimate comparison from one immutable forecast capture, not realised Gameweek points.",
                "The current player intervals are uncalibrated and are not combined into a squad-level probability distribution.",
            ]);
    }

    public async Task<SelectionRevisionDocument?> GetAsync(
        long selectionRevisionId,
        CancellationToken cancellationToken = default)
    {
        if (selectionRevisionId <= 0)
        {
            return null;
        }

        await using SqliteConnection connection =
            new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        SelectionRevisionRow? row = await ReadRowAsync(
            connection,
            transaction: null,
            selectionRevisionId,
            cancellationToken);
        return row is null
            ? null
            : Materialize(row, _timeProvider.GetUtcNow());
    }

    public async Task<SelectionRevisionDocument?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH current_target AS (
                SELECT
                    capture.season_code,
                    capture.next_gameweek_number AS gameweek
                FROM baseline_forecast_artifacts AS artifact
                INNER JOIN official_fpl_captures AS capture
                    ON capture.capture_id = artifact.capture_id
                WHERE capture.next_gameweek_number IS NOT NULL
                  AND capture.next_deadline_utc IS NOT NULL
                ORDER BY
                    capture.available_at_utc DESC,
                    artifact.artifact_id DESC
                LIMIT 1
            )
            SELECT selection_revision_id
            FROM selection_revisions
            INNER JOIN current_target
                ON current_target.season_code = selection_revisions.season_code
                AND current_target.gameweek = selection_revisions.gameweek
            ORDER BY revision DESC
            LIMIT 1;
            """;
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long selectionRevisionId
            ? await GetAsync(selectionRevisionId, cancellationToken)
            : null;
    }

    private static async Task<ForecastSelectionSource?> ReadForecastSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long forecastArtifactId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                artifact.artifact_id,
                artifact.document_json,
                artifact.content_sha256,
                capture.season_code,
                capture.next_gameweek_number,
                capture.next_deadline_utc
            FROM baseline_forecast_artifacts AS artifact
            INNER JOIN official_fpl_captures AS capture
                ON capture.capture_id = artifact.capture_id
            WHERE artifact.artifact_id = $forecastArtifactId;
            """;
        command.Parameters.AddWithValue(
            "$forecastArtifactId",
            forecastArtifactId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || reader.IsDBNull(4)
            || reader.IsDBNull(5))
        {
            return null;
        }

        string documentJson = reader.GetString(1);
        string forecastHash = reader.GetString(2);
        string actualForecastHash = Hash(documentJson);
        if (!StringComparer.Ordinal.Equals(forecastHash, actualForecastHash))
        {
            throw new InvalidOperationException(
                "The forecast artifact content hash is invalid.");
        }

        GameweekAdviceDocument advice =
            JsonSerializer.Deserialize<GameweekAdviceDocument>(
                documentJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The forecast artifact document is invalid.");
        string seasonCode = reader.GetString(3);
        int gameweek = reader.GetInt32(4);
        DateTimeOffset deadlineUtc = ParseUtc(reader.GetString(5));
        if (advice.IsSynthetic
            || advice.Gameweek != gameweek
            || advice.DeadlineUtc != deadlineUtc)
        {
            throw new InvalidOperationException(
                "The forecast artifact does not match its official target.");
        }

        LockedSelectionDocument selection = ToLockedSelection(advice);
        string selectionJson = JsonSerializer.Serialize(selection, JsonOptions);
        return new(
            forecastArtifactId,
            forecastHash,
            seasonCode,
            gameweek,
            deadlineUtc,
            selectionJson,
            Hash(selectionJson),
            advice);
    }

    private static LockedSelectionDocument ToLockedSelection(
        GameweekAdviceDocument advice)
    {
        AdvicePlayerDocument[] players = [.. advice.Selection.Players];
        int[] allPlayerIds = [.. players.Select(player => player.PlayerId)];
        AdvicePlayerDocument[] starters =
        [
            .. players.Where(player => player.LineupPlace == "starting"),
        ];
        AdvicePlayerDocument[] bench =
        [
            .. players
                .Where(player => player.LineupPlace == "bench")
                .OrderBy(player => player.BenchOrder),
        ];
        AdvicePlayerDocument[] captains =
        [
            .. players.Where(player => player.Captaincy == "captain"),
        ];
        AdvicePlayerDocument[] viceCaptains =
        [
            .. players.Where(player => player.Captaincy == "vice-captain"),
        ];
        AdvicePlayerDocument[] replacementGoalkeepers =
        [
            .. bench.Where(player => player.Position == "goalkeeper"),
        ];
        AdvicePlayerDocument[] outfieldSubstitutes =
        [
            .. bench.Where(player => player.Position != "goalkeeper"),
        ];
        if (players.Length != 15
            || allPlayerIds.Distinct().Count() != 15
            || starters.Length != 11
            || bench.Length != 4
            || captains.Length != 1
            || viceCaptains.Length != 1
            || replacementGoalkeepers.Length != 1
            || outfieldSubstitutes.Length != 3
            || bench.Select(player => player.BenchOrder).SequenceEqual(
                [1, 2, 3, 4]) is false)
        {
            throw new InvalidOperationException(
                "The forecast artifact does not contain a complete lockable selection.");
        }

        return new(
            [.. starters.Select(player => player.PlayerId)],
            captains[0].PlayerId,
            viceCaptains[0].PlayerId,
            replacementGoalkeepers[0].PlayerId,
            [.. outfieldSubstitutes.Select(player => player.PlayerId)]);
    }

    private static LockedSelectionDocument ValidateSelection(
        PlayerGameweekForecastDocument playerForecast,
        LockedSelectionDocument requestedSelection)
    {
        Dictionary<int, PlayerGameweekForecastPlayerDocument> forecastPlayers =
            playerForecast.Players.ToDictionary(player => player.PlayerId);
        string[] clubNames =
        [
            .. playerForecast.Players
                .Select(player => player.ClubShortName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        Dictionary<string, int> clubIds = clubNames
            .Select((name, index) => new { name, id = index + 1 })
            .ToDictionary(item => item.name, item => item.id, StringComparer.Ordinal);
        int[] requestedPlayerIds =
        [
            .. requestedSelection.StartingPlayerIds,
            requestedSelection.ReplacementGoalkeeperPlayerId,
            .. requestedSelection.OutfieldSubstitutePlayerIds,
        ];
        if (requestedPlayerIds.Any(playerId => !forecastPlayers.ContainsKey(playerId)))
        {
            throw new SelectionWorkflowException(
                "selection.player.not_in_forecast",
                "selection");
        }

        SquadPlayer[] players =
        [
            .. requestedPlayerIds.Select(
                playerId =>
                {
                    PlayerGameweekForecastPlayerDocument player =
                        forecastPlayers[playerId];
                    return SquadPlayer.Create(
                        player.PlayerId,
                        clubIds[player.ClubShortName],
                        player.Position,
                        player.PriceTenths);
                }),
        ];
        Squad squad = Squad.Create(budgetTenths: 1000, players);
        GameweekSelection selection = GameweekSelection.Create(
            squad,
            requestedSelection.StartingPlayerIds,
            requestedSelection.CaptainPlayerId,
            requestedSelection.ViceCaptainPlayerId,
            requestedSelection.ReplacementGoalkeeperPlayerId,
            requestedSelection.OutfieldSubstitutePlayerIds);
        return new(
            selection.Lineup.StartingPlayerIds,
            selection.Lineup.CaptainPlayerId,
            selection.Lineup.ViceCaptainPlayerId,
            selection.ReplacementGoalkeeperPlayerId,
            selection.OutfieldSubstitutePlayerIds);
    }

    private static SelectionProjectionDocument Project(
        LockedSelectionDocument selection,
        PlayerGameweekForecastDocument forecast)
    {
        Dictionary<int, PlayerGameweekForecastPlayerDocument> players =
            forecast.Players.ToDictionary(player => player.PlayerId);
        int[] squadPlayerIds =
        [
            .. selection.StartingPlayerIds,
            selection.ReplacementGoalkeeperPlayerId,
            .. selection.OutfieldSubstitutePlayerIds,
        ];
        decimal startingPoints = selection.StartingPlayerIds
            .Sum(playerId => players[playerId].ExpectedPoints);
        decimal captainBonus =
            players[selection.CaptainPlayerId].ExpectedPoints;
        int cost = squadPlayerIds.Sum(playerId => players[playerId].PriceTenths);
        return new(
            startingPoints + captainBonus,
            startingPoints,
            captainBonus,
            cost,
            1000 - cost,
            squadPlayerIds);
    }

    private static void EnsurePlayerPoolLineage(
        ForecastSelectionSource source,
        ForecastPlayerPool playerPool)
    {
        PlayerGameweekForecastDocument forecast = playerPool.Forecast;
        if (!StringComparer.Ordinal.Equals(forecast.SeasonCode, source.SeasonCode)
            || forecast.Gameweek != source.Gameweek
            || forecast.DeadlineUtc != source.DeadlineUtc
            || forecast.DecisionCutoffUtc != source.Advice.DecisionCutoffUtc)
        {
            throw new InvalidOperationException(
                "The player forecast artifact does not match the selection forecast.");
        }
    }

    private static async Task<ForecastPlayerPool?> ReadPlayerPoolAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long baselineForecastArtifactId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                player_artifact.forecast_artifact_id,
                player_artifact.document_json,
                player_artifact.content_sha256
            FROM baseline_forecast_artifacts AS baseline_artifact
            INNER JOIN player_gameweek_forecast_artifacts AS player_artifact
                ON player_artifact.official_capture_id = baseline_artifact.capture_id
            WHERE baseline_artifact.artifact_id = $baselineForecastArtifactId
              AND player_artifact.model_key =
                  'official-market-baseline-v0-player-table';
            """;
        command.Parameters.AddWithValue(
            "$baselineForecastArtifactId",
            baselineForecastArtifactId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        long artifactId = reader.GetInt64(0);
        string documentJson = reader.GetString(1);
        string contentHash = reader.GetString(2);
        if (!StringComparer.Ordinal.Equals(Hash(documentJson), contentHash))
        {
            throw new InvalidOperationException(
                "The player forecast artifact content hash is invalid.");
        }
        PlayerGameweekForecastDocument forecast =
            JsonSerializer.Deserialize<PlayerGameweekForecastDocument>(
                documentJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The player forecast artifact document is invalid.");
        if (!StringComparer.Ordinal.Equals(
                forecast.ModelKey,
                "official-market-baseline-v0-player-table")
            || !StringComparer.Ordinal.Equals(
                forecast.Status,
                "provisional-unvalidated")
            || !StringComparer.Ordinal.Equals(
                forecast.DistributionStatus,
                "interval-only-uncalibrated")
            || forecast.Players.Count == 0)
        {
            throw new InvalidOperationException(
                "The player forecast artifact has the wrong identity.");
        }

        return new(artifactId, contentHash, forecast);
    }

    private static async Task<SelectionRevisionRow?> ReadLatestRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seasonCode,
        int gameweek,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT selection_revision_id
            FROM selection_revisions
            WHERE season_code = $seasonCode
              AND gameweek = $gameweek
            ORDER BY revision DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$seasonCode", seasonCode);
        command.Parameters.AddWithValue("$gameweek", gameweek);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long selectionRevisionId
            ? await ReadRowAsync(
                connection,
                transaction,
                selectionRevisionId,
                cancellationToken)
            : null;
    }

    private static async Task<SelectionRevisionRow?> ReadRowAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long selectionRevisionId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                selection_revision_id,
                schema_version,
                revision,
                supersedes_selection_revision_id,
                season_code,
                gameweek,
                deadline_utc,
                forecast_artifact_id,
                forecast_artifact_content_sha256,
                selection_json,
                selection_content_sha256,
                created_at_utc,
                locked_at_utc
            FROM selection_revisions
            WHERE selection_revision_id = $selectionRevisionId;
            """;
        command.Parameters.AddWithValue(
            "$selectionRevisionId",
            selectionRevisionId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.GetString(4),
            reader.GetInt32(5),
            ParseUtc(reader.GetString(6)),
            reader.GetInt64(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            ParseUtc(reader.GetString(11)),
            reader.IsDBNull(12) ? null : ParseUtc(reader.GetString(12)));
    }

    private static SelectionRevisionDocument Materialize(
        SelectionRevisionRow row,
        DateTimeOffset now)
    {
        string actualHash = Hash(row.SelectionJson);
        if (!StringComparer.Ordinal.Equals(
            actualHash,
            row.SelectionContentHash))
        {
            throw new InvalidOperationException(
                "The stored selection revision content hash is invalid.");
        }
        LockedSelectionDocument selection =
            JsonSerializer.Deserialize<LockedSelectionDocument>(
                row.SelectionJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The stored selection revision is invalid.");
        string status = row.LockedAtUtc is null
            ? now >= row.DeadlineUtc ? "expired" : "draft"
            : now >= row.DeadlineUtc ? "frozen" : "locked";
        return new(
            row.SchemaVersion,
            row.SelectionRevisionId,
            row.Revision,
            row.SupersedesSelectionRevisionId,
            row.SeasonCode,
            row.Gameweek,
            row.DeadlineUtc,
            row.ForecastArtifactId,
            row.ForecastArtifactContentHash,
            row.SelectionContentHash,
            row.CreatedAtUtc,
            row.LockedAtUtc,
            status,
            status == "draft",
            selection);
    }

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static string Hash(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record ForecastSelectionSource(
        long ForecastArtifactId,
        string ForecastArtifactContentHash,
        string SeasonCode,
        int Gameweek,
        DateTimeOffset DeadlineUtc,
        string SelectionJson,
        string SelectionContentHash,
        GameweekAdviceDocument Advice);

    private sealed record ForecastPlayerPool(
        long ArtifactId,
        string ContentHash,
        PlayerGameweekForecastDocument Forecast);

    private sealed record SelectionRevisionRow(
        long SelectionRevisionId,
        string SchemaVersion,
        int Revision,
        long? SupersedesSelectionRevisionId,
        string SeasonCode,
        int Gameweek,
        DateTimeOffset DeadlineUtc,
        long ForecastArtifactId,
        string ForecastArtifactContentHash,
        string SelectionJson,
        string SelectionContentHash,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? LockedAtUtc);
}
