namespace AutoFpl.Api.Sources;

public sealed record OfficialFplPollingOptions
{
    private OfficialFplPollingOptions(TimeSpan? interval)
    {
        Interval = interval;
    }

    public TimeSpan? Interval { get; }

    public bool Enabled => Interval is not null;

    public static OfficialFplPollingOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? configured =
            configuration["AutoFpl:Research:OfficialFplPollIntervalMinutes"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new OfficialFplPollingOptions((TimeSpan?)null);
        }

        if (!int.TryParse(
                configured,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int minutes)
            || minutes is < FplFormForecastPollingOptions.MinimumIntervalMinutes
                or > FplFormForecastPollingOptions.MaximumIntervalMinutes)
        {
            throw new InvalidOperationException(
                "AutoFpl:Research:OfficialFplPollIntervalMinutes must be an integer "
                + $"from {FplFormForecastPollingOptions.MinimumIntervalMinutes} through "
                + $"{FplFormForecastPollingOptions.MaximumIntervalMinutes}.");
        }

        return new(TimeSpan.FromMinutes(minutes));
    }
}
