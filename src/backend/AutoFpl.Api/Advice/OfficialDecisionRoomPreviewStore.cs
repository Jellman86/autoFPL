using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Advice;

public sealed class OfficialDecisionRoomPreviewStore
{
    private static readonly IReadOnlyDictionary<string, int> SquadQuotas =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["goalkeeper"] = 2,
            ["defender"] = 5,
            ["midfielder"] = 5,
            ["forward"] = 3,
        };

    private readonly DatabaseOptions _options;
    private readonly OfficialFplCaptureStore _captureStore;

    public OfficialDecisionRoomPreviewStore(
        DatabaseOptions options,
        OfficialFplCaptureStore captureStore)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _captureStore = captureStore ?? throw new ArgumentNullException(nameof(captureStore));
    }

    public async Task<OfficialDecisionRoomPreview?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        var latest = await _captureStore.GetLatestAsync(cancellationToken);
        if (latest?.NextGameweekNumber is null || latest.NextDeadlineUtc is null)
        {
            return null;
        }

        var replay = await _captureStore.GetLatestPreDeadlineReplayAsync(
            latest.SeasonCode,
            latest.NextGameweekNumber.Value,
            cancellationToken);
        if (replay is null)
        {
            return null;
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        IReadOnlyList<PreviewCandidate> candidates = await ReadCandidatesAsync(
            connection,
            replay.SelectedCaptureId,
            cancellationToken);
        IReadOnlyDictionary<int, IReadOnlyList<PreviewFixture>> fixturesByTeam =
            await ReadFixturesAsync(
                connection,
                replay.SelectedCaptureId,
                replay.Gameweek,
                cancellationToken);
        IReadOnlyList<PreviewCandidate> selected = SelectSquad(candidates);
        if (selected.Count != 15)
        {
            return null;
        }

        OfficialDecisionRoomPlayer[] players = selected
            .Select(candidate =>
            {
                fixturesByTeam.TryGetValue(
                    candidate.TeamId,
                    out IReadOnlyList<PreviewFixture>? fixtures);
                fixtures ??= [];
                string opponent = fixtures.Count switch
                {
                    0 => "TBD",
                    1 => fixtures[0].OpponentShortName,
                    _ => $"{fixtures.Count} fixtures",
                };
                bool isHome = fixtures.Count > 0 && fixtures[0].IsHome;
                return new OfficialDecisionRoomPlayer(
                    candidate.PlayerId,
                    candidate.FullName,
                    candidate.TeamShortName,
                    candidate.Position,
                    opponent,
                    isHome,
                    OfficialFplPlayerDossierStore.CreatePhotoUrl(
                        candidate.PhotoIdentifier),
                    $"/api/v1/data/official-fpl/replays/"
                        + $"{Uri.EscapeDataString(latest.SeasonCode)}/"
                        + $"{replay.Gameweek}/players/{candidate.PlayerId}");
            })
            .ToArray();

        return new(
            latest.SeasonCode,
            replay.Gameweek,
            replay.DeadlineUtc,
            replay.CaptureAvailableAtUtc,
            replay.SelectedCaptureId,
            players);
    }

    private static IReadOnlyList<PreviewCandidate> SelectSquad(
        IReadOnlyList<PreviewCandidate> candidates)
    {
        var teamCounts = new Dictionary<int, int>();
        var selected = new List<PreviewCandidate>(15);
        foreach ((string position, int quota) in SquadQuotas)
        {
            foreach (PreviewCandidate candidate in candidates.Where(
                item => StringComparer.Ordinal.Equals(item.Position, position)))
            {
                int teamCount = teamCounts.GetValueOrDefault(candidate.TeamId);
                if (teamCount >= 3)
                {
                    continue;
                }

                selected.Add(candidate);
                teamCounts[candidate.TeamId] = teamCount + 1;
                if (selected.Count(item => StringComparer.Ordinal.Equals(
                        item.Position,
                        position)) == quota)
                {
                    break;
                }
            }
        }

        return selected;
    }

    private static async Task<IReadOnlyList<PreviewCandidate>> ReadCandidatesAsync(
        SqliteConnection connection,
        long captureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                player.player_id,
                player.first_name || ' ' || player.second_name,
                player.team_id,
                team.short_name,
                player.position,
                player.photo_identifier
            FROM official_fpl_players AS player
            INNER JOIN official_fpl_teams AS team
                ON team.capture_id = player.capture_id
               AND team.team_id = player.team_id
            WHERE player.capture_id = $captureId
              AND player.status <> 'u'
            ORDER BY
                CAST(player.selected_by_percent AS REAL) DESC,
                player.price_tenths DESC,
                player.player_id;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);

        var results = new List<PreviewCandidate>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return results;
    }

    private static async Task<IReadOnlyDictionary<int, IReadOnlyList<PreviewFixture>>>
        ReadFixturesAsync(
            SqliteConnection connection,
            long captureId,
            int gameweek,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                fixture.home_team_id,
                fixture.away_team_id,
                home.short_name,
                away.short_name,
                fixture.kickoff_utc,
                fixture.fixture_id
            FROM official_fpl_fixtures AS fixture
            INNER JOIN official_fpl_teams AS home
                ON home.capture_id = fixture.capture_id
               AND home.team_id = fixture.home_team_id
            INNER JOIN official_fpl_teams AS away
                ON away.capture_id = fixture.capture_id
               AND away.team_id = fixture.away_team_id
            WHERE fixture.capture_id = $captureId
              AND fixture.event_id = $gameweek
            ORDER BY fixture.kickoff_utc, fixture.fixture_id;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$gameweek", gameweek);

        var results = new Dictionary<int, List<PreviewFixture>>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int homeTeamId = reader.GetInt32(0);
            int awayTeamId = reader.GetInt32(1);
            AddFixture(
                results,
                homeTeamId,
                new(reader.GetString(3), IsHome: true));
            AddFixture(
                results,
                awayTeamId,
                new(reader.GetString(2), IsHome: false));
        }

        return results.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<PreviewFixture>)item.Value);
    }

    private static void AddFixture(
        IDictionary<int, List<PreviewFixture>> results,
        int teamId,
        PreviewFixture fixture)
    {
        if (!results.TryGetValue(teamId, out List<PreviewFixture>? fixtures))
        {
            fixtures = [];
            results[teamId] = fixtures;
        }

        fixtures.Add(fixture);
    }

    private sealed record PreviewCandidate(
        int PlayerId,
        string FullName,
        int TeamId,
        string TeamShortName,
        string Position,
        string? PhotoIdentifier);

    private sealed record PreviewFixture(
        string OpponentShortName,
        bool IsHome);
}

public sealed record OfficialDecisionRoomPreview(
    string SeasonCode,
    int Gameweek,
    DateTimeOffset DeadlineUtc,
    DateTimeOffset CaptureAvailableAtUtc,
    long CaptureId,
    IReadOnlyList<OfficialDecisionRoomPlayer> Players);

public sealed record OfficialDecisionRoomPlayer(
    int PlayerId,
    string Name,
    string ClubShortName,
    string Position,
    string Opponent,
    bool IsHome,
    string? PhotoUrl,
    string DossierPath);
