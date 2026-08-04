using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Persistence;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Forecasts;

public sealed class OfficialPublishedOpeningSquadStore
{
    public const string ArtifactType =
        "current-official-published-opening-squad-shadow";
    public const string ArtifactVersion =
        "current-official-published-opening-squad-shadow-v1";
    public const string Status =
        "prospective-official-baseline-unscored";
    public const string SourceKey = "official-fpl-ep-next";

    private const int MaximumDocumentBytes = 2 * 1024 * 1024;
    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public OfficialPublishedOpeningSquadStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<JsonElement> ImportAsync(
        JsonElement document,
        CancellationToken cancellationToken = default)
    {
        Envelope envelope = ReadEnvelope(document);
        string documentJson = document.GetRawText();
        Require(
            Encoding.UTF8.GetByteCount(documentJson)
                is >= 2 and <= MaximumDocumentBytes,
            "document-size");
        string contentSha256 = Sha256(documentJson);

        await using var connection =
            new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(
                cancellationToken);
        await ValidateLineageAsync(
            connection,
            transaction,
            envelope,
            cancellationToken);
        await PublicProjectionOpeningSquadStore.ValidateSelectionAsync(
            connection,
            transaction,
            envelope.OfficialCaptureId,
            document.GetProperty("incumbent").GetProperty("selection"),
            8,
            cancellationToken);
        await PublicProjectionOpeningSquadStore.ValidateSelectionAsync(
            connection,
            transaction,
            envelope.OfficialCaptureId,
            document.GetProperty("challenger").GetProperty("selection"),
            8,
            cancellationToken);
        PublicProjectionOpeningSquadStore.ValidateSelectionChange(document);

        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO official_published_opening_squad_artifacts (
                    schema_version, artifact_type, artifact_version, status,
                    official_capture_id, source_bootstrap_sha256,
                    source_fixtures_sha256, incumbent_run_identity_sha256,
                    producer_data_identity_sha256,
                    producer_run_identity_sha256, document_json,
                    content_sha256, created_at_utc
                )
                VALUES (
                    '1.0', $artifactType, $artifactVersion, $status,
                    $captureId, $bootstrapSha256, $fixturesSha256,
                    $incumbentRunIdentity, $dataIdentity, $runIdentity,
                    $documentJson, $contentSha256, $createdAtUtc
                )
                ON CONFLICT (official_capture_id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$artifactType", ArtifactType);
            insert.Parameters.AddWithValue("$artifactVersion", ArtifactVersion);
            insert.Parameters.AddWithValue("$status", Status);
            insert.Parameters.AddWithValue(
                "$captureId",
                envelope.OfficialCaptureId);
            insert.Parameters.AddWithValue(
                "$bootstrapSha256",
                envelope.BootstrapSha256);
            insert.Parameters.AddWithValue(
                "$fixturesSha256",
                envelope.FixturesSha256);
            insert.Parameters.AddWithValue(
                "$incumbentRunIdentity",
                envelope.IncumbentRunIdentitySha256);
            insert.Parameters.AddWithValue(
                "$dataIdentity",
                envelope.DataIdentitySha256);
            insert.Parameters.AddWithValue(
                "$runIdentity",
                envelope.RunIdentitySha256);
            insert.Parameters.AddWithValue("$documentJson", documentJson);
            insert.Parameters.AddWithValue("$contentSha256", contentSha256);
            insert.Parameters.AddWithValue(
                "$createdAtUtc",
                _timeProvider.GetUtcNow().UtcDateTime.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        string storedJson;
        string storedHash;
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                """
                SELECT document_json, content_sha256
                FROM official_published_opening_squad_artifacts
                WHERE official_capture_id = $captureId;
                """;
            read.Parameters.AddWithValue(
                "$captureId",
                envelope.OfficialCaptureId);
            await using SqliteDataReader reader =
                await read.ExecuteReaderAsync(cancellationToken);
            Require(await reader.ReadAsync(cancellationToken), "not-persisted");
            storedJson = reader.GetString(0);
            storedHash = reader.GetString(1);
        }
        Require(
            StringComparer.Ordinal.Equals(storedHash, contentSha256),
            "source-conflict");
        await transaction.CommitAsync(cancellationToken);
        return ParseAndVerify(storedJson, storedHash);
    }

    public async Task<JsonElement?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT document_json, content_sha256
            FROM official_published_opening_squad_artifacts
            WHERE official_capture_id = (
                SELECT capture_id
                FROM official_fpl_captures
                ORDER BY available_at_utc DESC, capture_id DESC
                LIMIT 1
            )
            LIMIT 1;
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ParseAndVerify(reader.GetString(0), reader.GetString(1))
            : null;
    }

    private static Envelope ReadEnvelope(JsonElement document)
    {
        Require(document.ValueKind == JsonValueKind.Object, "document");
        JsonElement source = document.GetProperty("source");
        JsonElement method = document.GetProperty("method");
        JsonElement registration = document.GetProperty(
            "prospectiveScoreRegistration");
        JsonElement solver = document.GetProperty("challenger")
            .GetProperty("solver");
        Require(
            document.GetProperty("schemaVersion").GetString() == "1.0"
                && document.GetProperty("artifactType").GetString()
                    == ArtifactType
                && document.GetProperty("artifactVersion").GetString()
                    == ArtifactVersion
                && document.GetProperty("status").GetString() == Status
                && !document.GetProperty("isPromoted").GetBoolean()
                && !document.GetProperty("influencesAdvice").GetBoolean()
                && document.GetProperty("seasonCode").GetString() == "2026-27"
                && document.GetProperty("openingGameweek").GetInt32() == 1
                && document.GetProperty("decision").GetString()
                    == "retain-as-prospective-official-baseline-only"
                && source.GetProperty("sourceKey").GetString() == SourceKey
                && DateTimeOffset.Parse(
                    source.GetProperty("deadlineUtc").GetString()!)
                    == DateTimeOffset.Parse(
                        document.GetProperty("deadlineUtc").GetString()!)
                && source.GetProperty("candidateCoverageFraction").GetDecimal()
                    == 1m
                && source.GetProperty("candidateCoverageCount").GetInt32()
                    == document.GetProperty("candidatePoolCount").GetInt32()
                && source.GetProperty("publishedPlayerCount").GetInt32()
                    == document.GetProperty("candidatePoolCount").GetInt32()
                && method.GetProperty("methodKey").GetString()
                    == "official-ep-next-gw1-mean-overlay-on-retained-six-week-policy-v1"
                && method.GetProperty("horizonGameweeks").GetInt32() == 6
                && method.GetProperty("laterGameweeksUnchanged").GetBoolean()
                && method.GetProperty("distributionPolicy").GetString()
                    == "published-values-affect-expected-value-surrogate-only"
                && method.GetProperty("evaluationPolicyKey").GetString()
                    == "6-expected-points"
                && method.GetProperty("optimizerVersion").GetString()
                    == "scipy-highs-multi-horizon-mean-cvar-v1"
                && method.GetProperty("sourceEvaluationVersion").GetString()
                    == "official-fpl-published-expected-points-evaluation-v1"
                && registration.GetProperty("outcomeGameweeks")
                    .EnumerateArray().Select(value => value.GetInt32())
                    .SequenceEqual(Enumerable.Range(1, 8))
                && registration.GetProperty("squadMembership").GetString()
                    == "fixed-opening-squad-no-transfers-for-all-eight-gameweeks"
                && registration.GetProperty("roles").GetString()
                    == "all-eight-weeks-frozen-from-preseason-scenario-means"
                && registration.GetProperty("realisedScorer").GetString()
                    == "exact-fpl-captain-fallback-and-ordered-auto-substitution"
                && registration.GetProperty("outcomeStatus").GetString()
                    == "waiting-for-official-2026-27-outcomes"
                && solver.GetProperty("optimizerVersion").GetString()
                    == "scipy-highs-multi-horizon-mean-cvar-v1"
                && solver.GetProperty("solver").GetString()
                    == "scipy.optimize.milp-highs"
                && solver.GetProperty("status").GetString()
                    == "global-linear-mean-cvar-surrogate-optimum"
                && solver.GetProperty("mipGap").GetDecimal() == 0m
                && solver.GetProperty("reportedMipGap").GetDecimal()
                    is >= 0m and <= 0.000000000001m,
            "identity");
        return new(
            document.GetProperty("officialCaptureId").GetInt64(),
            document.GetProperty("candidatePoolCount").GetInt32(),
            document.GetProperty("scenarioCount").GetInt32(),
            DateTimeOffset.Parse(
                document.GetProperty("deadlineUtc").GetString()!),
            DateTimeOffset.Parse(
                document.GetProperty("decisionCutoffUtc").GetString()!),
            DateTimeOffset.Parse(source.GetProperty("availableAtUtc").GetString()!),
            RequiredSha256(
                source.GetProperty("bootstrapSha256"),
                "source.bootstrapSha256"),
            RequiredSha256(
                source.GetProperty("fixturesSha256"),
                "source.fixturesSha256"),
            RequiredSha256(
                document.GetProperty("incumbent")
                    .GetProperty("sourceRunIdentitySha256"),
                "incumbent.sourceRunIdentitySha256"),
            RequiredSha256(
                document.GetProperty("dataIdentitySha256"),
                "dataIdentitySha256"),
            RequiredSha256(
                document.GetProperty("runIdentitySha256"),
                "runIdentitySha256"));
    }

    private static async Task ValidateLineageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Envelope envelope,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT capture.season_code, capture.next_gameweek_number,
                   capture.next_deadline_utc, capture.available_at_utc,
                   capture.bootstrap_sha256, capture.fixtures_sha256,
                   (SELECT COUNT(*)
                    FROM official_fpl_players AS player
                    WHERE player.capture_id = capture.capture_id
                      AND player.status <> 'u'),
                   (SELECT COUNT(*)
                    FROM official_fpl_players AS player
                    WHERE player.capture_id = capture.capture_id
                      AND player.status <> 'u'
                      AND player.expected_points_next IS NOT NULL),
                   selected.producer_run_identity_sha256
            FROM official_fpl_captures AS capture
            JOIN selected_opening_squad_shadow_artifacts AS selected
              ON selected.official_capture_id = capture.capture_id
             AND selected.artifact_version =
                    'current-selected-opening-squad-shadow-v2'
             AND selected.status =
                    'best-supported-current-prospective-unscored'
             AND selected.evaluation_policy_key = '6-expected-points'
             AND selected.producer_run_identity_sha256 =
                    $incumbentRunIdentity
            WHERE capture.capture_id = $captureId
              AND capture.capture_id = (
                  SELECT capture_id FROM official_fpl_captures
                  ORDER BY available_at_utc DESC, capture_id DESC LIMIT 1
              );
            """;
        command.Parameters.AddWithValue("$captureId", envelope.OfficialCaptureId);
        command.Parameters.AddWithValue(
            "$incumbentRunIdentity",
            envelope.IncumbentRunIdentitySha256);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        Require(await reader.ReadAsync(cancellationToken), "lineage");
        Require(
            reader.GetString(0) == "2026-27"
                && reader.GetInt32(1) == 1
                && DateTimeOffset.Parse(reader.GetString(2))
                    == envelope.DeadlineUtc
                && DateTimeOffset.Parse(reader.GetString(3))
                    == envelope.DecisionCutoffUtc
                && envelope.SourceAvailableAtUtc
                    == envelope.DecisionCutoffUtc
                && envelope.DecisionCutoffUtc <= envelope.DeadlineUtc
                && reader.GetString(4) == envelope.BootstrapSha256
                && reader.GetString(5) == envelope.FixturesSha256
                && reader.GetInt32(6) == envelope.CandidatePoolCount
                && reader.GetInt32(7) == envelope.CandidatePoolCount
                && reader.GetString(8)
                    == envelope.IncumbentRunIdentitySha256
                && envelope.CandidatePoolCount is >= 15 and <= 1024
                && envelope.ScenarioCount is >= 1 and <= 512,
            "lineage");
    }

    private static JsonElement ParseAndVerify(string json, string contentSha256)
    {
        Require(Sha256(json) == contentSha256, "content-sha256");
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string RequiredSha256(JsonElement value, string field)
    {
        string result = value.GetString() ?? string.Empty;
        Require(
            result.Length == 64
                && result.All(character =>
                    character is >= '0' and <= '9'
                        or >= 'a' and <= 'f'),
            field);
        return result;
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void Require(bool condition, string field)
    {
        if (!condition)
        {
            throw new InvalidDataException(
                $"Invalid official published opening squad: {field}.");
        }
    }

    private sealed record Envelope(
        long OfficialCaptureId,
        int CandidatePoolCount,
        int ScenarioCount,
        DateTimeOffset DeadlineUtc,
        DateTimeOffset DecisionCutoffUtc,
        DateTimeOffset SourceAvailableAtUtc,
        string BootstrapSha256,
        string FixturesSha256,
        string IncumbentRunIdentitySha256,
        string DataIdentitySha256,
        string RunIdentitySha256);
}

public sealed class OfficialPublishedOpeningSquadImporter
{
    private readonly OfficialPublishedOpeningSquadStore _store;

    public OfficialPublishedOpeningSquadImporter(
        OfficialPublishedOpeningSquadStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<JsonElement> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The official published squad file does not exist.", path);
        }
        if (file.Length is < 2 or > 2 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "The official published squad file is outside its size limit.");
        }
        await using FileStream stream = file.OpenRead();
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        return await _store.ImportAsync(
            document.RootElement,
            cancellationToken);
    }
}
