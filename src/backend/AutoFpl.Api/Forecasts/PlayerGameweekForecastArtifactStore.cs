using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Advice;
using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Forecasts;

public sealed class PlayerGameweekForecastArtifactStore
{
    public const string ModelKey = "official-market-baseline-v0-player-table";
    public const string Status = "provisional-unvalidated";
    public const string DistributionStatus = "interval-only-uncalibrated";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly OfficialDecisionRoomPreviewStore _previewStore;
    private readonly TimeProvider _timeProvider;

    public PlayerGameweekForecastArtifactStore(
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

    public async Task<PlayerGameweekForecastDocument?> RefreshLatestAsync(
        CancellationToken cancellationToken = default)
    {
        OfficialPlayerForecastPreview? preview =
            await _previewStore.GetLatestPlayerForecastAsync(cancellationToken);
        if (preview is null)
        {
            return null;
        }

        var document = new PlayerGameweekForecastDocument(
            SchemaVersion: "1.0",
            Status,
            ModelKey,
            preview.SeasonCode,
            preview.Gameweek,
            preview.DeadlineUtc,
            preview.CaptureAvailableAtUtc,
            preview.CaptureId,
            DistributionStatus,
            preview.Players
                .Select(player => new PlayerGameweekForecastPlayerDocument(
                    player.PlayerId,
                    player.Name,
                    player.ClubShortName,
                    player.Position,
                    player.PriceTenths,
                    player.OfficialStatus,
                    player.OfficialChanceOfPlayingNextRound,
                    player.FixtureCount,
                    player.Opponent,
                    player.IsHome,
                    player.ExpectedPoints,
                    player.Lower80,
                    player.Upper80,
                    player.ExpectedMinutes,
                    StartProbability: null,
                    SixtyMinuteProbability: null,
                    player.Reasons,
                    player.Risks,
                    player.PhotoUrl,
                    player.DossierPath))
                .ToArray(),
            Limitations:
            [
                "The points interval is a deliberately wide Baseline v0 heuristic, not a calibrated predictive distribution.",
                "Expected minutes is an availability-weighted proxy, not a fitted start or minutes model.",
                "Start and 60-minute probabilities remain unavailable until a temporal model earns promotion.",
            ]);
        string documentJson = JsonSerializer.Serialize(document, JsonOptions);
        string contentSha256 = Convert.ToHexStringLower(
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
                INSERT OR IGNORE INTO player_gameweek_forecast_artifacts (
                    schema_version,
                    model_key,
                    official_capture_id,
                    season_code,
                    gameweek,
                    decision_cutoff_utc,
                    document_json,
                    content_sha256,
                    created_at_utc
                )
                VALUES (
                    '1.0',
                    $modelKey,
                    $officialCaptureId,
                    $seasonCode,
                    $gameweek,
                    $decisionCutoffUtc,
                    $documentJson,
                    $contentSha256,
                    $createdAtUtc
                );
                """;
            insert.Parameters.AddWithValue("$modelKey", ModelKey);
            insert.Parameters.AddWithValue(
                "$officialCaptureId",
                preview.CaptureId);
            insert.Parameters.AddWithValue("$seasonCode", preview.SeasonCode);
            insert.Parameters.AddWithValue("$gameweek", preview.Gameweek);
            insert.Parameters.AddWithValue(
                "$decisionCutoffUtc",
                preview.CaptureAvailableAtUtc.ToUniversalTime().ToString("O"));
            insert.Parameters.AddWithValue("$documentJson", documentJson);
            insert.Parameters.AddWithValue("$contentSha256", contentSha256);
            insert.Parameters.AddWithValue(
                "$createdAtUtc",
                _timeProvider.GetUtcNow().ToUniversalTime().ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        ForecastArtifact artifact = await ReadForCaptureAsync(
            connection,
            transaction,
            preview.CaptureId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The player-Gameweek forecast artifact was not persisted.");
        if (!StringComparer.Ordinal.Equals(
            artifact.ContentSha256,
            contentSha256))
        {
            throw new InvalidOperationException(
                "The player-Gameweek forecast changed for an existing capture. "
                + "Increment the model key before persisting a different forecast.");
        }

        await transaction.CommitAsync(cancellationToken);
        return Materialize(artifact);
    }

    public async Task<PlayerGameweekForecastDocument?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                artifact.forecast_artifact_id,
                artifact.document_json,
                artifact.content_sha256
            FROM player_gameweek_forecast_artifacts AS artifact
            INNER JOIN official_fpl_captures AS capture
                ON capture.capture_id = artifact.official_capture_id
            WHERE artifact.model_key = $modelKey
            ORDER BY
                capture.available_at_utc DESC,
                artifact.forecast_artifact_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$modelKey", ModelKey);
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
            SELECT forecast_artifact_id, document_json, content_sha256
            FROM player_gameweek_forecast_artifacts
            WHERE official_capture_id = $officialCaptureId
              AND model_key = $modelKey;
            """;
        command.Parameters.AddWithValue("$officialCaptureId", captureId);
        command.Parameters.AddWithValue("$modelKey", ModelKey);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static PlayerGameweekForecastDocument Materialize(
        ForecastArtifact artifact)
    {
        string actualHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(artifact.DocumentJson)));
        if (!StringComparer.Ordinal.Equals(actualHash, artifact.ContentSha256))
        {
            throw new InvalidOperationException(
                "The stored player-Gameweek forecast content hash is invalid.");
        }

        PlayerGameweekForecastDocument document =
            JsonSerializer.Deserialize<PlayerGameweekForecastDocument>(
                artifact.DocumentJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The stored player-Gameweek forecast document is invalid.");
        if (!StringComparer.Ordinal.Equals(document.Status, Status)
            || !StringComparer.Ordinal.Equals(document.ModelKey, ModelKey)
            || !StringComparer.Ordinal.Equals(
                document.DistributionStatus,
                DistributionStatus)
            || document.Players.Count == 0)
        {
            throw new InvalidOperationException(
                "The stored player-Gameweek forecast has the wrong identity.");
        }

        return document with
        {
            ForecastArtifactId = artifact.ArtifactId,
            ForecastArtifactContentSha256 = artifact.ContentSha256,
        };
    }

    private sealed record ForecastArtifact(
        long ArtifactId,
        string DocumentJson,
        string ContentSha256);
}
