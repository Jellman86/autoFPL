using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Persistence;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Forecasts;

public sealed class PublicProjectionOpeningSquadStore
{
    public const string ArtifactType =
        "current-public-projection-opening-squad-shadow";
    public const string ArtifactVersion =
        "current-public-projection-opening-squad-shadow-v1";
    public const string Status =
        "prospective-external-challenger-unscored";
    public const string SourceKey = "solio-public-projections";

    private const int MaximumDocumentBytes = 2 * 1024 * 1024;
    private readonly DatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public PublicProjectionOpeningSquadStore(
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
        await ValidateSelectionAsync(
            connection,
            transaction,
            envelope.OfficialCaptureId,
            document.GetProperty("incumbent").GetProperty("selection"),
            8,
            cancellationToken);
        await ValidateSelectionAsync(
            connection,
            transaction,
            envelope.OfficialCaptureId,
            document.GetProperty("challenger").GetProperty("selection"),
            8,
            cancellationToken);
        ValidateSelectionChange(document);
        await ValidateProjectionPlayersAsync(
            connection,
            transaction,
            envelope.OfficialCaptureId,
            document.GetProperty("projections"),
            envelope.MatchedPlayerCount,
            cancellationToken);

        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO public_projection_opening_squad_artifacts (
                    schema_version, artifact_type, artifact_version, status,
                    official_capture_id, source_snapshot_id,
                    source_content_sha256, producer_data_identity_sha256,
                    producer_run_identity_sha256, document_json,
                    content_sha256, created_at_utc
                )
                VALUES (
                    '1.0', $artifactType, $artifactVersion, $status,
                    $captureId, $snapshotId, $sourceContentSha256,
                    $dataIdentity, $runIdentity, $documentJson,
                    $contentSha256, $createdAtUtc
                )
                ON CONFLICT (official_capture_id, source_snapshot_id)
                DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$artifactType", ArtifactType);
            insert.Parameters.AddWithValue(
                "$artifactVersion",
                ArtifactVersion);
            insert.Parameters.AddWithValue("$status", Status);
            insert.Parameters.AddWithValue(
                "$captureId",
                envelope.OfficialCaptureId);
            insert.Parameters.AddWithValue(
                "$snapshotId",
                envelope.SourceSnapshotId);
            insert.Parameters.AddWithValue(
                "$sourceContentSha256",
                envelope.SourceContentSha256);
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
                FROM public_projection_opening_squad_artifacts
                WHERE official_capture_id = $captureId
                  AND source_snapshot_id = $snapshotId;
                """;
            read.Parameters.AddWithValue(
                "$captureId",
                envelope.OfficialCaptureId);
            read.Parameters.AddWithValue(
                "$snapshotId",
                envelope.SourceSnapshotId);
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
            FROM public_projection_opening_squad_artifacts
            WHERE official_capture_id = (
                SELECT capture_id
                FROM official_fpl_captures
                ORDER BY available_at_utc DESC, capture_id DESC
                LIMIT 1
            )
            ORDER BY source_snapshot_id DESC, artifact_id DESC
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
                    == "retain-as-prospective-external-challenger-only"
                && source.GetProperty("sourceKey").GetString() == SourceKey
                && source.GetProperty("declaredSource").GetString()
                    == "https://fpl.solioanalytics.com/api/data/latest"
                && DateTimeOffset.Parse(
                    source.GetProperty("availableAtUtc").GetString()!)
                    == DateTimeOffset.Parse(document
                        .GetProperty("evidenceDecisionCutoffUtc").GetString()!)
                && method.GetProperty("methodKey").GetString()
                    == "solio-gw1-mean-overlay-on-retained-six-week-policy-v1"
                && method.GetProperty("horizonGameweeks").GetInt32() == 6
                && method.GetProperty("laterGameweeksUnchanged").GetBoolean()
                && method.GetProperty("unpublishedPlayersUnchanged").GetBoolean()
                && method.GetProperty("evaluationPolicyKey").GetString()
                    == "6-expected-points"
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
        int published = source.GetProperty("publishedPlayerCount").GetInt32();
        int matched = source.GetProperty("matchedPlayerCount").GetInt32();
        int unmatched = source.GetProperty("unmatchedPlayerCount").GetInt32();
        Require(
            published is >= 20 and <= 1024
                && matched + unmatched == published
                && matched >= 20
                && source.GetProperty("matchFraction").GetDecimal() >= 0.80m,
            "source-coverage");
        string sourceHash = RequiredSha256(
            source.GetProperty("contentSha256"),
            "source.contentSha256");
        return new(
            document.GetProperty("officialCaptureId").GetInt64(),
            document.GetProperty("candidatePoolCount").GetInt32(),
            document.GetProperty("scenarioCount").GetInt32(),
            DateTimeOffset.Parse(
                document.GetProperty("deadlineUtc").GetString()!),
            DateTimeOffset.Parse(
                document.GetProperty("forecastDecisionCutoffUtc").GetString()!),
            DateTimeOffset.Parse(
                document.GetProperty("evidenceDecisionCutoffUtc").GetString()!),
            source.GetProperty("snapshotId").GetInt64(),
            source.GetProperty("sourceRevision").GetInt32(),
            sourceHash,
            DateTimeOffset.Parse(
                source.GetProperty("generatedAtUtc").GetString()!),
            DateTimeOffset.Parse(
                source.GetProperty("deadlineUtc").GetString()!),
            matched,
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
                   snapshot.source_key, snapshot.source_revision,
                   snapshot.content_sha256, snapshot.available_at_utc,
                   snapshot.source_class, snapshot.canonical_url,
                   snapshot.final_url, snapshot.dependence_group,
                   snapshot.transport_key,
                   (SELECT COUNT(*) FROM official_fpl_players AS player
                    WHERE player.capture_id = capture.capture_id
                      AND player.status <> 'u')
            FROM official_fpl_captures AS capture
            INNER JOIN research_source_snapshots AS snapshot
                ON snapshot.identity_capture_id = capture.capture_id
               AND snapshot.snapshot_id = $snapshotId
            WHERE capture.capture_id = $captureId
              AND capture.capture_id = (
                  SELECT capture_id FROM official_fpl_captures
                  ORDER BY available_at_utc DESC, capture_id DESC LIMIT 1
              );
            """;
        command.Parameters.AddWithValue("$captureId", envelope.OfficialCaptureId);
        command.Parameters.AddWithValue("$snapshotId", envelope.SourceSnapshotId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        Require(await reader.ReadAsync(cancellationToken), "lineage");
        Require(
            reader.GetString(0) == "2026-27"
                && reader.GetInt32(1) == 1
                && DateTimeOffset.Parse(reader.GetString(2)) == envelope.DeadlineUtc
                && DateTimeOffset.Parse(reader.GetString(3))
                    == envelope.ForecastDecisionCutoffUtc
                && reader.GetString(4) == SourceKey
                && reader.GetInt32(5) == envelope.SourceRevision
                && reader.GetString(6) == envelope.SourceContentSha256
                && DateTimeOffset.Parse(reader.GetString(7))
                    == envelope.EvidenceDecisionCutoffUtc
                && reader.GetString(8)
                    == "public-quantitative-market-projection"
                && reader.GetString(9)
                    == "https://fpl.solioanalytics.com/api/data/latest.json"
                && reader.GetString(10)
                    == "https://fpl.solioanalytics.com/api/data/latest.json"
                && reader.GetString(11) == "solio-sports-market-model"
                && reader.GetString(12) == "spider-mcp"
                && reader.GetInt32(13) == envelope.CandidatePoolCount
                && envelope.ForecastDecisionCutoffUtc
                    <= envelope.EvidenceDecisionCutoffUtc
                && envelope.EvidenceDecisionCutoffUtc <= envelope.DeadlineUtc
                && envelope.SourceGeneratedAtUtc
                    <= envelope.EvidenceDecisionCutoffUtc
                && envelope.SourceDeadlineUtc == envelope.DeadlineUtc
                && envelope.ScenarioCount is >= 1 and <= 512,
            "lineage");
    }

    internal static async Task ValidateSelectionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        JsonElement selection,
        int expectedGameweeks,
        CancellationToken cancellationToken)
    {
        int[] playerIds = selection.GetProperty("playerIds")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        Require(playerIds.Length == 15 && playerIds.Distinct().Count() == 15,
            "selection-player-count");
        var players = new Dictionary<
            int,
            (string WebName, int TeamId, string Position, int Price)>();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT player_id, web_name, team_id, position, price_tenths
            FROM official_fpl_players
            WHERE capture_id = $captureId AND status <> 'u'
              AND player_id IN ({string.Join(",", playerIds.Select(
                  (_, index) => $"$player{index}"))});
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        for (int index = 0; index < playerIds.Length; index++)
        {
            command.Parameters.AddWithValue($"$player{index}", playerIds[index]);
        }
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            players.Add(
                reader.GetInt32(0),
                (
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt32(4)));
        }
        int budget = selection.GetProperty("budgetTenths").GetInt32();
        Require(
            players.Count == 15
                && budget is > 0 and <= 1000
                && players.Values.Sum(value => value.Price) == budget
                && players.Values.Sum(value =>
                    value.Position == "goalkeeper" ? 1 : 0) == 2
                && players.Values.Sum(value =>
                    value.Position == "defender" ? 1 : 0) == 5
                && players.Values.Sum(value =>
                    value.Position == "midfielder" ? 1 : 0) == 5
                && players.Values.Sum(value =>
                    value.Position == "forward" ? 1 : 0) == 3
                && players.Values.GroupBy(value => value.TeamId)
                    .All(group => group.Count() <= 3),
            "selection-constraints");
        JsonElement[] suppliedPlayers = selection.GetProperty("players")
            .EnumerateArray().ToArray();
        Require(
            suppliedPlayers.Length == 15
                && suppliedPlayers.Select(value =>
                    value.GetProperty("playerId").GetInt32())
                    .SequenceEqual(playerIds)
                && suppliedPlayers.All(value =>
                {
                    int playerId = value.GetProperty("playerId").GetInt32();
                    return players.TryGetValue(playerId, out var official)
                        && value.GetProperty("webName").GetString()
                            == official.WebName
                        && value.GetProperty("teamId").GetInt32()
                            == official.TeamId
                        && value.GetProperty("position").GetString()
                            == official.Position
                        && value.GetProperty("priceTenths").GetInt32()
                            == official.Price;
                }),
            "selection-player-identity");
        ValidateGameweekRoles(selection, players, expectedGameweeks);
    }

    private static void ValidateGameweekRoles(
        JsonElement selection,
        IReadOnlyDictionary<
            int,
            (string WebName, int TeamId, string Position, int Price)> players,
        int expectedGameweeks)
    {
        JsonElement[] gameweeks = selection.GetProperty("gameweeks")
            .EnumerateArray().ToArray();
        Require(
            gameweeks.Length == expectedGameweeks
                && gameweeks.Select(value =>
                    value.GetProperty("gameweek").GetInt32())
                    .SequenceEqual(Enumerable.Range(1, expectedGameweeks)),
            "selection-gameweeks");
        foreach (JsonElement gameweek in gameweeks)
        {
            int[] starters = gameweek.GetProperty("startingPlayerIds")
                .EnumerateArray().Select(value => value.GetInt32()).ToArray();
            int captain = gameweek.GetProperty("captainPlayerId").GetInt32();
            int vice = gameweek.GetProperty("viceCaptainPlayerId").GetInt32();
            int goalkeeper = gameweek
                .GetProperty("replacementGoalkeeperPlayerId").GetInt32();
            int[] outfield = gameweek
                .GetProperty("outfieldSubstitutePlayerIds")
                .EnumerateArray().Select(value => value.GetInt32()).ToArray();
            int[] all = [.. starters, goalkeeper, .. outfield];
            Require(
                starters.Length == 11
                    && starters.Distinct().Count() == 11
                    && all.Distinct().Count() == 15
                    && all.All(players.ContainsKey)
                    && starters.Contains(captain)
                    && starters.Contains(vice)
                    && captain != vice
                    && outfield.Length == 3
                    && players[goalkeeper].Position == "goalkeeper"
                    && outfield.All(value =>
                        players[value].Position != "goalkeeper")
                    && starters.Count(value =>
                        players[value].Position == "goalkeeper") == 1
                    && starters.Count(value =>
                        players[value].Position == "defender") is >= 3 and <= 5
                    && starters.Count(value =>
                        players[value].Position == "midfielder") is >= 2 and <= 5
                    && starters.Count(value =>
                        players[value].Position == "forward") is >= 1 and <= 3,
                "selection-gameweek-role");
        }
    }

    internal static void ValidateSelectionChange(JsonElement document)
    {
        var incumbent = document.GetProperty("incumbent")
            .GetProperty("selection").GetProperty("playerIds")
            .EnumerateArray().Select(value => value.GetInt32()).ToHashSet();
        var challenger = document.GetProperty("challenger")
            .GetProperty("selection").GetProperty("playerIds")
            .EnumerateArray().Select(value => value.GetInt32()).ToHashSet();
        JsonElement change = document.GetProperty("selectionChange");
        int[] removed = change.GetProperty("removedPlayers")
            .EnumerateArray()
            .Select(value => value.GetProperty("playerId").GetInt32())
            .ToArray();
        int[] added = change.GetProperty("addedPlayers")
            .EnumerateArray()
            .Select(value => value.GetProperty("playerId").GetInt32())
            .ToArray();
        Require(
            change.GetProperty("overlapPlayerCount").GetInt32()
                == incumbent.Intersect(challenger).Count()
                && removed.Order().SequenceEqual(
                    incumbent.Except(challenger).Order())
                && added.Order().SequenceEqual(
                    challenger.Except(incumbent).Order()),
            "selection-change");
    }

    private static async Task ValidateProjectionPlayersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        JsonElement projections,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        int[] ids = projections.EnumerateArray()
            .Select(value => value.GetProperty("playerId").GetInt32())
            .ToArray();
        Require(ids.Length == expectedCount && ids.Distinct().Count() == ids.Length,
            "projection-player-count");
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT COUNT(*) FROM official_fpl_players
            WHERE capture_id = $captureId AND status <> 'u'
              AND player_id IN ({string.Join(",", ids.Select(
                  (_, index) => $"$projection{index}"))});
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        for (int index = 0; index < ids.Length; index++)
        {
            command.Parameters.AddWithValue($"$projection{index}", ids[index]);
        }
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        Require(Convert.ToInt32(value) == ids.Length, "projection-player");
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
                $"Invalid public projection opening squad: {field}.");
        }
    }

    private sealed record Envelope(
        long OfficialCaptureId,
        int CandidatePoolCount,
        int ScenarioCount,
        DateTimeOffset DeadlineUtc,
        DateTimeOffset ForecastDecisionCutoffUtc,
        DateTimeOffset EvidenceDecisionCutoffUtc,
        long SourceSnapshotId,
        int SourceRevision,
        string SourceContentSha256,
        DateTimeOffset SourceGeneratedAtUtc,
        DateTimeOffset SourceDeadlineUtc,
        int MatchedPlayerCount,
        string DataIdentitySha256,
        string RunIdentitySha256);
}

public sealed class PublicProjectionOpeningSquadImporter
{
    private readonly PublicProjectionOpeningSquadStore _store;

    public PublicProjectionOpeningSquadImporter(
        PublicProjectionOpeningSquadStore store)
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
                "The public projection squad file does not exist.", path);
        }
        if (file.Length is < 2 or > 2 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "The public projection squad file is outside its size limit.");
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
