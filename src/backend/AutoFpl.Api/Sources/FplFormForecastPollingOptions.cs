namespace AutoFpl.Api.Sources;

public sealed record FplFormForecastPollingOptions
{
    public const int MinimumIntervalMinutes = 60;
    public const int MaximumIntervalMinutes = 24 * 60;

    private FplFormForecastPollingOptions(TimeSpan? interval)
    {
        Interval = interval;
    }

    public TimeSpan? Interval { get; }

    public bool Enabled => Interval is not null;

    public static FplFormForecastPollingOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? configured =
            configuration["AutoFpl:Research:FplFormPollIntervalMinutes"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new FplFormForecastPollingOptions((TimeSpan?)null);
        }

        if (!int.TryParse(
                configured,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int minutes)
            || minutes is < MinimumIntervalMinutes or > MaximumIntervalMinutes)
        {
            throw new InvalidOperationException(
                "AutoFpl:Research:FplFormPollIntervalMinutes must be an integer "
                + $"from {MinimumIntervalMinutes} through {MaximumIntervalMinutes}.");
        }

        return new(TimeSpan.FromMinutes(minutes));
    }
}
