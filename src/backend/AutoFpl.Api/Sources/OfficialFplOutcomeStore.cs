using System.Globalization;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Sources;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Sources;

public sealed class OfficialFplOutcomeStore
{
    private readonly DatabaseOptions _options;

    public OfficialFplOutcomeStore(DatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    internal async Task<OfficialFplOutcomeCaptureDocument> SaveAsync(
        OfficialFplCaptureDocument referenceCapture,
        int gameweek,
        OfficialFplOutcomePayload payload,
        Uri liveUri,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referenceCapture);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(liveUri);
        if (gameweek is < 1 or > 38)
        {
            throw new ArgumentOutOfRangeException(nameof(gameweek));
        }

        if (retrievedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The retrieval time must be expressed as UTC.",
                nameof(retrievedAtUtc));
        }

        await using SqliteConnection connection = new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

        OfficialFplOutcomeContext context = await ReadContextAsync(
            connection,
            transaction,
            referenceCapture.CaptureId,
            gameweek,
            cancellationToken)
            ?? throw Invalid("the reference capture does not contain the requested Gameweek.");
        if (!StringComparer.Ordinal.Equals(context.SeasonCode, referenceCapture.SeasonCode))
        {
            throw Invalid("the reference capture season does not match its stored season.");
        }

        EnsureFinal(context);
        await EnsureCompletePlayerCoverageAsync(
            connection,
            transaction,
            referenceCapture.CaptureId,
            payload.Players,
            cancellationToken);

        long? existingId = await FindExistingOutcomeIdAsync(
            connection,
            transaction,
            context.SeasonCode,
            gameweek,
            payload.LiveSha256,
            cancellationToken);
        if (existingId is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (await GetAsync(existingId.Value, cancellationToken))!;
        }

        long outcomeCaptureId = await InsertOutcomeCaptureAsync(
            connection,
            transaction,
            referenceCapture.CaptureId,
            context,
            payload,
            liveUri,
            retrievedAtUtc,
            cancellationToken);
        await InsertPlayerOutcomesAsync(
            connection,
            transaction,
            outcomeCaptureId,
            referenceCapture.CaptureId,
            payload.Players,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateDocument(
            outcomeCaptureId,
            referenceCapture.CaptureId,
            context,
            payload,
            liveUri,
            retrievedAtUtc);
    }

    public async Task<OfficialFplOutcomeCaptureDocument?> GetLatestAsync(
        string seasonCode,
        int gameweek,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidLookup(seasonCode, gameweek))
        {
            return null;
        }

        await using SqliteConnection connection = new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            {SelectDocumentSql}
            WHERE season_code = $seasonCode
              AND gameweek = $gameweek
            ORDER BY available_at_utc DESC, outcome_capture_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$seasonCode", seasonCode);
        command.Parameters.AddWithValue("$gameweek", gameweek);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocument(reader) : null;
    }

    public async Task<OfficialFplOutcomeCaptureDocument?> GetAsync(
        long outcomeCaptureId,
        CancellationToken cancellationToken = default)
    {
        if (outcomeCaptureId <= 0)
        {
            return null;
        }

        await using SqliteConnection connection = new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            {SelectDocumentSql}
            WHERE outcome_capture_id = $outcomeCaptureId;
            """;
        command.Parameters.AddWithValue("$outcomeCaptureId", outcomeCaptureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocument(reader) : null;
    }

    public async Task<OfficialFplReplayOutcomeDocument?> GetReplayOutcomeAsync(
        OfficialFplCaptureStore captureStore,
        string seasonCode,
        int gameweek,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureStore);
        if (!IsValidLookup(seasonCode, gameweek))
        {
            return null;
        }

        OfficialFplReplayDocument? replay =
            await captureStore.GetLatestPreDeadlineReplayAsync(
                seasonCode,
                gameweek,
                cancellationToken);
        OfficialFplOutcomeCaptureDocument? outcome =
            await GetLatestAsync(seasonCode, gameweek, cancellationToken);
        if (replay is null || outcome is null)
        {
            return null;
        }

        await using SqliteConnection connection = new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM official_fpl_players AS player
            INNER JOIN official_fpl_player_outcomes AS outcome
                ON outcome.player_id = player.player_id
               AND outcome.outcome_capture_id = $outcomeCaptureId
            WHERE player.capture_id = $replayCaptureId;
            """;
        command.Parameters.AddWithValue("$outcomeCaptureId", outcome.OutcomeCaptureId);
        command.Parameters.AddWithValue("$replayCaptureId", replay.SelectedCaptureId);
        int matchedPlayerCount = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        return matchedPlayerCount == replay.PlayerCount
            ? new("1.0", replay, outcome, matchedPlayerCount)
            : null;
    }

    public async Task<OfficialFplOutcomeReadinessDocument?> GetReadinessAsync(
        OfficialFplCaptureStore captureStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureStore);
        OfficialFplCaptureDocument? latestCapture =
            await captureStore.GetLatestAsync(cancellationToken);
        if (latestCapture is null)
        {
            return null;
        }

        var captured = new List<int>();
        var paired = new List<int>();
        var missingOutcomes = new List<int>();
        var missingReplays = new List<int>();
        var incompletePairs = new List<int>();
        if (latestCapture.LatestCompletedGameweek is int latestGameweek)
        {
            for (int gameweek = 1; gameweek <= latestGameweek; gameweek++)
            {
                OfficialFplOutcomeCaptureDocument? outcome =
                    await GetLatestAsync(
                        latestCapture.SeasonCode,
                        gameweek,
                        cancellationToken);
                OfficialFplReplayDocument? replay =
                    await captureStore.GetLatestPreDeadlineReplayAsync(
                        latestCapture.SeasonCode,
                        gameweek,
                        cancellationToken);
                if (outcome is null)
                {
                    missingOutcomes.Add(gameweek);
                }
                else
                {
                    captured.Add(gameweek);
                }

                if (replay is null)
                {
                    missingReplays.Add(gameweek);
                }
                else if (outcome is not null)
                {
                    OfficialFplReplayOutcomeDocument? pair =
                        await GetReplayOutcomeAsync(
                            captureStore,
                            latestCapture.SeasonCode,
                            gameweek,
                            cancellationToken);
                    if (pair is null)
                    {
                        incompletePairs.Add(gameweek);
                    }
                    else
                    {
                        paired.Add(gameweek);
                    }
                }
            }
        }

        string status = latestCapture.LatestCompletedGameweek is null
            ? "waiting-for-final-gameweek"
            : missingOutcomes.Count > 0
                ? "outcome-capture-pending"
                : missingReplays.Count > 0 || incompletePairs.Count > 0
                    ? "pairing-blocked"
                    : "ready";
        return new(
            "1.0",
            status,
            latestCapture.SeasonCode,
            latestCapture.CaptureId,
            latestCapture.AvailableAtUtc,
            latestCapture.LatestCompletedGameweek,
            captured,
            paired,
            missingOutcomes,
            missingReplays,
            incompletePairs,
            OfficialFplPoller.MaximumOutcomeImportsPerPoll);
    }

    private static async Task<OfficialFplOutcomeContext?> ReadContextAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        int gameweek,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                capture.season_code,
                event.deadline_utc,
                event.finished,
                event.data_checked,
                capture.player_count,
                COUNT(fixture.fixture_id),
                COALESCE(SUM(fixture.finished), 0)
            FROM official_fpl_captures AS capture
            INNER JOIN official_fpl_events AS event
                ON event.capture_id = capture.capture_id
               AND event.event_id = $gameweek
            LEFT JOIN official_fpl_fixtures AS fixture
                ON fixture.capture_id = capture.capture_id
               AND fixture.event_id = event.event_id
            WHERE capture.capture_id = $captureId
            GROUP BY
                capture.season_code,
                event.deadline_utc,
                event.finished,
                event.data_checked,
                capture.player_count;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$gameweek", gameweek);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetString(0),
                gameweek,
                ParseUtc(reader.GetString(1)),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6))
            : null;
    }

    private static void EnsureFinal(OfficialFplOutcomeContext context)
    {
        if (!context.EventFinished || !context.DataChecked)
        {
            throw Invalid("the official event is not finished and data-checked.");
        }

        if (context.FixtureCount == 0)
        {
            throw Invalid("the reference capture has no fixtures for the Gameweek.");
        }

        if (context.FinishedFixtureCount != context.FixtureCount)
        {
            throw Invalid("not all official Gameweek fixtures are finished.");
        }
    }

    private static async Task EnsureCompletePlayerCoverageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long referenceCaptureId,
        IReadOnlyList<OfficialFplPlayerOutcome> players,
        CancellationToken cancellationToken)
    {
        var expected = new HashSet<int>();
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT player_id
                FROM official_fpl_players
                WHERE capture_id = $captureId;
                """;
            command.Parameters.AddWithValue("$captureId", referenceCaptureId);
            await using SqliteDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                expected.Add(reader.GetInt32(0));
            }
        }

        var actual = players.Select(player => player.PlayerId).ToHashSet();
        if (actual.Count != players.Count
            || !expected.SetEquals(actual))
        {
            throw Invalid(
                "the live player IDs do not exactly cover the reference capture.");
        }
    }

    private static async Task<long?> FindExistingOutcomeIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seasonCode,
        int gameweek,
        string liveSha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT outcome_capture_id
            FROM official_fpl_outcome_captures
            WHERE season_code = $seasonCode
              AND gameweek = $gameweek
              AND live_sha256 = $liveSha256;
            """;
        command.Parameters.AddWithValue("$seasonCode", seasonCode);
        command.Parameters.AddWithValue("$gameweek", gameweek);
        command.Parameters.AddWithValue("$liveSha256", liveSha256);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long outcomeCaptureId ? outcomeCaptureId : null;
    }

    private static async Task<long> InsertOutcomeCaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long referenceCaptureId,
        OfficialFplOutcomeContext context,
        OfficialFplOutcomePayload payload,
        Uri liveUri,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO official_fpl_outcome_captures (
                schema_version,
                source_key,
                season_code,
                gameweek,
                reference_capture_id,
                live_url,
                retrieved_at_utc,
                available_at_utc,
                live_sha256,
                live_json,
                player_count,
                gameweek_fixture_count,
                created_at_utc
            )
            VALUES (
                '1.0',
                $sourceKey,
                $seasonCode,
                $gameweek,
                $referenceCaptureId,
                $liveUrl,
                $retrievedAtUtc,
                $availableAtUtc,
                $liveSha256,
                $liveJson,
                $playerCount,
                $gameweekFixtureCount,
                $createdAtUtc
            );
            SELECT last_insert_rowid();
            """;
        string timestamp = FormatUtc(retrievedAtUtc);
        command.Parameters.AddWithValue("$sourceKey", OfficialFplOutcomeImporter.SourceKey);
        command.Parameters.AddWithValue("$seasonCode", context.SeasonCode);
        command.Parameters.AddWithValue("$gameweek", context.Gameweek);
        command.Parameters.AddWithValue("$referenceCaptureId", referenceCaptureId);
        command.Parameters.AddWithValue("$liveUrl", liveUri.AbsoluteUri);
        command.Parameters.AddWithValue("$retrievedAtUtc", timestamp);
        command.Parameters.AddWithValue("$availableAtUtc", timestamp);
        command.Parameters.AddWithValue("$liveSha256", payload.LiveSha256);
        command.Parameters.AddWithValue("$liveJson", payload.LiveJson);
        command.Parameters.AddWithValue("$playerCount", payload.Players.Count);
        command.Parameters.AddWithValue("$gameweekFixtureCount", context.FixtureCount);
        command.Parameters.AddWithValue("$createdAtUtc", timestamp);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long outcomeCaptureId
            ? outcomeCaptureId
            : throw new InvalidOperationException("SQLite did not return an outcome capture id.");
    }

    private static async Task InsertPlayerOutcomesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long outcomeCaptureId,
        long referenceCaptureId,
        IReadOnlyList<OfficialFplPlayerOutcome> players,
        CancellationToken cancellationToken)
    {
        foreach (OfficialFplPlayerOutcome player in players)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO official_fpl_player_outcomes (
                    outcome_capture_id,
                    reference_capture_id,
                    player_id,
                    minutes,
                    starts,
                    total_points,
                    goals_scored,
                    assists,
                    clean_sheets,
                    goals_conceded,
                    saves,
                    bonus,
                    yellow_cards,
                    red_cards,
                    own_goals,
                    penalties_saved,
                    penalties_missed,
                    bps,
                    influence,
                    creativity,
                    threat,
                    ict_index,
                    clearances_blocks_interceptions,
                    recoveries,
                    tackles,
                    defensive_contribution,
                    expected_goals,
                    expected_assists,
                    expected_goal_involvements,
                    expected_goals_conceded
                )
                VALUES (
                    $outcomeCaptureId,
                    $referenceCaptureId,
                    $playerId,
                    $minutes,
                    $starts,
                    $totalPoints,
                    $goalsScored,
                    $assists,
                    $cleanSheets,
                    $goalsConceded,
                    $saves,
                    $bonus,
                    $yellowCards,
                    $redCards,
                    $ownGoals,
                    $penaltiesSaved,
                    $penaltiesMissed,
                    $bps,
                    $influence,
                    $creativity,
                    $threat,
                    $ictIndex,
                    $clearancesBlocksInterceptions,
                    $recoveries,
                    $tackles,
                    $defensiveContribution,
                    $expectedGoals,
                    $expectedAssists,
                    $expectedGoalInvolvements,
                    $expectedGoalsConceded
                );
                """;
            command.Parameters.AddWithValue("$outcomeCaptureId", outcomeCaptureId);
            command.Parameters.AddWithValue("$referenceCaptureId", referenceCaptureId);
            command.Parameters.AddWithValue("$playerId", player.PlayerId);
            command.Parameters.AddWithValue("$minutes", player.Minutes);
            command.Parameters.AddWithValue("$starts", player.Starts);
            command.Parameters.AddWithValue("$totalPoints", player.TotalPoints);
            command.Parameters.AddWithValue("$goalsScored", player.GoalsScored);
            command.Parameters.AddWithValue("$assists", player.Assists);
            command.Parameters.AddWithValue("$cleanSheets", player.CleanSheets);
            command.Parameters.AddWithValue("$goalsConceded", player.GoalsConceded);
            command.Parameters.AddWithValue("$saves", player.Saves);
            command.Parameters.AddWithValue("$bonus", player.Bonus);
            command.Parameters.AddWithValue("$yellowCards", player.YellowCards);
            command.Parameters.AddWithValue("$redCards", player.RedCards);
            command.Parameters.AddWithValue("$ownGoals", player.OwnGoals);
            command.Parameters.AddWithValue("$penaltiesSaved", player.PenaltiesSaved);
            command.Parameters.AddWithValue("$penaltiesMissed", player.PenaltiesMissed);
            command.Parameters.AddWithValue("$bps", player.Bps);
            command.Parameters.AddWithValue("$influence", (double)player.Influence);
            command.Parameters.AddWithValue("$creativity", (double)player.Creativity);
            command.Parameters.AddWithValue("$threat", (double)player.Threat);
            command.Parameters.AddWithValue("$ictIndex", (double)player.IctIndex);
            command.Parameters.AddWithValue(
                "$clearancesBlocksInterceptions",
                player.ClearancesBlocksInterceptions);
            command.Parameters.AddWithValue("$recoveries", player.Recoveries);
            command.Parameters.AddWithValue("$tackles", player.Tackles);
            command.Parameters.AddWithValue(
                "$defensiveContribution",
                player.DefensiveContribution);
            command.Parameters.AddWithValue("$expectedGoals", (double)player.ExpectedGoals);
            command.Parameters.AddWithValue("$expectedAssists", (double)player.ExpectedAssists);
            command.Parameters.AddWithValue(
                "$expectedGoalInvolvements",
                (double)player.ExpectedGoalInvolvements);
            command.Parameters.AddWithValue(
                "$expectedGoalsConceded",
                (double)player.ExpectedGoalsConceded);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static OfficialFplOutcomeCaptureDocument CreateDocument(
        long outcomeCaptureId,
        long referenceCaptureId,
        OfficialFplOutcomeContext context,
        OfficialFplOutcomePayload payload,
        Uri liveUri,
        DateTimeOffset retrievedAtUtc) =>
        new(
            outcomeCaptureId,
            "1.0",
            OfficialFplOutcomeImporter.SourceKey,
            context.SeasonCode,
            context.Gameweek,
            referenceCaptureId,
            liveUri.AbsoluteUri,
            PublishedAtUtc: null,
            retrievedAtUtc,
            retrievedAtUtc,
            payload.LiveSha256,
            payload.Players.Count,
            context.FixtureCount);

    private static OfficialFplOutcomeCaptureDocument ReadDocument(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt64(5),
            reader.GetString(6),
            PublishedAtUtc: null,
            ParseUtc(reader.GetString(7)),
            ParseUtc(reader.GetString(8)),
            reader.GetString(9),
            reader.GetInt32(10),
            reader.GetInt32(11));

    private static bool IsValidLookup(string seasonCode, int gameweek) =>
        !string.IsNullOrWhiteSpace(seasonCode)
        && seasonCode.Length <= 16
        && gameweek is >= 1 and <= 38;

    private static string FormatUtc(DateTimeOffset value) =>
        value
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.ParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static OfficialFplPayloadException Invalid(string message) =>
        new($"Official FPL outcome validation failed: {message}");

    private const string SelectDocumentSql =
        """
        SELECT
            outcome_capture_id,
            schema_version,
            source_key,
            season_code,
            gameweek,
            reference_capture_id,
            live_url,
            retrieved_at_utc,
            available_at_utc,
            live_sha256,
            player_count,
            gameweek_fixture_count
        FROM official_fpl_outcome_captures
        """;
}
