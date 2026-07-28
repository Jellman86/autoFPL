using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefMatchLogBatchCapture
{
    public const int MinimumCaptureLimit = 1;
    public const int MaximumCaptureLimit = 5;

    private readonly Func<
        CancellationToken,
        Task<FbrefMatchLogCoverageDocument>> _readCoverage;
    private readonly Func<
        int,
        CancellationToken,
        Task<ResearchSourceSnapshotDocument>> _import;

    public FbrefMatchLogBatchCapture(
        FbrefMatchLogCoverageReader coverageReader,
        FbrefPlayerMatchLogImporter importer)
    {
        ArgumentNullException.ThrowIfNull(coverageReader);
        ArgumentNullException.ThrowIfNull(importer);
        _readCoverage = coverageReader.GetAsync;
        _import = importer.ImportAsync;
    }

    internal FbrefMatchLogBatchCapture(
        Func<
            CancellationToken,
            Task<FbrefMatchLogCoverageDocument>> readCoverage,
        Func<
            int,
            CancellationToken,
            Task<ResearchSourceSnapshotDocument>> import)
    {
        _readCoverage = readCoverage
            ?? throw new ArgumentNullException(nameof(readCoverage));
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

        FbrefMatchLogCoverageDocument coverage =
            await _readCoverage(cancellationToken);
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
