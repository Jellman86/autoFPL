using AutoFpl.Api.Sources;

using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class FplFormForecastPollingTests
{
    [Fact]
    public void Missing_interval_disables_background_collection()
    {
        FplFormForecastPollingOptions options =
            FplFormForecastPollingOptions.FromConfiguration(
                new ConfigurationBuilder().Build());

        Assert.False(options.Enabled);
        Assert.Null(options.Interval);
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("360", 360)]
    [InlineData("1440", 1440)]
    public void Bounded_interval_enables_background_collection(
        string configured,
        int expectedMinutes)
    {
        FplFormForecastPollingOptions options =
            FplFormForecastPollingOptions.FromConfiguration(
                Configuration(configured));

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), options.Interval);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("59")]
    [InlineData("1441")]
    [InlineData("-60")]
    [InlineData("60.5")]
    [InlineData("true")]
    public void Invalid_interval_fails_startup_configuration(string configured)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => FplFormForecastPollingOptions.FromConfiguration(
                Configuration(configured)));

        Assert.Contains(
            "must be an integer from 60 through 1440",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Official_source_uses_the_same_bounded_interval_contract()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:Research:OfficialFplPollIntervalMinutes"] = "360",
                })
            .Build();

        OfficialFplPollingOptions options =
            OfficialFplPollingOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromHours(6), options.Interval);
    }

    [Fact]
    public void Restarts_wait_for_the_remaining_interval()
    {
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        TimeSpan interval = TimeSpan.FromHours(6);

        Assert.Equal(
            TimeSpan.Zero,
            FplFormForecastPoller.DelayUntilNextCheck(null, now, interval));
        Assert.Equal(
            TimeSpan.FromHours(4),
            FplFormForecastPoller.DelayUntilNextCheck(
                now.AddHours(-2),
                now,
                interval));
        Assert.Equal(
            TimeSpan.Zero,
            FplFormForecastPoller.DelayUntilNextCheck(
                now.AddHours(-6),
                now,
                interval));
        Assert.Equal(
            interval,
            FplFormForecastPoller.DelayUntilNextCheck(
                now.AddMinutes(1),
                now,
                interval));
    }

    private static IConfiguration Configuration(string interval) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:Research:FplFormPollIntervalMinutes"] = interval,
                })
            .Build();
}
