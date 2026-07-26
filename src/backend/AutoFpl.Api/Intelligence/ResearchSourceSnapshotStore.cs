using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Intelligence;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Intelligence;

public sealed class ResearchSourceSnapshotStore
{
    public const string TransportVersion =
        "spider-mcp/de35b3a9dd740542070fa2ee0e70bc804dde07ee";

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public ResearchSourceSnapshotStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ResearchSourceSnapshotDocument> PersistAsync(
        ResearchSourceDefinition source,
        SpiderScrapeResult scrape,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(scrape);

        DateTimeOffset retrievedAtUtc =
            _timeProvider.GetUtcNow().ToUniversalTime();
        byte[] content = Encoding.UTF8.GetBytes(scrape.Content);
        string contentSha256 =
            Convert.ToHexStringLower(SHA256.HashData(content));
        byte[] compressed = Compress(content);

        await using var connection =
            new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        TargetContext target = await GetTargetContextAsync(
            connection,
            transaction,
            retrievedAtUtc,
            cancellationToken);
        long? existingId = await FindExistingAsync(
            connection,
            transaction,
            source.SourceKey,
            target.IdentityCaptureId,
            contentSha256,
            cancellationToken);
        long snapshotId;
        if (existingId is not null)
        {
            snapshotId = existingId.Value;
        }
        else
        {
            int revision = await GetNextRevisionAsync(
                connection,
                transaction,
                source.SourceKey,
                cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO research_source_snapshots (
                    schema_version,
                    status,
                    source_key,
                    source_class,
                    canonical_url,
                    final_url,
                    dependence_group,
                    transport_key,
                    transport_version,
                    season_code,
                    gameweek,
                    deadline_utc,
                    identity_capture_id,
                    retrieved_at_utc,
                    available_at_utc,
                    source_revision,
                    content_sha256,
                    content_bytes,
                    content_brotli,
                    created_at_utc
                )
                VALUES (
                    '1.0',
                    'shadow-only',
                    $sourceKey,
                    $sourceClass,
                    $canonicalUrl,
                    $finalUrl,
                    $dependenceGroup,
                    'spider-mcp',
                    $transportVersion,
                    $seasonCode,
                    $gameweek,
                    $deadlineUtc,
                    $identityCaptureId,
                    $retrievedAtUtc,
                    $retrievedAtUtc,
                    $sourceRevision,
                    $contentSha256,
                    $contentBytes,
                    $contentBrotli,
                    $retrievedAtUtc
                );
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$sourceKey", source.SourceKey);
            command.Parameters.AddWithValue("$sourceClass", source.SourceClass);
            command.Parameters.AddWithValue(
                "$canonicalUrl",
                source.CanonicalUri.AbsoluteUri);
            command.Parameters.AddWithValue(
                "$finalUrl",
                scrape.FinalUri.AbsoluteUri);
            command.Parameters.AddWithValue(
                "$dependenceGroup",
                source.DependenceGroup);
            command.Parameters.AddWithValue(
                "$transportVersion",
                TransportVersion);
            command.Parameters.AddWithValue("$seasonCode", target.SeasonCode);
            command.Parameters.AddWithValue("$gameweek", target.Gameweek);
            command.Parameters.AddWithValue(
                "$deadlineUtc",
                FormatUtc(target.DeadlineUtc));
            command.Parameters.AddWithValue(
                "$identityCaptureId",
                target.IdentityCaptureId);
            command.Parameters.AddWithValue(
                "$retrievedAtUtc",
                FormatUtc(retrievedAtUtc));
            command.Parameters.AddWithValue("$sourceRevision", revision);
            command.Parameters.AddWithValue("$contentSha256", contentSha256);
            command.Parameters.AddWithValue("$contentBytes", content.Length);
            command.Parameters.AddWithValue("$contentBrotli", compressed);
            snapshotId = (long)(await command.ExecuteScalarAsync(
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The research snapshot did not return an identifier."));
        }

        ResearchSourceSnapshotDocument snapshot =
            await ReadAsync(
                connection,
                transaction,
                snapshotId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The research snapshot was not persisted.");
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public async Task<ResearchSourceInventoryDocument> GetInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                snapshot_id,
                schema_version,
                status,
                source_key,
                source_class,
                canonical_url,
                final_url,
                dependence_group,
                transport_key,
                transport_version,
                season_code,
                gameweek,
                deadline_utc,
                identity_capture_id,
                retrieved_at_utc,
                available_at_utc,
                source_revision,
                content_sha256,
                content_bytes,
                created_at_utc
            FROM research_source_snapshots
            WHERE snapshot_id IN (
                SELECT MAX(snapshot_id)
                FROM research_source_snapshots
                GROUP BY source_key
            )
            ORDER BY source_key;
            """;
        var snapshots = new List<ResearchSourceSnapshotDocument>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            snapshots.Add(Read(reader));
        }

        return new(
            "1.0",
            ResearchSourceRegistry.All.Select(source => source.ToDocument()).ToArray(),
            snapshots);
    }

    internal async Task<ResearchSourceSnapshotContent?> ReadContentAsync(
        long snapshotId,
        CancellationToken cancellationToken = default)
    {
        if (snapshotId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotId));
        }

        await using var connection =
            new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                snapshot_id,
                schema_version,
                status,
                source_key,
                source_class,
                canonical_url,
                final_url,
                dependence_group,
                transport_key,
                transport_version,
                season_code,
                gameweek,
                deadline_utc,
                identity_capture_id,
                retrieved_at_utc,
                available_at_utc,
                source_revision,
                content_sha256,
                content_bytes,
                created_at_utc,
                content_brotli
            FROM research_source_snapshots
            WHERE snapshot_id = $snapshotId;
            """;
        command.Parameters.AddWithValue("$snapshotId", snapshotId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new(Read(reader), Decompress((byte[])reader[20]));
    }

    internal async Task<IReadOnlyDictionary<int, int>> GetPlayerIdsByCodeAsync(
        long identityCaptureId,
        CancellationToken cancellationToken = default)
    {
        if (identityCaptureId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(identityCaptureId));
        }

        await using var connection =
            new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT code, player_id
            FROM official_fpl_players
            WHERE capture_id = $captureId
            ORDER BY code;
            """;
        command.Parameters.AddWithValue("$captureId", identityCaptureId);
        var identities = new Dictionary<int, int>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!identities.TryAdd(reader.GetInt32(0), reader.GetInt32(1)))
            {
                throw new ResearchSourceSnapshotException(
                    "The official identity capture contains a duplicate player code.");
            }
        }

        return identities;
    }

    private static async Task<TargetContext> GetTargetContextAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                capture.capture_id,
                capture.season_code,
                event.event_id,
                event.deadline_utc
            FROM official_fpl_captures AS capture
            INNER JOIN official_fpl_events AS event
                ON event.capture_id = capture.capture_id
               AND event.event_id = capture.next_gameweek_number
            WHERE capture.available_at_utc <= $retrievedAtUtc
            ORDER BY capture.available_at_utc DESC, capture.capture_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$retrievedAtUtc",
            FormatUtc(retrievedAtUtc));
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ResearchSourceSnapshotException(
                "No cutoff-eligible official target exists for the research snapshot.");
        }

        return new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt32(2),
            ParseUtc(reader.GetString(3)));
    }

    private static async Task<long?> FindExistingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceKey,
        long identityCaptureId,
        string contentSha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT snapshot_id
            FROM research_source_snapshots
            WHERE source_key = $sourceKey
              AND identity_capture_id = $identityCaptureId
              AND content_sha256 = $contentSha256;
            """;
        command.Parameters.AddWithValue("$sourceKey", sourceKey);
        command.Parameters.AddWithValue("$identityCaptureId", identityCaptureId);
        command.Parameters.AddWithValue("$contentSha256", contentSha256);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task<int> GetNextRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceKey,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COALESCE(MAX(source_revision), 0) + 1
            FROM research_source_snapshots
            WHERE source_key = $sourceKey;
            """;
        command.Parameters.AddWithValue("$sourceKey", sourceKey);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<ResearchSourceSnapshotDocument?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long snapshotId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                snapshot_id,
                schema_version,
                status,
                source_key,
                source_class,
                canonical_url,
                final_url,
                dependence_group,
                transport_key,
                transport_version,
                season_code,
                gameweek,
                deadline_utc,
                identity_capture_id,
                retrieved_at_utc,
                available_at_utc,
                source_revision,
                content_sha256,
                content_bytes,
                created_at_utc
            FROM research_source_snapshots
            WHERE snapshot_id = $snapshotId;
            """;
        command.Parameters.AddWithValue("$snapshotId", snapshotId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static ResearchSourceSnapshotDocument Read(SqliteDataReader reader)
    {
        DateTimeOffset deadlineUtc = ParseUtc(reader.GetString(12));
        DateTimeOffset availableAtUtc = ParseUtc(reader.GetString(15));
        return new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetInt32(11),
            deadlineUtc,
            reader.GetInt64(13),
            ParseUtc(reader.GetString(14)),
            availableAtUtc,
            availableAtUtc <= deadlineUtc,
            reader.GetInt32(16),
            reader.GetString(17),
            reader.GetInt32(18),
            ParseUtc(reader.GetString(19)));
    }

    private static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(
            output,
            CompressionLevel.SmallestSize,
            leaveOpen: true))
        {
            compressor.Write(content);
        }

        return output.ToArray();
    }

    private static string Decompress(byte[] content)
    {
        using var input = new MemoryStream(content);
        using var decompressor = new BrotliStream(
            input,
            CompressionMode.Decompress);
        using var reader = new StreamReader(
            decompressor,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private sealed record TargetContext(
        long IdentityCaptureId,
        string SeasonCode,
        int Gameweek,
        DateTimeOffset DeadlineUtc);
}

internal sealed record ResearchSourceSnapshotContent(
    ResearchSourceSnapshotDocument Snapshot,
    string Content);
