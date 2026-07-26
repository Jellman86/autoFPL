using System.Globalization;
using System.IO.Compression;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Sources;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Sources;

public sealed class FplFormForecastStore
{
    private readonly DatabaseOptions _options;

    public FplFormForecastStore(DatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    internal async Task<FplFormForecastCaptureDocument> SaveAsync(
        FplFormForecastPayload payload,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        DateTimeOffset availableAtUtc = retrievedAtUtc.ToUniversalTime();

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

        long? existingId = await FindExistingCaptureIdAsync(
            connection,
            transaction,
            payload.ContentSha256,
            cancellationToken);
        if (existingId is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (await GetAsync(existingId.Value, cancellationToken))!;
        }

        byte[] compressedEvidence = Compress(payload.Evidence);
        int playerCount = payload.Predictions
            .Select(item => item.SourcePlayerId)
            .Distinct()
            .Count();
        int probabilityCount = payload.Predictions.Count(
            item => item.AppearanceProbability is not null);
        long captureId = await InsertCaptureAsync(
            connection,
            transaction,
            payload,
            retrievedAtUtc,
            availableAtUtc,
            compressedEvidence,
            playerCount,
            probabilityCount,
            cancellationToken);

        foreach (FplFormFixturePrediction prediction in payload.Predictions)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO fpl_form_fixture_predictions (
                    capture_id,
                    source_player_id,
                    fixture_id,
                    gameweek,
                    player_name,
                    team_name,
                    position,
                    kickoff_local,
                    predicted_points,
                    appearance_probability
                )
                VALUES (
                    $captureId,
                    $sourcePlayerId,
                    $fixtureId,
                    $gameweek,
                    $playerName,
                    $teamName,
                    $position,
                    $kickoffLocal,
                    $predictedPoints,
                    $appearanceProbability
                );
                """;
            command.Parameters.AddWithValue("$captureId", captureId);
            command.Parameters.AddWithValue("$sourcePlayerId", prediction.SourcePlayerId);
            command.Parameters.AddWithValue("$fixtureId", prediction.FixtureId);
            command.Parameters.AddWithValue("$gameweek", payload.Gameweek);
            command.Parameters.AddWithValue("$playerName", prediction.PlayerName);
            command.Parameters.AddWithValue("$teamName", prediction.TeamName);
            command.Parameters.AddWithValue("$position", prediction.Position);
            command.Parameters.AddWithValue("$kickoffLocal", prediction.KickoffLocal);
            command.Parameters.AddWithValue(
                "$predictedPoints",
                prediction.PredictedPoints.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue(
                "$appearanceProbability",
                prediction.AppearanceProbability is null
                    ? DBNull.Value
                    : prediction.AppearanceProbability.Value.ToString(
                        CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return CreateDocument(
            captureId,
            payload,
            retrievedAtUtc,
            availableAtUtc,
            playerCount,
            probabilityCount);
    }

    public async Task<FplFormForecastCaptureDocument?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                capture_id,
                schema_version,
                source_key,
                source_url,
                season_code,
                gameweek,
                retrieved_at_utc,
                available_at_utc,
                content_sha256,
                transport,
                extraction_version,
                provider_payload_sha256,
                player_count,
                fixture_prediction_count,
                appearance_probability_count
            FROM fpl_form_forecast_captures
            ORDER BY available_at_utc DESC, capture_id DESC
            LIMIT 1;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadDocument(reader)
            : null;
    }

    public async Task<FplFormForecastStatusDocument> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        FplFormForecastCaptureDocument? latestCapture =
            await GetLatestAsync(cancellationToken);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT checked_at_utc, status, reason_code
            FROM fpl_form_forecast_checks
            ORDER BY checked_at_utc DESC, check_id DESC
            LIMIT 1;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return latestCapture is null
                ? new FplFormForecastStatusDocument(
                    "1.0",
                    FplFormForecastImporter.SourceKey,
                    "not-checked",
                    null,
                    null,
                    null)
                : new FplFormForecastStatusDocument(
                    "1.0",
                    FplFormForecastImporter.SourceKey,
                    "captured",
                    latestCapture.AvailableAtUtc,
                    null,
                    latestCapture);
        }

        return new FplFormForecastStatusDocument(
            "1.0",
            FplFormForecastImporter.SourceKey,
            reader.GetString(1),
            DateTimeOffset.Parse(
                reader.GetString(0),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            latestCapture);
    }

    internal async Task RecordCheckAsync(
        DateTimeOffset checkedAtUtc,
        string status,
        string? reasonCode,
        long? captureId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO fpl_form_forecast_checks (
                source_key,
                checked_at_utc,
                status,
                reason_code,
                capture_id,
                created_at_utc
            )
            VALUES (
                $sourceKey,
                $checkedAtUtc,
                $status,
                $reasonCode,
                $captureId,
                $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$sourceKey", FplFormForecastImporter.SourceKey);
        command.Parameters.AddWithValue("$checkedAtUtc", FormatUtc(checkedAtUtc));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue(
            "$reasonCode",
            reasonCode is null ? DBNull.Value : reasonCode);
        command.Parameters.AddWithValue(
            "$captureId",
            captureId is null ? DBNull.Value : captureId.Value);
        command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(checkedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<FplFormForecastCaptureDocument?> GetAsync(
        long captureId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                capture_id,
                schema_version,
                source_key,
                source_url,
                season_code,
                gameweek,
                retrieved_at_utc,
                available_at_utc,
                content_sha256,
                transport,
                extraction_version,
                provider_payload_sha256,
                player_count,
                fixture_prediction_count,
                appearance_probability_count
            FROM fpl_form_forecast_captures
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadDocument(reader)
            : null;
    }

    private static async Task<long?> FindExistingCaptureIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string contentSha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT capture_id
            FROM fpl_form_forecast_captures
            WHERE content_sha256 = $contentSha256;
            """;
        command.Parameters.AddWithValue("$contentSha256", contentSha256);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long captureId ? captureId : null;
    }

    private static async Task<long> InsertCaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FplFormForecastPayload payload,
        DateTimeOffset retrievedAtUtc,
        DateTimeOffset availableAtUtc,
        byte[] compressedEvidence,
        int playerCount,
        int probabilityCount,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO fpl_form_forecast_captures (
                schema_version,
                source_key,
                source_url,
                season_code,
                gameweek,
                retrieved_at_utc,
                available_at_utc,
                content_sha256,
                transport,
                extraction_version,
                provider_payload_sha256,
                evidence_brotli,
                player_count,
                fixture_prediction_count,
                appearance_probability_count,
                created_at_utc
            )
            VALUES (
                '1.1',
                $sourceKey,
                $sourceUrl,
                $seasonCode,
                $gameweek,
                $retrievedAtUtc,
                $availableAtUtc,
                $contentSha256,
                $transport,
                $extractionVersion,
                $providerPayloadSha256,
                $evidenceBrotli,
                $playerCount,
                $fixturePredictionCount,
                $appearanceProbabilityCount,
                $createdAtUtc
            );
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$sourceKey", FplFormForecastImporter.SourceKey);
        command.Parameters.AddWithValue(
            "$sourceUrl",
            FplFormForecastImporter.ForecastUri.AbsoluteUri);
        command.Parameters.AddWithValue("$seasonCode", payload.SeasonCode);
        command.Parameters.AddWithValue("$gameweek", payload.Gameweek);
        command.Parameters.AddWithValue("$retrievedAtUtc", FormatUtc(retrievedAtUtc));
        command.Parameters.AddWithValue("$availableAtUtc", FormatUtc(availableAtUtc));
        command.Parameters.AddWithValue("$contentSha256", payload.ContentSha256);
        command.Parameters.AddWithValue("$transport", payload.Transport);
        command.Parameters.AddWithValue(
            "$extractionVersion",
            payload.ExtractionVersion);
        command.Parameters.AddWithValue(
            "$providerPayloadSha256",
            payload.ProviderPayloadSha256 is null
                ? DBNull.Value
                : payload.ProviderPayloadSha256);
        command.Parameters.AddWithValue("$evidenceBrotli", compressedEvidence);
        command.Parameters.AddWithValue("$playerCount", playerCount);
        command.Parameters.AddWithValue(
            "$fixturePredictionCount",
            payload.Predictions.Count);
        command.Parameters.AddWithValue("$appearanceProbabilityCount", probabilityCount);
        command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(DateTimeOffset.UtcNow));
        return (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("SQLite did not return a capture ID."));
    }

    private static FplFormForecastCaptureDocument ReadDocument(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            PublishedAtUtc: null,
            ParseUtc(reader.GetString(6)),
            ParseUtc(reader.GetString(7)),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetInt32(14));

    private static FplFormForecastCaptureDocument CreateDocument(
        long captureId,
        FplFormForecastPayload payload,
        DateTimeOffset retrievedAtUtc,
        DateTimeOffset availableAtUtc,
        int playerCount,
        int probabilityCount) =>
        new(
            captureId,
            "1.1",
            FplFormForecastImporter.SourceKey,
            FplFormForecastImporter.ForecastUri.AbsoluteUri,
            payload.SeasonCode,
            payload.Gameweek,
            PublishedAtUtc: null,
            retrievedAtUtc,
            availableAtUtc,
            payload.ContentSha256,
            payload.Transport,
            payload.ExtractionVersion,
            payload.ProviderPayloadSha256,
            playerCount,
            payload.Predictions.Count,
            probabilityCount);

    private static byte[] Compress(byte[] value)
    {
        using var output = new MemoryStream();
        using (var compression = new BrotliStream(
            output,
            CompressionLevel.SmallestSize,
            leaveOpen: true))
        {
            compression.Write(value);
        }

        return output.ToArray();
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
