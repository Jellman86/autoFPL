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
        OfficialPlayerForecastPreview? forecast =
            await GetLatestPlayerForecastAsync(cancellationToken);
        if (forecast is null)
        {
            return null;
        }

        IReadOnlyList<OfficialPlayerForecastCandidate> selected =
            SelectSquad(forecast.Players);
        if (selected.Count != 15)
        {
            return null;
        }

        IReadOnlyList<OfficialPlayerForecastCandidate> lineup =
            SelectLineup(selected);
        if (lineup.Count != 11)
        {
            return null;
        }

        var starterIds = lineup.Select(player => player.PlayerId).ToHashSet();
        int captainId = lineup.OrderByDescending(player => player.ExpectedPoints)
            .ThenBy(player => player.PlayerId)
            .First()
            .PlayerId;
        int viceCaptainId = lineup.OrderByDescending(player => player.ExpectedPoints)
            .ThenBy(player => player.PlayerId)
            .Skip(1)
            .First()
            .PlayerId;
        IReadOnlyDictionary<int, int> benchOrder = BenchOrder(selected, starterIds);
        OfficialDecisionRoomPlayer[] players = selected
            .OrderBy(candidate => starterIds.Contains(candidate.PlayerId) ? 0 : 1)
            .ThenBy(candidate => PositionOrder(candidate.Position))
            .ThenByDescending(candidate => candidate.ExpectedPoints)
            .Select(candidate => new OfficialDecisionRoomPlayer(
                candidate.PlayerId,
                candidate.Name,
                candidate.ClubShortName,
                candidate.Position,
                candidate.Opponent,
                candidate.IsHome,
                starterIds.Contains(candidate.PlayerId) ? "starting" : "bench",
                benchOrder.GetValueOrDefault(candidate.PlayerId) is 0
                    ? null
                    : benchOrder[candidate.PlayerId],
                candidate.PlayerId == captainId
                    ? "captain"
                    : candidate.PlayerId == viceCaptainId
                        ? "vice-captain"
                        : null,
                candidate.ExpectedPoints,
                candidate.Lower80,
                candidate.Upper80,
                candidate.ExpectedMinutes,
                candidate.Reasons,
                candidate.Risks,
                candidate.PhotoUrl,
                candidate.DossierPath))
            .ToArray();

        return new(
            forecast.SeasonCode,
            forecast.Gameweek,
            forecast.DeadlineUtc,
            forecast.CaptureAvailableAtUtc,
            forecast.CaptureId,
            players);
    }

    public async Task<OfficialPlayerForecastPreview?> GetLatestPlayerForecastAsync(
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
        OfficialPlayerForecastCandidate[] scored = candidates
            .Select(candidate => Score(
                candidate,
                fixturesByTeam,
                latest.SeasonCode,
                replay.Gameweek))
            .OrderBy(candidate => PositionOrder(candidate.Position))
            .ThenByDescending(candidate => candidate.ExpectedPoints)
            .ThenBy(candidate => candidate.PlayerId)
            .ToArray();

        return new(
            latest.SeasonCode,
            replay.Gameweek,
            replay.DeadlineUtc,
            replay.CaptureAvailableAtUtc,
            replay.SelectedCaptureId,
            scored);
    }

    private static IReadOnlyList<OfficialPlayerForecastCandidate> SelectSquad(
        IReadOnlyList<OfficialPlayerForecastCandidate> candidates)
    {
        var teamCounts = new Dictionary<int, int>();
        var selected = new List<OfficialPlayerForecastCandidate>(15);
        foreach ((string position, int quota) in SquadQuotas)
        {
            foreach (OfficialPlayerForecastCandidate candidate in candidates
                .Where(item => StringComparer.Ordinal.Equals(item.Position, position))
                .OrderByDescending(item => item.ExpectedPoints)
                .ThenBy(item => item.PriceTenths)
                .ThenBy(item => item.PlayerId))
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

        while (selected.Sum(player => player.PriceTenths) > 1000)
        {
            (
                OfficialPlayerForecastCandidate Selected,
                OfficialPlayerForecastCandidate Replacement,
                decimal Cost)? best = null;
            foreach (OfficialPlayerForecastCandidate current in selected)
            {
                foreach (OfficialPlayerForecastCandidate replacement
                    in candidates.Where(candidate =>
                    StringComparer.Ordinal.Equals(candidate.Position, current.Position)
                    && candidate.PriceTenths < current.PriceTenths
                    && selected.All(item => item.PlayerId != candidate.PlayerId)))
                {
                    int replacementTeamCount = teamCounts.GetValueOrDefault(replacement.TeamId)
                        - (replacement.TeamId == current.TeamId ? 1 : 0);
                    if (replacementTeamCount >= 3)
                    {
                        continue;
                    }

                    int saving = current.PriceTenths - replacement.PriceTenths;
                    decimal loss = current.ExpectedPoints - replacement.ExpectedPoints;
                    decimal cost = loss / saving;
                    if (best is null
                        || cost < best.Value.Cost
                        || (cost == best.Value.Cost
                            && replacement.PlayerId < best.Value.Replacement.PlayerId))
                    {
                        best = (current, replacement, cost);
                    }
                }
            }

            if (best is null)
            {
                return [];
            }

            selected.Remove(best.Value.Selected);
            teamCounts[best.Value.Selected.TeamId]--;
            selected.Add(best.Value.Replacement);
            teamCounts[best.Value.Replacement.TeamId] =
                teamCounts.GetValueOrDefault(best.Value.Replacement.TeamId) + 1;
        }

        return selected;
    }

    private static OfficialPlayerForecastCandidate Score(
        PreviewCandidate candidate,
        IReadOnlyDictionary<int, IReadOnlyList<PreviewFixture>> fixturesByTeam,
        string seasonCode,
        int gameweek)
    {
        fixturesByTeam.TryGetValue(candidate.TeamId, out var fixtures);
        fixtures ??= [];
        int fixtureCount = Math.Max(1, fixtures.Count);
        decimal positionPrior = candidate.Position switch
        {
            "goalkeeper" => 3.2m,
            "defender" => 3.4m,
            "midfielder" => 3.6m,
            _ => 3.7m,
        };
        decimal marketPrior = positionPrior
            + Math.Clamp((candidate.PriceTenths - 45) * 0.045m, -0.45m, 3.6m)
            + (decimal)Math.Sqrt((double)candidate.SelectedByPercent) * 0.035m;
        decimal observed = candidate.Starts > 0
            ? candidate.TotalPoints / (decimal)candidate.Starts
            : positionPrior;
        decimal historyWeight = candidate.Starts / (candidate.Starts + 5m);
        decimal perFixture = marketPrior * (1m - historyWeight) + observed * historyWeight;
        if (fixtures.Count > 0 && fixtures[0].IsHome)
        {
            perFixture += 0.2m;
        }

        int availability = candidate.Status switch
        {
            "u" => 0,
            "i" or "s" => candidate.ChanceOfPlaying ?? 20,
            "d" => candidate.ChanceOfPlaying ?? 50,
            _ => candidate.ChanceOfPlaying ?? 92,
        };
        int expectedMinutes = (int)Math.Round(90m * availability / 100m);
        decimal expected = Math.Round(
            perFixture * fixtureCount * availability / 100m,
            2,
            MidpointRounding.AwayFromZero);
        decimal spread = 3.5m + expected * 0.55m;
        string opponent = fixtures.Count switch
        {
            0 => "TBD",
            1 => fixtures[0].OpponentShortName,
            _ => $"{fixtures.Count} fixtures",
        };
        return new(
            candidate.PlayerId,
            candidate.FullName,
            candidate.TeamId,
            candidate.TeamShortName,
            candidate.Position,
            candidate.PriceTenths,
            candidate.Status,
            candidate.ChanceOfPlaying,
            fixtures.Count,
            opponent,
            fixtures.Count > 0 && fixtures[0].IsHome,
            expected,
            Math.Max(0m, Math.Round(expected - spread, 1)),
            Math.Round(expected + spread, 1),
            expectedMinutes,
            [
                $"Market prior uses £{candidate.PriceTenths / 10m:0.0}m price and {candidate.SelectedByPercent:0.0}% ownership.",
                fixtures.Count == 0
                    ? "No confirmed Gameweek fixture is present; a one-fixture neutral prior is used."
                    : $"{fixtures.Count} confirmed fixture{(fixtures.Count == 1 ? "" : "s")} with {(fixtures[0].IsHome ? "home" : "away")} context.",
            ],
            [
                candidate.Starts == 0
                    ? "No capture-reported starts are available, so uncertainty remains deliberately wide."
                    : $"Only {candidate.Starts} capture-reported start{(candidate.Starts == 1 ? "" : "s")} inform the observed-rate blend.",
                "This preseason market baseline has not yet earned promotion through rolling out-of-time evaluation.",
            ],
            OfficialFplPlayerDossierStore.CreatePhotoUrl(
                candidate.PhotoIdentifier),
            $"/api/v1/data/official-fpl/replays/"
                + $"{Uri.EscapeDataString(seasonCode)}/"
                + $"{gameweek}/players/{candidate.PlayerId}");
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
                player.photo_identifier,
                player.price_tenths,
                CAST(player.selected_by_percent AS REAL),
                player.status,
                player.chance_next_round,
                player.total_points,
                player.minutes,
                player.starts
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
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetInt32(6),
                    Convert.ToDecimal(
                        reader.GetDouble(7),
                        System.Globalization.CultureInfo.InvariantCulture),
                    reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11),
                    reader.GetInt32(12)));
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

    private static IReadOnlyList<OfficialPlayerForecastCandidate> SelectLineup(
        IReadOnlyList<OfficialPlayerForecastCandidate> squad)
    {
        IReadOnlyList<OfficialPlayerForecastCandidate> best = [];
        decimal bestScore = decimal.MinValue;
        for (int defenders = 3; defenders <= 5; defenders++)
        {
            for (int midfielders = 2; midfielders <= 5; midfielders++)
            {
                int forwards = 10 - defenders - midfielders;
                if (forwards is < 1 or > 3)
                {
                    continue;
                }

                OfficialPlayerForecastCandidate[] candidate =
                [
                    .. squad.Where(item => item.Position == "goalkeeper")
                        .OrderByDescending(item => item.ExpectedPoints)
                        .Take(1),
                    .. squad.Where(item => item.Position == "defender")
                        .OrderByDescending(item => item.ExpectedPoints)
                        .Take(defenders),
                    .. squad.Where(item => item.Position == "midfielder")
                        .OrderByDescending(item => item.ExpectedPoints)
                        .Take(midfielders),
                    .. squad.Where(item => item.Position == "forward")
                        .OrderByDescending(item => item.ExpectedPoints)
                        .Take(forwards),
                ];
                decimal score = candidate.Sum(item => item.ExpectedPoints);
                if (candidate.Length == 11 && score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
        }

        return best;
    }

    private static IReadOnlyDictionary<int, int> BenchOrder(
        IReadOnlyList<OfficialPlayerForecastCandidate> squad,
        IReadOnlySet<int> starterIds)
    {
        OfficialPlayerForecastCandidate goalkeeper = squad.Single(
            item => item.Position == "goalkeeper" && !starterIds.Contains(item.PlayerId));
        OfficialPlayerForecastCandidate[] outfield = squad
            .Where(item => item.Position != "goalkeeper" && !starterIds.Contains(item.PlayerId))
            .OrderByDescending(item => item.ExpectedPoints)
            .ThenBy(item => item.PlayerId)
            .ToArray();
        var result = new Dictionary<int, int> { [goalkeeper.PlayerId] = 1 };
        for (int index = 0; index < outfield.Length; index++)
        {
            result[outfield[index].PlayerId] = index + 2;
        }

        return result;
    }

    private static int PositionOrder(string position) => position switch
    {
        "goalkeeper" => 0,
        "defender" => 1,
        "midfielder" => 2,
        _ => 3,
    };

    private sealed record PreviewCandidate(
        int PlayerId,
        string FullName,
        int TeamId,
        string TeamShortName,
        string Position,
        string? PhotoIdentifier,
        int PriceTenths,
        decimal SelectedByPercent,
        string Status,
        int? ChanceOfPlaying,
        int TotalPoints,
        int Minutes,
        int Starts);

    private sealed record PreviewFixture(
        string OpponentShortName,
        bool IsHome);
}

public sealed record OfficialPlayerForecastPreview(
    string SeasonCode,
    int Gameweek,
    DateTimeOffset DeadlineUtc,
    DateTimeOffset CaptureAvailableAtUtc,
    long CaptureId,
    IReadOnlyList<OfficialPlayerForecastCandidate> Players);

public sealed record OfficialPlayerForecastCandidate(
    int PlayerId,
    string Name,
    int TeamId,
    string ClubShortName,
    string Position,
    int PriceTenths,
    string OfficialStatus,
    int? OfficialChanceOfPlayingNextRound,
    int FixtureCount,
    string Opponent,
    bool IsHome,
    decimal ExpectedPoints,
    decimal Lower80,
    decimal Upper80,
    int ExpectedMinutes,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Risks,
    string? PhotoUrl,
    string DossierPath);

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
    string LineupPlace,
    int? BenchOrder,
    string? Captaincy,
    decimal ExpectedPoints,
    decimal Lower80,
    decimal Upper80,
    int ExpectedMinutes,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Risks,
    string? PhotoUrl,
    string DossierPath);
