using AutoFpl.Api.Advice;
using AutoFpl.Api.Forecasts;
using AutoFpl.Contracts.Sources;

namespace AutoFpl.Api.Sources;

public sealed class OfficialFplPoller : BackgroundService
{
    private readonly OfficialFplImporter _importer;
    private readonly OfficialFplCaptureStore _store;
    private readonly OfficialFplPollingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly BaselineForecastArtifactStore _forecastStore;
    private readonly PlayerGameweekForecastArtifactStore _playerForecastStore;
    private readonly OfficialFplOutcomeImporter _outcomeImporter;
    private readonly OfficialFplOutcomeStore _outcomeStore;

    public const int MaximumOutcomeImportsPerPoll = 3;

    public OfficialFplPoller(
        OfficialFplImporter importer,
        OfficialFplCaptureStore store,
        OfficialFplPollingOptions options,
        TimeProvider timeProvider,
        BaselineForecastArtifactStore forecastStore,
        PlayerGameweekForecastArtifactStore playerForecastStore,
        OfficialFplOutcomeImporter outcomeImporter,
        OfficialFplOutcomeStore outcomeStore)
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
        _outcomeImporter =
            outcomeImporter ?? throw new ArgumentNullException(nameof(outcomeImporter));
        _outcomeStore =
            outcomeStore ?? throw new ArgumentNullException(nameof(outcomeStore));
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
                OfficialFplCaptureDocument capture =
                    await _importer.ImportLatestAsync(stoppingToken);
                await _forecastStore.RefreshLatestAsync(stoppingToken);
                await _playerForecastStore.RefreshLatestAsync(stoppingToken);
                await CaptureFinalOutcomesOnceAsync(capture, stoppingToken);
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

    public async Task<IReadOnlyList<OfficialFplOutcomeCaptureDocument>>
        CaptureFinalOutcomesOnceAsync(
            OfficialFplCaptureDocument referenceCapture,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referenceCapture);
        var captured = new HashSet<int>();
        if (referenceCapture.LatestCompletedGameweek is int latest)
        {
            for (int gameweek = 1; gameweek <= latest; gameweek++)
            {
                if (await _outcomeStore.GetLatestAsync(
                        referenceCapture.SeasonCode,
                        gameweek,
                        cancellationToken) is not null)
                {
                    captured.Add(gameweek);
                }
            }
        }

        var outcomes = new List<OfficialFplOutcomeCaptureDocument>();
        foreach (int gameweek in SelectOutcomeCandidates(
            referenceCapture.LatestCompletedGameweek,
            captured,
            MaximumOutcomeImportsPerPoll))
        {
            try
            {
                outcomes.Add(
                    await _outcomeImporter.ImportForReferenceAsync(
                        referenceCapture,
                        gameweek,
                        cancellationToken));
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested
                && exception is (
                    OfficialFplPayloadException
                    or HttpRequestException
                    or TaskCanceledException))
            {
                // One provider or validation failure must not discard earlier captures.
            }
        }

        return outcomes;
    }

    public static IReadOnlyList<int> SelectOutcomeCandidates(
        int? latestCompletedGameweek,
        IReadOnlySet<int> capturedGameweeks,
        int maximumImports)
    {
        ArgumentNullException.ThrowIfNull(capturedGameweeks);
        if (latestCompletedGameweek is null)
        {
            return [];
        }

        if (latestCompletedGameweek is < 1 or > 38)
        {
            throw new ArgumentOutOfRangeException(nameof(latestCompletedGameweek));
        }

        if (maximumImports is < 1 or > 38)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumImports));
        }

        var candidates = Enumerable
            .Range(1, latestCompletedGameweek.Value)
            .Where(gameweek => !capturedGameweeks.Contains(gameweek))
            .Take(maximumImports)
            .ToList();
        if (candidates.Count < maximumImports
            && !candidates.Contains(latestCompletedGameweek.Value))
        {
            candidates.Add(latestCompletedGameweek.Value);
        }

        return candidates;
    }
}
