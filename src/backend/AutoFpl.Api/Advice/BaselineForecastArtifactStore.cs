using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Advice;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Advice;

public sealed class BaselineForecastArtifactStore
{
    public const string ModelKey = "official-market-baseline-v0";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly OfficialDecisionRoomPreviewStore _previewStore;
    private readonly TimeProvider _timeProvider;

    public BaselineForecastArtifactStore(
        DatabaseOptions options,
        OfficialDecisionRoomPreviewStore previewStore,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _previewStore =
            previewStore ?? throw new ArgumentNullException(nameof(previewStore));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<GameweekAdviceDocument?> RefreshLatestAsync(
        CancellationToken cancellationToken = default)
    {
        OfficialDecisionRoomPreview? preview =
            await _previewStore.GetLatestAsync(cancellationToken);
        if (preview is null)
        {
            return null;
        }

        GameweekAdviceDocument advice =
            DemoGameweekAdvice.Create(officialPreview: preview);
        string documentJson = JsonSerializer.Serialize(advice, JsonOptions);
        string contentHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(documentJson)));

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT OR IGNORE INTO baseline_forecast_artifacts (
                    schema_version,
                    model_key,
                    capture_id,
                    document_json,
                    content_sha256,
                    created_at_utc
                )
                VALUES (
                    '1.0',
                    $modelKey,
                    $captureId,
                    $documentJson,
                    $contentHash,
                    $createdAtUtc
                );
                """;
            insert.Parameters.AddWithValue("$modelKey", ModelKey);
            insert.Parameters.AddWithValue("$captureId", preview.CaptureId);
            insert.Parameters.AddWithValue("$documentJson", documentJson);
            insert.Parameters.AddWithValue("$contentHash", contentHash);
            insert.Parameters.AddWithValue(
                "$createdAtUtc",
                _timeProvider.GetUtcNow().ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        ForecastArtifact artifact = await ReadForCaptureAsync(
            connection,
            transaction,
            preview.CaptureId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The Baseline v0 forecast artifact was not persisted.");
        if (!StringComparer.Ordinal.Equals(artifact.ContentHash, contentHash))
        {
            throw new InvalidOperationException(
                "Baseline v0 changed for an existing capture. "
                + "Increment the model key before persisting a different forecast.");
        }

        await transaction.CommitAsync(cancellationToken);
        return Materialize(artifact);
    }

    public async Task<GameweekAdviceDocument?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                artifact.artifact_id,
                artifact.document_json,
                artifact.content_sha256
            FROM baseline_forecast_artifacts AS artifact
            INNER JOIN official_fpl_captures AS capture
                ON capture.capture_id = artifact.capture_id
            WHERE artifact.model_key = $modelKey
            ORDER BY
                capture.available_at_utc DESC,
                artifact.artifact_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$modelKey", ModelKey);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return Materialize(
            new(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
    }

    private static async Task<ForecastArtifact?> ReadForCaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT artifact_id, document_json, content_sha256
            FROM baseline_forecast_artifacts
            WHERE capture_id = $captureId
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

    private static GameweekAdviceDocument Materialize(ForecastArtifact artifact)
    {
        string actualHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(artifact.DocumentJson)));
        if (!StringComparer.Ordinal.Equals(actualHash, artifact.ContentHash))
        {
            throw new InvalidOperationException(
                "The stored Baseline v0 forecast content hash is invalid.");
        }

        GameweekAdviceDocument advice =
            JsonSerializer.Deserialize<GameweekAdviceDocument>(
                artifact.DocumentJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The stored Baseline v0 forecast document is invalid.");
        if (advice.IsSynthetic
            || !StringComparer.Ordinal.Equals(advice.EvidenceStatus, ModelKey))
        {
            throw new InvalidOperationException(
                "The stored Baseline v0 forecast document has the wrong evidence identity.");
        }

        return advice with
        {
            ForecastArtifactId = artifact.ArtifactId,
            ForecastArtifactContentHash = artifact.ContentHash,
        };
    }

    private sealed record ForecastArtifact(
        long ArtifactId,
        string DocumentJson,
        string ContentHash);
}
