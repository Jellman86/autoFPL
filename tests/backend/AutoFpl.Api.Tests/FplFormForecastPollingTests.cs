using AutoFpl.Api.Sources;
using AutoFpl.Api.Intelligence;

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
    public void Research_sources_use_the_same_bounded_interval_contract()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:Research:ResearchSourcePollIntervalMinutes"] = "360",
                })
            .Build();

        ResearchSourcePollingOptions options =
            ResearchSourcePollingOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromHours(6), options.Interval);
    }

    [Fact]
    public void Fbref_match_logs_use_a_bounded_interval_and_batch()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:Research:FbrefMatchLogCaptureIntervalMinutes"] =
                        "360",
                    ["AutoFpl:Research:FbrefMatchLogCaptureBatchSize"] = "3",
                })
            .Build();

        FbrefMatchLogPollingOptions options =
            FbrefMatchLogPollingOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromHours(6), options.Interval);
        Assert.Equal(3, options.BatchSize);
    }

    [Fact]
    public void Fbref_match_log_polling_defaults_to_the_maximum_bounded_batch()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:Research:FbrefMatchLogCaptureIntervalMinutes"] =
                        "60",
                })
            .Build();

        FbrefMatchLogPollingOptions options =
            FbrefMatchLogPollingOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.Equal(5, options.BatchSize);
    }

    [Fact]
    public async Task Fbref_match_log_polling_isolates_a_failed_coverage_read()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:Research:FbrefMatchLogCaptureIntervalMinutes"] =
                        "60",
                })
            .Build();
        FbrefMatchLogPollingOptions options =
            FbrefMatchLogPollingOptions.FromConfiguration(configuration);
        var capture = new FbrefMatchLogBatchCapture(
            _ => throw new ResearchSourceSnapshotException("expected"),
            (_, _, _) => throw new InvalidOperationException("not reached"));
        var poller = new FbrefMatchLogPoller(
            capture,
            options,
            TimeProvider.System);

        AutoFpl.Contracts.Intelligence.FbrefMatchLogBatchCaptureDocument? result =
            await poller.CaptureOnceAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("59", null)]
    [InlineData("1441", null)]
    [InlineData("60", "0")]
    [InlineData("60", "6")]
    [InlineData(null, "5")]
    public void Invalid_fbref_match_log_polling_configuration_fails_startup(
        string? interval,
        string? batchSize)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:Research:FbrefMatchLogCaptureIntervalMinutes"] =
                        interval,
                    ["AutoFpl:Research:FbrefMatchLogCaptureBatchSize"] =
                        batchSize,
                })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => FbrefMatchLogPollingOptions.FromConfiguration(configuration));
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

    [Fact]
    public void Final_outcome_candidates_catch_up_oldest_gaps_then_recheck_latest()
    {
        Assert.Empty(
            OfficialFplPoller.SelectOutcomeCandidates(
                null,
                new HashSet<int>(),
                OfficialFplPoller.MaximumOutcomeImportsPerPoll));
        Assert.Equal(
            [1, 2, 3],
            OfficialFplPoller.SelectOutcomeCandidates(
                5,
                new HashSet<int>([5]),
                OfficialFplPoller.MaximumOutcomeImportsPerPoll));
        Assert.Equal(
            [1, 2, 5],
            OfficialFplPoller.SelectOutcomeCandidates(
                5,
                new HashSet<int>([3, 4, 5]),
                OfficialFplPoller.MaximumOutcomeImportsPerPoll));
        Assert.Equal(
            [5],
            OfficialFplPoller.SelectOutcomeCandidates(
                5,
                new HashSet<int>([1, 2, 3, 4, 5]),
                OfficialFplPoller.MaximumOutcomeImportsPerPoll));
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
