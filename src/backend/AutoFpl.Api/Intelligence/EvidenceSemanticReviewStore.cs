using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Intelligence;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Intelligence;

public sealed class EvidenceSemanticReviewStore
{
    public const string ArtifactType = "current-evidence-semantic-review";
    public const string ArtifactVersion = "current-evidence-semantic-review-v1";
    public const string StatusComplete = "complete";
    public const string StatusInsufficientEvidence = "insufficient-evidence";
    public const string StatusRefused = "refused";
    public const string StatusUnavailable = "unavailable";

    private static readonly HashSet<string> AllowedStatuses =
    [
        StatusComplete,
        StatusInsufficientEvidence,
        StatusRefused,
        StatusUnavailable,
    ];
    private static readonly HashSet<string> AllowedEvidenceQualities =
    [
        "high",
        "medium",
        "low",
        "mixed",
        "insufficient",
    ];

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly CurrentEvidenceReviewContextStore _contextStore;
    private readonly TimeProvider _timeProvider;

    public EvidenceSemanticReviewStore(
        DatabaseOptions options,
        CurrentEvidenceReviewContextStore contextStore,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _contextStore =
            contextStore ?? throw new ArgumentNullException(nameof(contextStore));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<EvidenceSemanticReviewDocument> ImportAsync(
        EvidenceSemanticReviewDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        EvidenceReviewContextDocument context = await EnsureCurrentContextAsync(
            cancellationToken);
        ValidateDocument(document, context);

        await using var connection =
            new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        string documentJson = JsonSerializer.Serialize(document, JsonOptions);
        string contentSha256 = Sha256(documentJson);
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO evidence_semantic_review_artifacts (
                    schema_version,
                    artifact_type,
                    artifact_version,
                    status,
                    reason,
                    is_promoted,
                    influences_forecast,
                    season_code,
                    opening_gameweek,
                    deadline_utc,
                    decision_cutoff_utc,
                    official_capture_id,
                    stress_artifact_id,
                    stress_artifact_content_sha256,
                    context_identity_sha256,
                    provider,
                    provider_model,
                    prompt_version,
                    output_schema_version,
                    requested_at_utc,
                    completed_at_utc,
                    data_identity_sha256,
                    run_identity_sha256,
                    document_json,
                    content_sha256,
                    created_at_utc
                )
                VALUES (
                    $schemaVersion,
                    $artifactType,
                    $artifactVersion,
                    $status,
                    $reason,
                    0,
                    0,
                    $seasonCode,
                    $gameweek,
                    $deadlineUtc,
                    $decisionCutoffUtc,
                    $officialCaptureId,
                    $stressArtifactId,
                    $stressArtifactContentSha256,
                    $contextIdentitySha256,
                    $provider,
                    $providerModel,
                    $promptVersion,
                    $outputSchemaVersion,
                    $requestedAtUtc,
                    $completedAtUtc,
                    $dataIdentitySha256,
                    $runIdentitySha256,
                    $documentJson,
                    $contentSha256,
                    $createdAtUtc
                )
                ON CONFLICT (
                    stress_artifact_id,
                    run_identity_sha256
                ) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$schemaVersion", "1.0");
            insert.Parameters.AddWithValue("$artifactType", ArtifactType);
            insert.Parameters.AddWithValue("$artifactVersion", ArtifactVersion);
            insert.Parameters.AddWithValue("$status", document.Status);
            insert.Parameters.AddWithValue("$reason", DbValue(document.Reason));
            insert.Parameters.AddWithValue("$seasonCode", document.SeasonCode);
            insert.Parameters.AddWithValue("$gameweek", document.Gameweek);
            insert.Parameters.AddWithValue(
                "$deadlineUtc",
                FormatUtc(document.DeadlineUtc));
            insert.Parameters.AddWithValue(
                "$decisionCutoffUtc",
                FormatUtc(document.DecisionCutoffUtc));
            insert.Parameters.AddWithValue(
                "$officialCaptureId",
                document.OfficialCaptureId);
            insert.Parameters.AddWithValue(
                "$stressArtifactId",
                document.StressArtifactId);
            insert.Parameters.AddWithValue(
                "$stressArtifactContentSha256",
                document.StressArtifactContentSha256);
            insert.Parameters.AddWithValue(
                "$contextIdentitySha256",
                document.ContextIdentitySha256);
            insert.Parameters.AddWithValue("$provider", document.Provider);
            insert.Parameters.AddWithValue("$providerModel", document.ProviderModel);
            insert.Parameters.AddWithValue("$promptVersion", document.PromptVersion);
            insert.Parameters.AddWithValue(
                "$outputSchemaVersion",
                document.OutputSchemaVersion);
            insert.Parameters.AddWithValue(
                "$requestedAtUtc",
                FormatUtc(document.RequestedAtUtc));
            insert.Parameters.AddWithValue(
                "$completedAtUtc",
                FormatUtc(document.CompletedAtUtc));
            insert.Parameters.AddWithValue(
                "$dataIdentitySha256",
                document.DataIdentitySha256);
            insert.Parameters.AddWithValue(
                "$runIdentitySha256",
                document.RunIdentitySha256);
            insert.Parameters.AddWithValue("$documentJson", documentJson);
            insert.Parameters.AddWithValue("$contentSha256", contentSha256);
            insert.Parameters.AddWithValue(
                "$createdAtUtc",
                FormatUtc(_timeProvider.GetUtcNow()));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        StoredArtifact? stored = await ReadByRunIdentityAsync(
            connection,
            transaction,
            document.StressArtifactId,
            document.RunIdentitySha256,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (stored is null)
        {
            throw new EvidenceSemanticReviewValidationException(
                "not-persisted",
                "runIdentitySha256");
        }
        if (!string.Equals(
                stored.ContentSha256,
                contentSha256,
                StringComparison.Ordinal))
        {
            throw new EvidenceSemanticReviewValidationException(
                "conflict-run-identity",
                "runIdentitySha256");
        }

        return Materialize(stored);
    }

    public async Task<EvidenceSemanticReviewDocument?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        EvidenceReviewContextDocument? context =
            await _contextStore.GetCurrentAsync(cancellationToken);
        if (context is null)
        {
            return null;
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT review_artifact_id, document_json, content_sha256
            FROM evidence_semantic_review_artifacts
            WHERE stress_artifact_id = $stressArtifactId
              AND context_identity_sha256 = $contextIdentitySha256
            ORDER BY review_artifact_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$stressArtifactId",
            context.StressArtifactId);
        command.Parameters.AddWithValue(
            "$contextIdentitySha256",
            context.ContextIdentitySha256);
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

    private async Task<EvidenceReviewContextDocument> EnsureCurrentContextAsync(
        CancellationToken cancellationToken)
    {
        EvidenceReviewContextDocument? context =
            await _contextStore.GetCurrentAsync(cancellationToken);
        return context ?? throw new EvidenceSemanticReviewValidationException(
            "context-missing",
            "review-context");
    }

    private static void ValidateDocument(
        EvidenceSemanticReviewDocument document,
        EvidenceReviewContextDocument context)
    {
        Require(
            document.EvidenceSemanticReviewArtifactId is null
                && document.EvidenceSemanticReviewArtifactContentSha256 is null,
            "identity",
            "artifactId");
        Require(
            document.SchemaVersion == "1.0",
            "identity",
            "schemaVersion");
        Require(document.ArtifactType == ArtifactType, "identity", "artifactType");
        Require(document.ArtifactVersion == ArtifactVersion, "identity", "artifactVersion");
        Require(AllowedStatuses.Contains(document.Status), "identity", "status");
        Require(
            document.IsPromoted == false && document.InfluencesForecast == false,
            "identity",
            "state");
        Require(
            !string.IsNullOrWhiteSpace(document.SeasonCode)
                && document.SeasonCode.Length is >= 4 and <= 16
                && document.Gameweek is >= 1 and <= 38
                && document.SeasonCode == context.SeasonCode
                && document.Gameweek == context.Gameweek,
            "identity",
            "target");
        Require(
            document.OfficialCaptureId > 0
                && document.StressArtifactId > 0
                && IsSha256(document.StressArtifactContentSha256),
            "identity",
            "capture");
        Require(
            document.OfficialCaptureId == context.OfficialCaptureId
                && document.StressArtifactId == context.StressArtifactId
                && document.StressArtifactContentSha256
                    == context.StressArtifactContentSha256
                && document.ContextIdentitySha256 == context.ContextIdentitySha256,
            "identity",
            "context");
        Require(
            IsSha256(document.ContextIdentitySha256),
            "identity",
            "contextIdentitySha256");
        Require(
            !string.IsNullOrWhiteSpace(document.Provider),
            "identity",
            "provider");
        Require(
            !string.IsNullOrWhiteSpace(document.ProviderModel),
            "identity",
            "providerModel");
        Require(
            !string.IsNullOrWhiteSpace(document.PromptVersion)
                && document.PromptVersion == context.ReviewPolicy.PromptVersion,
            "identity",
            "promptVersion");
        Require(
            !string.IsNullOrWhiteSpace(document.OutputSchemaVersion)
                && document.OutputSchemaVersion
                    == context.ReviewPolicy.OutputSchemaVersion,
            "identity",
            "outputSchemaVersion");
        Require(
            IsSha256(document.DataIdentitySha256)
                && IsSha256(document.RunIdentitySha256),
            "identity",
            "identitySha256");
        Require(
            document.RequestedAtUtc >= context.DecisionCutoffUtc
                && document.DecisionCutoffUtc == context.DecisionCutoffUtc,
            "identity",
            "requestedAtUtc");
        Require(
            document.DeadlineUtc == context.DeadlineUtc,
            "identity",
            "deadlineUtc");
        Require(
            document.CompletedAtUtc >= document.RequestedAtUtc
                && document.CompletedAtUtc <= context.DeadlineUtc,
            "identity",
            "completedAtUtc");
        if (document.Status == StatusComplete)
        {
            Require(document.Reason is null, "result", "reason");
        }
        else
        {
            Require(
                !string.IsNullOrWhiteSpace(document.Reason),
                "result",
                "reason");
        }

        HashSet<string> allowedVerdicts = context.ReviewPolicy.AllowedVerdicts
            .ToHashSet(StringComparer.Ordinal);
        Dictionary<int, EvidenceReviewTargetDocument> currentTargets =
            context.Targets.ToDictionary(row => row.Player.PlayerId);
        Dictionary<int, EvidenceSemanticReviewResultDocument> byPlayer = [];

        foreach (EvidenceSemanticReviewResultDocument result in document.Results)
        {
            byPlayer[result.Player.PlayerId] = result;
            ValidateResult(result, currentTargets, allowedVerdicts, context);
        }

        Require(
            document.Results.Count == byPlayer.Count,
            "results",
            "unique-results");
        ValidateCoverage(document.Coverage, document.Results, context);

        if (document.Status == StatusComplete)
        {
            Require(
                document.Results.Count == context.Targets.Count,
                "status",
                "complete-results");
            foreach (EvidenceReviewTargetDocument target in context.Targets)
            {
                Require(
                    byPlayer.ContainsKey(target.Player.PlayerId),
                    "status",
                    "complete-missing-target");
            }
        }
        else if (document.Status is StatusRefused or StatusUnavailable)
        {
            Require(
                context.Targets.Count == 0 || document.Results.Count == 0,
                "status",
                "refusal-results");
        }
        else
        {
            Require(
                document.Results.Count < context.Targets.Count
                    || document.Results.Any(result => result.IsAbstained),
                "coverage",
                "insufficient-evidence-state");
        }
    }

    private static void ValidateResult(
        EvidenceSemanticReviewResultDocument result,
        IReadOnlyDictionary<int, EvidenceReviewTargetDocument> currentTargets,
        IReadOnlySet<string> allowedVerdicts,
        EvidenceReviewContextDocument context)
    {
        Require(
            result.Player.PlayerId > 0,
            "target-player",
            "player.playerId");
        Require(
            currentTargets.TryGetValue(result.Player.PlayerId, out EvidenceReviewTargetDocument? target),
            "target-player",
            "player.playerId");
        Require(
            result.Player.WebName == target!.Player.WebName
                && result.Player.TeamId == target.Player.TeamId
                && result.Player.TeamName == target.Player.TeamName
                && result.Player.Position == target.Player.Position,
            "target-player",
            "player.identity");
        Require(
            allowedVerdicts.Contains(result.Verdict),
            "result-verdict",
            $"player[{result.Player.PlayerId}]");
        Require(
            AllowedEvidenceQualities.Contains(result.EvidenceQuality),
            "result-quality",
            $"player[{result.Player.PlayerId}]");
        Require(
            !string.IsNullOrWhiteSpace(result.TimeHorizon)
                && result.TimeHorizon.Length <= 120,
            "result-time-horizon",
            $"player[{result.Player.PlayerId}]");
        Require(
            !string.IsNullOrWhiteSpace(result.Scope)
                && result.Scope.Length <= 180,
            "result-scope",
            $"player[{result.Player.PlayerId}]");
        if (result.IsUnavailable)
        {
            Require(result.IsAbstained, "result-state", "isUnavailable");
            Require(
                !string.IsNullOrWhiteSpace(result.UnavailableReason),
                "result-state",
                "unavailableReason");
        }
        else
        {
            Require(
                string.IsNullOrWhiteSpace(result.UnavailableReason),
                "result-state",
                "unavailableReason");
        }

        if (!result.IsAbstained)
        {
            Require(
                result.SupportingClaimIds.Count + result.ContradictingClaimIds.Count > 0,
                "result-evidence",
                $"player[{result.Player.PlayerId}]");
            Require(
                !string.IsNullOrWhiteSpace(result.Rationale),
                "result-rationale",
                $"player[{result.Player.PlayerId}]");
            Require(
                result.Rationale!.Length <= 2000,
                "result-rationale",
                $"player[{result.Player.PlayerId}]");
        }

        HashSet<long> allClaims = [];
        foreach (long claimId in result.SupportingClaimIds)
        {
            Require(claimId > 0, "result-supporting-claim", "supportingClaimIds");
            allClaims.Add(claimId);
        }
        foreach (long claimId in result.ContradictingClaimIds)
        {
            Require(
                claimId > 0,
                "result-contradicting-claim",
                "contradictingClaimIds");
            allClaims.Add(claimId);
        }
        foreach (long claimId in result.DependentClaimIds)
        {
            Require(claimId > 0, "result-dependent-claim", "dependentClaimIds");
            allClaims.Add(claimId);
        }
        HashSet<long> targetClaimIds = target!.Claims
            .Select(claim => claim.ClaimId)
            .ToHashSet();
        Require(
            allClaims.All(claimId => targetClaimIds.Contains(claimId)),
            "result-citation",
            $"player[{result.Player.PlayerId}]");
        Require(
            result.SupportingClaimIds.Count == result.SupportingClaimIds.Distinct()
                .Count(),
            "result-citation",
            "supportingClaimIds");
        Require(
            result.ContradictingClaimIds.Count
            == result.ContradictingClaimIds.Distinct().Count(),
            "result-citation",
            "contradictingClaimIds");
        Require(
            result.DependentClaimIds.Count
            == result.DependentClaimIds.Distinct().Count(),
            "result-citation",
            "dependentClaimIds");
        Require(
            result.ScenarioKeys.Count
            == result.ScenarioKeys.Distinct(StringComparer.Ordinal).Count(),
            "result-scenario",
            $"player[{result.Player.PlayerId}]");
        Require(
            result.ScenarioKeys.All(
                scenarioKey => target.ScenarioKeys.Contains(
                    scenarioKey,
                    StringComparer.Ordinal)),
            "result-scenario",
            $"player[{result.Player.PlayerId}]");
        HashSet<string> citedSourceKeys = target.Claims
            .Where(claim => allClaims.Contains(claim.ClaimId))
            .Select(claim => claim.SourceKey)
            .ToHashSet(StringComparer.Ordinal);
        EnsureSourceList(
            result.CorroboratingSourceKeys,
            citedSourceKeys,
            "result-corroborating-source",
            context.Coverage.SourceCount);
        EnsureSourceList(
            result.ContradictingSourceKeys,
            citedSourceKeys,
            "result-contradicting-source",
            context.Coverage.SourceCount);
        EnsureTextList(result.Assumptions, "result-assumption");
        EnsureTextList(result.Uncertainties, "result-uncertainty");
    }

    private static void ValidateCoverage(
        EvidenceSemanticReviewCoverageDocument coverage,
        IReadOnlyList<EvidenceSemanticReviewResultDocument> results,
        EvidenceReviewContextDocument context)
    {
        int resultCount = results.Count;
        int abstainedCount = results.Count(result => result.IsAbstained);
        int unavailableCount = results.Count(result => result.IsUnavailable);
        int citedClaimCount = results.SelectMany(
                result =>
                    result.SupportingClaimIds
                        .Concat(result.ContradictingClaimIds)
                        .Concat(result.DependentClaimIds))
            .Distinct()
            .Count();
        HashSet<string> sources = results.SelectMany(
                result =>
                    result.CorroboratingSourceKeys.Concat(
                        result.ContradictingSourceKeys))
            .ToHashSet(StringComparer.Ordinal);

        Require(
            coverage.ContextTargetCount == context.Targets.Count,
            "coverage",
            "contextTargetCount");
        Require(
            coverage.ResultTargetCount == resultCount,
            "coverage",
            "resultTargetCount");
        Require(
            coverage.AbstainedTargetCount == abstainedCount,
            "coverage",
            "abstainedTargetCount");
        Require(
            coverage.UnavailableTargetCount == unavailableCount,
            "coverage",
            "unavailableTargetCount");
        Require(
            coverage.CitedClaimCount == citedClaimCount,
            "coverage",
            "citedClaimCount");
        Require(
            coverage.ClaimedSourceCount == sources.Count,
            "coverage",
            "claimedSourceCount");
    }

    private static void EnsureTextList(
        IReadOnlyList<string> values,
        string field)
    {
        foreach (string value in values)
        {
            Require(
                !string.IsNullOrWhiteSpace(value) && value.Length <= 240,
                "result-list",
                field);
        }
    }

    private static void EnsureSourceList(
        IReadOnlyList<string> values,
        IReadOnlySet<string> allowedValues,
        string field,
        int maxTotalItems)
    {
        Require(values.Count <= maxTotalItems, field, "max-items");
        EnsureTextList(values, field);
        Require(
            values.Count == values.Distinct(StringComparer.Ordinal).Count(),
            field,
            "unique-items");
        Require(
            values.All(allowedValues.Contains),
            field,
            "unknown-source");
    }

    private static async Task<StoredArtifact?> ReadByRunIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long stressArtifactId,
        string runIdentitySha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT review_artifact_id, document_json, content_sha256
            FROM evidence_semantic_review_artifacts
            WHERE stress_artifact_id = $stressArtifactId
              AND run_identity_sha256 = $runIdentitySha256
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$stressArtifactId", stressArtifactId);
        command.Parameters.AddWithValue("$runIdentitySha256", runIdentitySha256);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2))
            : null;
    }

    private static EvidenceSemanticReviewDocument Materialize(
        StoredArtifact artifact)
    {
        EvidenceSemanticReviewDocument document =
            JsonSerializer.Deserialize<EvidenceSemanticReviewDocument>(
                artifact.DocumentJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The evidence semantic review document is invalid.");
        return document with
        {
            EvidenceSemanticReviewArtifactId = artifact.ArtifactId,
            EvidenceSemanticReviewArtifactContentSha256 = artifact.ContentSha256,
        };
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(
            character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");

    private static object? DbValue(object? value) => value ?? DBNull.Value;

    private static void Require(
        bool condition,
        string code,
        string field)
    {
        if (!condition)
        {
            throw new EvidenceSemanticReviewValidationException(code, field);
        }
    }

    private sealed record StoredArtifact(
        long ArtifactId,
        string DocumentJson,
        string ContentSha256);
}

public sealed class EvidenceSemanticReviewImporter
{
    public const int MaximumInputBytes = 5_242_880;

    private readonly EvidenceSemanticReviewStore _store;

    public EvidenceSemanticReviewImporter(EvidenceSemanticReviewStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<EvidenceSemanticReviewDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The evidence semantic review file does not exist.",
                fullPath);
        }
        if (file.Length is <= 0 or > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Evidence semantic review files must contain 1 to {MaximumInputBytes} bytes.");
        }
        await using FileStream stream = file.OpenRead();
        EvidenceSemanticReviewDocument document =
            await JsonSerializer.DeserializeAsync<EvidenceSemanticReviewDocument>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new JsonException(
                "The evidence semantic review document cannot be null.");
        if (stream.Position > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Evidence semantic review files must contain at most {MaximumInputBytes} bytes.");
        }

        return await _store.ImportAsync(document, cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            AllowDuplicateProperties = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
}

public sealed class EvidenceSemanticReviewValidationException : Exception
{
    public EvidenceSemanticReviewValidationException(string code, string field)
        : base($"Evidence semantic review validation failed: {code}.")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
