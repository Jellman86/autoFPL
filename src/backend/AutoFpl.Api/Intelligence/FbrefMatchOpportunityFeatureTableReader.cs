using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Intelligence;
using AutoFpl.Contracts.Sources;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefMatchOpportunityFeatureTableReader
{
    public const string FeatureVersion =
        "fbref-match-opportunity-features/v1";

    private readonly OfficialFplCaptureStore _captureStore;
    private readonly FbrefMatchLogCoverageReader _coverageReader;
    private readonly ResearchSourceSnapshotStore _snapshotStore;
    private readonly FbrefPlayerMatchLogExtractor _playerExtractor;
    private readonly FbrefTeamScheduleExtractor _scheduleExtractor;

    public FbrefMatchOpportunityFeatureTableReader(
        OfficialFplCaptureStore captureStore,
        FbrefMatchLogCoverageReader coverageReader,
        ResearchSourceSnapshotStore snapshotStore,
        FbrefPlayerMatchLogExtractor playerExtractor,
        FbrefTeamScheduleExtractor scheduleExtractor)
    {
        _captureStore = captureStore
            ?? throw new ArgumentNullException(nameof(captureStore));
        _coverageReader = coverageReader
            ?? throw new ArgumentNullException(nameof(coverageReader));
        _snapshotStore = snapshotStore
            ?? throw new ArgumentNullException(nameof(snapshotStore));
        _playerExtractor = playerExtractor
            ?? throw new ArgumentNullException(nameof(playerExtractor));
        _scheduleExtractor = scheduleExtractor
            ?? throw new ArgumentNullException(nameof(scheduleExtractor));
    }

    public async Task<FbrefMatchOpportunityFeatureTableDocument> GetAsync(
        CancellationToken cancellationToken = default)
    {
        OfficialFplCaptureDocument target =
            await _captureStore.GetLatestAsync(cancellationToken)
            ?? throw Invalid(
                "An official target capture is required for the FBref feature table.");
        if (target.NextGameweekNumber is null
            || target.NextDeadlineUtc is null
            || target.AvailableAtUtc > target.NextDeadlineUtc.Value)
        {
            throw Invalid(
                "The latest official capture has no supported pre-deadline target.");
        }

        FbrefMatchLogCoverageContext context =
            await _coverageReader.GetContextAsync(cancellationToken);
        ResearchSourceInventoryDocument inventory =
            await _snapshotStore.GetInventoryAsync(cancellationToken);
        FbrefMatchLogCoverageDocument coverage =
            FbrefMatchLogCoverageReader.Build(
                context.PlayingTime,
                inventory);
        IReadOnlyDictionary<string, ResearchSourceSnapshotDocument>
            latestBySourceKey = inventory.LatestSnapshots.ToDictionary(
                snapshot => snapshot.SourceKey,
                StringComparer.Ordinal);
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities =
            await _snapshotStore.GetPlayerIdentitiesAsync(
                target.CaptureId,
                cancellationToken);
        IReadOnlyDictionary<int, ResearchOfficialPlayerIdentity>
            targetIdentityByCode = identities.ToDictionary(
                identity => identity.PlayerCode);

        var scheduleByTeamId =
            new Dictionary<string, FbrefTeamScheduleDocument?>(
                StringComparer.Ordinal);
        foreach (FbrefTeamScheduleSource source
                 in FbrefTeamScheduleSources.All)
        {
            latestBySourceKey.TryGetValue(
                source.SourceKey,
                out ResearchSourceSnapshotDocument? snapshot);
            if (snapshot is null)
            {
                scheduleByTeamId.Add(source.SourceTeamId, null);
                continue;
            }
            ValidateCutoff(snapshot, target);
            FbrefTeamScheduleDocument schedule =
                await _scheduleExtractor.GetAsync(
                    snapshot.SnapshotId,
                    cancellationToken)
                ?? throw Invalid(
                    "A registered FBref team schedule snapshot was unavailable.");
            scheduleByTeamId.Add(source.SourceTeamId, schedule);
        }

        var players =
            new List<FbrefMatchOpportunityPlayerFeatureDocument>(
                coverage.ReviewedPlayerCount);
        foreach (FbrefMatchLogPlayerCoverageDocument player
                 in coverage.Players)
        {
            if (!targetIdentityByCode.TryGetValue(
                    player.OfficialPlayerCode,
                    out ResearchOfficialPlayerIdentity? targetIdentity))
            {
                throw Invalid(
                    "A reviewed FBref player is absent from the official target capture.");
            }
            if (!scheduleByTeamId.TryGetValue(
                    player.SourceTeamId,
                    out FbrefTeamScheduleDocument? schedule))
            {
                throw Invalid(
                    "A reviewed FBref player has no registered team schedule.");
            }
            latestBySourceKey.TryGetValue(
                player.SourceKey,
                out ResearchSourceSnapshotDocument? playerSnapshot);
            ResearchSourceSnapshotDocument? scheduleSnapshot =
                schedule is null
                    ? null
                    : latestBySourceKey.Values.Single(snapshot =>
                        snapshot.SnapshotId == schedule.SnapshotId);
            if (playerSnapshot is null || schedule is null)
            {
                players.Add(CreateMissing(
                    player,
                    targetIdentity,
                    playerSnapshot,
                    scheduleSnapshot));
                continue;
            }

            ValidateCutoff(playerSnapshot, target);
            try
            {
                FbrefPlayerMatchLogDocument playerLog =
                    await _playerExtractor.GetAsync(
                        playerSnapshot.SnapshotId,
                        context.PlayingTime,
                        cancellationToken)
                    ?? throw Invalid(
                        "A covered FBref player match-log snapshot was unavailable.");
                FbrefPlayerMatchOpportunityDocument opportunity =
                    FbrefPlayerMatchOpportunityExtractor.Build(
                        playerLog,
                        schedule);
                if (opportunity.OfficialPlayerCode != targetIdentity.PlayerCode)
                {
                    throw Invalid(
                        "An FBref feature row did not match its official target identity.");
                }
                players.Add(CreateReady(
                    opportunity,
                    targetIdentity,
                    playerSnapshot,
                    scheduleSnapshot!));
            }
            catch (ResearchSourceSnapshotException)
            {
                players.Add(CreateRejected(
                    player,
                    targetIdentity,
                    playerSnapshot,
                    scheduleSnapshot!));
            }
        }

        int readyCount = players.Count(player =>
            StringComparer.Ordinal.Equals(
                player.FeatureStatus,
                "shadow-feature-ready"));
        int rejectedCount = players.Count(player =>
            StringComparer.Ordinal.Equals(
                player.FeatureStatus,
                "rejected-incompatible-source-pair"));
        int missingCount = players.Count - readyCount - rejectedCount;
        return new(
            "1.0",
            FeatureVersion,
            coverage.IdentityBridgeVersion,
            rejectedCount > 0
                ? "blocked-incompatible-source-pairs"
                : missingCount > 0
                    ? "blocked-incomplete-source-pairs"
                    : "complete-shadow-feature-table",
            "exploratory-not-promoted",
            false,
            new(
                target.CaptureId,
                target.SeasonCode,
                target.NextGameweekNumber.Value,
                target.NextDeadlineUtc.Value,
                target.AvailableAtUtc),
            players.Count,
            readyCount,
            missingCount,
            rejectedCount,
            players);
    }

    internal static FbrefMatchOpportunityPlayerFeatureDocument CreateMissing(
        FbrefMatchLogPlayerCoverageDocument player,
        ResearchOfficialPlayerIdentity targetIdentity,
        ResearchSourceSnapshotDocument? playerSnapshot,
        ResearchSourceSnapshotDocument? scheduleSnapshot)
    {
        string status = playerSnapshot is null && scheduleSnapshot is null
            ? "missing-player-log-and-schedule"
            : playerSnapshot is null
                ? "missing-player-log"
                : "missing-team-schedule";
        return new(
            targetIdentity.PlayerId,
            targetIdentity.PlayerCode,
            player.PlayerName,
            targetIdentity.TeamName,
            player.SourcePlayerId,
            player.SourceTeamId,
            player.TeamName,
            status,
            playerSnapshot?.SnapshotId,
            playerSnapshot?.ContentSha256,
            scheduleSnapshot?.SnapshotId,
            scheduleSnapshot?.ContentSha256,
            null,
            null,
            []);
    }

    internal static FbrefMatchOpportunityPlayerFeatureDocument CreateRejected(
        FbrefMatchLogPlayerCoverageDocument player,
        ResearchOfficialPlayerIdentity targetIdentity,
        ResearchSourceSnapshotDocument playerSnapshot,
        ResearchSourceSnapshotDocument scheduleSnapshot) =>
        new(
            targetIdentity.PlayerId,
            targetIdentity.PlayerCode,
            player.PlayerName,
            targetIdentity.TeamName,
            player.SourcePlayerId,
            player.SourceTeamId,
            player.TeamName,
            "rejected-incompatible-source-pair",
            playerSnapshot.SnapshotId,
            playerSnapshot.ContentSha256,
            scheduleSnapshot.SnapshotId,
            scheduleSnapshot.ContentSha256,
            null,
            null,
            []);

    internal static FbrefMatchOpportunityPlayerFeatureDocument CreateReady(
        FbrefPlayerMatchOpportunityDocument opportunity,
        ResearchOfficialPlayerIdentity targetIdentity,
        ResearchSourceSnapshotDocument playerSnapshot,
        ResearchSourceSnapshotDocument scheduleSnapshot)
    {
        FbrefMatchOpportunityFeatureWindowDocument season =
            CreateWindow(
                null,
                opportunity.NoPlayerRowCount == 0
                    ? "complete-observed"
                    : "complete-with-missing-player-rows",
                opportunity.Opportunities);
        FbrefMatchOpportunityFeatureWindowDocument[] rolling =
            opportunity.RollingWindows
                .Select(window =>
                    CreateWindow(
                        window.WindowSize,
                        window.WindowStatus,
                        opportunity.Opportunities
                            .Skip(Math.Max(
                                0,
                                opportunity.Opportunities.Count
                                - window.WindowSize))
                            .ToArray()))
                .ToArray();
        return new(
            targetIdentity.PlayerId,
            targetIdentity.PlayerCode,
            opportunity.PlayerName,
            targetIdentity.TeamName,
            opportunity.SourcePlayerId,
            opportunity.SourceTeamId,
            opportunity.TeamName,
            "shadow-feature-ready",
            playerSnapshot.SnapshotId,
            playerSnapshot.ContentSha256,
            scheduleSnapshot.SnapshotId,
            scheduleSnapshot.ContentSha256,
            playerSnapshot.AvailableAtUtc >= scheduleSnapshot.AvailableAtUtc
                ? playerSnapshot.AvailableAtUtc
                : scheduleSnapshot.AvailableAtUtc,
            season,
            rolling);
    }

    private static FbrefMatchOpportunityFeatureWindowDocument CreateWindow(
        int? windowSize,
        string windowStatus,
        IReadOnlyList<FbrefPlayerMatchOpportunityRowDocument> rows)
    {
        int observed = rows.Count(row =>
            !StringComparer.Ordinal.Equals(
                row.PlayerEvidenceStatus,
                "no-player-row"));
        int appearances = rows.Count(row =>
            StringComparer.Ordinal.Equals(
                row.PlayerEvidenceStatus,
                "appeared"));
        return new(
            windowSize,
            windowStatus,
            rows.Count,
            observed,
            appearances,
            rows.Count(row => row.Started is true),
            rows.Count(row =>
                StringComparer.Ordinal.Equals(
                    row.PlayerEvidenceStatus,
                    "unused-bench")),
            rows.Count - observed,
            rows.Sum(row => row.Minutes ?? 0),
            rows.Sum(row => row.Goals ?? 0),
            rows.Sum(row => row.Assists ?? 0),
            rows.Sum(row => row.YellowCards ?? 0),
            rows.Sum(row => row.RedCards ?? 0));
    }

    private static void ValidateCutoff(
        ResearchSourceSnapshotDocument snapshot,
        OfficialFplCaptureDocument target)
    {
        if (!snapshot.IsPreDeadline
            || !StringComparer.Ordinal.Equals(
                snapshot.SeasonCode,
                target.SeasonCode)
            || snapshot.Gameweek != target.NextGameweekNumber
            || snapshot.DeadlineUtc != target.NextDeadlineUtc
            || snapshot.AvailableAtUtc > target.NextDeadlineUtc
            || snapshot.RetrievedAtUtc > target.NextDeadlineUtc)
        {
            throw Invalid(
                "An FBref feature source is not compatible with the official target cutoff.");
        }
    }

    private static ResearchSourceSnapshotException Invalid(string message) =>
        new(message);
}
