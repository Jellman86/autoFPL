using System.Globalization;
using System.Net;
using System.Text;

using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefPlayingTimeExtractor
{
    public const string SourceKey =
        FbrefPlayingTimeSources.SourceKeyPrefix
        + FbrefPlayingTimeSources.CurrentCompetitionSeason;
    public const string ExtractionVersion =
        "fbref-championship-playing-time/v1";

    private const int MinimumPlayerRows = 500;
    private const int MaximumPlayerRows = 1200;
    private const int MinimumTargetTeamRows = 15;

    private static readonly string[] TargetTeamNames =
    [
        "Coventry City",
        "Hull City",
        "Ipswich Town",
    ];

    private readonly ResearchSourceSnapshotStore _snapshotStore;

    public FbrefPlayingTimeExtractor(ResearchSourceSnapshotStore snapshotStore)
    {
        _snapshotStore =
            snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
    }

    public async Task<FbrefPlayingTimeDocument?> GetAsync(
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
        FbrefPlayingTimeSource source =
            FbrefPlayingTimeSources.Get(snapshot.SourceKey);
        if (!StringComparer.Ordinal.Equals(
                snapshot.SourceClass,
                source.Definition.SourceClass)
            || !StringComparer.Ordinal.Equals(
                snapshot.CanonicalUrl,
                source.Definition.CanonicalUri.AbsoluteUri)
            || !Uri.TryCreate(
                snapshot.FinalUrl,
                UriKind.Absolute,
                out Uri? finalUri)
            || !source.Definition.AllowsFinalUri(finalUri))
        {
            throw Invalid(
                $"Snapshot {snapshotId} no longer matches its registered "
                + "FBref playing-time source.");
        }
        if (!StringComparer.Ordinal.Equals(snapshot.Status, "shadow-only")
            || !snapshot.IsPreDeadline)
        {
            throw Invalid(
                "Only pre-deadline shadow FBref snapshots can produce playing-time evidence.");
        }

        IReadOnlyList<FbrefPlayingTimeRow> rows =
            Parse(retained.Content, source);
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities =
            source.IsCurrentTargetSource
                ? await _snapshotStore.GetPlayerIdentitiesAsync(
                    snapshot.IdentityCaptureId,
                    cancellationToken)
                : [];
        return BuildDocument(snapshot, source, rows, identities);
    }

    internal static IReadOnlyList<FbrefPlayingTimeRow> Parse(string content) =>
        Parse(content, FbrefPlayingTimeSources.Current);

    internal static IReadOnlyList<FbrefPlayingTimeRow> Parse(
        string content,
        FbrefPlayingTimeSource source)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(source);
        content = RemoveHtmlComments(content);
        string table = FindUniqueElement(
            content,
            "table",
            "id",
            "stats_playing_time");
        string body = FindUniqueElement(table, "tbody");

        var rows = new List<FbrefPlayingTimeRow>();
        var sourceTeamPairs = new HashSet<string>(StringComparer.Ordinal);
        int position = 0;
        while (true)
        {
            int rowStart = body.IndexOf("<tr", position, StringComparison.Ordinal);
            if (rowStart < 0)
            {
                break;
            }
            int openingEnd = FindOpeningTagEnd(body, rowStart, "tr");
            int rowEnd = body.IndexOf(
                "</tr>",
                openingEnd + 1,
                StringComparison.Ordinal);
            if (rowEnd < 0)
            {
                throw Invalid("The FBref playing-time table contained an unterminated row.");
            }

            string row = body[(openingEnd + 1)..rowEnd];
            position = rowEnd + "</tr>".Length;
            HtmlCell? playerCell = FindCell(row, "player");
            if (playerCell is null)
            {
                continue;
            }

            string playerName = ReadText(playerCell.Content);
            string? playerHref = ReadHref(playerCell.Content);
            if (playerHref is null)
            {
                if (StringComparer.Ordinal.Equals(playerName, "Player"))
                {
                    continue;
                }
                throw Invalid("An FBref player row did not contain a stable player link.");
            }

            HtmlCell teamCell = RequireCell(row, "team");
            HtmlCell gamesCell = RequireCell(row, "games");
            HtmlCell startsCell = RequireCell(row, "games_starts");
            HtmlCell minutesCell = RequireCell(row, "minutes");
            HtmlCell matchesCell = RequireCell(row, "matches");
            string? teamHref = ReadHref(teamCell.Content);
            string? matchesHref = ReadHref(matchesCell.Content);
            if (teamHref is null || matchesHref is null)
            {
                throw Invalid(
                    "An FBref player row did not contain stable team and match-log links.");
            }

            string sourcePlayerId = ParseResourceId(
                playerHref,
                "players");
            string sourceTeamId = ParseResourceId(
                teamHref,
                "squads");
            if (!matchesHref.StartsWith(
                    $"/en/players/{sourcePlayerId}/matchlogs/"
                    + $"{source.FbrefSeason}/summary/",
                    StringComparison.Ordinal)
                || matchesHref.Contains('?', StringComparison.Ordinal)
                || matchesHref.Contains('#', StringComparison.Ordinal))
            {
                throw Invalid(
                    "An FBref player row contained an unexpected match-log resource.");
            }

            string teamName = ReadText(teamCell.Content);
            int appearances = ParseCount(
                ReadText(gamesCell.Content),
                "appearances",
                allowEmpty: false);
            int starts = ParseCount(
                ReadText(startsCell.Content),
                "starts",
                allowEmpty: false);
            int minutes = ParseCount(
                ReadText(minutesCell.Content),
                "minutes",
                allowEmpty: true);
            if (playerName.Length is < 1 or > 100
                || teamName.Length is < 1 or > 100
                || appearances is < 0 or > 46
                || starts < 0
                || starts > appearances
                || minutes is < 0 or > 6000
                || (appearances == 0 && minutes != 0))
            {
                throw Invalid(
                    "An FBref player row contained unsupported playing-time values.");
            }

            string sourceTeamKey = $"{sourcePlayerId}|{sourceTeamId}";
            if (!sourceTeamPairs.Add(sourceTeamKey))
            {
                throw Invalid(
                    "The FBref playing-time table contained a duplicate player-team row.");
            }

            rows.Add(
                new(
                    sourcePlayerId,
                    playerName,
                    sourceTeamId,
                    teamName,
                    appearances,
                    starts,
                    minutes,
                    $"https://fbref.com{matchesHref}"));
        }

        if (rows.Count is < MinimumPlayerRows or > MaximumPlayerRows
            || rows.Select(row => row.SourcePlayerId).Distinct().Count()
                < MinimumPlayerRows - 100)
        {
            throw Invalid(
                "The FBref playing-time table did not contain the expected bounded player population.");
        }
        foreach (string teamName in source.IsCurrentTargetSource
            ? TargetTeamNames
            : [])
        {
            if (rows.Count(row => StringComparer.Ordinal.Equals(
                    row.TeamName,
                    teamName)) < MinimumTargetTeamRows)
            {
                throw Invalid(
                    $"The FBref playing-time table did not contain enough {teamName} rows.");
            }
        }

        return rows;
    }

    internal static string RemoveHtmlComments(string content)
    {
        var output = new StringBuilder(content.Length);
        int position = 0;
        while (true)
        {
            int commentStart = content.IndexOf(
                "<!--",
                position,
                StringComparison.Ordinal);
            if (commentStart < 0)
            {
                output.Append(content, position, content.Length - position);
                return output.ToString();
            }
            output.Append(content, position, commentStart - position);
            int commentEnd = content.IndexOf(
                "-->",
                commentStart + 4,
                StringComparison.Ordinal);
            if (commentEnd < 0)
            {
                throw Invalid("The FBref page contained an unterminated HTML comment.");
            }
            position = commentEnd + 3;
        }
    }

    internal static FbrefPlayingTimeDocument BuildDocument(
        ResearchSourceSnapshotDocument snapshot,
        IReadOnlyList<FbrefPlayingTimeRow> rows,
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities) =>
        BuildDocument(
            snapshot,
            FbrefPlayingTimeSources.Current,
            rows,
            identities);

    internal static FbrefPlayingTimeDocument BuildDocument(
        ResearchSourceSnapshotDocument snapshot,
        FbrefPlayingTimeSource source,
        IReadOnlyList<FbrefPlayingTimeRow> rows,
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(identities);

        HashSet<string> targetTeams = source.IsCurrentTargetSource
            ? TargetTeamNames.ToHashSet(StringComparer.Ordinal)
            : [];
        Dictionary<(string TeamName, string PlayerName),
            ResearchOfficialPlayerIdentity[]> identitiesByTeamAndName =
            identities
                .Where(identity => targetTeams.Contains(identity.TeamName))
                .GroupBy(
                    identity => (
                        identity.TeamName,
                        Normalize(
                            $"{identity.FirstName} {identity.SecondName}")))
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray());
        IReadOnlyDictionary<int, ResearchOfficialPlayerIdentity>
            identitiesByCode = identities.ToDictionary(
                identity => identity.PlayerCode);
        bool reviewedBridgeApplies = source.IsCurrentTargetSource
            && FbrefPlayerIdentityBridge.AppliesTo(snapshot);
        var matchedOfficialPlayers =
            new HashSet<(string TeamName, int PlayerCode)>();
        var players = new List<FbrefPlayingTimePlayerDocument>(rows.Count);
        foreach (FbrefPlayingTimeRow row in rows)
        {
            ResearchOfficialPlayerIdentity? identity = null;
            string identityStatus = "not-in-scope";
            if (targetTeams.Contains(row.TeamName))
            {
                identityStatus = "unresolved";
                FbrefReviewedPlayerIdentity? reviewed = reviewedBridgeApplies
                    ? FbrefPlayerIdentityBridge.Find(
                        row.SourcePlayerId,
                        row.SourceTeamId)
                    : null;
                if (reviewed is not null)
                {
                    if (!StringComparer.Ordinal.Equals(
                            reviewed.SourcePlayerName,
                            row.PlayerName)
                        || !StringComparer.Ordinal.Equals(
                            reviewed.TeamName,
                            row.TeamName)
                        || !identitiesByCode.TryGetValue(
                            reviewed.OfficialPlayerCode,
                            out identity)
                        || !StringComparer.Ordinal.Equals(
                            identity.TeamName,
                            reviewed.TeamName)
                        || !StringComparer.Ordinal.Equals(
                            Normalize(
                                $"{identity.FirstName} {identity.SecondName}"),
                            Normalize(reviewed.SourcePlayerName)))
                    {
                        throw Invalid(
                            "A reviewed FBref identity no longer matches its source and official records.");
                    }
                    identityStatus = "reviewed-v1";
                }
                else if (identitiesByTeamAndName.TryGetValue(
                        (row.TeamName, Normalize(row.PlayerName)),
                        out ResearchOfficialPlayerIdentity[]? matches)
                    && matches.Length == 1)
                {
                    identity = matches[0];
                    identityStatus = "exact-current-team-proposal";
                }
                if (identity is not null
                    && !matchedOfficialPlayers.Add(
                        (identity.TeamName, identity.PlayerCode)))
                {
                    throw Invalid(
                        "Two FBref rows resolved to the same current official player.");
                }
            }

            players.Add(
                new(
                    row.SourcePlayerId,
                    row.PlayerName,
                    row.SourceTeamId,
                    row.TeamName,
                    row.Appearances,
                    row.Starts,
                    row.Minutes,
                    row.MatchLogsUrl,
                    identityStatus,
                    identity?.PlayerId,
                    identity?.PlayerCode));
        }

        var coverage = new List<FbrefPlayingTimeTeamCoverageDocument>();
        foreach (string teamName in targetTeams.Order(StringComparer.Ordinal))
        {
            ResearchOfficialPlayerIdentity[] officialPlayers = identities
                .Where(identity => StringComparer.Ordinal.Equals(
                    identity.TeamName,
                    teamName))
                .OrderBy(identity => identity.PlayerCode)
                .ToArray();
            FbrefPlayingTimePlayerDocument[] sourceRows = players
                .Where(player => StringComparer.Ordinal.Equals(
                    player.TeamName,
                    teamName))
                .ToArray();
            HashSet<int> matchedCodes = sourceRows
                .Where(player => player.OfficialPlayerCode.HasValue)
                .Select(player => player.OfficialPlayerCode!.Value)
                .ToHashSet();
            int reviewedMatchCount = sourceRows.Count(player =>
                StringComparer.Ordinal.Equals(
                    player.IdentityStatus,
                    "reviewed-v1"));
            int exactProposalCount = sourceRows.Count(player =>
                StringComparer.Ordinal.Equals(
                    player.IdentityStatus,
                    "exact-current-team-proposal"));
            coverage.Add(
                new(
                    teamName,
                    officialPlayers.Length,
                    sourceRows.Length,
                    matchedCodes.Count,
                    reviewedMatchCount,
                    exactProposalCount,
                    officialPlayers
                        .Where(identity => !matchedCodes.Contains(
                            identity.PlayerCode))
                        .Select(identity => identity.PlayerCode)
                        .ToArray(),
                    sourceRows
                        .Where(player => !player.OfficialPlayerCode.HasValue)
                        .Select(player => player.SourcePlayerId)
                        .Order(StringComparer.Ordinal)
                        .ToArray()));
        }

        return new(
            "1.0",
            snapshot.SnapshotId,
            snapshot.SourceKey,
            ExtractionVersion,
            reviewedBridgeApplies
                ? FbrefPlayerIdentityBridge.Version
                : null,
            snapshot.ContentSha256,
            snapshot.IdentityCaptureId,
            snapshot.RetrievedAtUtc,
            "Championship",
            source.CompetitionSeason,
            players.Count,
            players.Select(player => player.SourcePlayerId).Distinct().Count(),
            players.Count(player => player.OfficialPlayerCode.HasValue),
            players.Count(player => StringComparer.Ordinal.Equals(
                player.IdentityStatus,
                "reviewed-v1")),
            players.Count(player => StringComparer.Ordinal.Equals(
                player.IdentityStatus,
                "exact-current-team-proposal")),
            players,
            coverage);
    }

    internal static HtmlCell RequireCell(string row, string dataStat) =>
        FindCell(row, dataStat)
        ?? throw Invalid(
            $"An FBref player row did not contain the {dataStat} cell.");

    internal static HtmlCell? FindCell(string row, string dataStat)
    {
        HtmlCell? found = null;
        int position = 0;
        while (true)
        {
            (int Start, string TagName) next =
                FindNextCellOpening(row, position);
            if (next.Start < 0)
            {
                return found;
            }

            int openingEnd = FindOpeningTagEnd(
                row,
                next.Start,
                next.TagName);
            string openingTag = row[next.Start..(openingEnd + 1)];
            string closingTag = $"</{next.TagName}>";
            int cellEnd = row.IndexOf(
                closingTag,
                openingEnd + 1,
                StringComparison.Ordinal);
            if (cellEnd < 0)
            {
                throw Invalid("The FBref playing-time table contained an unterminated cell.");
            }
            position = cellEnd + closingTag.Length;
            if (!StringComparer.Ordinal.Equals(
                    ReadAttribute(openingTag, "data-stat"),
                    dataStat))
            {
                continue;
            }
            if (found is not null)
            {
                throw Invalid(
                    $"An FBref row contained duplicate {dataStat} cells.");
            }
            found = new(
                openingTag,
                row[(openingEnd + 1)..cellEnd]);
        }
    }

    private static (int Start, string TagName) FindNextCellOpening(
        string content,
        int start)
    {
        int th = content.IndexOf("<th", start, StringComparison.Ordinal);
        int td = content.IndexOf("<td", start, StringComparison.Ordinal);
        if (th < 0)
        {
            return (td, "td");
        }
        if (td < 0 || th < td)
        {
            return (th, "th");
        }
        return (td, "td");
    }

    internal static string FindUniqueElement(
        string content,
        string tagName,
        string? attributeName = null,
        string? attributeValue = null)
    {
        string openingMarker = $"<{tagName}";
        string closingMarker = $"</{tagName}>";
        string? found = null;
        int position = 0;
        while (true)
        {
            int start = content.IndexOf(
                openingMarker,
                position,
                StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }
            int openingEnd = FindOpeningTagEnd(content, start, tagName);
            string openingTag = content[start..(openingEnd + 1)];
            int end = content.IndexOf(
                closingMarker,
                openingEnd + 1,
                StringComparison.Ordinal);
            if (end < 0)
            {
                throw Invalid($"The FBref {tagName} element was not terminated.");
            }
            position = end + closingMarker.Length;
            if (attributeName is not null
                && !StringComparer.Ordinal.Equals(
                    ReadAttribute(openingTag, attributeName),
                    attributeValue))
            {
                continue;
            }
            if (found is not null)
            {
                throw Invalid($"The FBref page contained duplicate {tagName} elements.");
            }
            found = content[(openingEnd + 1)..end];
        }

        return found
            ?? throw Invalid($"The required FBref {tagName} element was not found.");
    }

    internal static int FindOpeningTagEnd(
        string content,
        int start,
        string tagName)
    {
        int end = content.IndexOf('>', start);
        if (end < 0)
        {
            throw Invalid($"The FBref {tagName} opening tag was not terminated.");
        }
        return end;
    }

    internal static string? ReadAttribute(
        string openingTag,
        string attributeName)
    {
        string marker = $"{attributeName}=\"";
        int start = openingTag.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }
        start += marker.Length;
        int end = openingTag.IndexOf('"', start);
        if (end < 0)
        {
            throw Invalid($"An FBref {attributeName} attribute was not terminated.");
        }
        return WebUtility.HtmlDecode(openingTag[start..end]);
    }

    internal static string? ReadHref(string cellContent)
    {
        int anchor = cellContent.IndexOf("<a", StringComparison.Ordinal);
        if (anchor < 0)
        {
            return null;
        }
        int end = FindOpeningTagEnd(cellContent, anchor, "a");
        return ReadAttribute(cellContent[anchor..(end + 1)], "href");
    }

    internal static string ReadText(string content)
    {
        var visible = new StringBuilder(content.Length);
        bool inTag = false;
        foreach (char character in content)
        {
            if (character == '<')
            {
                inTag = true;
                continue;
            }
            if (character == '>')
            {
                inTag = false;
                continue;
            }
            if (!inTag)
            {
                visible.Append(character);
            }
        }

        string decoded = WebUtility.HtmlDecode(visible.ToString());
        var normalized = new StringBuilder(decoded.Length);
        bool lastWasSpace = true;
        foreach (char character in decoded)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace)
                {
                    normalized.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }
            if (char.IsControl(character))
            {
                throw Invalid("FBref visible text contained a control character.");
            }
            normalized.Append(character);
            lastWasSpace = false;
        }
        return normalized.ToString().Trim();
    }

    internal static string ParseResourceId(
        string href,
        string resourceType)
    {
        string prefix = $"/en/{resourceType}/";
        if (!href.StartsWith(prefix, StringComparison.Ordinal)
            || href.Contains('?', StringComparison.Ordinal)
            || href.Contains('#', StringComparison.Ordinal))
        {
            throw Invalid($"An FBref {resourceType} resource was invalid.");
        }
        int idStart = prefix.Length;
        int idEnd = href.IndexOf('/', idStart);
        if (idEnd < 0)
        {
            throw Invalid($"An FBref {resourceType} resource had no slug.");
        }
        string identifier = href[idStart..idEnd];
        if (identifier.Length != 8
            || identifier.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw Invalid($"An FBref {resourceType} identifier was invalid.");
        }
        return identifier.ToLowerInvariant();
    }

    internal static int ParseCount(
        string value,
        string fieldName,
        bool allowEmpty)
    {
        if (value.Length == 0 && allowEmpty)
        {
            return 0;
        }
        if (!int.TryParse(
                value.Replace(",", string.Empty, StringComparison.Ordinal),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsed))
        {
            throw Invalid($"An FBref {fieldName} value was invalid.");
        }
        return parsed;
    }

    private static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var output = new StringBuilder(decomposed.Length);
        bool lastWasSpace = true;
        foreach (char character in decomposed)
        {
            UnicodeCategory category =
                CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsLetterOrDigit(character))
            {
                output.Append(char.ToLowerInvariant(character));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                output.Append(' ');
                lastWasSpace = true;
            }
        }
        return output.ToString().Trim();
    }

    private static ResearchSourceSnapshotException Invalid(string message) =>
        new(message);

    internal sealed record HtmlCell(string OpeningTag, string Content);

    internal sealed record FbrefPlayingTimeRow(
        string SourcePlayerId,
        string PlayerName,
        string SourceTeamId,
        string TeamName,
        int Appearances,
        int Starts,
        int Minutes,
        string MatchLogsUrl);
}
