using AutoFpl.Contracts.Sources;

namespace AutoFpl.Api.Sources;

public sealed class FplFormForecastPoller : BackgroundService
{
    private readonly FplFormForecastImporter _importer;
    private readonly FplFormForecastStore _store;
    private readonly FplFormForecastPollingOptions _options;
    private readonly TimeProvider _timeProvider;

    public FplFormForecastPoller(
        FplFormForecastImporter importer,
        FplFormForecastStore store,
        FplFormForecastPollingOptions options,
        TimeProvider timeProvider)
    {
        _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = _options.Interval
            ?? throw new InvalidOperationException(
                "The FPL Form poller cannot run without an interval.");

        while (!stoppingToken.IsCancellationRequested)
        {
            FplFormForecastStatusDocument status =
                await _store.GetStatusAsync(stoppingToken);
            TimeSpan delay = DelayUntilNextCheck(
                status.CheckedAtUtc,
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
            }
            catch (FplFormForecastPayloadException)
            {
                // The importer persists a bounded waiting or failed status.
            }
        }
    }

    public static TimeSpan DelayUntilNextCheck(
        DateTimeOffset? checkedAtUtc,
        DateTimeOffset nowUtc,
        TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        if (checkedAtUtc is null)
        {
            return TimeSpan.Zero;
        }

        TimeSpan elapsed = nowUtc - checkedAtUtc.Value;
        return elapsed <= TimeSpan.Zero
            ? interval
            : elapsed >= interval
                ? TimeSpan.Zero
                : interval - elapsed;
    }
}
