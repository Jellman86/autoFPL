using System.Globalization;
using System.Text;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Sources;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Sources;

public sealed class FplFormIdentityCoverageStore
{
    private const string Complete = "complete";
    private const string Incomplete = "incomplete";
    private const string CatalogueUnavailable = "official-catalogue-unavailable";
    private const string ForecastAfterDeadline = "forecast-after-deadline";
    private static readonly TimeZoneInfo London =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private readonly DatabaseOptions _options;

    public FplFormIdentityCoverageStore(DatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<FplFormIdentityCoverageDocument?> GetAsync(
        long forecastCaptureId,
        CancellationToken cancellationToken = default) =>
        (await GetResolutionAsync(forecastCaptureId, cancellationToken))?.Document;

    internal async Task<FplFormIdentityResolution?> GetResolutionAsync(
        long forecastCaptureId,
        CancellationToken cancellationToken = default)
    {
        if (forecastCaptureId <= 0)
        {
            return null;
        }

        await using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder(_options.ConnectionString)
            {
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
        await connection.OpenAsync(cancellationToken);
        ForecastCapture? forecast = await ReadForecastAsync(
            connection,
            forecastCaptureId,
            cancellationToken);
        if (forecast is null)
        {
            return null;
        }

        List<SourcePrediction> predictions = await ReadPredictionsAsync(
            connection,
            forecastCaptureId,
            cancellationToken);
        OfficialCapture? official = await ReadOfficialCaptureAsync(
            connection,
            forecast,
            cancellationToken);
        if (official is null)
        {
            return new(CreateUnavailableDocument(forecast, predictions), []);
        }

        List<OfficialPlayer> officialPlayers = await ReadOfficialPlayersAsync(
            connection,
            official.CaptureId,
            cancellationToken);
        List<OfficialFixture> officialFixtures = await ReadOfficialFixturesAsync(
            connection,
            official.CaptureId,
            forecast.Gameweek,
            cancellationToken);
        return Resolve(forecast, official, predictions, officialPlayers, officialFixtures);
    }

    private static FplFormIdentityResolution Resolve(
        ForecastCapture forecast,
        OfficialCapture official,
        IReadOnlyList<SourcePrediction> predictions,
        IReadOnlyList<OfficialPlayer> officialPlayers,
        IReadOnlyList<OfficialFixture> officialFixtures)
    {
        var issues = new List<FplFormIdentityCoverageIssueDocument>();
        var playerResolutions = new Dictionary<int, PlayerResolution>();
        int directPlayers = 0;
        int fallbackPlayers = 0;
        int unmatchedPlayers = 0;
        int conflictingPlayers = 0;

        foreach (IGrouping<int, SourcePrediction> group in predictions.GroupBy(
                     prediction => prediction.SourcePlayerId))
        {
            SourcePrediction source = group.First();
            PlayerResolution resolution = ResolvePlayer(group, officialPlayers);
            playerResolutions.Add(source.SourcePlayerId, resolution);
            switch (resolution.Kind)
            {
                case ResolutionKind.Direct:
                    directPlayers++;
                    break;
                case ResolutionKind.Fallback:
                    fallbackPlayers++;
                    break;
                case ResolutionKind.Unmatched:
                    unmatchedPlayers++;
                    issues.Add(CreateIssue("player", "unmatched", resolution.Reason, source));
                    break;
                case ResolutionKind.Conflict:
                    conflictingPlayers++;
                    issues.Add(CreateIssue("player", "conflict", resolution.Reason, source));
                    break;
            }
        }

        int directFixtures = 0;
        int fallbackFixtures = 0;
        int unmatchedPredictions = 0;
        int conflictingPredictions = 0;
        var resolvedPredictions = new List<FplFormResolvedPrediction>();
        foreach (SourcePrediction prediction in predictions)
        {
            PlayerResolution player = playerResolutions[prediction.SourcePlayerId];
            FixtureResolution resolution = ResolveFixture(
                prediction,
                player,
                officialFixtures);
            switch (resolution.Kind)
            {
                case ResolutionKind.Direct:
                    directFixtures++;
                    resolvedPredictions.Add(CreateResolvedPrediction(
                        prediction,
                        player,
                        resolution));
                    break;
                case ResolutionKind.Fallback:
                    fallbackFixtures++;
                    resolvedPredictions.Add(CreateResolvedPrediction(
                        prediction,
                        player,
                        resolution));
                    break;
                case ResolutionKind.Unmatched:
                    unmatchedPredictions++;
                    issues.Add(CreateIssue(
                        "fixturePrediction",
                        "unmatched",
                        resolution.Reason,
                        prediction));
                    break;
                case ResolutionKind.Conflict:
                    conflictingPredictions++;
                    issues.Add(CreateIssue(
                        "fixturePrediction",
                        "conflict",
                        resolution.Reason,
                        prediction));
                    break;
            }
        }

        bool identitiesComplete =
            unmatchedPlayers == 0
            && conflictingPlayers == 0
            && unmatchedPredictions == 0
            && conflictingPredictions == 0;
        bool beforeDeadline = forecast.AvailableAtUtc <= official.DeadlineUtc;
        string status = !beforeDeadline
            ? ForecastAfterDeadline
            : identitiesComplete
                ? Complete
                : Incomplete;
        var document = new FplFormIdentityCoverageDocument(
                "1.0",
                status,
                beforeDeadline && identitiesComplete,
                forecast.CaptureId,
                forecast.ContentSha256,
                forecast.SeasonCode,
                forecast.Gameweek,
                forecast.AvailableAtUtc,
                official.CaptureId,
                official.AvailableAtUtc,
                official.BootstrapSha256,
                official.FixturesSha256,
                official.DeadlineUtc,
                playerResolutions.Count,
                directPlayers + fallbackPlayers,
                directPlayers,
                fallbackPlayers,
                unmatchedPlayers,
                conflictingPlayers,
                predictions.Count,
                directFixtures + fallbackFixtures,
                directFixtures,
                fallbackFixtures,
                unmatchedPredictions,
                conflictingPredictions,
                issues);
        return new(document, identitiesComplete ? resolvedPredictions : []);
    }

    private static FplFormResolvedPrediction CreateResolvedPrediction(
        SourcePrediction prediction,
        PlayerResolution player,
        FixtureResolution fixture) =>
        new(
            prediction.SourcePlayerId,
            prediction.FixtureId,
            player.Player!.PlayerId,
            fixture.Fixture!.FixtureId,
            prediction.Position,
            prediction.PredictedPoints,
            prediction.AppearanceProbability);

    private static PlayerResolution ResolvePlayer(
        IEnumerable<SourcePrediction> sourcePredictions,
        IReadOnlyList<OfficialPlayer> officialPlayers)
    {
        SourcePrediction source = sourcePredictions.First();
        if (sourcePredictions.Any(candidate =>
                Normalize(candidate.PlayerName) != Normalize(source.PlayerName)
                || Normalize(candidate.TeamName) != Normalize(source.TeamName)
                || candidate.Position != source.Position))
        {
            return new(ResolutionKind.Conflict, null, "source-player-attributes-vary");
        }

        OfficialPlayer? idMatch = officialPlayers.FirstOrDefault(
            player => player.PlayerId == source.SourcePlayerId);
        if (idMatch is not null)
        {
            return PlayerAttributesMatch(source, idMatch)
                ? new(ResolutionKind.Direct, idMatch, "source-player-id")
                : new(
                    ResolutionKind.Conflict,
                    null,
                    "source-player-id-attributes-disagree");
        }

        List<OfficialPlayer> attributeMatches = officialPlayers
            .Where(player => PlayerAttributesMatch(source, player))
            .ToList();
        return attributeMatches.Count switch
        {
            0 => new(ResolutionKind.Unmatched, null, "no-official-player-match"),
            1 => new(ResolutionKind.Fallback, attributeMatches[0], "unique-player-attributes"),
            _ => new(ResolutionKind.Conflict, null, "ambiguous-official-player-match"),
        };
    }

    private static FixtureResolution ResolveFixture(
        SourcePrediction source,
        PlayerResolution playerResolution,
        IReadOnlyList<OfficialFixture> officialFixtures)
    {
        if (playerResolution.Player is null)
        {
            return new(
                playerResolution.Kind == ResolutionKind.Conflict
                    ? ResolutionKind.Conflict
                    : ResolutionKind.Unmatched,
                null,
                "official-player-unresolved");
        }

        if (!TryParseLondonKickoff(source.KickoffLocal, out DateTimeOffset kickoffUtc))
        {
            return new(
                ResolutionKind.Conflict,
                null,
                "invalid-or-ambiguous-local-kickoff");
        }

        OfficialFixture? idMatch = officialFixtures.FirstOrDefault(
            fixture => fixture.FixtureId == source.FixtureId);
        if (idMatch is not null)
        {
            return FixtureAttributesMatch(idMatch, playerResolution.Player.TeamId, kickoffUtc)
                ? new(ResolutionKind.Direct, idMatch, "source-fixture-id")
                : new(
                    ResolutionKind.Conflict,
                    null,
                    "source-fixture-id-attributes-disagree");
        }

        List<OfficialFixture> attributeMatches = officialFixtures
            .Where(fixture => FixtureAttributesMatch(
                fixture,
                playerResolution.Player.TeamId,
                kickoffUtc))
            .ToList();
        return attributeMatches.Count switch
        {
            0 => new(ResolutionKind.Unmatched, null, "no-official-fixture-match"),
            1 => new(
                ResolutionKind.Fallback,
                attributeMatches[0],
                "unique-team-and-kickoff"),
            _ => new(
                ResolutionKind.Conflict,
                null,
                "ambiguous-official-fixture-match"),
        };
    }

    private static bool PlayerAttributesMatch(
        SourcePrediction source,
        OfficialPlayer official)
    {
        string sourceName = Normalize(source.PlayerName);
        return source.Position == official.Position
            && (Normalize(source.TeamName) == Normalize(official.TeamName)
                || Normalize(source.TeamName) == Normalize(official.TeamShortName))
            && (sourceName == Normalize(official.FullName)
                || sourceName == Normalize(official.WebName));
    }

    private static bool FixtureAttributesMatch(
        OfficialFixture fixture,
        int playerTeamId,
        DateTimeOffset kickoffUtc) =>
        (fixture.HomeTeamId == playerTeamId || fixture.AwayTeamId == playerTeamId)
        && fixture.KickoffUtc == kickoffUtc;

    private static bool TryParseLondonKickoff(
        string value,
        out DateTimeOffset kickoffUtc)
    {
        kickoffUtc = default;
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime local))
        {
            return false;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (London.IsInvalidTime(local) || London.IsAmbiguousTime(local))
        {
            return false;
        }

        kickoffUtc = TimeZoneInfo.ConvertTimeToUtc(local, London);
        return true;
    }

    private static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        bool pendingSpace = false;
        foreach (char character in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && result.Length > 0)
                {
                    result.Append(' ');
                }

                result.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return result.ToString();
    }

    private static FplFormIdentityCoverageIssueDocument CreateIssue(
        string entityType,
        string kind,
        string reason,
        SourcePrediction prediction) =>
        new(
            entityType,
            kind,
            reason,
            prediction.SourcePlayerId,
            entityType == "player" ? null : prediction.FixtureId,
            prediction.PlayerName);

    private static FplFormIdentityCoverageDocument CreateUnavailableDocument(
        ForecastCapture forecast,
        IReadOnlyList<SourcePrediction> predictions)
    {
        List<SourcePrediction> players = predictions
            .GroupBy(prediction => prediction.SourcePlayerId)
            .Select(group => group.First())
            .ToList();
        List<FplFormIdentityCoverageIssueDocument> issues =
        [
            .. players.Select(player => CreateIssue(
                "player",
                "unmatched",
                CatalogueUnavailable,
                player)),
            .. predictions.Select(prediction => CreateIssue(
                "fixturePrediction",
                "unmatched",
                CatalogueUnavailable,
                prediction)),
        ];
        return new(
            "1.0",
            CatalogueUnavailable,
            false,
            forecast.CaptureId,
            forecast.ContentSha256,
            forecast.SeasonCode,
            forecast.Gameweek,
            forecast.AvailableAtUtc,
            null,
            null,
            null,
            null,
            null,
            players.Count,
            0,
            0,
            0,
            players.Count,
            0,
            predictions.Count,
            0,
            0,
            0,
            predictions.Count,
            0,
            issues);
    }

    private static async Task<ForecastCapture?> ReadForecastAsync(
        SqliteConnection connection,
        long captureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                capture_id,
                season_code,
                gameweek,
                available_at_utc,
                content_sha256
            FROM fpl_form_forecast_captures
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                ParseTimestamp(reader.GetString(3)),
                reader.GetString(4))
            : null;
    }

    private static async Task<List<SourcePrediction>> ReadPredictionsAsync(
        SqliteConnection connection,
        long captureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                source_player_id,
                fixture_id,
                player_name,
                team_name,
                position,
                kickoff_local,
                predicted_points,
                appearance_probability
            FROM fpl_form_fixture_predictions
            WHERE capture_id = $captureId
            ORDER BY source_player_id, fixture_id;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var predictions = new List<SourcePrediction>();
        while (await reader.ReadAsync(cancellationToken))
        {
            predictions.Add(
                new(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                    reader.IsDBNull(7)
                        ? null
                        : decimal.Parse(
                            reader.GetString(7),
                            CultureInfo.InvariantCulture)));
        }

        return predictions;
    }

    private static async Task<OfficialCapture?> ReadOfficialCaptureAsync(
        SqliteConnection connection,
        ForecastCapture forecast,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                capture.capture_id,
                capture.available_at_utc,
                capture.bootstrap_sha256,
                capture.fixtures_sha256,
                event.deadline_utc
            FROM official_fpl_captures AS capture
            INNER JOIN official_fpl_events AS event
                ON event.capture_id = capture.capture_id
                AND event.event_id = $gameweek
            WHERE capture.season_code = $seasonCode
                AND julianday(capture.available_at_utc)
                    <= julianday($forecastAvailableAtUtc)
                AND julianday(capture.available_at_utc)
                    <= julianday(event.deadline_utc)
            ORDER BY julianday(capture.available_at_utc) DESC, capture.capture_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$gameweek", forecast.Gameweek);
        command.Parameters.AddWithValue("$seasonCode", forecast.SeasonCode);
        command.Parameters.AddWithValue(
            "$forecastAvailableAtUtc",
            forecast.AvailableAtUtc.ToString("O", CultureInfo.InvariantCulture));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetInt64(0),
                ParseTimestamp(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                ParseTimestamp(reader.GetString(4)))
            : null;
    }

    private static async Task<List<OfficialPlayer>> ReadOfficialPlayersAsync(
        SqliteConnection connection,
        long captureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                player.player_id,
                player.team_id,
                player.position,
                player.first_name,
                player.second_name,
                player.web_name,
                team.name,
                team.short_name
            FROM official_fpl_players AS player
            INNER JOIN official_fpl_teams AS team
                ON team.capture_id = player.capture_id
                AND team.team_id = player.team_id
            WHERE player.capture_id = $captureId
            ORDER BY player.player_id;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var players = new List<OfficialPlayer>();
        while (await reader.ReadAsync(cancellationToken))
        {
            players.Add(
                new(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    $"{reader.GetString(3)} {reader.GetString(4)}",
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7)));
        }

        return players;
    }

    private static async Task<List<OfficialFixture>> ReadOfficialFixturesAsync(
        SqliteConnection connection,
        long captureId,
        int gameweek,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT fixture_id, home_team_id, away_team_id, kickoff_utc
            FROM official_fpl_fixtures
            WHERE capture_id = $captureId
                AND event_id = $gameweek
                AND kickoff_utc IS NOT NULL
            ORDER BY fixture_id;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$gameweek", gameweek);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var fixtures = new List<OfficialFixture>();
        while (await reader.ReadAsync(cancellationToken))
        {
            fixtures.Add(
                new(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    ParseTimestamp(reader.GetString(3))));
        }

        return fixtures;
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private sealed record ForecastCapture(
        long CaptureId,
        string SeasonCode,
        int Gameweek,
        DateTimeOffset AvailableAtUtc,
        string ContentSha256);

    private sealed record SourcePrediction(
        int SourcePlayerId,
        int FixtureId,
        string PlayerName,
        string TeamName,
        string Position,
        string KickoffLocal,
        decimal PredictedPoints,
        decimal? AppearanceProbability);

    private sealed record OfficialCapture(
        long CaptureId,
        DateTimeOffset AvailableAtUtc,
        string BootstrapSha256,
        string FixturesSha256,
        DateTimeOffset DeadlineUtc);

    private sealed record OfficialPlayer(
        int PlayerId,
        int TeamId,
        string Position,
        string FullName,
        string WebName,
        string TeamName,
        string TeamShortName);

    private sealed record OfficialFixture(
        int FixtureId,
        int HomeTeamId,
        int AwayTeamId,
        DateTimeOffset KickoffUtc);

    private sealed record PlayerResolution(
        ResolutionKind Kind,
        OfficialPlayer? Player,
        string Reason);

    private sealed record FixtureResolution(
        ResolutionKind Kind,
        OfficialFixture? Fixture,
        string Reason);

    private enum ResolutionKind
    {
        Direct,
        Fallback,
        Unmatched,
        Conflict,
    }
}

internal sealed record FplFormIdentityResolution(
    FplFormIdentityCoverageDocument Document,
    IReadOnlyList<FplFormResolvedPrediction> Predictions);

internal sealed record FplFormResolvedPrediction(
    int SourcePlayerId,
    int SourceFixtureId,
    int OfficialPlayerId,
    int OfficialFixtureId,
    string Position,
    decimal PredictedPoints,
    decimal? AppearanceProbability);
