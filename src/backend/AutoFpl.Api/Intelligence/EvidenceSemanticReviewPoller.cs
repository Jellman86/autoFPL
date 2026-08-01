namespace AutoFpl.Api.Intelligence;

public sealed class EvidenceSemanticReviewPoller : BackgroundService
{
    private readonly EvidenceSemanticReviewGenerator _generator;
    private readonly EvidenceSemanticReviewProviderOptions _options;
    private readonly TimeProvider _timeProvider;

    public EvidenceSemanticReviewPoller(
        EvidenceSemanticReviewGenerator generator,
        EvidenceSemanticReviewProviderOptions options,
        TimeProvider timeProvider)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = _options.Interval
            ?? throw new InvalidOperationException(
                "The evidence semantic review poller cannot run without an interval.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _generator.GenerateCurrentAsync(stoppingToken);
            }
            catch (Exception exception) when (
                !stoppingToken.IsCancellationRequested
                && exception is InvalidOperationException
                    or HttpRequestException
                    or EvidenceSemanticReviewValidationException)
            {
                // A later cycle may observe a new complete context or repaired
                // owner configuration. Provider response bodies are never logged.
            }
            await Task.Delay(interval, _timeProvider, stoppingToken);
        }
    }
}
