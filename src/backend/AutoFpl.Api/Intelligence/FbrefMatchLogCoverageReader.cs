using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefMatchLogCoverageReader
{
    private readonly FbrefPlayingTimeExtractor _playingTimeExtractor;
    private readonly ResearchSourceSnapshotStore _snapshotStore;

    public FbrefMatchLogCoverageReader(
        FbrefPlayingTimeExtractor playingTimeExtractor,
        ResearchSourceSnapshotStore snapshotStore)
    {
        _playingTimeExtractor = playingTimeExtractor
            ?? throw new ArgumentNullException(nameof(playingTimeExtractor));
        _snapshotStore = snapshotStore
            ?? throw new ArgumentNullException(nameof(snapshotStore));
    }

    public async Task<FbrefMatchLogCoverageDocument> GetAsync(
        CancellationToken cancellationToken = default)
    {
        FbrefPlayingTimeDocument playingTime =
            await _playingTimeExtractor.GetAsync(
                FbrefPlayerIdentityBridge.ReviewedSnapshotId,
                cancellationToken)
            ?? throw new ResearchSourceSnapshotException(
                "The reviewed FBref playing-time snapshot is not available.");
        if (!StringComparer.Ordinal.Equals(
                playingTime.IdentityBridgeVersion,
                FbrefPlayerIdentityBridge.Version))
        {
            throw new ResearchSourceSnapshotException(
                "The FBref reviewed identity bridge is not applicable.");
        }

        ResearchSourceInventoryDocument inventory =
            await _snapshotStore.GetInventoryAsync(cancellationToken);
        return Build(playingTime, inventory);
    }

    internal static FbrefMatchLogCoverageDocument Build(
        FbrefPlayingTimeDocument playingTime,
        ResearchSourceInventoryDocument inventory)
    {
        ArgumentNullException.ThrowIfNull(playingTime);
        ArgumentNullException.ThrowIfNull(inventory);
        IReadOnlyDictionary<string, ResearchSourceSnapshotDocument>
            latestBySourceKey = inventory.LatestSnapshots.ToDictionary(
                snapshot => snapshot.SourceKey,
                StringComparer.Ordinal);
        FbrefMatchLogPlayerCoverageDocument[] players = playingTime.Players
            .Where(player => StringComparer.Ordinal.Equals(
                player.IdentityStatus,
                "reviewed-v1"))
            .Select(
                player =>
                {
                    if (player.OfficialPlayerId is null
                        || player.OfficialPlayerCode is null)
                    {
                        throw new ResearchSourceSnapshotException(
                            "A reviewed FBref identity lacked an official player identity.");
                    }
                    string sourceKey =
                        $"{FbrefPlayerMatchLogImporter.SourceKeyPrefix}"
                        + $"{player.SourcePlayerId}"
                        + $"{FbrefPlayerMatchLogImporter.SourceKeySuffix}";
                    latestBySourceKey.TryGetValue(
                        sourceKey,
                        out ResearchSourceSnapshotDocument? snapshot);
                    return new FbrefMatchLogPlayerCoverageDocument(
                        player.SourcePlayerId,
                        player.PlayerName,
                        player.SourceTeamId,
                        player.TeamName,
                        player.OfficialPlayerId.Value,
                        player.OfficialPlayerCode.Value,
                        sourceKey,
                        snapshot is null ? "missing" : "captured",
                        snapshot?.SnapshotId,
                        snapshot?.SourceRevision,
                        snapshot?.RetrievedAtUtc,
                        snapshot?.ContentSha256);
                })
            .OrderBy(player => player.TeamName, StringComparer.Ordinal)
            .ThenBy(player => player.PlayerName, StringComparer.Ordinal)
            .ThenBy(player => player.OfficialPlayerCode)
            .ToArray();
        if (players.Length != FbrefPlayerIdentityBridge.All.Count)
        {
            throw new ResearchSourceSnapshotException(
                "The FBref match-log coverage population was incomplete.");
        }

        FbrefMatchLogTeamCoverageDocument[] teams = players
            .GroupBy(player => player.TeamName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(
                group =>
                {
                    int captured = group.Count(player =>
                        StringComparer.Ordinal.Equals(
                            player.CaptureStatus,
                            "captured"));
                    return new FbrefMatchLogTeamCoverageDocument(
                        group.Key,
                        group.Count(),
                        captured,
                        group.Count() - captured);
                })
            .ToArray();
        int capturedCount = players.Count(player =>
            StringComparer.Ordinal.Equals(
                player.CaptureStatus,
                "captured"));
        return new(
            "1.0",
            FbrefPlayerIdentityBridge.Version,
            playingTime.SnapshotId,
            players.Length,
            capturedCount,
            players.Length - capturedCount,
            teams,
            players);
    }
}
