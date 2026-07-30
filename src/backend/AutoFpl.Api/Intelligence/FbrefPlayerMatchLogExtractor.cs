using System.Globalization;

using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefPlayerMatchLogExtractor
{
    public const string ExtractionVersion = "fbref-player-match-log/v1";

    private const int MaximumMatchRows = 80;

    private readonly ResearchSourceSnapshotStore _snapshotStore;
    private readonly FbrefPlayingTimeExtractor _playingTimeExtractor;

    public FbrefPlayerMatchLogExtractor(
        ResearchSourceSnapshotStore snapshotStore,
        FbrefPlayingTimeExtractor playingTimeExtractor)
    {
        _snapshotStore = snapshotStore
            ?? throw new ArgumentNullException(nameof(snapshotStore));
        _playingTimeExtractor = playingTimeExtractor
            ?? throw new ArgumentNullException(nameof(playingTimeExtractor));
    }

    public async Task<FbrefPlayerMatchLogDocument?> GetAsync(
        long snapshotId,
        CancellationToken cancellationToken = default)
    {
        FbrefPlayingTimeDocument playingTime =
            await _playingTimeExtractor.GetAsync(
                FbrefPlayerIdentityBridge.ReviewedSnapshotId,
                cancellationToken)
            ?? throw Invalid(
                "The reviewed FBref playing-time snapshot is not available.");
        return await GetAsync(snapshotId, playingTime, cancellationToken);
    }

    internal async Task<FbrefPlayerMatchLogDocument?> GetAsync(
        long snapshotId,
        FbrefPlayingTimeDocument playingTime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playingTime);
        ResearchSourceSnapshotContent? retained =
            await _snapshotStore.ReadContentAsync(snapshotId, cancellationToken);
        if (retained is null)
        {
            return null;
        }

        ResearchSourceSnapshotDocument snapshot = retained.Snapshot;
        if (!FbrefPlayerMatchLogImporter.IsMatchLogSourceKey(
                snapshot.SourceKey)
            || !StringComparer.Ordinal.Equals(snapshot.Status, "shadow-only")
            || !snapshot.IsPreDeadline
            || !StringComparer.Ordinal.Equals(
                snapshot.TransportKey,
                ByparrClient.TransportKey))
        {
            throw Invalid(
                $"Snapshot {snapshotId} is not a supported pre-deadline FBref match log.");
        }

        string sourcePlayerId = snapshot.SourceKey.Substring(
            FbrefPlayerMatchLogImporter.SourceKeyPrefix.Length,
            8);
        FbrefPlayingTimePlayerDocument[] reviewedPlayers = playingTime.Players
            .Where(player =>
                StringComparer.Ordinal.Equals(
                    player.SourcePlayerId,
                    sourcePlayerId)
                && StringComparer.Ordinal.Equals(
                    player.IdentityStatus,
                    "reviewed-v1"))
            .ToArray();
        if (reviewedPlayers.Length != 1)
        {
            throw Invalid(
                "The FBref match-log snapshot is not linked to one reviewed player.");
        }
        FbrefPlayingTimePlayerDocument player = reviewedPlayers[0];
        ResearchSourceDefinition expected =
            FbrefPlayerMatchLogImporter.CreateSourceDefinition(player);
        if (!StringComparer.Ordinal.Equals(
                snapshot.SourceKey,
                expected.SourceKey)
            || !StringComparer.Ordinal.Equals(
                snapshot.CanonicalUrl,
                expected.CanonicalUri.AbsoluteUri)
            || !Uri.TryCreate(
                snapshot.FinalUrl,
                UriKind.Absolute,
                out Uri? finalUri)
            || !expected.AllowsFinalUri(finalUri)
            || player.OfficialPlayerCode is null)
        {
            throw Invalid(
                "The FBref match-log snapshot no longer matches its reviewed source.");
        }

        IReadOnlyList<ResearchOfficialPlayerIdentity> identities =
            await _snapshotStore.GetPlayerIdentitiesAsync(
                snapshot.IdentityCaptureId,
                cancellationToken);
        ResearchOfficialPlayerIdentity[] officialMatches = identities
            .Where(identity =>
                identity.PlayerCode == player.OfficialPlayerCode.Value)
            .ToArray();
        if (officialMatches.Length != 1)
        {
            throw Invalid(
                "The reviewed FBref player is not unique in the capture identity.");
        }

        FbrefPlayerMatchLogRowDocument[] matches = Parse(retained.Content)
            .Where(match => StringComparer.Ordinal.Equals(
                match.SourceTeamId,
                player.SourceTeamId))
            .ToArray();
        int appearanceCount =
            matches.Count(match => match.Minutes > 0);
        int startCount =
            matches.Count(match => match.Started);
        int minutes =
            matches.Sum(match => match.Minutes);
        if (matches.Length == 0)
        {
            throw Invalid(
                "The FBref match chronology contained no reviewed source-team rows.");
        }
        string aggregateReconciliationStatus =
            appearanceCount == player.Appearances
                && startCount == player.Starts
                && minutes == player.Minutes
            ? "exact"
            : "source-revision-mismatch";
        ResearchOfficialPlayerIdentity identity = officialMatches[0];
        return new(
            "1.0",
            snapshot.SnapshotId,
            snapshot.SourceKey,
            ExtractionVersion,
            FbrefPlayerIdentityBridge.Version,
            snapshot.ContentSha256,
            snapshot.IdentityCaptureId,
            snapshot.RetrievedAtUtc,
            sourcePlayerId,
            player.PlayerName,
            player.SourceTeamId,
            player.TeamName,
            identity.PlayerId,
            identity.PlayerCode,
            identity.TeamName,
            "2025-26",
            aggregateReconciliationStatus,
            player.Appearances,
            player.Starts,
            player.Minutes,
            matches.Length,
            appearanceCount,
            startCount,
            minutes,
            matches);
    }

    internal static IReadOnlyList<FbrefPlayerMatchLogRowDocument> Parse(
        string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        content = FbrefPlayingTimeExtractor.RemoveHtmlComments(content);
        string table = FbrefPlayingTimeExtractor.FindUniqueElement(
            content,
            "table",
            "id",
            "matchlogs_all");
        string body = FbrefPlayingTimeExtractor.FindUniqueElement(
            table,
            "tbody");

        var rows = new List<FbrefPlayerMatchLogRowDocument>();
        var sourceMatchIds = new HashSet<string>(StringComparer.Ordinal);
        DateOnly? previousDate = null;
        int position = 0;
        while (true)
        {
            int rowStart = body.IndexOf("<tr", position, StringComparison.Ordinal);
            if (rowStart < 0)
            {
                break;
            }
            int openingEnd = FbrefPlayingTimeExtractor.FindOpeningTagEnd(
                body,
                rowStart,
                "tr");
            int rowEnd = body.IndexOf(
                "</tr>",
                openingEnd + 1,
                StringComparison.Ordinal);
            if (rowEnd < 0)
            {
                throw Invalid("The FBref match-log table contained an unterminated row.");
            }
            string row = body[(openingEnd + 1)..rowEnd];
            position = rowEnd + "</tr>".Length;

            FbrefPlayingTimeExtractor.HtmlCell? dateCell =
                FbrefPlayingTimeExtractor.FindCell(row, "date");
            if (dateCell is null)
            {
                continue;
            }
            string dateText =
                FbrefPlayingTimeExtractor.ReadText(dateCell.Content);
            if (dateText.Length == 0)
            {
                continue;
            }
            if (StringComparer.Ordinal.Equals(dateText, "Date"))
            {
                continue;
            }
            if (!DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly matchDate)
                || previousDate is not null
                    && matchDate < previousDate.Value)
            {
                throw Invalid(
                    "The FBref match-log table contained an invalid match sequence.");
            }
            previousDate = matchDate;

            FbrefPlayingTimeExtractor.HtmlCell teamCell =
                FbrefPlayingTimeExtractor.RequireCell(row, "team");
            FbrefPlayingTimeExtractor.HtmlCell opponentCell =
                FbrefPlayingTimeExtractor.RequireCell(row, "opponent");
            string? teamHref =
                FbrefPlayingTimeExtractor.ReadHref(teamCell.Content);
            string? opponentHref =
                FbrefPlayingTimeExtractor.ReadHref(opponentCell.Content);
            FbrefPlayingTimeExtractor.HtmlCell reportCell =
                FbrefPlayingTimeExtractor.RequireCell(row, "match_report");
            string? reportHref =
                FbrefPlayingTimeExtractor.ReadHref(reportCell.Content);
            if (teamHref is null || opponentHref is null || reportHref is null)
            {
                throw Invalid(
                    "A dated FBref match-log row lacked stable resource links.");
            }

            string sourceMatchId =
                FbrefPlayingTimeExtractor.ParseResourceId(
                    reportHref,
                    "matches");
            if (!sourceMatchIds.Add(sourceMatchId))
            {
                throw Invalid(
                    "The FBref match-log table contained a duplicate match.");
            }

            string startedText = ReadCellText(row, "game_started");
            bool started = startedText switch
            {
                "Y" or "Y*" => true,
                "N" => false,
                _ => throw Invalid(
                    "An FBref match-log row contained an invalid start value."),
            };
            FbrefPlayingTimeExtractor.HtmlCell? minutesCell =
                FbrefPlayingTimeExtractor.FindCell(row, "minutes");
            FbrefPlayingTimeExtractor.HtmlCell? benchCell =
                FbrefPlayingTimeExtractor.FindCell(row, "bench_explain");
            int minutes;
            if (minutesCell is not null)
            {
                minutes = FbrefPlayingTimeExtractor.ParseCount(
                    FbrefPlayingTimeExtractor.ReadText(minutesCell.Content),
                    "minutes",
                    allowEmpty: false);
            }
            else if (!started
                && benchCell is not null
                && StringComparer.Ordinal.Equals(
                    FbrefPlayingTimeExtractor.ReadText(benchCell.Content),
                    "On matchday squad, but did not play"))
            {
                minutes = 0;
            }
            else
            {
                throw Invalid(
                    "An FBref match-log row lacked supported minutes evidence.");
            }

            string venue = ReadCellText(row, "venue");
            string result = ReadCellText(row, "result");
            string competition = ReadCellText(row, "comp");
            string round = ReadCellText(row, "round");
            string teamName =
                FbrefPlayingTimeExtractor.ReadText(teamCell.Content);
            string opponentName =
                FbrefPlayingTimeExtractor.ReadText(opponentCell.Content);
            int goals = ReadOptionalCount(row, "goals");
            int assists = ReadOptionalCount(row, "assists");
            int yellowCards = ReadOptionalCount(row, "cards_yellow");
            int redCards = ReadOptionalCount(row, "cards_red");
            if (venue is not ("Home" or "Away" or "Neutral")
                || competition.Length is < 1 or > 100
                || round.Length is < 1 or > 100
                || result.Length is < 3 or > 20
                || teamName.Length is < 1 or > 100
                || opponentName.Length is < 1 or > 100
                || minutes is < 0 or > 120
                || started && minutes == 0
                || goals is < 0 or > 10
                || assists is < 0 or > 10
                || yellowCards is < 0 or > 2
                || redCards is < 0 or > 2)
            {
                throw Invalid(
                    "An FBref match-log row contained unsupported values.");
            }

            rows.Add(
                new(
                    sourceMatchId,
                    matchDate,
                    competition,
                    round,
                    venue.ToLowerInvariant(),
                    result,
                    FbrefPlayingTimeExtractor.ParseResourceId(
                        teamHref,
                        "squads"),
                    teamName,
                    FbrefPlayingTimeExtractor.ParseResourceId(
                        opponentHref,
                        "squads"),
                    opponentName,
                    started,
                    minutes,
                    goals,
                    assists,
                    yellowCards,
                    redCards));
        }

        if (rows.Count is < 1 or > MaximumMatchRows)
        {
            throw Invalid(
                "The FBref match-log table contained an unexpected row count.");
        }
        return rows;
    }

    private static string ReadCellText(string row, string dataStat) =>
        FbrefPlayingTimeExtractor.ReadText(
            FbrefPlayingTimeExtractor.RequireCell(row, dataStat).Content);

    private static int ReadOptionalCount(string row, string dataStat)
    {
        FbrefPlayingTimeExtractor.HtmlCell? cell =
            FbrefPlayingTimeExtractor.FindCell(row, dataStat);
        return cell is null
            ? 0
            : FbrefPlayingTimeExtractor.ParseCount(
                FbrefPlayingTimeExtractor.ReadText(cell.Content),
                dataStat,
                allowEmpty: true);
    }

    private static ResearchSourceSnapshotException Invalid(string message) =>
        new(message);
}
