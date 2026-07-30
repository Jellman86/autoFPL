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
    private readonly TimeProvider _timeProvider;

    public ResearchSourcePoller(
        ResearchSourceSnapshotImporter importer,
        ResearchSourceClaimExtractor extractor,
        ResearchSourcePollingOptions options,
        TimeProvider timeProvider)
    {
        _importer =
            importer ?? throw new ArgumentNullException(nameof(importer));
        _extractor =
            extractor ?? throw new ArgumentNullException(nameof(extractor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = _options.Interval
            ?? throw new InvalidOperationException(
                "The research source poller cannot run without an interval.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshOnceAsync(stoppingToken);
            await Task.Delay(interval, _timeProvider, stoppingToken);
        }
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
