using AutoFpl.Api.Intelligence;
using AutoFpl.Api.Sources;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class ResearchSourceRefreshSignalTests
{
    [Fact]
    public void Equal_polling_intervals_coalesce_official_capture_requests()
    {
        ResearchSourceRefreshSignal signal = CreateSignal("360", "360");

        Assert.True(signal.Enabled);
        signal.RequestAfterOfficialCapture();
        signal.RequestAfterOfficialCapture();

        Assert.True(signal.ConsumeRequest());
        Assert.False(signal.ConsumeRequest());
    }

    [Theory]
    [InlineData(null, "360")]
    [InlineData("360", null)]
    [InlineData("360", "180")]
    public void Disabled_or_different_cadences_keep_independent_polling(
        string? officialMinutes,
        string? researchMinutes)
    {
        ResearchSourceRefreshSignal signal = CreateSignal(
            officialMinutes,
            researchMinutes);

        Assert.False(signal.Enabled);
        signal.RequestAfterOfficialCapture();

        Assert.False(signal.ConsumeRequest());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    [InlineData(120, 60)]
    public void Poller_checks_for_a_signal_at_most_once_per_minute(
        int secondsUntilRefresh,
        int expectedDelaySeconds)
    {
        DateTimeOffset now = new(2026, 8, 4, 7, 0, 0, TimeSpan.Zero);

        TimeSpan delay = ResearchSourcePoller.DelayUntilNextCheck(
            now,
            now.AddSeconds(secondsUntilRefresh));

        Assert.Equal(TimeSpan.FromSeconds(expectedDelaySeconds), delay);
    }

    private static ResearchSourceRefreshSignal CreateSignal(
        string? officialMinutes,
        string? researchMinutes)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:Research:OfficialFplPollIntervalMinutes"] =
                        officialMinutes,
                    ["AutoFpl:Research:ResearchSourcePollIntervalMinutes"] =
                        researchMinutes,
                })
            .Build();
        return new ResearchSourceRefreshSignal(
            OfficialFplPollingOptions.FromConfiguration(configuration),
            ResearchSourcePollingOptions.FromConfiguration(configuration));
    }
}
