using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Forecasts;

public sealed class ExternalEvidenceStressStore
{
    public const string ArtifactType = "current-external-evidence-stress";
    public const string ArtifactVersion =
        "current-external-evidence-stress-v1";
    public const string Status = "external-evidence-stress-non-serving";
    public const string Method = "gameweek-one-zero-appearance-extreme";

    private const string EvaluationPolicyKey = "6-expected-points";
    private const string OptimizerVersion =
        "scipy-highs-multi-horizon-mean-cvar-v1";
    private const string OptimizerStatus =
        "global-linear-mean-cvar-surrogate-optimum";
    private const decimal NumericMipGapLimit = 0.000000000001m;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public ExternalEvidenceStressStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ExternalEvidenceStressDocument> ImportAsync(
        ExternalEvidenceStressDocument document,
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
        await ValidateCurrentCaptureAsync(
            connection,
            transaction,
            document,
            cancellationToken);
        await ValidatePlayersAsync(
            connection,
            transaction,
            document,
            cancellationToken);
        await ValidateClaimsAsync(
            connection,
            transaction,
            document,
            cancellationToken);

        string documentJson = JsonSerializer.Serialize(document, JsonOptions);
        string contentSha256 = Sha256(documentJson);
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT OR IGNORE INTO external_evidence_stress_artifacts (
                    schema_version,
                    artifact_type,
                    artifact_version,
                    status,
                    official_capture_id,
                    season_code,
                    opening_gameweek,
                    evidence_cutoff_utc,
                    producer_data_identity_sha256,
                    producer_run_identity_sha256,
                    document_json,
                    content_sha256,
                    created_at_utc
                )
                VALUES (
                    $schemaVersion,
                    $artifactType,
                    $artifactVersion,
                    $status,
                    $officialCaptureId,
                    $seasonCode,
                    $openingGameweek,
                    $evidenceCutoffUtc,
                    $dataIdentity,
                    $runIdentity,
                    $documentJson,
                    $contentSha256,
                    $createdAtUtc
                );
                """;
            insert.Parameters.AddWithValue(
                "$schemaVersion",
                document.SchemaVersion);
            insert.Parameters.AddWithValue(
                "$artifactType",
                document.ArtifactType);
            insert.Parameters.AddWithValue(
                "$artifactVersion",
                document.ArtifactVersion);
            insert.Parameters.AddWithValue("$status", document.Status);
            insert.Parameters.AddWithValue(
                "$officialCaptureId",
                document.OfficialCaptureId);
            insert.Parameters.AddWithValue(
                "$seasonCode",
                document.SeasonCode);
            insert.Parameters.AddWithValue(
                "$openingGameweek",
                document.OpeningGameweek);
            insert.Parameters.AddWithValue(
                "$evidenceCutoffUtc",
                FormatUtc(document.EvidenceDecisionCutoffUtc));
            insert.Parameters.AddWithValue(
                "$dataIdentity",
                document.DataIdentitySha256);
            insert.Parameters.AddWithValue(
                "$runIdentity",
                document.RunIdentitySha256);
            insert.Parameters.AddWithValue("$documentJson", documentJson);
            insert.Parameters.AddWithValue(
                "$contentSha256",
                contentSha256);
            insert.Parameters.AddWithValue(
                "$createdAtUtc",
                FormatUtc(_timeProvider.GetUtcNow()));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        StoredArtifact? stored = await ReadByDataIdentityAsync(
            connection,
            transaction,
            document.DataIdentitySha256,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return stored is null
            ? throw new ExternalEvidenceStressValidationException(
                "not-persisted",
                "dataIdentitySha256")
            : Materialize(stored);
    }

    public async Task<ExternalEvidenceStressDocument?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT stress_artifact_id, document_json, content_sha256
            FROM external_evidence_stress_artifacts
            WHERE official_capture_id = (
                SELECT capture_id
                FROM official_fpl_captures
                ORDER BY available_at_utc DESC, capture_id DESC
                LIMIT 1
            )
            ORDER BY
                evidence_cutoff_utc DESC,
                stress_artifact_id DESC
            LIMIT 1;
            """;
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
        ExternalEvidenceStressDocument document)
    {
        Require(document.SchemaVersion == "1.0", "identity", "schemaVersion");
        Require(document.ArtifactType == ArtifactType, "identity", "artifactType");
        Require(
            document.ArtifactVersion == ArtifactVersion,
            "identity",
            "artifactVersion");
        Require(document.Status == Status, "identity", "status");
        Require(!document.IsPromoted, "must-be-false", "isPromoted");
        Require(!document.InfluencesAdvice, "must-be-false", "influencesAdvice");
        Require(
            document.ExternalEvidenceStressArtifactId is null
                && document.ExternalEvidenceStressArtifactContentSha256 is null,
            "must-be-null",
            "externalEvidenceStressArtifactId");
        Require(
            document.SeasonCode == "2026-27"
                && document.OpeningGameweek == 1
                && document.OfficialCaptureId > 0
                && document.ForecastDecisionCutoffUtc
                    <= document.EvidenceDecisionCutoffUtc
                && document.EvidenceDecisionCutoffUtc <= document.DeadlineUtc
                && document.ScenarioCount is >= 1 and <= 512
                && document.CandidatePoolCount is >= 15 and <= 1024,
            "target",
            "officialCaptureId");
        Require(
            document.StressMethod.Method == Method
                && document.StressMethod.AffectedGameweeks.SequenceEqual([1])
                && document.StressMethod.LaterGameweeksUnchanged
                && !document.StressMethod.AssignsSourceProbability,
            "method",
            "stressMethod");
        Require(
            document.Policy.EvaluationPolicyKey == EvaluationPolicyKey
                && document.Policy.HorizonGameweeks == 6
                && document.Policy.OptimizerPolicyKey == "expected-points"
                && document.Policy.OptimizerVersion == OptimizerVersion
                && document.Policy.BenchWeight == 0.15m
                && document.Policy.CvarWeight == 0m,
            "policy",
            "policy");
        Require(
            document.Incumbent.PlayerIds.Count == 15
                && document.Incumbent.PlayerIds.Distinct().Count() == 15
                && document.Incumbent.Players.Count == 15
                && document.Incumbent.Players.Select(row => row.PlayerId)
                    .SequenceEqual(document.Incumbent.PlayerIds)
                && document.Incumbent.SolverStatus == OptimizerStatus
                && document.Incumbent.ReportedMipGap
                    is >= 0m and <= NumericMipGapLimit,
            "incumbent",
            "incumbent");
        Require(
            document.Coverage.LatestClaimCount >= 0
                && document.Coverage.AdverseClaimCount
                    == document.AdverseClaims.Count
                && document.Coverage.SelectedAdversePlayerCount
                    == document.SelectedAdversePlayers.Count
                && document.Coverage.StressScenarioCount
                    == document.StressScenarios.Count
                && document.Coverage.SquadChangingScenarioCount
                    == document.StressScenarios.Count(row => row.SquadChanged)
                && document.Coverage.DecisionRelevantScenarioCount
                    == document.StressScenarios.Count(
                        row => row.StressDecision
                            == "consider-alternative-if-source-trusted")
                && (
                    document.Decision == "review-evidence-sensitive-squad"
                ) == (document.Coverage.DecisionRelevantScenarioCount > 0),
            "coverage",
            "coverage");
        Require(
            document.Decision
                is "review-evidence-sensitive-squad"
                or "retain-incumbent-under-current-evidence-stresses",
            "decision",
            "decision");
        Require(
            document.Source.ScenarioArtifactVersion
                    == "current-multi-horizon-joint-scenario-shadow-v1"
                && IsSha256(document.Source.ScenarioContentSha256)
                && IsSha256(document.Source.ScenarioRunIdentitySha256)
                && IsSha256(document.DataIdentitySha256)
                && IsSha256(document.RunIdentitySha256),
            "source",
            "source");
        Require(
            document.Limitations.Count is >= 1 and <= 16
                && document.Limitations.All(
                    value => !string.IsNullOrWhiteSpace(value)
                        && value.Length <= 1000),
            "limitations",
            "limitations");
        foreach (ExternalEvidenceStressScenarioDocument scenario
            in document.StressScenarios)
        {
            ValidateScenario(document.Incumbent.PlayerIds, scenario);
        }
    }

    private static void ValidateScenario(
        IReadOnlyList<int> incumbentPlayerIds,
        ExternalEvidenceStressScenarioDocument scenario)
    {
        HashSet<int> incumbent = incumbentPlayerIds.ToHashSet();
        HashSet<int> affected = scenario.AffectedPlayerIds.ToHashSet();
        HashSet<int> selection = scenario.SelectionPlayerIds.ToHashSet();
        HashSet<int> affectedSelected = scenario.AffectedSelectedPlayers
            .Select(row => row.PlayerId)
            .ToHashSet();
        HashSet<int> removed = scenario.RemovedPlayers
            .Select(row => row.PlayerId)
            .ToHashSet();
        HashSet<int> added = scenario.AddedPlayers
            .Select(row => row.PlayerId)
            .ToHashSet();
        bool sourceConsistent =
            scenario.UnappliedAdverseEvidenceForAddedPlayers.Count == 0;
        bool shouldConsider =
            !selection.SetEquals(incumbent)
            && sourceConsistent
            && scenario.ExactScenarioMeanDifferenceIfStressTruePoints > 0m;
        Require(
            !string.IsNullOrWhiteSpace(scenario.ScenarioKey)
                && scenario.SourceKeys.Count is >= 1 and <= 3
                && scenario.SourceKeys.Distinct().Count()
                    == scenario.SourceKeys.Count
                && scenario.AffectedPlayerCount
                    == scenario.AffectedPlayerIds.Distinct().Count()
                && scenario.AffectedPlayerCount > 0
                && scenario.ClaimIds.Count > 0
                && scenario.ClaimIds.Distinct().Count()
                    == scenario.ClaimIds.Count
                && scenario.SelectionPlayerIds.Count == 15
                && scenario.SelectionPlayerIds.Distinct().Count() == 15
                && affectedSelected.SetEquals(
                    affected.Intersect(incumbent))
                && removed.SetEquals(incumbent.Except(selection))
                && added.SetEquals(selection.Except(incumbent))
                && scenario.SquadChanged
                    == !selection.SetEquals(incumbent)
                && scenario.SelectionBudgetTenths is > 0 and <= 1000
                && scenario.SolverStatus == OptimizerStatus
                && scenario.ReportedMipGap
                    is >= 0m and <= NumericMipGapLimit
                && scenario.IsSourceConsistentAlternative
                    == sourceConsistent
                && (
                    scenario.StressDecision
                        == "consider-alternative-if-source-trusted"
                ) == shouldConsider,
            "scenario",
            "stressScenarios");
    }

    private static async Task ValidateCurrentCaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExternalEvidenceStressDocument document,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT capture_id, season_code, next_gameweek_number,
                   next_deadline_utc, available_at_utc
            FROM official_fpl_captures
            ORDER BY available_at_utc DESC, capture_id DESC
            LIMIT 1;
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        Require(
            await reader.ReadAsync(cancellationToken)
                && reader.GetInt64(0) == document.OfficialCaptureId
                && reader.GetString(1) == document.SeasonCode
                && reader.GetInt32(2) == document.OpeningGameweek
                && DateTimeOffset.Parse(reader.GetString(3))
                    == document.DeadlineUtc
                && DateTimeOffset.Parse(reader.GetString(4))
                    == document.ForecastDecisionCutoffUtc,
            "official-capture",
            "officialCaptureId");
    }

    private static async Task ValidatePlayersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExternalEvidenceStressDocument document,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT player.player_id, player.web_name, player.team_id,
                   team.name, player.position, player.price_tenths
            FROM official_fpl_players AS player
            INNER JOIN official_fpl_teams AS team
                ON team.capture_id = player.capture_id
               AND team.team_id = player.team_id
            WHERE player.capture_id = $captureId
              AND player.status <> 'u';
            """;
        command.Parameters.AddWithValue(
            "$captureId",
            document.OfficialCaptureId);
        var official = new Dictionary<int, ExternalEvidenceStressPlayerDocument>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            official.Add(
                reader.GetInt32(0),
                new(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5)));
        }
        Require(
            official.Count == document.CandidatePoolCount,
            "candidate-pool",
            "candidatePoolCount");
        IEnumerable<ExternalEvidenceStressPlayerDocument> referenced =
            document.Incumbent.Players
                .Concat(document.SelectedAdversePlayers)
                .Concat(document.StressScenarios.SelectMany(
                    row => row.AffectedSelectedPlayers
                        .Concat(row.RemovedPlayers)
                        .Concat(row.AddedPlayers)));
        foreach (ExternalEvidenceStressPlayerDocument player
            in referenced)
        {
            Require(
                official.TryGetValue(player.PlayerId, out var expected)
                    && expected == player,
                "official-player",
                $"player[{player.PlayerId}]");
        }
        foreach (ExternalEvidenceStressClaimDocument claim
            in document.AdverseClaims.Concat(
                document.StressScenarios.SelectMany(
                    row => row.UnappliedAdverseEvidenceForAddedPlayers)))
        {
            Require(
                official.TryGetValue(claim.PlayerId, out var expected)
                    && expected.WebName == claim.WebName
                    && expected.TeamId == claim.TeamId
                    && expected.TeamName == claim.TeamName
                    && expected.Position == claim.Position,
                "official-player",
                $"claim[{claim.ClaimId}].playerId");
        }
    }

    private static async Task ValidateClaimsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExternalEvidenceStressDocument document,
        CancellationToken cancellationToken)
    {
        long[] claimIds =
        [
            .. document.StressScenarios
                .SelectMany(row => row.ClaimIds)
                .Distinct()
                .Order(),
        ];
        long[] documented =
        [
            .. document.AdverseClaims
                .Select(row => row.ClaimId)
                .Distinct()
                .Order(),
        ];
        Require(
            claimIds.ToHashSet().SetEquals(documented),
            "claim-reference",
            "stressScenarios.claimIds");
        if (documented.Length == 0)
        {
            return;
        }
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        string parameters = string.Join(
            ",",
            documented.Select((_, index) => $"$claim{index}"));
        command.CommandText =
            $"""
            SELECT
                claim.claim_id,
                claim.source_key,
                claim.claim_type,
                claim.available_at_utc,
                current_identity.player_id,
                claim.start_status,
                claim.availability_status,
                claim.forecast_probability,
                claim.claim_content_sha256,
                claim.duplicate_cluster_key
            FROM evidence_claims AS claim
            INNER JOIN official_fpl_players AS claim_identity
                ON claim_identity.capture_id = claim.identity_capture_id
               AND claim_identity.player_id = claim.player_id
            INNER JOIN official_fpl_players AS current_identity
                ON current_identity.capture_id = $captureId
               AND current_identity.code = claim_identity.code
            WHERE claim.claim_id IN ({parameters})
              AND claim.status = 'quarantined';
            """;
        command.Parameters.AddWithValue(
            "$captureId",
            document.OfficialCaptureId);
        for (int index = 0; index < documented.Length; index++)
        {
            command.Parameters.AddWithValue(
                $"$claim{index}",
                documented[index]);
        }
        var expectedClaims = document.AdverseClaims.ToDictionary(
            row => row.ClaimId);
        var found = new HashSet<long>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long claimId = reader.GetInt64(0);
            Require(
                expectedClaims.TryGetValue(claimId, out var expected)
                    && expected.SourceKey == reader.GetString(1)
                    && expected.ClaimType == reader.GetString(2)
                    && expected.AvailableAtUtc
                        == DateTimeOffset.Parse(reader.GetString(3))
                    && expected.AvailableAtUtc
                        <= document.EvidenceDecisionCutoffUtc
                    && expected.PlayerId == reader.GetInt32(4)
                    && expected.StartStatus
                        == (reader.IsDBNull(5) ? null : reader.GetString(5))
                    && expected.AvailabilityStatus
                        == (reader.IsDBNull(6) ? null : reader.GetString(6))
                    && expected.ForecastProbability
                        == (reader.IsDBNull(7)
                            ? null
                            : reader.GetDecimal(7))
                    && expected.ClaimContentSha256 == reader.GetString(8)
                    && expected.DuplicateClusterKey
                        == (reader.IsDBNull(9) ? null : reader.GetString(9)),
                "claim-content",
                $"adverseClaims[{claimId}]");
            found.Add(claimId);
        }
        Require(
            found.SetEquals(documented),
            "claim-reference",
            "adverseClaims");
    }

    private static async Task<StoredArtifact?> ReadByDataIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string dataIdentity,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT stress_artifact_id, document_json, content_sha256
            FROM external_evidence_stress_artifacts
            WHERE producer_data_identity_sha256 = $dataIdentity;
            """;
        command.Parameters.AddWithValue("$dataIdentity", dataIdentity);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2))
            : null;
    }

    private static ExternalEvidenceStressDocument Materialize(
        StoredArtifact artifact)
    {
        ExternalEvidenceStressDocument document =
            JsonSerializer.Deserialize<ExternalEvidenceStressDocument>(
                artifact.DocumentJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The external evidence stress document is invalid.");
        return document with
        {
            ExternalEvidenceStressArtifactId = artifact.ArtifactId,
            ExternalEvidenceStressArtifactContentSha256 =
                artifact.ContentSha256,
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

    private static void Require(
        bool condition,
        string code,
        string field)
    {
        if (!condition)
        {
            throw new ExternalEvidenceStressValidationException(code, field);
        }
    }

    private sealed record StoredArtifact(
        long ArtifactId,
        string DocumentJson,
        string ContentSha256);
}

public sealed class ExternalEvidenceStressImporter
{
    private readonly ExternalEvidenceStressStore _store;

    public ExternalEvidenceStressImporter(
        ExternalEvidenceStressStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ExternalEvidenceStressDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The external evidence stress file does not exist.",
                path);
        }
        if (file.Length is < 2 or > 1024 * 1024)
        {
            throw new InvalidDataException(
                "The external evidence stress file is outside its size limit.");
        }
        await using FileStream stream = file.OpenRead();
        ExternalEvidenceStressDocument document =
            await JsonSerializer.DeserializeAsync<
                ExternalEvidenceStressDocument>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken)
            ?? throw new JsonException(
                "The external evidence stress document cannot be null.");
        return await _store.ImportAsync(document, cancellationToken);
    }
}

public sealed class ExternalEvidenceStressValidationException : Exception
{
    public ExternalEvidenceStressValidationException(
        string code,
        string field)
        : base($"{code}: {field}")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
