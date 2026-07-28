namespace AutoFpl.Api.Intelligence;

public sealed record FbrefMatchLogPollingOptions
{
    public const int DefaultBatchSize = 5;

    private FbrefMatchLogPollingOptions(
        TimeSpan? interval,
        int batchSize)
    {
        Interval = interval;
        BatchSize = batchSize;
    }

    public TimeSpan? Interval { get; }

    public int BatchSize { get; }

    public bool Enabled => Interval is not null;

    public static FbrefMatchLogPollingOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? configuredInterval =
            configuration[
                "AutoFpl:Research:FbrefMatchLogCaptureIntervalMinutes"];
        string? configuredBatchSize =
            configuration["AutoFpl:Research:FbrefMatchLogCaptureBatchSize"];
        if (string.IsNullOrWhiteSpace(configuredInterval))
        {
            if (!string.IsNullOrWhiteSpace(configuredBatchSize))
            {
                throw new InvalidOperationException(
                    "AutoFpl:Research:FbrefMatchLogCaptureBatchSize requires "
                    + "AutoFpl:Research:FbrefMatchLogCaptureIntervalMinutes.");
            }

            return new(null, DefaultBatchSize);
        }

        if (!int.TryParse(
                configuredInterval,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int intervalMinutes)
            || intervalMinutes
                is < Sources.FplFormForecastPollingOptions.MinimumIntervalMinutes
                or > Sources.FplFormForecastPollingOptions.MaximumIntervalMinutes)
        {
            throw new InvalidOperationException(
                "AutoFpl:Research:FbrefMatchLogCaptureIntervalMinutes must be "
                + "an integer from "
                + $"{Sources.FplFormForecastPollingOptions.MinimumIntervalMinutes} "
                + "through "
                + $"{Sources.FplFormForecastPollingOptions.MaximumIntervalMinutes}.");
        }

        int batchSize = DefaultBatchSize;
        if (!string.IsNullOrWhiteSpace(configuredBatchSize)
            && (!int.TryParse(
                    configuredBatchSize,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out batchSize)
                || batchSize
                    is < FbrefMatchLogBatchCapture.MinimumCaptureLimit
                    or > FbrefMatchLogBatchCapture.MaximumCaptureLimit))
        {
            throw new InvalidOperationException(
                "AutoFpl:Research:FbrefMatchLogCaptureBatchSize must be an "
                + $"integer from {FbrefMatchLogBatchCapture.MinimumCaptureLimit} "
                + $"through {FbrefMatchLogBatchCapture.MaximumCaptureLimit}.");
        }

        return new(TimeSpan.FromMinutes(intervalMinutes), batchSize);
    }
}
