using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Forecasts;

public sealed class SelectedOpeningSquadShadowStore
{
    public const string ArtifactType =
        "current-selected-opening-squad-shadow";
    public const string ArtifactVersion =
        "current-selected-opening-squad-shadow-v1";
    public const string Status = "prospective-shadow-unscored";
    public const string EvaluationPolicyKey = "6-expected-points";
    public const string EvaluationArtifactVersion =
        "historical-opening-policy-evaluation-v1";
    public const string EvaluationDataIdentity =
        "e99bb4fce3615c91ecaf642037c6cebb3d36efffc2df54857ffbc4c27714e048";
    public const string EvaluationRunIdentity =
        "a79acdbc991768c31f4bf3abc1bcaa5724dcdfbdb89f0e3f0c0b63a530c5008e";

    private const string OptimizerVersion =
        "scipy-highs-multi-horizon-mean-cvar-v1";
    private const string OptimizerStatus =
        "global-linear-mean-cvar-surrogate-optimum";
    private const decimal MaximumNumericalMipGap = 0.000000000001m;
    private const decimal NumericTolerance = 0.000001m;
    private const decimal LowerTailFraction = 0.20m;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public SelectedOpeningSquadShadowStore(
        DatabaseOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<SelectedOpeningSquadShadowDocument> ImportAsync(
        SelectedOpeningSquadShadowDocument document,
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
        OfficialCapture capture = await ReadOfficialCaptureAsync(
            connection,
            transaction,
            document.OfficialCaptureId,
            cancellationToken);
        ValidateCapture(document, capture);
        await ValidatePlayersAsync(
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
                INSERT INTO selected_opening_squad_shadow_artifacts (
                    schema_version, artifact_type, artifact_version, status,
                    official_capture_id, season_code, opening_gameweek,
                    decision_cutoff_utc, evaluation_policy_key,
                    evaluation_data_identity_sha256, scenario_count,
                    candidate_pool_count, budget_tenths,
                    producer_data_identity_sha256,
                    producer_run_identity_sha256, document_json,
                    content_sha256, created_at_utc
                )
                VALUES (
                    $schemaVersion, $artifactType, $artifactVersion, $status,
                    $officialCaptureId, $seasonCode, $openingGameweek,
                    $decisionCutoffUtc, $evaluationPolicyKey,
                    $evaluationDataIdentity, $scenarioCount,
                    $candidatePoolCount, $budgetTenths, $dataIdentity,
                    $runIdentity, $documentJson, $contentSha256, $createdAtUtc
                )
                ON CONFLICT (
                    official_capture_id,
                    evaluation_policy_key,
                    evaluation_data_identity_sha256
                ) DO NOTHING;
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
            insert.Parameters.AddWithValue("$seasonCode", document.SeasonCode);
            insert.Parameters.AddWithValue(
                "$openingGameweek",
                document.OpeningGameweek);
            insert.Parameters.AddWithValue(
                "$decisionCutoffUtc",
                document.DecisionCutoffUtc.UtcDateTime.ToString("O"));
            insert.Parameters.AddWithValue(
                "$evaluationPolicyKey",
                document.SelectedPolicy.EvaluationPolicyKey);
            insert.Parameters.AddWithValue(
                "$evaluationDataIdentity",
                document.SelectedPolicy.RetrospectiveEvaluationSource
                    .DataIdentitySha256);
            insert.Parameters.AddWithValue(
                "$scenarioCount",
                document.ScenarioCount);
            insert.Parameters.AddWithValue(
                "$candidatePoolCount",
                document.CandidatePoolCount);
            insert.Parameters.AddWithValue(
                "$budgetTenths",
                document.Selection.BudgetTenths);
            insert.Parameters.AddWithValue(
                "$dataIdentity",
                document.DataIdentitySha256);
            insert.Parameters.AddWithValue(
                "$runIdentity",
                document.RunIdentitySha256);
            insert.Parameters.AddWithValue("$documentJson", documentJson);
            insert.Parameters.AddWithValue("$contentSha256", contentSha256);
            insert.Parameters.AddWithValue(
                "$createdAtUtc",
                _timeProvider.GetUtcNow().UtcDateTime.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        StoredArtifact stored = await ReadExactAsync(
            connection,
            transaction,
            document.OfficialCaptureId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The selected opening squad shadow was not persisted.");
        if (!StringComparer.Ordinal.Equals(
            stored.ContentSha256,
            contentSha256))
        {
            throw new SelectedOpeningSquadValidationException(
                "source-conflict",
                "dataIdentitySha256");
        }
        await transaction.CommitAsync(cancellationToken);
        return Materialize(stored);
    }

    public async Task<SelectedOpeningSquadShadowDocument?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT selected_opening_squad_artifact_id,
                   document_json, content_sha256
            FROM selected_opening_squad_shadow_artifacts
            WHERE official_capture_id = (
                SELECT capture_id
                FROM official_fpl_captures
                ORDER BY available_at_utc DESC, capture_id DESC
                LIMIT 1
            )
              AND evaluation_policy_key = $policyKey
              AND evaluation_data_identity_sha256 = $evaluationIdentity
            ORDER BY selected_opening_squad_artifact_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$policyKey", EvaluationPolicyKey);
        command.Parameters.AddWithValue(
            "$evaluationIdentity",
            EvaluationDataIdentity);
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
        SelectedOpeningSquadShadowDocument document)
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
            document.SelectedOpeningSquadArtifactId is null
                && document.SelectedOpeningSquadArtifactContentSha256 is null,
            "must-be-null",
            "selectedOpeningSquadArtifactId");
        Require(
            document.SeasonCode == "2026-27"
                && document.OpeningGameweek == 1
                && document.OfficialCaptureId > 0
                && document.DecisionCutoffUtc < document.DeadlineUtc
                && document.CandidatePoolCount is >= 15 and <= 1024
                && document.ScenarioCount is >= 1 and <= 512,
            "target",
            "officialCaptureId");
        ValidatePolicy(document.SelectedPolicy);
        ValidateSelection(document.Selection);
        ValidatePreseasonScore(
            document.PreseasonScenarioScore,
            document.ScenarioCount);
        Require(
            document.ProspectiveScoreRegistration.OutcomeGameweeks
                .SequenceEqual(Enumerable.Range(1, 8))
                && document.ProspectiveScoreRegistration.SquadMembership
                    == "fixed-opening-squad-no-transfers-for-all-eight-gameweeks"
                && document.ProspectiveScoreRegistration.Roles
                    == "all-eight-weeks-frozen-from-preseason-scenario-means"
                && document.ProspectiveScoreRegistration.RealisedScorer
                    == "exact-fpl-captain-fallback-and-ordered-auto-substitution"
                && document.ProspectiveScoreRegistration.OutcomeStatus
                    == "waiting-for-official-2026-27-outcomes",
            "prospective-registration",
            "prospectiveScoreRegistration");
        Require(
            document.Source.ArtifactVersion
                    == "current-multi-horizon-initial-squad-shadow-v2"
                && IsSha256(document.Source.DataIdentitySha256)
                && IsSha256(document.Source.RunIdentitySha256),
            "source",
            "source");
        Require(
            document.Limitations.Count is >= 1 and <= 32
                && document.Limitations.All(
                    value => !string.IsNullOrWhiteSpace(value)
                        && value.Length <= 1000),
            "boundary",
            "limitations");
        Require(
            IsSha256(document.DataIdentitySha256)
                && IsSha256(document.RunIdentitySha256),
            "sha256",
            "dataIdentitySha256");
    }

    private static void ValidatePolicy(SelectedOpeningPolicyDocument policy)
    {
        Require(
            policy.EvaluationPolicyKey == EvaluationPolicyKey
                && policy.HorizonGameweeks == 6
                && policy.OptimizerPolicyKey == "expected-points"
                && policy.Solver.OptimizerVersion == OptimizerVersion
                && policy.Solver.Solver == "scipy.optimize.milp-highs"
                && policy.Solver.Status == OptimizerStatus
                && policy.Solver.MipGap == 0m
                && policy.Solver.ReportedMipGap is >= 0m
                    and <= MaximumNumericalMipGap
                && policy.Solver.MaximumNumericalMipGap
                    == MaximumNumericalMipGap,
            "policy",
            "selectedPolicy");
        SelectedOpeningEvaluationSourceDocument source =
            policy.RetrospectiveEvaluationSource;
        Require(
            source.ArtifactVersion == EvaluationArtifactVersion
                && source.DataIdentitySha256 == EvaluationDataIdentity
                && source.RunIdentitySha256 == EvaluationRunIdentity,
            "evaluation-source",
            "selectedPolicy.retrospectiveEvaluationSource");
    }

    private static void ValidateSelection(
        SelectedOpeningSelectionDocument selection)
    {
        Require(
            selection.PlayerIds.Count == 15
                && selection.PlayerIds.Distinct().Count() == 15
                && selection.Players.Count == 15
                && selection.Players.Select(row => row.PlayerId)
                    .SequenceEqual(selection.PlayerIds)
                && selection.BudgetTenths
                    == selection.Players.Sum(row => row.PriceTenths)
                && selection.BudgetTenths is > 0 and <= 1000,
            "squad",
            "selection");
        var positionCounts = selection.Players
            .GroupBy(row => row.Position)
            .ToDictionary(group => group.Key, group => group.Count());
        Require(
            positionCounts.Count == 4
                && positionCounts.GetValueOrDefault("goalkeeper") == 2
                && positionCounts.GetValueOrDefault("defender") == 5
                && positionCounts.GetValueOrDefault("midfielder") == 5
                && positionCounts.GetValueOrDefault("forward") == 3
                && selection.Players.GroupBy(row => row.TeamId)
                    .All(group => group.Count() <= 3),
            "squad-constraints",
            "selection.players");
        Require(
            selection.Gameweeks.Count == 8
                && selection.Gameweeks.Select(row => row.Gameweek)
                    .SequenceEqual(Enumerable.Range(1, 8)),
            "gameweek-coverage",
            "selection.gameweeks");
        var positionByPlayer = selection.Players.ToDictionary(
            row => row.PlayerId,
            row => row.Position);
        foreach (SelectedOpeningGameweekDocument gameweek
            in selection.Gameweeks)
        {
            ValidateGameweek(gameweek, positionByPlayer);
        }
    }

    private static void ValidatePreseasonScore(
        SelectedOpeningPreseasonScoreDocument score,
        int scenarioCount)
    {
        Require(
            score.Weekly.Count == 8
                && score.Weekly.Select(row => row.Gameweek)
                    .SequenceEqual(Enumerable.Range(1, 8))
                && score.Weekly.All(
                    row => row.SchemaVersion == "1.0"
                        && row.EngineVersion
                            == "cpu-joint-scenario-reference-v1"
                        && row.ScenarioCount == scenarioCount
                        && row.MinimumPoints <= row.P10Points
                        && row.P10Points <= row.MedianPoints
                        && row.MedianPoints <= row.P90Points
                        && row.P90Points <= row.MaximumPoints
                        && row.MeanActivatedSubstitutes is >= 0m and <= 4m
                        && row.ProbabilityOfUnreplacedStarter
                            is >= 0m and <= 1m),
            "weekly-score",
            "preseasonScenarioScore.weekly");
        Require(
            score.LowerTailFraction == LowerTailFraction
                && score.PathTotalPoints.Count == scenarioCount
                && score.PathTotalPoints.All(
                    value => value is >= -1600 and <= 16000),
            "score-rows",
            "preseasonScenarioScore.pathTotalPoints");
        int[] sorted = [.. score.PathTotalPoints.Order()];
        decimal mean =
            score.PathTotalPoints.Sum(value => (decimal)value) / scenarioCount;
        decimal variance = score.PathTotalPoints.Sum(
            value =>
            {
                decimal difference = value - mean;
                return difference * difference;
            }) / scenarioCount;
        SelectedOpeningCumulativeScoreDocument cumulative = score.Cumulative;
        Require(
            cumulative.ScenarioCount == scenarioCount
                && Close(cumulative.MeanPoints, mean)
                && Close(
                    cumulative.StandardDeviationPoints,
                    (decimal)Math.Sqrt((double)variance))
                && cumulative.MinimumPoints == sorted[0]
                && Close(cumulative.P10Points, Quantile(sorted, 0.10m))
                && Close(cumulative.MedianPoints, Quantile(sorted, 0.50m))
                && Close(cumulative.P90Points, Quantile(sorted, 0.90m))
                && cumulative.MaximumPoints == sorted[^1]
                && Close(
                    score.LowerTailCvarPoints,
                    LowerTailCvar(sorted)),
            "cumulative-score",
            "preseasonScenarioScore.cumulative");
    }

    private static void ValidateGameweek(
        SelectedOpeningGameweekDocument gameweek,
        IReadOnlyDictionary<int, string> positionByPlayer)
    {
        var players = positionByPlayer.Keys.ToHashSet();
        var starters = gameweek.StartingPlayerIds.ToHashSet();
        var substitutes = gameweek.OutfieldSubstitutePlayerIds
            .Append(gameweek.ReplacementGoalkeeperPlayerId)
            .ToHashSet();
        Require(
            gameweek.StartingPlayerIds.Count == 11
                && starters.Count == 11
                && substitutes.Count == 4
                && starters.IsSubsetOf(players)
                && substitutes.IsSubsetOf(players)
                && !starters.Overlaps(substitutes)
                && starters.Contains(gameweek.CaptainPlayerId)
                && starters.Contains(gameweek.ViceCaptainPlayerId)
                && gameweek.CaptainPlayerId
                    != gameweek.ViceCaptainPlayerId
                && positionByPlayer[
                    gameweek.ReplacementGoalkeeperPlayerId] == "goalkeeper"
                && gameweek.OutfieldSubstitutePlayerIds.Count == 3
                && gameweek.OutfieldSubstitutePlayerIds.All(
                    value => positionByPlayer[value] != "goalkeeper")
                && starters.Count(
                    value => positionByPlayer[value] == "goalkeeper") == 1
                && starters.Count(
                    value => positionByPlayer[value] == "defender") is >= 3
                    and <= 5
                && starters.Count(
                    value => positionByPlayer[value] == "midfielder") is >= 2
                    and <= 5
                && starters.Count(
                    value => positionByPlayer[value] == "forward") is >= 1
                    and <= 3,
            "gameweek-roles",
            $"selection.gameweeks[{gameweek.Gameweek}]");
    }

    private static async Task<OfficialCapture> ReadOfficialCaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT capture_id, season_code, next_gameweek_number,
                   next_deadline_utc, available_at_utc
            FROM official_fpl_captures
            WHERE capture_id = $captureId
              AND capture_id = (
                  SELECT capture_id
                  FROM official_fpl_captures
                  ORDER BY available_at_utc DESC, capture_id DESC
                  LIMIT 1
              );
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new SelectedOpeningSquadValidationException(
                "official-capture-not-current",
                "officialCaptureId");
        }
        return new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt32(2),
            DateTimeOffset.Parse(reader.GetString(3)),
            DateTimeOffset.Parse(reader.GetString(4)));
    }

    private static void ValidateCapture(
        SelectedOpeningSquadShadowDocument document,
        OfficialCapture capture)
    {
        Require(
            capture.CaptureId == document.OfficialCaptureId
                && capture.SeasonCode == document.SeasonCode
                && capture.Gameweek == document.OpeningGameweek
                && capture.DeadlineUtc == document.DeadlineUtc
                && capture.AvailableAtUtc == document.DecisionCutoffUtc,
            "official-capture",
            "officialCaptureId");
    }

    private static async Task ValidatePlayersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SelectedOpeningSquadShadowDocument document,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT player_id, web_name, team_id, position, price_tenths,
                   status, chance_next_round
            FROM official_fpl_players
            WHERE capture_id = $captureId
              AND status <> 'u';
            """;
        command.Parameters.AddWithValue(
            "$captureId",
            document.OfficialCaptureId);
        var official = new Dictionary<int, OfficialPlayer>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            official.Add(
                reader.GetInt32(0),
                new(
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6)));
        }
        Require(
            official.Count == document.CandidatePoolCount,
            "candidate-pool",
            "candidatePoolCount");
        foreach (SelectedOpeningPlayerDocument player
            in document.Selection.Players)
        {
            Require(
                official.TryGetValue(player.PlayerId, out OfficialPlayer? row)
                    && row.WebName == player.WebName
                    && row.TeamId == player.TeamId
                    && row.Position == player.Position
                    && row.PriceTenths == player.PriceTenths
                    && row.Status == player.OfficialStatus
                    && row.ChanceNextRound
                        == player.OfficialChanceOfPlayingNextRound
                    && row.Status != "u",
                "official-player",
                $"selection.players[{player.PlayerId}]");
        }
    }

    private static async Task<StoredArtifact?> ReadExactAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT selected_opening_squad_artifact_id,
                   document_json, content_sha256
            FROM selected_opening_squad_shadow_artifacts
            WHERE official_capture_id = $captureId
              AND evaluation_policy_key = $policyKey
              AND evaluation_data_identity_sha256 = $evaluationIdentity
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$policyKey", EvaluationPolicyKey);
        command.Parameters.AddWithValue(
            "$evaluationIdentity",
            EvaluationDataIdentity);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static SelectedOpeningSquadShadowDocument Materialize(
        StoredArtifact artifact)
    {
        if (Sha256(artifact.DocumentJson) != artifact.ContentSha256)
        {
            throw new InvalidDataException(
                "The selected opening squad content hash is invalid.");
        }
        SelectedOpeningSquadShadowDocument document =
            JsonSerializer.Deserialize<SelectedOpeningSquadShadowDocument>(
                artifact.DocumentJson,
                JsonOptions)
            ?? throw new JsonException(
                "The selected opening squad document cannot be null.");
        return document with
        {
            SelectedOpeningSquadArtifactId = artifact.ArtifactId,
            SelectedOpeningSquadArtifactContentSha256 =
                artifact.ContentSha256,
        };
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static decimal Quantile(int[] sorted, decimal probability)
    {
        decimal index = (sorted.Length - 1) * probability;
        int lower = (int)decimal.Floor(index);
        int upper = (int)decimal.Ceiling(index);
        decimal fraction = index - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static decimal LowerTailCvar(int[] sorted)
    {
        decimal tailMass = LowerTailFraction * sorted.Length;
        int full = (int)decimal.Floor(tailMass);
        decimal fraction = tailMass - full;
        decimal total = sorted.Take(full).Sum(value => (decimal)value);
        if (fraction > 0m)
        {
            total += fraction * sorted[full];
        }
        return total / tailMass;
    }

    private static bool Close(decimal left, decimal right) =>
        Math.Abs(left - right) <= NumericTolerance;

    private static void Require(
        bool condition,
        string code,
        string field)
    {
        if (!condition)
        {
            throw new SelectedOpeningSquadValidationException(code, field);
        }
    }

    private sealed record OfficialCapture(
        long CaptureId,
        string SeasonCode,
        int Gameweek,
        DateTimeOffset DeadlineUtc,
        DateTimeOffset AvailableAtUtc);

    private sealed record OfficialPlayer(
        string WebName,
        int TeamId,
        string Position,
        int PriceTenths,
        string Status,
        int? ChanceNextRound);

    private sealed record StoredArtifact(
        long ArtifactId,
        string DocumentJson,
        string ContentSha256);
}

public sealed class SelectedOpeningSquadShadowImporter
{
    private readonly SelectedOpeningSquadShadowStore _store;

    public SelectedOpeningSquadShadowImporter(
        SelectedOpeningSquadShadowStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<SelectedOpeningSquadShadowDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The selected opening squad file does not exist.",
                path);
        }
        if (file.Length is < 2 or > 2 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "The selected opening squad file is outside its size limit.");
        }
        await using FileStream stream = file.OpenRead();
        SelectedOpeningSquadShadowDocument document =
            await JsonSerializer.DeserializeAsync<
                SelectedOpeningSquadShadowDocument>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken)
            ?? throw new JsonException(
                "The selected opening squad document cannot be null.");
        return await _store.ImportAsync(document, cancellationToken);
    }
}

public sealed class SelectedOpeningSquadValidationException : Exception
{
    public SelectedOpeningSquadValidationException(
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
