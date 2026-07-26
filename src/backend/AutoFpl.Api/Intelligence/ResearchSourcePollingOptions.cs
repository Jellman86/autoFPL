namespace AutoFpl.Api.Intelligence;

public sealed record ResearchSourcePollingOptions
{
    private ResearchSourcePollingOptions(TimeSpan? interval)
    {
        Interval = interval;
    }

    public TimeSpan? Interval { get; }

    public bool Enabled => Interval is not null;

    public static ResearchSourcePollingOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? configured =
            configuration["AutoFpl:Research:ResearchSourcePollIntervalMinutes"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new ResearchSourcePollingOptions((TimeSpan?)null);
        }

        if (!int.TryParse(
                configured,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int minutes)
            || minutes
                is < Sources.FplFormForecastPollingOptions.MinimumIntervalMinutes
                or > Sources.FplFormForecastPollingOptions.MaximumIntervalMinutes)
        {
            throw new InvalidOperationException(
                "AutoFpl:Research:ResearchSourcePollIntervalMinutes must be an "
                + "integer from "
                + $"{Sources.FplFormForecastPollingOptions.MinimumIntervalMinutes} "
                + "through "
                + $"{Sources.FplFormForecastPollingOptions.MaximumIntervalMinutes}.");
        }

        return new(TimeSpan.FromMinutes(minutes));
    }
}
