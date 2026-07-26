using System.Globalization;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Sources;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Sources;

public sealed class OfficialFplPlayerDossierStore
{
    public const string PhotoBaseUrl =
        "https://resources.premierleague.com/premierleague/photos/players/110x140/";

    private const int RecentOutcomeLimit = 5;
    private const int UpcomingGameweekWindow = 6;

    private readonly DatabaseOptions _options;
    private readonly OfficialFplCaptureStore _captureStore;

    public OfficialFplPlayerDossierStore(
        DatabaseOptions options,
        OfficialFplCaptureStore captureStore)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _captureStore = captureStore ?? throw new ArgumentNullException(nameof(captureStore));
    }

    public async Task<OfficialFplPlayerDossierDocument?> GetAsync(
        string seasonCode,
        int targetGameweek,
        int playerId,
        CancellationToken cancellationToken = default)
    {
        OfficialFplReplayDocument? replay =
            await _captureStore.GetLatestPreDeadlineReplayAsync(
                seasonCode,
                targetGameweek,
                cancellationToken);
        if (replay is null)
        {
            return null;
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        PlayerIdentityRow? identity = await ReadIdentityAsync(
            connection,
            replay.SelectedCaptureId,
            playerId,
            cancellationToken);
        if (identity is null)
        {
            return null;
        }

        IReadOnlyList<OutcomeRow> outcomeRows = await ReadOutcomeRowsAsync(
            connection,
            seasonCode,
            targetGameweek,
            replay.DeadlineUtc,
            identity.PlayerCode,
            cancellationToken);
        var outcomes = new List<OfficialFplPlayerOutcomeDocument>(outcomeRows.Count);
        foreach (OutcomeRow outcome in outcomeRows.OrderBy(item => item.Gameweek))
        {
            IReadOnlyList<OfficialFplPlayerFixtureDocument> fixtures =
                await ReadFixturesAsync(
                    connection,
                    outcome.ReferenceCaptureId,
                    outcome.TeamId,
                    outcome.Gameweek,
                    outcome.Gameweek,
                    cancellationToken);
            outcomes.Add(
                new(
                    outcome.Gameweek,
                    outcome.OutcomeCaptureId,
                    outcome.AvailableAtUtc,
                    fixtures.Count > 1,
                    fixtures,
                    outcome.Minutes,
                    outcome.Starts,
                    outcome.TotalPoints,
                    outcome.GoalsScored,
                    outcome.Assists,
                    outcome.CleanSheets,
                    outcome.GoalsConceded,
                    outcome.Saves,
                    outcome.Bonus,
                    outcome.YellowCards,
                    outcome.RedCards));
        }

        IReadOnlyList<OfficialFplPlayerFixtureDocument> upcoming =
            await ReadFixturesAsync(
                connection,
                replay.SelectedCaptureId,
                identity.TeamId,
                targetGameweek,
                Math.Min(38, targetGameweek + UpcomingGameweekWindow - 1),
                cancellationToken);

        return new(
            "1.0",
            seasonCode,
            targetGameweek,
            replay.DeadlineUtc,
            replay.SelectedCaptureId,
            replay.CaptureAvailableAtUtc,
            new(
                identity.PlayerId,
                identity.PlayerCode,
                $"{identity.FirstName} {identity.SecondName}",
                identity.WebName,
                identity.TeamId,
                identity.TeamName,
                identity.TeamShortName,
                identity.Position,
                identity.PriceTenths,
                identity.Status,
                identity.News,
                identity.PhotoIdentifier,
                CreatePhotoUrl(identity.PhotoIdentifier)),
            outcomes,
            upcoming);
    }

    private static async Task<PlayerIdentityRow?> ReadIdentityAsync(
        SqliteConnection connection,
        long captureId,
        int playerId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                player.player_id,
                player.code,
                player.first_name,
                player.second_name,
                player.web_name,
                player.team_id,
                team.name,
                team.short_name,
                player.position,
                player.price_tenths,
                player.status,
                player.news,
                player.photo_identifier
            FROM official_fpl_players AS player
            INNER JOIN official_fpl_teams AS team
                ON team.capture_id = player.capture_id
               AND team.team_id = player.team_id
            WHERE player.capture_id = $captureId
              AND player.player_id = $playerId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$playerId", playerId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt32(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12))
            : null;
    }

    private static async Task<IReadOnlyList<OutcomeRow>> ReadOutcomeRowsAsync(
        SqliteConnection connection,
        string seasonCode,
        int targetGameweek,
        DateTimeOffset cutoffUtc,
        int playerCode,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH ranked_outcomes AS (
                SELECT
                    capture.outcome_capture_id,
                    capture.gameweek,
                    capture.available_at_utc,
                    capture.reference_capture_id,
                    player.team_id,
                    outcome.minutes,
                    outcome.starts,
                    outcome.total_points,
                    outcome.goals_scored,
                    outcome.assists,
                    outcome.clean_sheets,
                    outcome.goals_conceded,
                    outcome.saves,
                    outcome.bonus,
                    outcome.yellow_cards,
                    outcome.red_cards,
                    ROW_NUMBER() OVER (
                        PARTITION BY capture.gameweek
                        ORDER BY
                            capture.available_at_utc DESC,
                            capture.outcome_capture_id DESC
                    ) AS correction_rank
                FROM official_fpl_outcome_captures AS capture
                INNER JOIN official_fpl_players AS player
                    ON player.capture_id = capture.reference_capture_id
                   AND player.code = $playerCode
                INNER JOIN official_fpl_player_outcomes AS outcome
                    ON outcome.outcome_capture_id = capture.outcome_capture_id
                   AND outcome.reference_capture_id = capture.reference_capture_id
                   AND outcome.player_id = player.player_id
                WHERE capture.season_code = $seasonCode
                  AND capture.gameweek < $targetGameweek
                  AND capture.available_at_utc <= $cutoffUtc
            )
            SELECT
                outcome_capture_id,
                gameweek,
                available_at_utc,
                reference_capture_id,
                team_id,
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
                red_cards
            FROM ranked_outcomes
            WHERE correction_rank = 1
            ORDER BY gameweek DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$playerCode", playerCode);
        command.Parameters.AddWithValue("$seasonCode", seasonCode);
        command.Parameters.AddWithValue("$targetGameweek", targetGameweek);
        command.Parameters.AddWithValue("$cutoffUtc", FormatUtc(cutoffUtc));
        command.Parameters.AddWithValue("$limit", RecentOutcomeLimit);

        var results = new List<OutcomeRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new(
                    reader.GetInt64(0),
                    reader.GetInt32(1),
                    ParseUtc(reader.GetString(2)),
                    reader.GetInt64(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11),
                    reader.GetInt32(12),
                    reader.GetInt32(13),
                    reader.GetInt32(14),
                    reader.GetInt32(15)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<OfficialFplPlayerFixtureDocument>>
        ReadFixturesAsync(
            SqliteConnection connection,
            long captureId,
            int teamId,
            int firstGameweek,
            int lastGameweek,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                fixture.fixture_id,
                fixture.event_id,
                fixture.kickoff_utc,
                opponent.team_id,
                opponent.name,
                opponent.short_name,
                CASE WHEN fixture.home_team_id = $teamId THEN 1 ELSE 0 END,
                fixture.started,
                fixture.finished
            FROM official_fpl_fixtures AS fixture
            INNER JOIN official_fpl_teams AS opponent
                ON opponent.capture_id = fixture.capture_id
               AND opponent.team_id = CASE
                    WHEN fixture.home_team_id = $teamId
                    THEN fixture.away_team_id
                    ELSE fixture.home_team_id
               END
            WHERE fixture.capture_id = $captureId
              AND fixture.event_id BETWEEN $firstGameweek AND $lastGameweek
              AND (
                    fixture.home_team_id = $teamId
                    OR fixture.away_team_id = $teamId
              )
            ORDER BY fixture.event_id, fixture.kickoff_utc, fixture.fixture_id;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$teamId", teamId);
        command.Parameters.AddWithValue("$firstGameweek", firstGameweek);
        command.Parameters.AddWithValue("$lastGameweek", lastGameweek);

        var results = new List<OfficialFplPlayerFixtureDocument>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : ParseUtc(reader.GetString(2)),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(7),
                    reader.GetBoolean(8)));
        }

        return results;
    }

    public static string? CreatePhotoUrl(string? photoIdentifier)
    {
        if (string.IsNullOrEmpty(photoIdentifier))
        {
            return null;
        }

        int extensionIndex = photoIdentifier.LastIndexOf('.');
        if (extensionIndex <= 0
            || !int.TryParse(
                photoIdentifier.AsSpan(0, extensionIndex),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int photoCode)
            || photoCode <= 0)
        {
            return null;
        }

        return $"{PhotoBaseUrl}p{photoCode}.png";
    }

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

    private sealed record PlayerIdentityRow(
        int PlayerId,
        int PlayerCode,
        string FirstName,
        string SecondName,
        string WebName,
        int TeamId,
        string TeamName,
        string TeamShortName,
        string Position,
        int PriceTenths,
        string Status,
        string News,
        string? PhotoIdentifier);

    private sealed record OutcomeRow(
        long OutcomeCaptureId,
        int Gameweek,
        DateTimeOffset AvailableAtUtc,
        long ReferenceCaptureId,
        int TeamId,
        int Minutes,
        int Starts,
        int TotalPoints,
        int GoalsScored,
        int Assists,
        int CleanSheets,
        int GoalsConceded,
        int Saves,
        int Bonus,
        int YellowCards,
        int RedCards);
}
