using System.Globalization;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Sources;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Sources;

public sealed class OfficialFplCaptureStore
{
    private readonly DatabaseOptions _options;

    public OfficialFplCaptureStore(DatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    internal async Task<OfficialFplCaptureDocument> SaveAsync(
        OfficialFplPayload payload,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        DateTimeOffset availableAtUtc = retrievedAtUtc.ToUniversalTime();

        await using SqliteConnection connection = new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

        long? existingId = await FindExistingCaptureIdAsync(
            connection,
            transaction,
            payload.BootstrapSha256,
            payload.FixturesSha256,
            cancellationToken);
        if (existingId is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (await GetAsync(existingId.Value, cancellationToken))!;
        }

        long captureId = await InsertCaptureAsync(
            connection,
            transaction,
            payload,
            availableAtUtc,
            cancellationToken);
        await InsertEventsAsync(connection, transaction, captureId, payload.Events, cancellationToken);
        await InsertTeamsAsync(connection, transaction, captureId, payload.Teams, cancellationToken);
        await InsertPlayersAsync(connection, transaction, captureId, payload.Players, cancellationToken);
        await InsertFixturesAsync(
            connection,
            transaction,
            captureId,
            payload.Fixtures,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateDocument(captureId, payload, availableAtUtc);
    }

    public async Task<OfficialFplCaptureDocument?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                capture_id,
                schema_version,
                source_key,
                season_code,
                bootstrap_url,
                fixtures_url,
                retrieved_at_utc,
                available_at_utc,
                bootstrap_sha256,
                fixtures_sha256,
                event_count,
                team_count,
                player_count,
                fixture_count,
                next_gameweek_number,
                next_deadline_utc,
                latest_completed_gameweek
            FROM official_fpl_captures
            ORDER BY available_at_utc DESC, capture_id DESC
            LIMIT 1;
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadDocument(reader)
            : null;
    }

    public async Task<OfficialFplCaptureDocument?> GetAsync(
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
            """
            SELECT
                capture_id,
                schema_version,
                source_key,
                season_code,
                bootstrap_url,
                fixtures_url,
                retrieved_at_utc,
                available_at_utc,
                bootstrap_sha256,
                fixtures_sha256,
                event_count,
                team_count,
                player_count,
                fixture_count,
                next_gameweek_number,
                next_deadline_utc,
                latest_completed_gameweek
            FROM official_fpl_captures
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadDocument(reader)
            : null;
    }

    private static async Task<long?> FindExistingCaptureIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string bootstrapSha256,
        string fixturesSha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT capture_id
            FROM official_fpl_captures
            WHERE bootstrap_sha256 = $bootstrapSha256
              AND fixtures_sha256 = $fixturesSha256;
            """;
        command.Parameters.AddWithValue("$bootstrapSha256", bootstrapSha256);
        command.Parameters.AddWithValue("$fixturesSha256", fixturesSha256);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long captureId ? captureId : null;
    }

    private static async Task<long> InsertCaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OfficialFplPayload payload,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO official_fpl_captures (
                schema_version,
                source_key,
                season_code,
                bootstrap_url,
                fixtures_url,
                retrieved_at_utc,
                available_at_utc,
                bootstrap_sha256,
                fixtures_sha256,
                bootstrap_json,
                fixtures_json,
                event_count,
                team_count,
                player_count,
                fixture_count,
                next_gameweek_number,
                next_deadline_utc,
                latest_completed_gameweek,
                created_at_utc
            )
            VALUES (
                '1.0',
                $sourceKey,
                $seasonCode,
                $bootstrapUrl,
                $fixturesUrl,
                $retrievedAtUtc,
                $availableAtUtc,
                $bootstrapSha256,
                $fixturesSha256,
                $bootstrapJson,
                $fixturesJson,
                $eventCount,
                $teamCount,
                $playerCount,
                $fixtureCount,
                $nextGameweekNumber,
                $nextDeadlineUtc,
                $latestCompletedGameweek,
                $createdAtUtc
            );
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$sourceKey", OfficialFplImporter.SourceKey);
        command.Parameters.AddWithValue("$seasonCode", payload.SeasonCode);
        command.Parameters.AddWithValue(
            "$bootstrapUrl",
            OfficialFplImporter.BootstrapUri.AbsoluteUri);
        command.Parameters.AddWithValue(
            "$fixturesUrl",
            OfficialFplImporter.FixturesUri.AbsoluteUri);
        command.Parameters.AddWithValue("$retrievedAtUtc", FormatUtc(retrievedAtUtc));
        command.Parameters.AddWithValue("$availableAtUtc", FormatUtc(retrievedAtUtc));
        command.Parameters.AddWithValue("$bootstrapSha256", payload.BootstrapSha256);
        command.Parameters.AddWithValue("$fixturesSha256", payload.FixturesSha256);
        command.Parameters.Add("$bootstrapJson", SqliteType.Blob).Value = payload.BootstrapJson;
        command.Parameters.Add("$fixturesJson", SqliteType.Blob).Value = payload.FixturesJson;
        command.Parameters.AddWithValue("$eventCount", payload.Events.Count);
        command.Parameters.AddWithValue("$teamCount", payload.Teams.Count);
        command.Parameters.AddWithValue("$playerCount", payload.Players.Count);
        command.Parameters.AddWithValue("$fixtureCount", payload.Fixtures.Count);
        command.Parameters.AddWithValue(
            "$nextGameweekNumber",
            (object?)payload.NextGameweekNumber ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$nextDeadlineUtc",
            payload.NextDeadlineUtc is null
                ? DBNull.Value
                : FormatUtc(payload.NextDeadlineUtc.Value));
        command.Parameters.AddWithValue(
            "$latestCompletedGameweek",
            (object?)payload.LatestCompletedGameweek ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(retrievedAtUtc));

        return (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("SQLite did not return a capture ID."));
    }

    private static async Task InsertEventsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        IReadOnlyList<OfficialFplEvent> events,
        CancellationToken cancellationToken)
    {
        foreach (OfficialFplEvent item in events)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO official_fpl_events (
                    capture_id,
                    event_id,
                    name,
                    deadline_utc,
                    finished,
                    data_checked,
                    is_current,
                    is_next
                )
                VALUES (
                    $captureId,
                    $eventId,
                    $name,
                    $deadlineUtc,
                    $finished,
                    $dataChecked,
                    $isCurrent,
                    $isNext
                );
                """;
            command.Parameters.AddWithValue("$captureId", captureId);
            command.Parameters.AddWithValue("$eventId", item.Id);
            command.Parameters.AddWithValue("$name", item.Name);
            command.Parameters.AddWithValue("$deadlineUtc", FormatUtc(item.DeadlineUtc));
            command.Parameters.AddWithValue("$finished", item.Finished);
            command.Parameters.AddWithValue("$dataChecked", item.DataChecked);
            command.Parameters.AddWithValue("$isCurrent", item.IsCurrent);
            command.Parameters.AddWithValue("$isNext", item.IsNext);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertTeamsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        IReadOnlyList<OfficialFplTeam> teams,
        CancellationToken cancellationToken)
    {
        foreach (OfficialFplTeam item in teams)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO official_fpl_teams (
                    capture_id,
                    team_id,
                    code,
                    name,
                    short_name
                )
                VALUES ($captureId, $teamId, $code, $name, $shortName);
                """;
            command.Parameters.AddWithValue("$captureId", captureId);
            command.Parameters.AddWithValue("$teamId", item.Id);
            command.Parameters.AddWithValue("$code", item.Code);
            command.Parameters.AddWithValue("$name", item.Name);
            command.Parameters.AddWithValue("$shortName", item.ShortName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertPlayersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        IReadOnlyList<OfficialFplPlayer> players,
        CancellationToken cancellationToken)
    {
        foreach (OfficialFplPlayer item in players)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO official_fpl_players (
                    capture_id,
                    player_id,
                    code,
                    team_id,
                    position,
                    first_name,
                    second_name,
                    web_name,
                    price_tenths,
                    status,
                    news,
                    news_added_utc,
                    chance_next_round,
                    selected_by_percent,
                    total_points,
                    minutes,
                    starts
                )
                VALUES (
                    $captureId,
                    $playerId,
                    $code,
                    $teamId,
                    $position,
                    $firstName,
                    $secondName,
                    $webName,
                    $priceTenths,
                    $status,
                    $news,
                    $newsAddedUtc,
                    $chanceNextRound,
                    $selectedByPercent,
                    $totalPoints,
                    $minutes,
                    $starts
                );
                """;
            command.Parameters.AddWithValue("$captureId", captureId);
            command.Parameters.AddWithValue("$playerId", item.Id);
            command.Parameters.AddWithValue("$code", item.Code);
            command.Parameters.AddWithValue("$teamId", item.TeamId);
            command.Parameters.AddWithValue("$position", item.Position);
            command.Parameters.AddWithValue("$firstName", item.FirstName);
            command.Parameters.AddWithValue("$secondName", item.SecondName);
            command.Parameters.AddWithValue("$webName", item.WebName);
            command.Parameters.AddWithValue("$priceTenths", item.PriceTenths);
            command.Parameters.AddWithValue("$status", item.Status);
            command.Parameters.AddWithValue("$news", item.News);
            command.Parameters.AddWithValue(
                "$newsAddedUtc",
                item.NewsAddedUtc is null
                    ? DBNull.Value
                    : FormatUtc(item.NewsAddedUtc.Value));
            command.Parameters.AddWithValue(
                "$chanceNextRound",
                (object?)item.ChanceNextRound ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$selectedByPercent",
                item.SelectedByPercent.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$totalPoints", item.TotalPoints);
            command.Parameters.AddWithValue("$minutes", item.Minutes);
            command.Parameters.AddWithValue("$starts", item.Starts);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertFixturesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long captureId,
        IReadOnlyList<OfficialFplFixture> fixtures,
        CancellationToken cancellationToken)
    {
        foreach (OfficialFplFixture item in fixtures)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO official_fpl_fixtures (
                    capture_id,
                    fixture_id,
                    event_id,
                    home_team_id,
                    away_team_id,
                    kickoff_utc,
                    started,
                    finished,
                    finished_provisional,
                    home_score,
                    away_score
                )
                VALUES (
                    $captureId,
                    $fixtureId,
                    $eventId,
                    $homeTeamId,
                    $awayTeamId,
                    $kickoffUtc,
                    $started,
                    $finished,
                    $finishedProvisional,
                    $homeScore,
                    $awayScore
                );
                """;
            command.Parameters.AddWithValue("$captureId", captureId);
            command.Parameters.AddWithValue("$fixtureId", item.Id);
            command.Parameters.AddWithValue("$eventId", (object?)item.EventId ?? DBNull.Value);
            command.Parameters.AddWithValue("$homeTeamId", item.HomeTeamId);
            command.Parameters.AddWithValue("$awayTeamId", item.AwayTeamId);
            command.Parameters.AddWithValue(
                "$kickoffUtc",
                item.KickoffUtc is null
                    ? DBNull.Value
                    : FormatUtc(item.KickoffUtc.Value));
            command.Parameters.AddWithValue("$started", item.Started);
            command.Parameters.AddWithValue("$finished", item.Finished);
            command.Parameters.AddWithValue("$finishedProvisional", item.FinishedProvisional);
            command.Parameters.AddWithValue("$homeScore", (object?)item.HomeScore ?? DBNull.Value);
            command.Parameters.AddWithValue("$awayScore", (object?)item.AwayScore ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static OfficialFplCaptureDocument CreateDocument(
        long captureId,
        OfficialFplPayload payload,
        DateTimeOffset retrievedAtUtc) =>
        new(
            captureId,
            "1.0",
            OfficialFplImporter.SourceKey,
            payload.SeasonCode,
            OfficialFplImporter.BootstrapUri.AbsoluteUri,
            OfficialFplImporter.FixturesUri.AbsoluteUri,
            PublishedAtUtc: null,
            retrievedAtUtc,
            retrievedAtUtc,
            payload.BootstrapSha256,
            payload.FixturesSha256,
            payload.Events.Count,
            payload.Teams.Count,
            payload.Players.Count,
            payload.Fixtures.Count,
            payload.NextGameweekNumber,
            payload.NextDeadlineUtc,
            payload.LatestCompletedGameweek);

    private static OfficialFplCaptureDocument ReadDocument(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            PublishedAtUtc: null,
            ParseUtc(reader.GetString(6)),
            ParseUtc(reader.GetString(7)),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.IsDBNull(14) ? null : reader.GetInt32(14),
            reader.IsDBNull(15) ? null : ParseUtc(reader.GetString(15)),
            reader.IsDBNull(16) ? null : reader.GetInt32(16));

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
}
