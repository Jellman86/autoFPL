using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Intelligence;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Intelligence;

public sealed class EvidenceClaimStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> AvailabilityStatuses =
        new(StringComparer.Ordinal)
        {
            "available",
            "doubtful",
            "unavailable",
            "expected-return",
        };

    private static readonly HashSet<string> StartStatuses =
        new(StringComparer.Ordinal)
        {
            "starts",
            "does-not-start",
            "uncertain",
        };

    private static readonly HashSet<string> DirectnessValues =
        new(StringComparer.Ordinal)
        {
            "direct-quote",
            "reported",
            "opinion",
            "model-forecast",
        };

    private static readonly HashSet<string> ExtractionMethods =
        new(StringComparer.Ordinal)
        {
            "deterministic",
            "human",
            "llm",
        };

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public EvidenceClaimStore(DatabaseOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<EvidenceClaimDocument> ImportAsync(
        EvidenceClaimImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        NormalizedEvidenceClaim claim = Normalize(request);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        IdentityTarget identity = await ResolveIdentityAsync(
            connection,
            transaction,
            claim,
            cancellationToken);
        string claimContentSha256 = ComputeClaimContentSha256(claim, identity);
        DateTimeOffset createdAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT OR IGNORE INTO evidence_claims (
                    schema_version,
                    status,
                    source_key,
                    canonical_url,
                    author,
                    published_at_utc,
                    retrieved_at_utc,
                    available_at_utc,
                    content_sha256,
                    source_revision,
                    season_code,
                    gameweek,
                    deadline_utc,
                    player_id,
                    identity_capture_id,
                    claim_type,
                    availability_status,
                    start_status,
                    forecast_probability,
                    expected_minutes,
                    role,
                    directness,
                    source_span,
                    extraction_method,
                    extraction_version,
                    extraction_confidence,
                    duplicate_cluster_key,
                    claim_content_sha256,
                    created_at_utc
                )
                VALUES (
                    '1.0',
                    'quarantined',
                    $sourceKey,
                    $canonicalUrl,
                    $author,
                    $publishedAtUtc,
                    $retrievedAtUtc,
                    $availableAtUtc,
                    $contentSha256,
                    $sourceRevision,
                    $seasonCode,
                    $gameweek,
                    $deadlineUtc,
                    $playerId,
                    $identityCaptureId,
                    $claimType,
                    $availabilityStatus,
                    $startStatus,
                    $forecastProbability,
                    $expectedMinutes,
                    $role,
                    $directness,
                    $sourceSpan,
                    $extractionMethod,
                    $extractionVersion,
                    $extractionConfidence,
                    $duplicateClusterKey,
                    $claimContentSha256,
                    $createdAtUtc
                );
                """;
            command.Parameters.AddWithValue("$sourceKey", claim.SourceKey);
            command.Parameters.AddWithValue("$canonicalUrl", claim.CanonicalUrl);
            command.Parameters.AddWithValue("$author", DbValue(claim.Author));
            command.Parameters.AddWithValue(
                "$publishedAtUtc",
                DbValue(FormatUtc(claim.PublishedAtUtc)));
            command.Parameters.AddWithValue(
                "$retrievedAtUtc",
                FormatUtc(claim.RetrievedAtUtc)!);
            command.Parameters.AddWithValue(
                "$availableAtUtc",
                FormatUtc(claim.AvailableAtUtc)!);
            command.Parameters.AddWithValue("$contentSha256", claim.ContentSha256);
            command.Parameters.AddWithValue("$sourceRevision", claim.SourceRevision);
            command.Parameters.AddWithValue("$seasonCode", claim.SeasonCode);
            command.Parameters.AddWithValue("$gameweek", claim.Gameweek);
            command.Parameters.AddWithValue("$deadlineUtc", FormatUtc(identity.DeadlineUtc)!);
            command.Parameters.AddWithValue("$playerId", claim.PlayerId);
            command.Parameters.AddWithValue(
                "$identityCaptureId",
                identity.IdentityCaptureId);
            command.Parameters.AddWithValue("$claimType", claim.ClaimType);
            command.Parameters.AddWithValue(
                "$availabilityStatus",
                DbValue(claim.AvailabilityStatus));
            command.Parameters.AddWithValue("$startStatus", DbValue(claim.StartStatus));
            command.Parameters.AddWithValue(
                "$forecastProbability",
                DbValue(FormatDecimal(claim.ForecastProbability)));
            command.Parameters.AddWithValue(
                "$expectedMinutes",
                DbValue(claim.ExpectedMinutes));
            command.Parameters.AddWithValue("$role", DbValue(claim.Role));
            command.Parameters.AddWithValue("$directness", claim.Directness);
            command.Parameters.AddWithValue("$sourceSpan", claim.SourceSpan);
            command.Parameters.AddWithValue(
                "$extractionMethod",
                claim.ExtractionMethod);
            command.Parameters.AddWithValue(
                "$extractionVersion",
                claim.ExtractionVersion);
            command.Parameters.AddWithValue(
                "$extractionConfidence",
                FormatDecimal(claim.ExtractionConfidence)!);
            command.Parameters.AddWithValue(
                "$duplicateClusterKey",
                DbValue(claim.DuplicateClusterKey));
            command.Parameters.AddWithValue(
                "$claimContentSha256",
                claimContentSha256);
            command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(createdAtUtc)!);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        EvidenceClaimDocument document =
            await ReadByContentHashAsync(
                connection,
                transaction,
                claimContentSha256,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The evidence claim was not persisted.");
        await transaction.CommitAsync(cancellationToken);
        return document;
    }

    public async Task<EvidenceClaimSetDocument> GetForGameweekAsync(
        string seasonCode,
        int gameweek,
        DateTimeOffset decisionCutoffUtc,
        CancellationToken cancellationToken = default)
    {
        string normalizedSeasonCode = RequiredText(
            seasonCode,
            "seasonCode",
            minimumLength: 4,
            maximumLength: 16);
        if (gameweek is < 1 or > 38)
        {
            throw Invalid("gameweek.range", "gameweek");
        }

        DateTimeOffset cutoffUtc = decisionCutoffUtc.ToUniversalTime();
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                claim_id,
                schema_version,
                status,
                source_key,
                canonical_url,
                author,
                published_at_utc,
                retrieved_at_utc,
                available_at_utc,
                content_sha256,
                source_revision,
                season_code,
                gameweek,
                deadline_utc,
                player_id,
                identity_capture_id,
                claim_type,
                availability_status,
                start_status,
                forecast_probability,
                expected_minutes,
                role,
                directness,
                source_span,
                extraction_method,
                extraction_version,
                extraction_confidence,
                duplicate_cluster_key,
                claim_content_sha256,
                created_at_utc
            FROM evidence_claims
            WHERE season_code = $seasonCode
              AND gameweek = $gameweek
              AND available_at_utc <= $decisionCutoffUtc
            ORDER BY available_at_utc, claim_id;
            """;
        command.Parameters.AddWithValue("$seasonCode", normalizedSeasonCode);
        command.Parameters.AddWithValue("$gameweek", gameweek);
        command.Parameters.AddWithValue(
            "$decisionCutoffUtc",
            FormatUtc(cutoffUtc)!);

        var claims = new List<EvidenceClaimDocument>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claims.Add(Read(reader));
        }

        return new(
            "1.0",
            normalizedSeasonCode,
            gameweek,
            cutoffUtc,
            claims);
    }

    private static async Task<IdentityTarget> ResolveIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedEvidenceClaim claim,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT capture.capture_id, event.deadline_utc
            FROM official_fpl_captures AS capture
            INNER JOIN official_fpl_events AS event
                ON event.capture_id = capture.capture_id
               AND event.event_id = $gameweek
            INNER JOIN official_fpl_players AS player
                ON player.capture_id = capture.capture_id
               AND player.player_id = $playerId
            WHERE capture.season_code = $seasonCode
              AND capture.available_at_utc <= $availableAtUtc
            ORDER BY capture.available_at_utc DESC, capture.capture_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$seasonCode", claim.SeasonCode);
        command.Parameters.AddWithValue("$gameweek", claim.Gameweek);
        command.Parameters.AddWithValue("$playerId", claim.PlayerId);
        command.Parameters.AddWithValue(
            "$availableAtUtc",
            FormatUtc(claim.AvailableAtUtc)!);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw Invalid("player.identity-unavailable", "playerId");
        }

        return new(
            reader.GetInt64(0),
            DateTimeOffset.Parse(
                reader.GetString(1),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
    }

    private static async Task<EvidenceClaimDocument?> ReadByContentHashAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string claimContentSha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                claim_id,
                schema_version,
                status,
                source_key,
                canonical_url,
                author,
                published_at_utc,
                retrieved_at_utc,
                available_at_utc,
                content_sha256,
                source_revision,
                season_code,
                gameweek,
                deadline_utc,
                player_id,
                identity_capture_id,
                claim_type,
                availability_status,
                start_status,
                forecast_probability,
                expected_minutes,
                role,
                directness,
                source_span,
                extraction_method,
                extraction_version,
                extraction_confidence,
                duplicate_cluster_key,
                claim_content_sha256,
                created_at_utc
            FROM evidence_claims
            WHERE claim_content_sha256 = $claimContentSha256;
            """;
        command.Parameters.AddWithValue(
            "$claimContentSha256",
            claimContentSha256);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static EvidenceClaimDocument Read(SqliteDataReader reader)
    {
        DateTimeOffset deadlineUtc = ReadDateTimeOffset(reader, 13);
        DateTimeOffset availableAtUtc = ReadDateTimeOffset(reader, 8);
        return new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : ReadDateTimeOffset(reader, 6),
            ReadDateTimeOffset(reader, 7),
            availableAtUtc,
            reader.GetString(9),
            reader.GetInt32(10),
            reader.GetString(11),
            reader.GetInt32(12),
            deadlineUtc,
            availableAtUtc <= deadlineUtc,
            reader.GetInt32(14),
            reader.GetInt64(15),
            reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.IsDBNull(19)
                ? null
                : decimal.Parse(reader.GetString(19), CultureInfo.InvariantCulture),
            reader.IsDBNull(20) ? null : reader.GetInt32(20),
            reader.IsDBNull(21) ? null : reader.GetString(21),
            reader.GetString(22),
            reader.GetString(23),
            reader.GetString(24),
            reader.GetString(25),
            decimal.Parse(reader.GetString(26), CultureInfo.InvariantCulture),
            reader.IsDBNull(27) ? null : reader.GetString(27),
            reader.GetString(28),
            ReadDateTimeOffset(reader, 29));
    }

    private static NormalizedEvidenceClaim Normalize(
        EvidenceClaimImportRequest request)
    {
        string schemaVersion = RequiredText(
            request.SchemaVersion,
            "schemaVersion",
            3,
            8);
        if (!StringComparer.Ordinal.Equals(schemaVersion, "1.0"))
        {
            throw Invalid("schema-version.unsupported", "schemaVersion");
        }

        string sourceKey = RequiredText(request.SourceKey, "sourceKey", 1, 100);
        string canonicalUrl = RequiredText(
            request.CanonicalUrl,
            "canonicalUrl",
            8,
            2048);
        if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out Uri? sourceUri)
            || sourceUri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(sourceUri.UserInfo)
            || !string.IsNullOrEmpty(sourceUri.Fragment))
        {
            throw Invalid("canonical-url.invalid", "canonicalUrl");
        }

        string? author = OptionalText(request.Author, "author", 1, 150);
        DateTimeOffset retrievedAtUtc =
            RequiredTimestamp(request.RetrievedAtUtc, "retrievedAtUtc");
        DateTimeOffset availableAtUtc =
            RequiredTimestamp(request.AvailableAtUtc, "availableAtUtc");
        DateTimeOffset? publishedAtUtc = request.PublishedAtUtc?.ToUniversalTime();
        if (publishedAtUtc > retrievedAtUtc)
        {
            throw Invalid("published-after-retrieval", "publishedAtUtc");
        }
        if (retrievedAtUtc > availableAtUtc)
        {
            throw Invalid("retrieved-after-available", "retrievedAtUtc");
        }

        string contentSha256 = RequiredSha256(
            request.ContentSha256,
            "contentSha256");
        int sourceRevision = request.SourceRevision
            ?? throw Invalid("required", "sourceRevision");
        if (sourceRevision < 1)
        {
            throw Invalid("source-revision.range", "sourceRevision");
        }

        string seasonCode = RequiredText(
            request.SeasonCode,
            "seasonCode",
            4,
            16);
        int gameweek = request.Gameweek ?? throw Invalid("required", "gameweek");
        if (gameweek is < 1 or > 38)
        {
            throw Invalid("gameweek.range", "gameweek");
        }
        int playerId = request.PlayerId ?? throw Invalid("required", "playerId");
        if (playerId <= 0)
        {
            throw Invalid("player-id.range", "playerId");
        }

        string claimType = RequiredText(request.ClaimType, "claimType", 4, 32);
        string? availabilityStatus = OptionalText(
            request.AvailabilityStatus,
            "availabilityStatus",
            4,
            32);
        string? startStatus = OptionalText(
            request.StartStatus,
            "startStatus",
            6,
            32);
        string? role = OptionalText(request.Role, "role", 1, 100);
        decimal? forecastProbability = request.ForecastProbability;
        if (forecastProbability is < 0m or > 1m)
        {
            throw Invalid("forecast-probability.range", "forecastProbability");
        }
        int? expectedMinutes = request.ExpectedMinutes;

        switch (claimType)
        {
            case "availability"
                when availabilityStatus is not null
                    && AvailabilityStatuses.Contains(availabilityStatus)
                    && startStatus is null
                    && expectedMinutes is null
                    && role is null:
                break;
            case "start"
                when startStatus is not null
                    && StartStatuses.Contains(startStatus)
                    && availabilityStatus is null
                    && expectedMinutes is null
                    && role is null:
                break;
            case "minutes"
                when expectedMinutes is >= 0 and <= 180
                    && availabilityStatus is null
                    && startStatus is null
                    && role is null
                    && forecastProbability is null:
                break;
            case "role"
                when role is not null
                    && availabilityStatus is null
                    && startStatus is null
                    && expectedMinutes is null
                    && forecastProbability is null:
                break;
            default:
                throw Invalid("claim-value.invalid", "claimType");
        }

        string directness = RequiredText(
            request.Directness,
            "directness",
            7,
            32);
        if (!DirectnessValues.Contains(directness))
        {
            throw Invalid("directness.invalid", "directness");
        }
        string sourceSpan = RequiredText(
            request.SourceSpan,
            "sourceSpan",
            1,
            500);
        string extractionMethod = RequiredText(
            request.ExtractionMethod,
            "extractionMethod",
            3,
            32);
        if (!ExtractionMethods.Contains(extractionMethod))
        {
            throw Invalid("extraction-method.invalid", "extractionMethod");
        }
        string extractionVersion = RequiredText(
            request.ExtractionVersion,
            "extractionVersion",
            1,
            100);
        decimal extractionConfidence = request.ExtractionConfidence
            ?? throw Invalid("required", "extractionConfidence");
        if (extractionConfidence is < 0m or > 1m)
        {
            throw Invalid(
                "extraction-confidence.range",
                "extractionConfidence");
        }
        string? duplicateClusterKey = request.DuplicateClusterKey is null
            ? null
            : RequiredSha256(
                request.DuplicateClusterKey,
                "duplicateClusterKey");

        return new(
            schemaVersion,
            sourceKey,
            sourceUri.AbsoluteUri,
            author,
            publishedAtUtc,
            retrievedAtUtc,
            availableAtUtc,
            contentSha256,
            sourceRevision,
            seasonCode,
            gameweek,
            playerId,
            claimType,
            availabilityStatus,
            startStatus,
            forecastProbability,
            expectedMinutes,
            role,
            directness,
            sourceSpan,
            extractionMethod,
            extractionVersion,
            extractionConfidence,
            duplicateClusterKey);
    }

    private static string ComputeClaimContentSha256(
        NormalizedEvidenceClaim claim,
        IdentityTarget identity)
    {
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                claim.SchemaVersion,
                claim.SourceKey,
                claim.CanonicalUrl,
                claim.Author,
                claim.PublishedAtUtc,
                claim.RetrievedAtUtc,
                claim.AvailableAtUtc,
                claim.ContentSha256,
                claim.SourceRevision,
                claim.SeasonCode,
                claim.Gameweek,
                claim.PlayerId,
                identity.IdentityCaptureId,
                claim.ClaimType,
                claim.AvailabilityStatus,
                claim.StartStatus,
                claim.ForecastProbability,
                claim.ExpectedMinutes,
                claim.Role,
                claim.Directness,
                claim.SourceSpan,
                claim.ExtractionMethod,
                claim.ExtractionVersion,
                claim.ExtractionConfidence,
                claim.DuplicateClusterKey,
            },
            JsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(canonical));
    }

    private static string RequiredText(
        string? value,
        string field,
        int minimumLength,
        int maximumLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length < minimumLength || normalized.Length > maximumLength)
        {
            throw Invalid("text-length", field);
        }
        return normalized;
    }

    private static string? OptionalText(
        string? value,
        string field,
        int minimumLength,
        int maximumLength) =>
        value is null
            ? null
            : RequiredText(value, field, minimumLength, maximumLength);

    private static DateTimeOffset RequiredTimestamp(
        DateTimeOffset? value,
        string field) =>
        value?.ToUniversalTime() ?? throw Invalid("required", field);

    private static string RequiredSha256(string? value, string field)
    {
        string normalized = RequiredText(value, field, 64, 64);
        if (normalized.Any(character =>
            character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            throw Invalid("sha256.invalid", field);
        }
        return normalized;
    }

    private static EvidenceClaimValidationException Invalid(
        string code,
        string field) => new(code, field);

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private static string? FormatUtc(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? FormatDecimal(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset ReadDateTimeOffset(
        SqliteDataReader reader,
        int ordinal) =>
        DateTimeOffset.Parse(
            reader.GetString(ordinal),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private sealed record NormalizedEvidenceClaim(
        string SchemaVersion,
        string SourceKey,
        string CanonicalUrl,
        string? Author,
        DateTimeOffset? PublishedAtUtc,
        DateTimeOffset RetrievedAtUtc,
        DateTimeOffset AvailableAtUtc,
        string ContentSha256,
        int SourceRevision,
        string SeasonCode,
        int Gameweek,
        int PlayerId,
        string ClaimType,
        string? AvailabilityStatus,
        string? StartStatus,
        decimal? ForecastProbability,
        int? ExpectedMinutes,
        string? Role,
        string Directness,
        string SourceSpan,
        string ExtractionMethod,
        string ExtractionVersion,
        decimal ExtractionConfidence,
        string? DuplicateClusterKey);

    private sealed record IdentityTarget(
        long IdentityCaptureId,
        DateTimeOffset DeadlineUtc);
}
