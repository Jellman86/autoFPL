namespace AutoFpl.Api.Intelligence;

public sealed class ResearchSourcePoller : BackgroundService
{
    private static readonly IReadOnlySet<string> ExtractableSources =
        new HashSet<string>(
            [
                ResearchSourceClaimExtractor.FfScoutSourceKey,
                ResearchSourceClaimExtractor.PremierLeagueInjurySourceKey,
                ResearchSourceClaimExtractor.StraightredSourceKey,
            ],
            StringComparer.Ordinal);

    private readonly ResearchSourceSnapshotImporter _importer;
    private readonly ResearchSourceClaimExtractor _extractor;
    private readonly ResearchSourcePollingOptions _options;
    private readonly ResearchSourceRefreshSignal _refreshSignal;
    private readonly TimeProvider _timeProvider;

    internal static readonly TimeSpan MaximumSignalLatency =
        TimeSpan.FromMinutes(1);

    public ResearchSourcePoller(
        ResearchSourceSnapshotImporter importer,
        ResearchSourceClaimExtractor extractor,
        ResearchSourcePollingOptions options,
        ResearchSourceRefreshSignal refreshSignal,
        TimeProvider timeProvider)
    {
        _importer =
            importer ?? throw new ArgumentNullException(nameof(importer));
        _extractor =
            extractor ?? throw new ArgumentNullException(nameof(extractor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _refreshSignal =
            refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = _options.Interval
            ?? throw new InvalidOperationException(
                "The research source poller cannot run without an interval.");

        DateTimeOffset nextRefreshUtc = _timeProvider.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            bool refreshRequested = _refreshSignal.ConsumeRequest();
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (refreshRequested || now >= nextRefreshUtc)
            {
                await RefreshOnceAsync(stoppingToken);
                nextRefreshUtc = _timeProvider.GetUtcNow() + interval;
            }

            TimeSpan delay = _refreshSignal.Enabled
                ? DelayUntilNextCheck(
                    _timeProvider.GetUtcNow(),
                    nextRefreshUtc)
                : interval;
            await Task.Delay(delay, _timeProvider, stoppingToken);
        }
    }

    internal static TimeSpan DelayUntilNextCheck(
        DateTimeOffset nowUtc,
        DateTimeOffset nextRefreshUtc)
    {
        TimeSpan remaining = nextRefreshUtc - nowUtc;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return remaining < MaximumSignalLatency
            ? remaining
            : MaximumSignalLatency;
    }

    internal async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        foreach (ResearchSourceDefinition source in ResearchSourceRegistry.All.Where(
                     definition => definition.PollAutomatically))
        {
            try
            {
                Contracts.Intelligence.ResearchSourceSnapshotDocument snapshot =
                    await _importer.ImportAsync(
                        source.SourceKey,
                        cancellationToken);
                if (ExtractableSources.Contains(source.SourceKey))
                {
                    await _extractor.ExtractAsync(
                        snapshot.SnapshotId,
                        cancellationToken);
                }
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested
                && exception is ResearchSourceSnapshotException
                    or HttpRequestException
                    or TaskCanceledException)
            {
                // Each bounded source refresh is independent. A failed source
                // cannot suppress the remaining portfolio until the next cycle.
            }
        }
    }
}
