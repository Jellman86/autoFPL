using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using AutoFpl.Contracts.Snapshots;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Persistence;

public sealed class DecisionSnapshotStore
{
    private readonly DatabaseOptions _options;

    public DecisionSnapshotStore(DatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string DatabasePath => _options.DatabasePath;

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            transaction: null,
            "PRAGMA journal_mode = WAL;",
            cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            transaction: null,
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                applied_at_utc TEXT NOT NULL
            );
            """,
            cancellationToken);
        await ValidateMigrationHistoryAsync(connection, cancellationToken);

        foreach (DatabaseMigration migration in DatabaseMigrations.All)
        {
            await using SqliteTransaction transaction =
                connection.BeginTransaction(deferred: false);
            await using SqliteCommand appliedCommand = connection.CreateCommand();
            appliedCommand.Transaction = transaction;
            appliedCommand.CommandText =
                "SELECT COUNT(*) FROM schema_migrations WHERE version = $version;";
            appliedCommand.Parameters.AddWithValue("$version", migration.Version);
            long applied = (long)(await appliedCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (applied != 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                migration.Sql,
                cancellationToken);

            await using SqliteCommand recordCommand = connection.CreateCommand();
            recordCommand.Transaction = transaction;
            recordCommand.CommandText =
                """
                INSERT INTO schema_migrations (version, name, applied_at_utc)
                VALUES ($version, $name, $appliedAtUtc);
                """;
            recordCommand.Parameters.AddWithValue("$version", migration.Version);
            recordCommand.Parameters.AddWithValue("$name", migration.Name);
            recordCommand.Parameters.AddWithValue("$appliedAtUtc", FormatUtc(DateTimeOffset.UtcNow));
            await recordCommand.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task<DecisionSnapshotDocument> CreateSnapshotAsync(
        DecisionSnapshotCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();

        long seasonId = await EnsureSeasonAsync(
            connection,
            transaction,
            command.SeasonCode,
            cancellationToken);
        long gameweekId = await EnsureGameweekAsync(
            connection,
            transaction,
            seasonId,
            command.Gameweek,
            command.DeadlineUtc,
            cancellationToken);

        foreach (SnapshotPlayer player in command.Players)
        {
            await using SqliteCommand playerCommand = connection.CreateCommand();
            playerCommand.Transaction = transaction;
            playerCommand.CommandText =
                """
                INSERT INTO players (player_id, created_at_utc)
                VALUES ($playerId, $createdAtUtc)
                ON CONFLICT (player_id) DO NOTHING;
                """;
            playerCommand.Parameters.AddWithValue("$playerId", player.PlayerId);
            playerCommand.Parameters.AddWithValue("$createdAtUtc", FormatUtc(now));
            await playerCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        long squadId = await InsertSquadAsync(
            connection,
            transaction,
            gameweekId,
            command,
            now,
            cancellationToken);
        long selectionId = await InsertSelectionAsync(
            connection,
            transaction,
            squadId,
            command,
            now,
            cancellationToken);

        foreach (SnapshotObservation observation in command.Observations)
        {
            await InsertObservationAsync(
                connection,
                transaction,
                gameweekId,
                observation,
                now,
                cancellationToken);
        }

        IReadOnlyList<SourceObservationDocument> selectedObservations =
            await ReadCutoffObservationsAsync(
                connection,
                transaction,
                gameweekId,
                squadId,
                command.DecisionCutoffUtc,
                cancellationToken);
        if (selectedObservations.Count == 0)
        {
            throw new DecisionSnapshotPersistenceException(
                "snapshot.observations.none_available_at_cutoff",
                "decisionCutoffUtc");
        }

        int revision = await ResolveSnapshotRevisionAsync(
            connection,
            transaction,
            gameweekId,
            command.SupersedesSnapshotId,
            cancellationToken);
        string contentHash = ComputeContentHash(command, selectedObservations);

        await using SqliteCommand snapshotCommand = connection.CreateCommand();
        snapshotCommand.Transaction = transaction;
        snapshotCommand.CommandText =
            """
            INSERT INTO decision_snapshots (
                schema_version,
                revision,
                supersedes_snapshot_id,
                gameweek_id,
                squad_id,
                selection_id,
                season_code,
                gameweek_number,
                deadline_utc,
                decision_cutoff_utc,
                content_hash,
                created_at_utc
            )
            VALUES (
                $schemaVersion,
                $revision,
                $supersedesSnapshotId,
                $gameweekId,
                $squadId,
                $selectionId,
                $seasonCode,
                $gameweek,
                $deadlineUtc,
                $decisionCutoffUtc,
                $contentHash,
                $createdAtUtc
            );
            SELECT last_insert_rowid();
            """;
        snapshotCommand.Parameters.AddWithValue("$schemaVersion", command.SchemaVersion);
        snapshotCommand.Parameters.AddWithValue("$revision", revision);
        snapshotCommand.Parameters.AddWithValue(
            "$supersedesSnapshotId",
            (object?)command.SupersedesSnapshotId ?? DBNull.Value);
        snapshotCommand.Parameters.AddWithValue("$gameweekId", gameweekId);
        snapshotCommand.Parameters.AddWithValue("$squadId", squadId);
        snapshotCommand.Parameters.AddWithValue("$selectionId", selectionId);
        snapshotCommand.Parameters.AddWithValue("$seasonCode", command.SeasonCode);
        snapshotCommand.Parameters.AddWithValue("$gameweek", command.Gameweek);
        snapshotCommand.Parameters.AddWithValue("$deadlineUtc", FormatUtc(command.DeadlineUtc));
        snapshotCommand.Parameters.AddWithValue(
            "$decisionCutoffUtc",
            FormatUtc(command.DecisionCutoffUtc));
        snapshotCommand.Parameters.AddWithValue("$contentHash", contentHash);
        snapshotCommand.Parameters.AddWithValue("$createdAtUtc", FormatUtc(now));
        long snapshotId =
            (long)(await snapshotCommand.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("SQLite did not return a snapshot ID."));

        foreach (SourceObservationDocument observation in selectedObservations)
        {
            await using SqliteCommand linkCommand = connection.CreateCommand();
            linkCommand.Transaction = transaction;
            linkCommand.CommandText =
                """
                INSERT INTO decision_snapshot_observations (snapshot_id, observation_id)
                VALUES ($snapshotId, $observationId);
                """;
            linkCommand.Parameters.AddWithValue("$snapshotId", snapshotId);
            linkCommand.Parameters.AddWithValue("$observationId", observation.ObservationId);
            await linkCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetSnapshotAsync(snapshotId, cancellationToken)
            ?? throw new InvalidOperationException("The committed snapshot could not be read back.");
    }

    public async Task<DecisionSnapshotDocument?> GetSnapshotAsync(
        long snapshotId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);

        await using SqliteCommand headerCommand = connection.CreateCommand();
        headerCommand.CommandText =
            """
            SELECT
                snapshot_id,
                schema_version,
                revision,
                supersedes_snapshot_id,
                season_code,
                gameweek_number,
                deadline_utc,
                decision_cutoff_utc,
                created_at_utc,
                content_hash,
                squad_id,
                selection_id
            FROM decision_snapshots
            WHERE snapshot_id = $snapshotId;
            """;
        headerCommand.Parameters.AddWithValue("$snapshotId", snapshotId);

        await using SqliteDataReader headerReader =
            await headerCommand.ExecuteReaderAsync(cancellationToken);
        if (!await headerReader.ReadAsync(cancellationToken))
        {
            return null;
        }

        string schemaVersion = headerReader.GetString(1);
        int revision = headerReader.GetInt32(2);
        long? supersedesSnapshotId = headerReader.IsDBNull(3) ? null : headerReader.GetInt64(3);
        string seasonCode = headerReader.GetString(4);
        int gameweek = headerReader.GetInt32(5);
        DateTimeOffset deadlineUtc = ParseUtc(headerReader.GetString(6));
        DateTimeOffset decisionCutoffUtc = ParseUtc(headerReader.GetString(7));
        DateTimeOffset createdAtUtc = ParseUtc(headerReader.GetString(8));
        string contentHash = headerReader.GetString(9);
        long squadId = headerReader.GetInt64(10);
        long selectionId = headerReader.GetInt64(11);
        await headerReader.DisposeAsync();

        PersistedSquadDocument squad = await ReadSquadAsync(
            connection,
            squadId,
            cancellationToken);
        PersistedSelectionDocument selection = await ReadSelectionAsync(
            connection,
            selectionId,
            cancellationToken);
        IReadOnlyList<SourceObservationDocument> observations =
            await ReadSnapshotObservationsAsync(connection, snapshotId, cancellationToken);

        return new(
            snapshotId,
            schemaVersion,
            revision,
            supersedesSnapshotId,
            seasonCode,
            gameweek,
            deadlineUtc,
            decisionCutoffUtc,
            createdAtUtc,
            contentHash,
            squad,
            selection,
            observations);
    }

    public async Task<DecisionSnapshotDocument?> GetLatestSnapshotAsync(
        string seasonCode,
        int gameweek,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seasonCode);

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT snapshot_id
            FROM decision_snapshots
            WHERE season_code = $seasonCode
              AND gameweek_number = $gameweek
            ORDER BY revision DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$seasonCode", seasonCode);
        command.Parameters.AddWithValue("$gameweek", gameweek);
        object? snapshotId = await command.ExecuteScalarAsync(cancellationToken);
        return snapshotId is long id
            ? await GetSnapshotAsync(id, cancellationToken)
            : null;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM schema_migrations WHERE version = $version;";
            command.Parameters.AddWithValue("$version", DatabaseMigrations.CurrentVersion);
            long count = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            return count == 1;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public async Task<string> IntegrityCheckAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand integrityCommand = connection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA integrity_check;";
        string result = (string)(await integrityCommand.ExecuteScalarAsync(cancellationToken) ?? string.Empty);
        if (!StringComparer.Ordinal.Equals(result, "ok"))
        {
            return result;
        }

        await using SqliteCommand foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
        await using SqliteDataReader foreignKeyReader =
            await foreignKeyCommand.ExecuteReaderAsync(cancellationToken);
        return await foreignKeyReader.ReadAsync(cancellationToken)
            ? "foreign-key-violation"
            : "ok";
    }

    public async Task BackupAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string fullDestinationPath = Path.GetFullPath(destinationPath);
        if (StringComparer.Ordinal.Equals(fullDestinationPath, _options.DatabasePath))
        {
            throw new InvalidOperationException("Backup destination must differ from the live database.");
        }

        if (File.Exists(fullDestinationPath))
        {
            throw new IOException("Backup destination already exists.");
        }

        string? directory = Path.GetDirectoryName(fullDestinationPath);
        if (directory is null)
        {
            throw new InvalidOperationException("Backup destination must identify a file.");
        }

        Directory.CreateDirectory(directory);
        await using SqliteConnection source = await OpenConnectionAsync(cancellationToken);
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = fullDestinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = DatabaseOptions.BusyTimeoutSeconds,
        };
        await using var destination = new SqliteConnection(destinationBuilder.ToString());
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            transaction: null,
            $"PRAGMA busy_timeout = {DatabaseOptions.BusyTimeoutSeconds * 1000};",
            cancellationToken);
        return connection;
    }

    private static async Task<long> EnsureSeasonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seasonCode,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            "INSERT INTO seasons (code) VALUES ($code) ON CONFLICT (code) DO NOTHING;";
        insertCommand.Parameters.AddWithValue("$code", seasonCode);
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);

        await using SqliteCommand selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText = "SELECT season_id FROM seasons WHERE code = $code;";
        selectCommand.Parameters.AddWithValue("$code", seasonCode);
        return (long)(await selectCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("SQLite did not return a season ID."));
    }

    private static async Task<long> EnsureGameweekAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long seasonId,
        int gameweek,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO gameweeks (season_id, number, deadline_utc)
            VALUES ($seasonId, $number, $deadlineUtc)
            ON CONFLICT (season_id, number)
            DO UPDATE SET deadline_utc = excluded.deadline_utc;
            """;
        insertCommand.Parameters.AddWithValue("$seasonId", seasonId);
        insertCommand.Parameters.AddWithValue("$number", gameweek);
        insertCommand.Parameters.AddWithValue("$deadlineUtc", FormatUtc(deadlineUtc));
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);

        await using SqliteCommand selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT gameweek_id
            FROM gameweeks
            WHERE season_id = $seasonId AND number = $number;
            """;
        selectCommand.Parameters.AddWithValue("$seasonId", seasonId);
        selectCommand.Parameters.AddWithValue("$number", gameweek);
        return (long)(await selectCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("SQLite did not return a gameweek ID."));
    }

    private static async Task<long> InsertSquadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long gameweekId,
        DecisionSnapshotCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand squadCommand = connection.CreateCommand();
        squadCommand.Transaction = transaction;
        squadCommand.CommandText =
            """
            INSERT INTO squads (gameweek_id, budget_tenths, created_at_utc)
            VALUES ($gameweekId, $budgetTenths, $createdAtUtc);
            SELECT last_insert_rowid();
            """;
        squadCommand.Parameters.AddWithValue("$gameweekId", gameweekId);
        squadCommand.Parameters.AddWithValue("$budgetTenths", command.BudgetTenths);
        squadCommand.Parameters.AddWithValue("$createdAtUtc", FormatUtc(now));
        long squadId =
            (long)(await squadCommand.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("SQLite did not return a squad ID."));

        for (int index = 0; index < command.Players.Count; index++)
        {
            SnapshotPlayer player = command.Players[index];
            await using SqliteCommand playerCommand = connection.CreateCommand();
            playerCommand.Transaction = transaction;
            playerCommand.CommandText =
                """
                INSERT INTO squad_players (
                    squad_id,
                    player_id,
                    squad_order,
                    display_name,
                    club_id,
                    position,
                    price_tenths
                )
                VALUES (
                    $squadId,
                    $playerId,
                    $squadOrder,
                    $displayName,
                    $clubId,
                    $position,
                    $priceTenths
                );
                """;
            playerCommand.Parameters.AddWithValue("$squadId", squadId);
            playerCommand.Parameters.AddWithValue("$playerId", player.PlayerId);
            playerCommand.Parameters.AddWithValue("$squadOrder", index + 1);
            playerCommand.Parameters.AddWithValue("$displayName", player.DisplayName);
            playerCommand.Parameters.AddWithValue("$clubId", player.ClubId);
            playerCommand.Parameters.AddWithValue("$position", player.Position);
            playerCommand.Parameters.AddWithValue("$priceTenths", player.PriceTenths);
            await playerCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return squadId;
    }

    private static async Task<long> InsertSelectionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long squadId,
        DecisionSnapshotCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand selectionCommand = connection.CreateCommand();
        selectionCommand.Transaction = transaction;
        selectionCommand.CommandText =
            """
            INSERT INTO selections (
                squad_id,
                captain_player_id,
                vice_captain_player_id,
                created_at_utc
            )
            VALUES ($squadId, $captainPlayerId, $viceCaptainPlayerId, $createdAtUtc);
            SELECT last_insert_rowid();
            """;
        selectionCommand.Parameters.AddWithValue("$squadId", squadId);
        selectionCommand.Parameters.AddWithValue("$captainPlayerId", command.CaptainPlayerId);
        selectionCommand.Parameters.AddWithValue(
            "$viceCaptainPlayerId",
            command.ViceCaptainPlayerId);
        selectionCommand.Parameters.AddWithValue("$createdAtUtc", FormatUtc(now));
        long selectionId =
            (long)(await selectionCommand.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("SQLite did not return a selection ID."));

        foreach (int playerId in command.StartingPlayerIds)
        {
            await InsertSelectionPlayerAsync(
                connection,
                transaction,
                selectionId,
                squadId,
                playerId,
                "starter",
                benchOrder: null,
                cancellationToken);
        }

        await InsertSelectionPlayerAsync(
            connection,
            transaction,
            selectionId,
            squadId,
            command.ReplacementGoalkeeperPlayerId,
            "replacement-goalkeeper",
            benchOrder: 1,
            cancellationToken);
        for (int index = 0; index < command.OutfieldSubstitutePlayerIds.Count; index++)
        {
            await InsertSelectionPlayerAsync(
                connection,
                transaction,
                selectionId,
                squadId,
                command.OutfieldSubstitutePlayerIds[index],
                "outfield-substitute",
                benchOrder: index + 2,
                cancellationToken);
        }

        return selectionId;
    }

    private static async Task InsertSelectionPlayerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long selectionId,
        long squadId,
        int playerId,
        string role,
        int? benchOrder,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO selection_players (
                selection_id,
                squad_id,
                player_id,
                role,
                bench_order
            )
            VALUES ($selectionId, $squadId, $playerId, $role, $benchOrder);
            """;
        command.Parameters.AddWithValue("$selectionId", selectionId);
        command.Parameters.AddWithValue("$squadId", squadId);
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$benchOrder", (object?)benchOrder ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertObservationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long gameweekId,
        SnapshotObservation observation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        int revision = 1;
        if (observation.SupersedesObservationId is null)
        {
            await using SqliteCommand existingCommand = connection.CreateCommand();
            existingCommand.Transaction = transaction;
            existingCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM source_observations
                WHERE gameweek_id = $gameweekId
                  AND player_id = $playerId
                  AND source_key = $sourceKey
                  AND metric = $metric;
                """;
            AddObservationIdentityParameters(existingCommand, gameweekId, observation);
            long existing =
                (long)(await existingCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (existing != 0)
            {
                throw new DecisionSnapshotPersistenceException(
                    "observation.supersedes.required",
                    "supersedesObservationId");
            }
        }
        else
        {
            await using SqliteCommand supersededCommand = connection.CreateCommand();
            supersededCommand.Transaction = transaction;
            supersededCommand.CommandText =
                """
                SELECT revision, available_at_utc
                FROM source_observations
                WHERE observation_id = $observationId
                  AND gameweek_id = $gameweekId
                  AND player_id = $playerId
                  AND source_key = $sourceKey
                  AND metric = $metric
                  AND NOT EXISTS (
                      SELECT 1
                      FROM source_observations correction
                      WHERE correction.supersedes_observation_id = $observationId
                  );
                """;
            supersededCommand.Parameters.AddWithValue(
                "$observationId",
                observation.SupersedesObservationId.Value);
            AddObservationIdentityParameters(supersededCommand, gameweekId, observation);
            await using SqliteDataReader reader =
                await supersededCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new DecisionSnapshotPersistenceException(
                    "observation.supersedes.invalid",
                    "supersedesObservationId");
            }

            revision = reader.GetInt32(0) + 1;
            DateTimeOffset supersededAvailableAt = ParseUtc(reader.GetString(1));
            if (observation.AvailableAtUtc < supersededAvailableAt)
            {
                throw new DecisionSnapshotPersistenceException(
                    "observation.available_at.precedes_superseded",
                    "availableAtUtc");
            }
        }

        await using SqliteCommand insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO source_observations (
                gameweek_id,
                player_id,
                source_key,
                metric,
                value_decimal,
                revision,
                supersedes_observation_id,
                observed_at_utc,
                retrieved_at_utc,
                available_at_utc,
                created_at_utc
            )
            VALUES (
                $gameweekId,
                $playerId,
                $sourceKey,
                $metric,
                $value,
                $revision,
                $supersedesObservationId,
                $observedAtUtc,
                $retrievedAtUtc,
                $availableAtUtc,
                $createdAtUtc
            );
            """;
        AddObservationIdentityParameters(insertCommand, gameweekId, observation);
        insertCommand.Parameters.AddWithValue(
            "$value",
            observation.Value.ToString(CultureInfo.InvariantCulture));
        insertCommand.Parameters.AddWithValue("$revision", revision);
        insertCommand.Parameters.AddWithValue(
            "$supersedesObservationId",
            (object?)observation.SupersedesObservationId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$observedAtUtc", FormatUtc(observation.ObservedAtUtc));
        insertCommand.Parameters.AddWithValue(
            "$retrievedAtUtc",
            FormatUtc(observation.RetrievedAtUtc));
        insertCommand.Parameters.AddWithValue(
            "$availableAtUtc",
            FormatUtc(observation.AvailableAtUtc));
        insertCommand.Parameters.AddWithValue("$createdAtUtc", FormatUtc(now));
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddObservationIdentityParameters(
        SqliteCommand command,
        long gameweekId,
        SnapshotObservation observation)
    {
        command.Parameters.AddWithValue("$gameweekId", gameweekId);
        command.Parameters.AddWithValue("$playerId", observation.PlayerId);
        command.Parameters.AddWithValue("$sourceKey", observation.SourceKey);
        command.Parameters.AddWithValue("$metric", observation.Metric);
    }

    private static async Task<IReadOnlyList<SourceObservationDocument>>
        ReadCutoffObservationsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long gameweekId,
            long squadId,
            DateTimeOffset decisionCutoffUtc,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                observation.observation_id,
                observation.revision,
                observation.supersedes_observation_id,
                observation.source_key,
                observation.player_id,
                observation.metric,
                observation.value_decimal,
                observation.observed_at_utc,
                observation.retrieved_at_utc,
                observation.available_at_utc
            FROM source_observations observation
            INNER JOIN squad_players squad_player
                ON squad_player.squad_id = $squadId
               AND squad_player.player_id = observation.player_id
            WHERE observation.gameweek_id = $gameweekId
              AND observation.available_at_utc <= $decisionCutoffUtc
              AND NOT EXISTS (
                  SELECT 1
                  FROM source_observations newer
                  WHERE newer.gameweek_id = observation.gameweek_id
                    AND newer.player_id = observation.player_id
                    AND newer.source_key = observation.source_key
                    AND newer.metric = observation.metric
                    AND newer.revision > observation.revision
                    AND newer.available_at_utc <= $decisionCutoffUtc
              )
            ORDER BY
                observation.player_id,
                observation.source_key,
                observation.metric;
            """;
        command.Parameters.AddWithValue("$squadId", squadId);
        command.Parameters.AddWithValue("$gameweekId", gameweekId);
        command.Parameters.AddWithValue("$decisionCutoffUtc", FormatUtc(decisionCutoffUtc));
        return await ReadObservationsAsync(command, cancellationToken);
    }

    private static async Task<int> ResolveSnapshotRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long gameweekId,
        long? supersedesSnapshotId,
        CancellationToken cancellationToken)
    {
        if (supersedesSnapshotId is null)
        {
            await using SqliteCommand countCommand = connection.CreateCommand();
            countCommand.Transaction = transaction;
            countCommand.CommandText =
                "SELECT COUNT(*) FROM decision_snapshots WHERE gameweek_id = $gameweekId;";
            countCommand.Parameters.AddWithValue("$gameweekId", gameweekId);
            long existing = (long)(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (existing != 0)
            {
                throw new DecisionSnapshotPersistenceException(
                    "snapshot.supersedes.required",
                    "supersedesSnapshotId");
            }

            return 1;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT revision
            FROM decision_snapshots
            WHERE snapshot_id = $snapshotId
              AND gameweek_id = $gameweekId
              AND NOT EXISTS (
                  SELECT 1
                  FROM decision_snapshots correction
                  WHERE correction.supersedes_snapshot_id = $snapshotId
              );
            """;
        command.Parameters.AddWithValue("$snapshotId", supersedesSnapshotId.Value);
        command.Parameters.AddWithValue("$gameweekId", gameweekId);
        object? revision = await command.ExecuteScalarAsync(cancellationToken);
        if (revision is not long persistedRevision)
        {
            throw new DecisionSnapshotPersistenceException(
                "snapshot.supersedes.invalid",
                "supersedesSnapshotId");
        }

        return checked((int)persistedRevision + 1);
    }

    private static string ComputeContentHash(
        DecisionSnapshotCommand command,
        IReadOnlyList<SourceObservationDocument> observations)
    {
        var canonical = new StringBuilder();
        canonical.Append(command.SchemaVersion).Append('\n')
            .Append(command.SeasonCode).Append('\n')
            .Append(command.Gameweek.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(FormatUtc(command.DeadlineUtc)).Append('\n')
            .Append(FormatUtc(command.DecisionCutoffUtc)).Append('\n')
            .Append(command.BudgetTenths.ToString(CultureInfo.InvariantCulture)).Append('\n');

        foreach (SnapshotPlayer player in command.Players.OrderBy(player => player.PlayerId))
        {
            canonical.Append(player.PlayerId.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(player.DisplayName).Append('|')
                .Append(player.ClubId.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(player.Position).Append('|')
                .Append(player.PriceTenths.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        canonical.AppendJoin(',', command.StartingPlayerIds).Append('\n')
            .Append(command.CaptainPlayerId.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(command.ViceCaptainPlayerId.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(command.ReplacementGoalkeeperPlayerId.ToString(CultureInfo.InvariantCulture))
            .Append('|')
            .AppendJoin(',', command.OutfieldSubstitutePlayerIds)
            .Append('\n');

        foreach (SourceObservationDocument observation in observations)
        {
            canonical.Append(observation.PlayerId.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(observation.SourceKey)
                .Append('|')
                .Append(observation.Metric)
                .Append('|')
                .Append(observation.Revision.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(observation.Value.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(FormatUtc(observation.ObservedAtUtc))
                .Append('|')
                .Append(FormatUtc(observation.RetrievedAtUtc))
                .Append('|')
                .Append(FormatUtc(observation.AvailableAtUtc))
                .Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static async Task<PersistedSquadDocument> ReadSquadAsync(
        SqliteConnection connection,
        long squadId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand budgetCommand = connection.CreateCommand();
        budgetCommand.CommandText = "SELECT budget_tenths FROM squads WHERE squad_id = $squadId;";
        budgetCommand.Parameters.AddWithValue("$squadId", squadId);
        int budgetTenths = Convert.ToInt32(
            await budgetCommand.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);

        await using SqliteCommand playerCommand = connection.CreateCommand();
        playerCommand.CommandText =
            """
            SELECT player_id, display_name, club_id, position, price_tenths
            FROM squad_players
            WHERE squad_id = $squadId
            ORDER BY squad_order;
            """;
        playerCommand.Parameters.AddWithValue("$squadId", squadId);
        await using SqliteDataReader reader =
            await playerCommand.ExecuteReaderAsync(cancellationToken);
        var players = new List<PersistedPlayerDocument>();
        while (await reader.ReadAsync(cancellationToken))
        {
            players.Add(
                new(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt32(4)));
        }

        return new(budgetTenths, players);
    }

    private static async Task<PersistedSelectionDocument> ReadSelectionAsync(
        SqliteConnection connection,
        long selectionId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand captainCommand = connection.CreateCommand();
        captainCommand.CommandText =
            """
            SELECT captain_player_id, vice_captain_player_id
            FROM selections
            WHERE selection_id = $selectionId;
            """;
        captainCommand.Parameters.AddWithValue("$selectionId", selectionId);
        await using SqliteDataReader captainReader =
            await captainCommand.ExecuteReaderAsync(cancellationToken);
        if (!await captainReader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Persisted selection is missing.");
        }

        int captainPlayerId = captainReader.GetInt32(0);
        int viceCaptainPlayerId = captainReader.GetInt32(1);
        await captainReader.DisposeAsync();

        await using SqliteCommand playerCommand = connection.CreateCommand();
        playerCommand.CommandText =
            """
            SELECT player_id, role, bench_order
            FROM selection_players
            WHERE selection_id = $selectionId
            ORDER BY COALESCE(bench_order, 0), player_id;
            """;
        playerCommand.Parameters.AddWithValue("$selectionId", selectionId);
        await using SqliteDataReader reader =
            await playerCommand.ExecuteReaderAsync(cancellationToken);
        var starters = new List<int>();
        int replacementGoalkeeperPlayerId = 0;
        var outfieldSubstitutes = new List<int>();
        while (await reader.ReadAsync(cancellationToken))
        {
            int playerId = reader.GetInt32(0);
            string role = reader.GetString(1);
            switch (role)
            {
                case "starter":
                    starters.Add(playerId);
                    break;
                case "replacement-goalkeeper":
                    replacementGoalkeeperPlayerId = playerId;
                    break;
                case "outfield-substitute":
                    outfieldSubstitutes.Add(playerId);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown persisted selection role '{role}'.");
            }
        }

        return new(
            starters,
            captainPlayerId,
            viceCaptainPlayerId,
            replacementGoalkeeperPlayerId,
            outfieldSubstitutes);
    }

    private static async Task<IReadOnlyList<SourceObservationDocument>>
        ReadSnapshotObservationsAsync(
            SqliteConnection connection,
            long snapshotId,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                observation.observation_id,
                observation.revision,
                observation.supersedes_observation_id,
                observation.source_key,
                observation.player_id,
                observation.metric,
                observation.value_decimal,
                observation.observed_at_utc,
                observation.retrieved_at_utc,
                observation.available_at_utc
            FROM decision_snapshot_observations snapshot_observation
            INNER JOIN source_observations observation
                ON observation.observation_id = snapshot_observation.observation_id
            WHERE snapshot_observation.snapshot_id = $snapshotId
            ORDER BY
                observation.player_id,
                observation.source_key,
                observation.metric;
            """;
        command.Parameters.AddWithValue("$snapshotId", snapshotId);
        return await ReadObservationsAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<SourceObservationDocument>> ReadObservationsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var observations = new List<SourceObservationDocument>();
        while (await reader.ReadAsync(cancellationToken))
        {
            observations.Add(
                new(
                    reader.GetInt64(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt64(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                    ParseUtc(reader.GetString(7)),
                    ParseUtc(reader.GetString(8)),
                    ParseUtc(reader.GetString(9))));
        }

        return observations;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ValidateMigrationHistoryAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT version, name FROM schema_migrations ORDER BY version;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        int index = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            int version = reader.GetInt32(0);
            if (version > DatabaseMigrations.CurrentVersion
                || index >= DatabaseMigrations.All.Count)
            {
                throw new InvalidOperationException(
                    "Database schema is newer than this application.");
            }

            DatabaseMigration expected = DatabaseMigrations.All[index];
            if (version != expected.Version
                || !StringComparer.Ordinal.Equals(reader.GetString(1), expected.Name))
            {
                throw new InvalidOperationException(
                    "Database migration history is incomplete or inconsistent.");
            }

            index++;
        }
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
