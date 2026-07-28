using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefPlayerMatchOpportunityExtractor
{
    public const string ExtractionVersion =
        "fbref-player-match-opportunity/v1";

    private static readonly int[] WindowSizes = [3, 6, 8];

    private readonly FbrefPlayerMatchLogExtractor _playerExtractor;
    private readonly FbrefTeamScheduleExtractor _scheduleExtractor;

    public FbrefPlayerMatchOpportunityExtractor(
        FbrefPlayerMatchLogExtractor playerExtractor,
        FbrefTeamScheduleExtractor scheduleExtractor)
    {
        _playerExtractor = playerExtractor
            ?? throw new ArgumentNullException(nameof(playerExtractor));
        _scheduleExtractor = scheduleExtractor
            ?? throw new ArgumentNullException(nameof(scheduleExtractor));
    }

    public async Task<FbrefPlayerMatchOpportunityDocument?> GetAsync(
        long playerMatchLogSnapshotId,
        long teamScheduleSnapshotId,
        CancellationToken cancellationToken = default)
    {
        FbrefPlayerMatchLogDocument? player =
            await _playerExtractor.GetAsync(
                playerMatchLogSnapshotId,
                cancellationToken);
        if (player is null)
        {
            return null;
        }
        FbrefTeamScheduleDocument? schedule =
            await _scheduleExtractor.GetAsync(
                teamScheduleSnapshotId,
                cancellationToken);
        return schedule is null ? null : Build(player, schedule);
    }

    internal static FbrefPlayerMatchOpportunityDocument Build(
        FbrefPlayerMatchLogDocument player,
        FbrefTeamScheduleDocument schedule)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(schedule);
        if (!StringComparer.Ordinal.Equals(
                player.IdentityBridgeVersion,
                FbrefPlayerIdentityBridge.Version)
            || !StringComparer.Ordinal.Equals(
                player.SourceTeamId,
                schedule.SourceTeamId)
            || !StringComparer.Ordinal.Equals(
                player.SourceTeamName,
                schedule.TeamName)
            || !StringComparer.Ordinal.Equals(
                player.CompetitionSeason,
                schedule.CompetitionSeason)
            || !StringComparer.Ordinal.Equals(
                schedule.Competition,
                "Championship"))
        {
            throw Invalid(
                "The FBref player log and team schedule are not join-compatible.");
        }

        FbrefPlayerMatchLogRowDocument[] competitionRows = player.Matches
            .Where(match => StringComparer.Ordinal.Equals(
                match.Competition,
                schedule.Competition))
            .ToArray();
        IReadOnlyDictionary<string, FbrefPlayerMatchLogRowDocument>
            playerByMatchId = competitionRows.ToDictionary(
                match => match.SourceMatchId,
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, int> scheduleIndexByMatchId =
            schedule.Matches
                .Select((match, index) => (match.SourceMatchId, index))
                .ToDictionary(
                    item => item.SourceMatchId,
                    item => item.index,
                    StringComparer.Ordinal);
        foreach (FbrefPlayerMatchLogRowDocument playerMatch
                 in competitionRows)
        {
            if (!scheduleIndexByMatchId.TryGetValue(
                    playerMatch.SourceMatchId,
                    out int scheduleIndex))
            {
                throw Invalid(
                    "A Championship player row was absent from the team schedule.");
            }
            FbrefTeamScheduleRowDocument scheduleMatch =
                schedule.Matches[scheduleIndex];
            if (playerMatch.MatchDate != scheduleMatch.MatchDate
                || !StringComparer.Ordinal.Equals(
                    playerMatch.SourceOpponentId,
                    scheduleMatch.SourceOpponentId)
                || !StringComparer.Ordinal.Equals(
                    playerMatch.Venue,
                    scheduleMatch.Venue))
            {
                throw Invalid(
                    "A stable FBref match ID had inconsistent player and team facts.");
            }
        }

        int? firstObservedIndex = competitionRows.Length == 0
            ? null
            : competitionRows.Min(match =>
                scheduleIndexByMatchId[match.SourceMatchId]);
        int? lastObservedIndex = competitionRows.Length == 0
            ? null
            : competitionRows.Max(match =>
                scheduleIndexByMatchId[match.SourceMatchId]);
        FbrefPlayerMatchOpportunityRowDocument[] opportunities =
            schedule.Matches
                .Select(
                    (match, index) => CreateOpportunity(
                        match,
                        index,
                        playerByMatchId,
                        firstObservedIndex,
                        lastObservedIndex))
                .ToArray();
        FbrefPlayerMatchOpportunityWindowDocument[] windows =
            WindowSizes
                .Select(windowSize => CreateWindow(
                    opportunities,
                    windowSize))
                .ToArray();
        int observedPlayerRowCount = opportunities.Count(opportunity =>
            !StringComparer.Ordinal.Equals(
                opportunity.PlayerEvidenceStatus,
                "no-player-row"));
        return new(
            "1.0",
            ExtractionVersion,
            player.IdentityBridgeVersion,
            player.SnapshotId,
            player.ContentSha256,
            player.RetrievedAtUtc,
            schedule.SnapshotId,
            schedule.ContentSha256,
            schedule.RetrievedAtUtc,
            player.RetrievedAtUtc >= schedule.RetrievedAtUtc
                ? player.RetrievedAtUtc
                : schedule.RetrievedAtUtc,
            player.SourcePlayerId,
            player.PlayerName,
            player.OfficialPlayerId,
            player.OfficialPlayerCode,
            player.SourceTeamId,
            player.SourceTeamName,
            schedule.Competition,
            schedule.CompetitionSeason,
            opportunities.Length,
            observedPlayerRowCount,
            player.Matches.Count - competitionRows.Length,
            opportunities.Length - observedPlayerRowCount,
            opportunities,
            windows);
    }

    private static FbrefPlayerMatchOpportunityRowDocument CreateOpportunity(
        FbrefTeamScheduleRowDocument scheduleMatch,
        int scheduleIndex,
        IReadOnlyDictionary<string, FbrefPlayerMatchLogRowDocument>
            playerByMatchId,
        int? firstObservedIndex,
        int? lastObservedIndex)
    {
        playerByMatchId.TryGetValue(
            scheduleMatch.SourceMatchId,
            out FbrefPlayerMatchLogRowDocument? playerMatch);
        string playerEvidenceStatus = playerMatch switch
        {
            { Minutes: > 0 } => "appeared",
            { Minutes: 0 } => "unused-bench",
            null => "no-player-row",
            _ => throw Invalid(
                "An FBref player row contained unsupported minutes."),
        };
        string observedRangeStatus =
            firstObservedIndex is null || lastObservedIndex is null
                ? "not-established"
                : scheduleIndex < firstObservedIndex.Value
                    ? "before-first-observed"
                    : scheduleIndex > lastObservedIndex.Value
                        ? "after-last-observed"
                        : "within-observed-range";
        return new(
            scheduleMatch.SourceMatchId,
            scheduleMatch.MatchDate,
            scheduleMatch.KickoffUtc,
            scheduleMatch.Round,
            scheduleMatch.Venue,
            scheduleMatch.Result,
            scheduleMatch.SourceOpponentId,
            scheduleMatch.OpponentName,
            playerEvidenceStatus,
            observedRangeStatus,
            playerMatch?.Started,
            playerMatch?.Minutes,
            playerMatch?.Goals,
            playerMatch?.Assists,
            playerMatch?.YellowCards,
            playerMatch?.RedCards);
    }

    private static FbrefPlayerMatchOpportunityWindowDocument CreateWindow(
        IReadOnlyList<FbrefPlayerMatchOpportunityRowDocument> opportunities,
        int windowSize)
    {
        FbrefPlayerMatchOpportunityRowDocument[] window =
            opportunities
                .Skip(Math.Max(0, opportunities.Count - windowSize))
                .ToArray();
        int observed = window.Count(opportunity =>
            !StringComparer.Ordinal.Equals(
                opportunity.PlayerEvidenceStatus,
                "no-player-row"));
        int appearances = window.Count(opportunity =>
            StringComparer.Ordinal.Equals(
                opportunity.PlayerEvidenceStatus,
                "appeared"));
        int unusedBench = window.Count(opportunity =>
            StringComparer.Ordinal.Equals(
                opportunity.PlayerEvidenceStatus,
                "unused-bench"));
        int noPlayerRow = window.Length - observed;
        return new(
            windowSize,
            window.Length == windowSize
                ? noPlayerRow == 0
                    ? "complete-observed"
                    : "complete-with-missing-player-rows"
                : "incomplete-schedule-window",
            window[0].MatchDate,
            window[^1].MatchDate,
            window.Length,
            observed,
            appearances,
            window.Count(opportunity => opportunity.Started is true),
            unusedBench,
            noPlayerRow,
            window.Sum(opportunity => opportunity.Minutes ?? 0));
    }

    private static ResearchSourceSnapshotException Invalid(string message) =>
        new(message);
}
