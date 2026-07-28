using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefMatchOpportunityCoverageReader
{
    private readonly FbrefMatchLogCoverageReader _matchLogCoverageReader;
    private readonly ResearchSourceSnapshotStore _snapshotStore;

    public FbrefMatchOpportunityCoverageReader(
        FbrefMatchLogCoverageReader matchLogCoverageReader,
        ResearchSourceSnapshotStore snapshotStore)
    {
        _matchLogCoverageReader = matchLogCoverageReader
            ?? throw new ArgumentNullException(
                nameof(matchLogCoverageReader));
        _snapshotStore = snapshotStore
            ?? throw new ArgumentNullException(nameof(snapshotStore));
    }

    public async Task<FbrefMatchOpportunityCoverageDocument> GetAsync(
        CancellationToken cancellationToken = default)
    {
        FbrefMatchLogCoverageDocument matchLogs =
            await _matchLogCoverageReader.GetAsync(cancellationToken);
        ResearchSourceInventoryDocument inventory =
            await _snapshotStore.GetInventoryAsync(cancellationToken);
        return Build(matchLogs, inventory);
    }

    internal static FbrefMatchOpportunityCoverageDocument Build(
        FbrefMatchLogCoverageDocument matchLogs,
        ResearchSourceInventoryDocument inventory)
    {
        ArgumentNullException.ThrowIfNull(matchLogs);
        ArgumentNullException.ThrowIfNull(inventory);
        IReadOnlyDictionary<string, ResearchSourceSnapshotDocument>
            latestBySourceKey = inventory.LatestSnapshots.ToDictionary(
                snapshot => snapshot.SourceKey,
                StringComparer.Ordinal);
        var scheduleByTeamId =
            new Dictionary<string, ResearchSourceSnapshotDocument?>(
                StringComparer.Ordinal);
        foreach (FbrefTeamScheduleSource source
                 in FbrefTeamScheduleSources.All)
        {
            latestBySourceKey.TryGetValue(
                source.SourceKey,
                out ResearchSourceSnapshotDocument? snapshot);
            scheduleByTeamId.Add(source.SourceTeamId, snapshot);
        }

        FbrefMatchOpportunityPlayerCoverageDocument[] players =
            matchLogs.Players
                .Select(
                    player =>
                    {
                        if (!scheduleByTeamId.TryGetValue(
                                player.SourceTeamId,
                                out ResearchSourceSnapshotDocument? schedule))
                        {
                            throw Invalid(
                                "A reviewed player did not belong to a registered team schedule.");
                        }
                        bool hasPlayerLog = player.SnapshotId is not null;
                        bool hasSchedule = schedule is not null;
                        return new FbrefMatchOpportunityPlayerCoverageDocument(
                            player.SourcePlayerId,
                            player.PlayerName,
                            player.SourceTeamId,
                            player.TeamName,
                            player.OfficialPlayerCode,
                            player.CaptureStatus,
                            player.SnapshotId,
                            schedule?.SnapshotId,
                            hasPlayerLog && hasSchedule
                                ? "source-pair-ready"
                                : !hasPlayerLog && !hasSchedule
                                    ? "missing-player-log-and-schedule"
                                    : !hasPlayerLog
                                        ? "missing-player-log"
                                        : "missing-team-schedule");
                    })
                .ToArray();
        FbrefMatchOpportunityTeamCoverageDocument[] teams =
            players
                .GroupBy(
                    player => (player.SourceTeamId, player.TeamName))
                .OrderBy(group => group.Key.TeamName, StringComparer.Ordinal)
                .Select(
                    group =>
                    {
                        ResearchSourceSnapshotDocument? schedule =
                            scheduleByTeamId[group.Key.SourceTeamId];
                        return new FbrefMatchOpportunityTeamCoverageDocument(
                            group.Key.SourceTeamId,
                            group.Key.TeamName,
                            group.Count(),
                            group.Count(player =>
                                player.PlayerMatchLogSnapshotId is not null),
                            group.Count(player =>
                                StringComparer.Ordinal.Equals(
                                    player.SourcePairStatus,
                                    "source-pair-ready")),
                            schedule is null ? "missing" : "captured",
                            schedule?.SnapshotId,
                            schedule?.ContentSha256);
                    })
                .ToArray();
        int capturedSchedules = teams.Count(team =>
            team.TeamScheduleSnapshotId is not null);
        int sourcePairs = players.Count(player =>
            StringComparer.Ordinal.Equals(
                player.SourcePairStatus,
                "source-pair-ready"));
        string status = capturedSchedules < FbrefTeamScheduleSources.All.Count
            ? "blocked-incomplete-team-schedules"
            : matchLogs.MissingPlayerCount > 0
                ? "blocked-incomplete-player-logs"
                : "source-pairs-ready-for-shadow-evaluation";
        return new(
            "1.0",
            matchLogs.IdentityBridgeVersion,
            status,
            matchLogs.ReviewedPlayerCount,
            matchLogs.CapturedPlayerCount,
            matchLogs.MissingPlayerCount,
            capturedSchedules,
            FbrefTeamScheduleSources.All.Count - capturedSchedules,
            sourcePairs,
            teams,
            players);
    }

    private static ResearchSourceSnapshotException Invalid(string message) =>
        new(message);
}
