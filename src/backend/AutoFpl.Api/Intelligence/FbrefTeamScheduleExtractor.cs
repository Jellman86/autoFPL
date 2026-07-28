using System.Globalization;

using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefTeamScheduleExtractor
{
    public const string ExtractionVersion = "fbref-team-schedule/v1";

    private const int MinimumMatchRows = 46;
    private const int MaximumMatchRows = 55;

    private readonly ResearchSourceSnapshotStore _snapshotStore;

    public FbrefTeamScheduleExtractor(
        ResearchSourceSnapshotStore snapshotStore)
    {
        _snapshotStore = snapshotStore
            ?? throw new ArgumentNullException(nameof(snapshotStore));
    }

    public async Task<FbrefTeamScheduleDocument?> GetAsync(
        long snapshotId,
        CancellationToken cancellationToken = default)
    {
        ResearchSourceSnapshotContent? retained =
            await _snapshotStore.ReadContentAsync(snapshotId, cancellationToken);
        if (retained is null)
        {
            return null;
        }

        ResearchSourceSnapshotDocument snapshot = retained.Snapshot;
        FbrefTeamScheduleSource source =
            FbrefTeamScheduleSources.Get(snapshot.SourceKey);
        ResearchSourceDefinition expected = source.Definition;
        if (!StringComparer.Ordinal.Equals(snapshot.Status, "shadow-only")
            || !snapshot.IsPreDeadline
            || !StringComparer.Ordinal.Equals(
                snapshot.TransportKey,
                ByparrClient.TransportKey)
            || !StringComparer.Ordinal.Equals(
                snapshot.CanonicalUrl,
                expected.CanonicalUri.AbsoluteUri)
            || !Uri.TryCreate(
                snapshot.FinalUrl,
                UriKind.Absolute,
                out Uri? finalUri)
            || !expected.AllowsFinalUri(finalUri))
        {
            throw Invalid(
                $"Snapshot {snapshotId} is not a supported pre-deadline FBref team schedule.");
        }

        IReadOnlyList<FbrefTeamScheduleRowDocument> matches =
            Parse(retained.Content);
        return new(
            "1.0",
            snapshot.SnapshotId,
            snapshot.SourceKey,
            ExtractionVersion,
            snapshot.ContentSha256,
            snapshot.IdentityCaptureId,
            snapshot.RetrievedAtUtc,
            source.SourceTeamId,
            source.TeamName,
            "Championship",
            "2025-26",
            matches.Count,
            matches);
    }

    internal static IReadOnlyList<FbrefTeamScheduleRowDocument> Parse(
        string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        content = FbrefPlayingTimeExtractor.RemoveHtmlComments(content);
        string table = FbrefPlayingTimeExtractor.FindUniqueElement(
            content,
            "table",
            "id",
            "matchlogs_for");
        string body = FbrefPlayingTimeExtractor.FindUniqueElement(
            table,
            "tbody");

        var matches = new List<FbrefTeamScheduleRowDocument>();
        var sourceMatchIds = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset? previousKickoff = null;
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
                throw Invalid(
                    "The FBref team-schedule table contained an unterminated row.");
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
            if (!DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly matchDate))
            {
                continue;
            }

            string? matchHref =
                FbrefPlayingTimeExtractor.ReadHref(dateCell.Content);
            if (matchHref is null)
            {
                throw Invalid(
                    "A dated FBref team-schedule row lacked a stable match link.");
            }
            string sourceMatchId =
                FbrefPlayingTimeExtractor.ParseResourceId(
                    matchHref,
                    "matches");
            FbrefPlayingTimeExtractor.HtmlCell reportCell =
                FbrefPlayingTimeExtractor.RequireCell(row, "match_report");
            string? reportHref =
                FbrefPlayingTimeExtractor.ReadHref(reportCell.Content);
            if (reportHref is null
                || !StringComparer.Ordinal.Equals(
                    sourceMatchId,
                    FbrefPlayingTimeExtractor.ParseResourceId(
                        reportHref,
                        "matches")))
            {
                throw Invalid(
                    "A dated FBref team-schedule row had inconsistent match links.");
            }
            if (!sourceMatchIds.Add(sourceMatchId))
            {
                throw Invalid(
                    "The FBref team schedule contained a duplicate match.");
            }

            FbrefPlayingTimeExtractor.HtmlCell kickoffCell =
                FbrefPlayingTimeExtractor.RequireCell(row, "start_time");
            DateTimeOffset kickoffUtc =
                ParseKickoffUtc(kickoffCell.Content);
            if (previousKickoff is not null
                && kickoffUtc <= previousKickoff.Value)
            {
                throw Invalid(
                    "The FBref team schedule contained an invalid match sequence.");
            }
            previousKickoff = kickoffUtc;

            FbrefPlayingTimeExtractor.HtmlCell opponentCell =
                FbrefPlayingTimeExtractor.RequireCell(row, "opponent");
            string? opponentHref =
                FbrefPlayingTimeExtractor.ReadHref(opponentCell.Content);
            if (opponentHref is null)
            {
                throw Invalid(
                    "A dated FBref team-schedule row lacked a stable opponent link.");
            }
            string venue = ReadCellText(row, "venue");
            string result = ReadCellText(row, "result");
            string round = ReadCellText(row, "round");
            string opponentName =
                FbrefPlayingTimeExtractor.ReadText(opponentCell.Content);
            int goalsFor = ReadCount(row, "goals_for");
            int goalsAgainst = ReadCount(row, "goals_against");
            DateOnly kickoffDate =
                DateOnly.FromDateTime(kickoffUtc.UtcDateTime);
            if (Math.Abs(matchDate.DayNumber - kickoffDate.DayNumber) > 1
                || venue is not ("Home" or "Away" or "Neutral")
                || result is not ("W" or "D" or "L")
                || round.Length is < 1 or > 100
                || opponentName.Length is < 1 or > 100
                || goalsFor is < 0 or > 20
                || goalsAgainst is < 0 or > 20)
            {
                throw Invalid(
                    "An FBref team-schedule row contained unsupported values.");
            }

            matches.Add(
                new(
                    sourceMatchId,
                    matchDate,
                    kickoffUtc,
                    round,
                    venue.ToLowerInvariant(),
                    result,
                    goalsFor,
                    goalsAgainst,
                    FbrefPlayingTimeExtractor.ParseResourceId(
                        opponentHref,
                        "squads"),
                    opponentName));
        }

        if (matches.Count is < MinimumMatchRows or > MaximumMatchRows)
        {
            throw Invalid(
                "The FBref team schedule contained an unexpected row count.");
        }
        return matches;
    }

    private static DateTimeOffset ParseKickoffUtc(string content)
    {
        int spanStart = content.IndexOf("<span", StringComparison.Ordinal);
        if (spanStart < 0)
        {
            throw Invalid(
                "An FBref team-schedule row lacked an exact kickoff.");
        }
        int spanEnd = FbrefPlayingTimeExtractor.FindOpeningTagEnd(
            content,
            spanStart,
            "span");
        string openingTag = content[spanStart..(spanEnd + 1)];
        string? epochText = FbrefPlayingTimeExtractor.ReadAttribute(
            openingTag,
            "data-venue-epoch");
        if (!long.TryParse(
                epochText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long epoch)
            || epoch is < 1_700_000_000 or > 1_900_000_000)
        {
            throw Invalid(
                "An FBref team-schedule row contained an invalid kickoff.");
        }
        return DateTimeOffset.FromUnixTimeSeconds(epoch);
    }

    private static string ReadCellText(string row, string dataStat) =>
        FbrefPlayingTimeExtractor.ReadText(
            FbrefPlayingTimeExtractor.RequireCell(row, dataStat).Content);

    private static int ReadCount(string row, string dataStat) =>
        FbrefPlayingTimeExtractor.ParseCount(
            ReadCellText(row, dataStat),
            dataStat,
            allowEmpty: false);

    private static ResearchSourceSnapshotException Invalid(string message) =>
        new(message);
}
