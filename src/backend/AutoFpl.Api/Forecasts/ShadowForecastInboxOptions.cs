using System.Globalization;

namespace AutoFpl.Api.Forecasts;

public sealed record ShadowForecastInboxOptions
{
    public const int MinimumIntervalMinutes = 1;
    public const int MaximumIntervalMinutes = 60;
    public const string DefaultInboxPath = "/analytics-inbox";

    private ShadowForecastInboxOptions(
        TimeSpan? interval,
        string inboxPath)
    {
        Interval = interval;
        InboxPath = inboxPath;
    }

    public TimeSpan? Interval { get; }

    public string InboxPath { get; }

    public bool Enabled => Interval is not null;

    public static ShadowForecastInboxOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? configuredInterval =
            configuration[
                "AutoFpl:Analytics:ShadowInboxPollIntervalMinutes"];
        string configuredPath =
            configuration["AutoFpl:Analytics:ShadowInboxPath"]
            ?? DefaultInboxPath;
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
                "AutoFpl:Analytics:ShadowInboxPollIntervalMinutes must be an "
                + $"integer from {MinimumIntervalMinutes} through "
                + $"{MaximumIntervalMinutes}.");
        }
        if (string.IsNullOrWhiteSpace(configuredPath)
            || configuredPath.Length > 4096
            || !Path.IsPathFullyQualified(configuredPath))
        {
            throw new InvalidOperationException(
                "AutoFpl:Analytics:ShadowInboxPath must be a non-empty "
                + "absolute path of at most 4096 characters.");
        }
        return new(
            TimeSpan.FromMinutes(minutes),
            Path.GetFullPath(configuredPath));
    }
}
