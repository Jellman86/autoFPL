using System.Globalization;

namespace AutoFpl.Api.Forecasts;

public sealed record AnalyticsSnapshotOptions
{
    public const int MinimumIntervalMinutes = 1;
    public const int MaximumIntervalMinutes = 60;
    public const string DefaultSnapshotPath =
        "/analytics-snapshot/autofpl.db";

    private AnalyticsSnapshotOptions(
        TimeSpan? interval,
        string snapshotPath)
    {
        Interval = interval;
        SnapshotPath = snapshotPath;
    }

    public TimeSpan? Interval { get; }

    public string SnapshotPath { get; }

    public bool Enabled => Interval is not null;

    public static AnalyticsSnapshotOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? configuredInterval =
            configuration[
                "AutoFpl:Analytics:SnapshotPollIntervalMinutes"];
        string configuredPath =
            configuration["AutoFpl:Analytics:SnapshotPath"]
            ?? DefaultSnapshotPath;
        if (string.IsNullOrWhiteSpace(configuredInterval))
        {
            return new(null, configuredPath);
        }
        if (!int.TryParse(
                configuredInterval,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int minutes)
            || minutes is < MinimumIntervalMinutes
                or > MaximumIntervalMinutes)
        {
            throw new InvalidOperationException(
                "AutoFpl:Analytics:SnapshotPollIntervalMinutes must be an "
                + $"integer from {MinimumIntervalMinutes} through "
                + $"{MaximumIntervalMinutes}.");
        }
        if (string.IsNullOrWhiteSpace(configuredPath)
            || configuredPath.Length > 4096
            || !Path.IsPathFullyQualified(configuredPath))
        {
            throw new InvalidOperationException(
                "AutoFpl:Analytics:SnapshotPath must be a non-empty "
                + "absolute path of at most 4096 characters.");
        }
        return new(
            TimeSpan.FromMinutes(minutes),
            Path.GetFullPath(configuredPath));
    }
}
