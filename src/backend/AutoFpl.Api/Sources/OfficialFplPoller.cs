using AutoFpl.Api.Advice;
using AutoFpl.Api.Forecasts;

namespace AutoFpl.Api.Sources;

public sealed class OfficialFplPoller : BackgroundService
{
    private readonly OfficialFplImporter _importer;
    private readonly OfficialFplCaptureStore _store;
    private readonly OfficialFplPollingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly BaselineForecastArtifactStore _forecastStore;
    private readonly PlayerGameweekForecastArtifactStore _playerForecastStore;

    public OfficialFplPoller(
        OfficialFplImporter importer,
        OfficialFplCaptureStore store,
        OfficialFplPollingOptions options,
        TimeProvider timeProvider,
        BaselineForecastArtifactStore forecastStore,
        PlayerGameweekForecastArtifactStore playerForecastStore)
    {
        _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _forecastStore =
            forecastStore ?? throw new ArgumentNullException(nameof(forecastStore));
        _playerForecastStore =
            playerForecastStore
            ?? throw new ArgumentNullException(nameof(playerForecastStore));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = _options.Interval
            ?? throw new InvalidOperationException(
                "The official FPL poller cannot run without an interval.");

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset? checkedAtUtc =
                await _store.GetLatestCheckTimeAsync(stoppingToken);
            TimeSpan delay = FplFormForecastPoller.DelayUntilNextCheck(
                checkedAtUtc,
                _timeProvider.GetUtcNow(),
                interval);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, stoppingToken);
                continue;
            }

            try
            {
                await _importer.ImportLatestAsync(stoppingToken);
                await _forecastStore.RefreshLatestAsync(stoppingToken);
                await _playerForecastStore.RefreshLatestAsync(stoppingToken);
            }
            catch (Exception exception) when (
                exception is OfficialFplPayloadException
                or HttpRequestException
                or TaskCanceledException)
            {
                // The importer persists a bounded failed status before retry delay.
            }
        }
    }
}
