using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefMatchLogPoller : BackgroundService
{
    private readonly FbrefMatchLogBatchCapture _capture;
    private readonly FbrefMatchLogPollingOptions _options;
    private readonly TimeProvider _timeProvider;

    public FbrefMatchLogPoller(
        FbrefMatchLogBatchCapture capture,
        FbrefMatchLogPollingOptions options,
        TimeProvider timeProvider)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = _options.Interval
            ?? throw new InvalidOperationException(
                "The FBref match-log poller cannot run without an interval.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await CaptureOnceAsync(stoppingToken);
            await Task.Delay(interval, _timeProvider, stoppingToken);
        }
    }

    internal async Task<FbrefMatchLogBatchCaptureDocument?> CaptureOnceAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _capture.RunAsync(
                _options.BatchSize,
                cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ResearchSourceSnapshotException)
        {
            return null;
        }
    }
}
