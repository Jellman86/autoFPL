using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefMatchLogBatchCapture
{
    public const int MinimumCaptureLimit = 1;
    public const int MaximumCaptureLimit = 5;

    private readonly Func<
        CancellationToken,
        Task<FbrefMatchLogCoverageContext>> _readContext;
    private readonly Func<
        FbrefPlayingTimeDocument,
        int,
        CancellationToken,
        Task<ResearchSourceSnapshotDocument>> _import;

    public FbrefMatchLogBatchCapture(
        FbrefMatchLogCoverageReader coverageReader,
        FbrefPlayerMatchLogImporter importer)
    {
        ArgumentNullException.ThrowIfNull(coverageReader);
        ArgumentNullException.ThrowIfNull(importer);
        _readContext = coverageReader.GetContextAsync;
        _import = importer.ImportAsync;
    }

    internal FbrefMatchLogBatchCapture(
        Func<
            CancellationToken,
            Task<FbrefMatchLogCoverageContext>> readContext,
        Func<
            FbrefPlayingTimeDocument,
            int,
            CancellationToken,
            Task<ResearchSourceSnapshotDocument>> import)
    {
        _readContext = readContext
            ?? throw new ArgumentNullException(nameof(readContext));
        _import = import ?? throw new ArgumentNullException(nameof(import));
    }

    public async Task<FbrefMatchLogBatchCaptureDocument> RunAsync(
        int captureLimit,
        CancellationToken cancellationToken = default)
    {
        if (captureLimit is < MinimumCaptureLimit or > MaximumCaptureLimit)
        {
            throw new ResearchSourceSnapshotException(
                $"The match-log capture limit must be between {MinimumCaptureLimit} and {MaximumCaptureLimit}.");
        }

        FbrefMatchLogCoverageContext context =
            await _readContext(cancellationToken);
        FbrefMatchLogCoverageDocument coverage = context.Coverage;
        var results = new List<FbrefMatchLogBatchCaptureResultDocument>();
        int capturedCount = 0;
        int maximumAttempts = Math.Min(
            coverage.MissingPlayerCount,
            captureLimit * 2);
        foreach (FbrefMatchLogPlayerCoverageDocument player
                 in coverage.Players.Where(player =>
                     StringComparer.Ordinal.Equals(
                         player.CaptureStatus,
                         "missing")))
        {
            if (capturedCount >= captureLimit
                || results.Count >= maximumAttempts)
            {
                break;
            }

            try
            {
                ResearchSourceSnapshotDocument snapshot =
                    await _import(
                        context.PlayingTime,
                        player.OfficialPlayerCode,
                        cancellationToken);
                capturedCount++;
                results.Add(
                    new(
                        player.SourcePlayerId,
                        player.PlayerName,
                        player.TeamName,
                        player.OfficialPlayerCode,
                        "captured",
                        snapshot.SnapshotId,
                        null));
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ResearchSourceSnapshotException)
            {
                results.Add(
                    new(
                        player.SourcePlayerId,
                        player.PlayerName,
                        player.TeamName,
                        player.OfficialPlayerCode,
                        "failed",
                        null,
                        "capture-failed"));
            }
        }

        int failedCount = results.Count(result =>
            StringComparer.Ordinal.Equals(result.Status, "failed"));
        int remainingCount =
            coverage.MissingPlayerCount - capturedCount;
        string status = remainingCount == 0
            ? "complete"
            : capturedCount > 0
                ? "partial"
                : failedCount > 0
                    ? "failed"
                    : "no-op";
        return new(
            "1.0",
            coverage.IdentityBridgeVersion,
            captureLimit,
            coverage.ReviewedPlayerCount,
            coverage.CapturedPlayerCount,
            results.Count,
            capturedCount,
            failedCount,
            remainingCount,
            status,
            results);
    }
}
