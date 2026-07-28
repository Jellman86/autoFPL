using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Sources;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Sources;

public sealed class HistoricalFplSeasonStore
{
    private readonly DatabaseOptions _options;

    public HistoricalFplSeasonStore(DatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    internal async Task<HistoricalFplSeasonCaptureDocument> SaveAsync(
        HistoricalFplSeasonPayload payload,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default) =>
        await SaveAsync(
            HistoricalFplSeasonRegistry.GetRequired(
                HistoricalFplSeasonRegistry.DefaultSeasonCode),
            payload,
            retrievedAtUtc,
            cancellationToken);

    internal async Task<HistoricalFplSeasonCaptureDocument> SaveAsync(
        HistoricalFplSeasonDefinition definition,
        HistoricalFplSeasonPayload payload,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(payload);
        if (retrievedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The retrieval time must be expressed as UTC.",
                nameof(retrievedAtUtc));
        }

        await using SqliteConnection connection = new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

        long? existingId = await FindExistingCaptureIdAsync(
            connection,
            transaction,
            definition,
            cancellationToken);
        if (existingId is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (await GetAsync(existingId.Value, cancellationToken))!;
        }

        long captureId = await InsertCaptureAsync(
            connection,
            transaction,
            definition,
            payload,
            retrievedAtUtc,
            cancellationToken);
        await InsertPlayersAsync(
            connection,
            transaction,
            captureId,
            payload.Players,
            cancellationToken);
        await InsertGameweeksAsync(
            connection,
            transaction,
            captureId,
            payload.PlayerGameweeks,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CreateDocument(captureId, definition, payload, retrievedAtUtc);
    }

    public async Task<HistoricalFplSeasonCaptureDocument?> GetLatestAsync(
        string seasonCode,
        CancellationToken cancellationToken = default)
    {
        if (!HistoricalFplSeasonRegistry.TryGet(seasonCode, out _))
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
            ORDER BY available_at_utc DESC, capture_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$seasonCode", seasonCode);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadDocument(reader)
            : null;
    }

    public async Task<HistoricalFplSeasonCaptureDocument?> GetAsync(
        long captureId,
        CancellationToken cancellationToken = default)
    {
        if (captureId <= 0)
        {
            return null;
        }

        await using SqliteConnection connection = new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            {SelectDocumentSql}
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadDocument(reader)
            : null;
    }

    public async Task<HistoricalFplIdentityCoverageDocument?> GetIdentityCoverageAsync(
        string fromSeasonCode,
        string toSeasonCode,
        CancellationToken cancellationToken = default)
    {
        if (!HistoricalFplSeasonRegistry.TryGet(
                fromSeasonCode,
                out HistoricalFplSeasonDefinition? fromDefinition)
            || !HistoricalFplSeasonRegistry.TryGet(
                toSeasonCode,
                out HistoricalFplSeasonDefinition? toDefinition)
            || StringComparer.Ordinal.Equals(fromSeasonCode, toSeasonCode))
        {
            return null;
        }

        await using SqliteConnection connection = new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        HistoricalFplSeasonIdentity? from = await ReadIdentityAsync(
            connection,
            fromDefinition,
            cancellationToken);
        HistoricalFplSeasonIdentity? to = await ReadIdentityAsync(
            connection,
            toDefinition,
            cancellationToken);
        if (from is null || to is null)
        {
            return null;
        }

        return CreateIdentityCoverage(from, to);
    }

    internal static HistoricalFplIdentityCoverageDocument CreateIdentityCoverage(
        HistoricalFplSeasonCaptureDocument fromDocument,
        IReadOnlyList<int> fromPlayerCodes,
        HistoricalFplSeasonCaptureDocument toDocument,
        IReadOnlyList<int> toPlayerCodes) =>
        CreateIdentityCoverage(
            new(
                fromDocument,
                fromPlayerCodes,
                fromDocument.PlayerGameweekCount),
            new(
                toDocument,
                toPlayerCodes,
                toDocument.PlayerGameweekCount));

    private static HistoricalFplIdentityCoverageDocument CreateIdentityCoverage(
        HistoricalFplSeasonIdentity from,
        HistoricalFplSeasonIdentity to)
    {
        ValidateIdentity(from);
        ValidateIdentity(to);
        int shared = from.PlayerCodes.Intersect(to.PlayerCodes).Count();
        int departed = from.PlayerCodes.Count - shared;
        int introduced = to.PlayerCodes.Count - shared;
        string identitySet = string.Join(
            '\n',
            from.PlayerCodes
                .Order()
                .Select(code => $"{from.Document.SeasonCode}:{code}")
                .Concat(
                    to.PlayerCodes
                        .Order()
                        .Select(
                            code => $"{to.Document.SeasonCode}:{code}")));
        return new HistoricalFplIdentityCoverageDocument(
            "1.0",
            "audited",
            "exact official player.code equality across pinned season archives",
            false,
            IdentitySeason(from.Document),
            IdentitySeason(to.Document),
            shared,
            departed,
            introduced,
            Fraction(shared, from.PlayerCodes.Count),
            Fraction(shared, to.PlayerCodes.Count),
            Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(identitySet)))
                .ToLowerInvariant());
    }

    private static async Task<long?> FindExistingCaptureIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HistoricalFplSeasonDefinition definition,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT capture_id
            FROM historical_fpl_season_captures
            WHERE source_key = $sourceKey
              AND season_code = $seasonCode
              AND source_revision = $sourceRevision;
            """;
        command.Parameters.AddWithValue(
            "$sourceKey",
            HistoricalFplSeasonRegistry.SourceKey);
        command.Parameters.AddWithValue(
            "$seasonCode",
            definition.SeasonCode);
        command.Parameters.AddWithValue(
            "$sourceRevision",
            definition.SourceRevision);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is long captureId ? captureId : null;
    }

    private static async Task<HistoricalFplSeasonIdentity?> ReadIdentityAsync(
        SqliteConnection connection,
        HistoricalFplSeasonDefinition definition,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand captureCommand = connection.CreateCommand();
        captureCommand.CommandText =
            $"""
            {SelectDocumentSql}
            WHERE season_code = $seasonCode
            ORDER BY available_at_utc DESC, capture_id DESC
            LIMIT 1;
            """;
        captureCommand.Parameters.AddWithValue(
            "$seasonCode",
            definition.SeasonCode);
        HistoricalFplSeasonCaptureDocument? document;
        await using (SqliteDataReader reader =
            await captureCommand.ExecuteReaderAsync(cancellationToken))
        {
            document = await reader.ReadAsync(cancellationToken)
                ? ReadDocument(reader)
                : null;
        }

        if (document is null)
        {
            return null;
        }

        if (!StringComparer.Ordinal.Equals(
                document.SourceKey,
                HistoricalFplSeasonRegistry.SourceKey)
            || !StringComparer.Ordinal.Equals(
                document.SourceRevision,
                definition.SourceRevision)
            || !StringComparer.Ordinal.Equals(
                document.PlayersSha256,
                definition.ExpectedPlayersSha256)
            || !StringComparer.Ordinal.Equals(
                document.GameweeksSha256,
                definition.ExpectedGameweeksSha256)
            || document.PlayerCount != definition.ExpectedPlayerCount
            || document.PlayerGameweekCount !=
                definition.ExpectedPlayerGameweekCount)
        {
            throw new HistoricalFplSeasonCoverageException(
                $"Historical FPL season '{definition.SeasonCode}' does not "
                + "match its pinned registry identity.");
        }

        await using SqliteCommand playerCommand = connection.CreateCommand();
        playerCommand.CommandText =
            """
            SELECT player_code
            FROM historical_fpl_players
            WHERE capture_id = $captureId
            ORDER BY player_code;
            """;
        playerCommand.Parameters.AddWithValue("$captureId", document.CaptureId);
        var playerCodes = new List<int>();
        await using SqliteDataReader playerReader =
            await playerCommand.ExecuteReaderAsync(cancellationToken);
        while (await playerReader.ReadAsync(cancellationToken))
        {
            playerCodes.Add(playerReader.GetInt32(0));
        }

        await using SqliteCommand gameweekCommand = connection.CreateCommand();
        gameweekCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM historical_fpl_player_gameweeks
            WHERE capture_id = $captureId;
            """;
        gameweekCommand.Parameters.AddWithValue(
            "$captureId",
            document.CaptureId);
        int playerGameweekCount = Convert.ToInt32(
            await gameweekCommand.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);

        return new(document, playerCodes, playerGameweekCount);
    }

    private static void ValidateIdentity(HistoricalFplSeasonIdentity identity)
    {
        if (identity.PlayerCodes.Count != identity.Document.PlayerCount
            || identity.PlayerCodes.Count != identity.Document.StableCodeCount
            || identity.PlayerCodes.Count != identity.PlayerCodes.Distinct().Count()
            || identity.PlayerCodes.Any(code => code <= 0)
            || identity.PlayerGameweekCount !=
                identity.Document.PlayerGameweekCount)
        {
            throw new HistoricalFplSeasonCoverageException(
                $"Historical FPL season '{identity.Document.SeasonCode}' has "
                + "incomplete or ambiguous stable player-code coverage.");
        }
    }

    private static HistoricalFplIdentityCoverageSeasonDocument IdentitySeason(
        HistoricalFplSeasonCaptureDocument document) =>
        new(
            document.SeasonCode,
            document.CaptureId,
            document.SourceRevision,
            document.PlayersSha256,
            document.GameweeksSha256,
            document.AvailableAtUtc,
            document.PlayerCount,
            document.StableCodeCount);

    private static decimal Fraction(int numerator, int denominator) =>
        Math.Round(
            (decimal)numerator / denominator,
            6,
            MidpointRounding.AwayFromZero);

    private static async Task<long> InsertCaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HistoricalFplSeasonDefinition definition,
        HistoricalFplSeasonPayload payload,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        byte[] compressedPlayers = Compress(payload.PlayersCsv);
        byte[] compressedGameweeks = Compress(payload.GameweeksCsv);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO historical_fpl_season_captures (
                schema_version,
                source_key,
                season_code,
                source_revision,
                players_url,
                gameweeks_url,
                published_at_utc,
                retrieved_at_utc,
                available_at_utc,
                players_sha256,
                gameweeks_sha256,
                players_csv_brotli,
                gameweeks_csv_brotli,
                player_count,
                player_gameweek_count,
                stable_code_count,
                created_at_utc
            )
            VALUES (
                '1.0',
                $sourceKey,
                $seasonCode,
                $sourceRevision,
                $playersUrl,
                $gameweeksUrl,
                $publishedAtUtc,
                $retrievedAtUtc,
                $retrievedAtUtc,
                $playersSha256,
                $gameweeksSha256,
                $playersCsvBrotli,
                $gameweeksCsvBrotli,
                $playerCount,
                $playerGameweekCount,
                $stableCodeCount,
                $retrievedAtUtc
            );
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue(
            "$sourceKey",
            HistoricalFplSeasonRegistry.SourceKey);
        command.Parameters.AddWithValue(
            "$seasonCode",
            definition.SeasonCode);
        command.Parameters.AddWithValue(
            "$sourceRevision",
            definition.SourceRevision);
        command.Parameters.AddWithValue(
            "$playersUrl",
            definition.PlayersUri.AbsoluteUri);
        command.Parameters.AddWithValue(
            "$gameweeksUrl",
            definition.GameweeksUri.AbsoluteUri);
        command.Parameters.AddWithValue(
            "$publishedAtUtc",
            Format(definition.PublishedAtUtc));
        command.Parameters.AddWithValue("$retrievedAtUtc", Format(retrievedAtUtc));
        command.Parameters.AddWithValue("$playersSha256", payload.PlayersSha256);
        command.Parameters.AddWithValue("$gameweeksSha256", payload.GameweeksSha256);
        command.Parameters.AddWithValue("$playersCsvBrotli", compressedPlayers);
        command.Parameters.AddWithValue("$gameweeksCsvBrotli", compressedGameweeks);
        command.Parameters.AddWithValue("$playerCount", payload.Players.Count);
        command.Parameters.AddWithValue(
            "$playerGameweekCount",
            payload.PlayerGameweeks.Count);
        command.Parameters.AddWithValue(
            "$stableCodeCount",
            payload.Players.Select(player => player.PlayerCode).Distinct().Count());
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task InsertPlayersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        IReadOnlyList<HistoricalFplPlayer> players,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO historical_fpl_players (
                capture_id,
                season_element_id,
                player_code,
                first_name,
                second_name,
                web_name,
                position,
                final_team_id,
                final_status,
                final_chance_next_round,
                final_news_sha256,
                final_news_added_utc
            )
            VALUES (
                $captureId,
                $seasonElementId,
                $playerCode,
                $firstName,
                $secondName,
                $webName,
                $position,
                $finalTeamId,
                $finalStatus,
                $finalChanceNextRound,
                $finalNewsSha256,
                $finalNewsAddedUtc
            );
            """;
        command.Parameters.Add("$captureId", SqliteType.Integer);
        command.Parameters.Add("$seasonElementId", SqliteType.Integer);
        command.Parameters.Add("$playerCode", SqliteType.Integer);
        command.Parameters.Add("$firstName", SqliteType.Text);
        command.Parameters.Add("$secondName", SqliteType.Text);
        command.Parameters.Add("$webName", SqliteType.Text);
        command.Parameters.Add("$position", SqliteType.Text);
        command.Parameters.Add("$finalTeamId", SqliteType.Integer);
        command.Parameters.Add("$finalStatus", SqliteType.Text);
        command.Parameters.Add("$finalChanceNextRound", SqliteType.Integer);
        command.Parameters.Add("$finalNewsSha256", SqliteType.Text);
        command.Parameters.Add("$finalNewsAddedUtc", SqliteType.Text);
        foreach (HistoricalFplPlayer player in players)
        {
            command.Parameters["$captureId"].Value = captureId;
            command.Parameters["$seasonElementId"].Value = player.SeasonElementId;
            command.Parameters["$playerCode"].Value = player.PlayerCode;
            command.Parameters["$firstName"].Value = player.FirstName;
            command.Parameters["$secondName"].Value = player.SecondName;
            command.Parameters["$webName"].Value = player.WebName;
            command.Parameters["$position"].Value = player.Position;
            command.Parameters["$finalTeamId"].Value = player.FinalTeamId;
            command.Parameters["$finalStatus"].Value = player.FinalStatus;
            command.Parameters["$finalChanceNextRound"].Value =
                player.FinalChanceNextRound is int chance ? chance : DBNull.Value;
            command.Parameters["$finalNewsSha256"].Value = player.FinalNewsSha256;
            command.Parameters["$finalNewsAddedUtc"].Value =
                player.FinalNewsAddedUtc is DateTimeOffset newsAdded
                    ? Format(newsAdded)
                    : DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertGameweeksAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        IReadOnlyList<HistoricalFplPlayerGameweek> gameweeks,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO historical_fpl_player_gameweeks (
                capture_id,
                season_element_id,
                player_code,
                gameweek,
                fixture_id,
                kickoff_utc,
                team_name,
                opponent_team_id,
                was_home,
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
                bps,
                influence,
                creativity,
                threat,
                ict_index,
                expected_goals,
                expected_assists,
                expected_goal_involvements,
                expected_goals_conceded,
                clearances_blocks_interceptions,
                defensive_contribution,
                recoveries,
                tackles
            )
            VALUES (
                $captureId,
                $seasonElementId,
                $playerCode,
                $gameweek,
                $fixtureId,
                $kickoffUtc,
                $teamName,
                $opponentTeamId,
                $wasHome,
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
                $bps,
                $influence,
                $creativity,
                $threat,
                $ictIndex,
                $expectedGoals,
                $expectedAssists,
                $expectedGoalInvolvements,
                $expectedGoalsConceded,
                $clearancesBlocksInterceptions,
                $defensiveContribution,
                $recoveries,
                $tackles
            );
            """;
        string[] integerParameters =
        [
            "$captureId",
            "$seasonElementId",
            "$playerCode",
            "$gameweek",
            "$fixtureId",
            "$opponentTeamId",
            "$wasHome",
            "$minutes",
            "$starts",
            "$totalPoints",
            "$goalsScored",
            "$assists",
            "$cleanSheets",
            "$goalsConceded",
            "$saves",
            "$bonus",
            "$yellowCards",
            "$redCards",
            "$bps",
            "$clearancesBlocksInterceptions",
            "$defensiveContribution",
            "$recoveries",
            "$tackles",
        ];
        foreach (string parameter in integerParameters)
        {
            command.Parameters.Add(parameter, SqliteType.Integer);
        }

        string[] textParameters =
        [
            "$kickoffUtc",
            "$teamName",
            "$influence",
            "$creativity",
            "$threat",
            "$ictIndex",
            "$expectedGoals",
            "$expectedAssists",
            "$expectedGoalInvolvements",
            "$expectedGoalsConceded",
        ];
        foreach (string parameter in textParameters)
        {
            command.Parameters.Add(parameter, SqliteType.Text);
        }

        foreach (HistoricalFplPlayerGameweek row in gameweeks)
        {
            command.Parameters["$captureId"].Value = captureId;
            command.Parameters["$seasonElementId"].Value = row.SeasonElementId;
            command.Parameters["$playerCode"].Value = row.PlayerCode;
            command.Parameters["$gameweek"].Value = row.Gameweek;
            command.Parameters["$fixtureId"].Value = row.FixtureId;
            command.Parameters["$kickoffUtc"].Value = Format(row.KickoffUtc);
            command.Parameters["$teamName"].Value = row.TeamName;
            command.Parameters["$opponentTeamId"].Value = row.OpponentTeamId;
            command.Parameters["$wasHome"].Value = row.WasHome ? 1 : 0;
            command.Parameters["$minutes"].Value = row.Minutes;
            command.Parameters["$starts"].Value = row.Starts;
            command.Parameters["$totalPoints"].Value = row.TotalPoints;
            command.Parameters["$goalsScored"].Value = row.GoalsScored;
            command.Parameters["$assists"].Value = row.Assists;
            command.Parameters["$cleanSheets"].Value = row.CleanSheets;
            command.Parameters["$goalsConceded"].Value = row.GoalsConceded;
            command.Parameters["$saves"].Value = row.Saves;
            command.Parameters["$bonus"].Value = row.Bonus;
            command.Parameters["$yellowCards"].Value = row.YellowCards;
            command.Parameters["$redCards"].Value = row.RedCards;
            command.Parameters["$bps"].Value = row.Bps;
            command.Parameters["$influence"].Value = Decimal(row.Influence);
            command.Parameters["$creativity"].Value = Decimal(row.Creativity);
            command.Parameters["$threat"].Value = Decimal(row.Threat);
            command.Parameters["$ictIndex"].Value = Decimal(row.IctIndex);
            command.Parameters["$expectedGoals"].Value = Decimal(row.ExpectedGoals);
            command.Parameters["$expectedAssists"].Value = Decimal(row.ExpectedAssists);
            command.Parameters["$expectedGoalInvolvements"].Value =
                Decimal(row.ExpectedGoalInvolvements);
            command.Parameters["$expectedGoalsConceded"].Value =
                Decimal(row.ExpectedGoalsConceded);
            command.Parameters["$clearancesBlocksInterceptions"].Value =
                row.ClearancesBlocksInterceptions is int clearances
                    ? clearances
                    : DBNull.Value;
            command.Parameters["$defensiveContribution"].Value =
                row.DefensiveContribution is int contribution
                    ? contribution
                    : DBNull.Value;
            command.Parameters["$recoveries"].Value =
                row.Recoveries is int recoveries ? recoveries : DBNull.Value;
            command.Parameters["$tackles"].Value =
                row.Tackles is int tackles ? tackles : DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static byte[] Compress(byte[] content)
    {
        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(
            compressed,
            CompressionLevel.SmallestSize,
            leaveOpen: true))
        {
            brotli.Write(content);
        }

        return compressed.ToArray();
    }

    private static HistoricalFplSeasonCaptureDocument CreateDocument(
        long captureId,
        HistoricalFplSeasonDefinition definition,
        HistoricalFplSeasonPayload payload,
        DateTimeOffset retrievedAtUtc) =>
        new(
            captureId,
            "1.0",
            HistoricalFplSeasonRegistry.SourceKey,
            definition.SeasonCode,
            definition.SourceRevision,
            definition.PlayersUri.AbsoluteUri,
            definition.GameweeksUri.AbsoluteUri,
            definition.PublishedAtUtc,
            retrievedAtUtc,
            retrievedAtUtc,
            payload.PlayersSha256,
            payload.GameweeksSha256,
            payload.Players.Count,
            payload.PlayerGameweeks.Count,
            payload.Players.Select(player => player.PlayerCode).Distinct().Count());

    private static HistoricalFplSeasonCaptureDocument ReadDocument(
        SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            Parse(reader.GetString(7)),
            Parse(reader.GetString(8)),
            Parse(reader.GetString(9)),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetInt32(14));

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string Decimal(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private const string SelectDocumentSql =
        """
        SELECT
            capture_id,
            schema_version,
            source_key,
            season_code,
            source_revision,
            players_url,
            gameweeks_url,
            published_at_utc,
            retrieved_at_utc,
            available_at_utc,
            players_sha256,
            gameweeks_sha256,
            player_count,
            player_gameweek_count,
            stable_code_count
        FROM historical_fpl_season_captures
        """;

    private sealed record HistoricalFplSeasonIdentity(
        HistoricalFplSeasonCaptureDocument Document,
        IReadOnlyList<int> PlayerCodes,
        int PlayerGameweekCount);
}

public sealed class HistoricalFplSeasonCoverageException : Exception
{
    public HistoricalFplSeasonCoverageException(string message)
        : base(message)
    {
    }
}
