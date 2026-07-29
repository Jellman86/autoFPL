using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class AnalyticsSnapshotPublisherTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            $"autofpl-analytics-snapshot-{Guid.NewGuid():N}");

    [Fact]
    public async Task Publisher_creates_standalone_snapshot_only_when_source_changes()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_root);
        string sourcePath = Path.Combine(_root, "source", "autofpl.db");
        string snapshotPath =
            Path.Combine(_root, "snapshot", "autofpl.db");
        DatabaseOptions databaseOptions = Options(sourcePath);
        var store = new DecisionSnapshotStore(databaseOptions);
        await store.MigrateAsync(cancellationToken);
        AnalyticsSnapshotOptions snapshotOptions =
            SnapshotOptions(snapshotPath, "1");
        var publisher = new AnalyticsSnapshotPublisher(
            store,
            databaseOptions,
            snapshotOptions,
            TimeProvider.System);

        Assert.Equal(
            "published",
            await publisher.PublishOnceAsync(cancellationToken));
        Assert.True(File.Exists(snapshotPath));
        Assert.Empty(
            Directory.EnumerateFiles(
                Path.GetDirectoryName(snapshotPath)!,
                ".autofpl-snapshot-*.tmp"));
        Assert.Equal(
            "unchanged",
            await publisher.PublishOnceAsync(cancellationToken));

        var readOnly = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        await using (var snapshot =
            new SqliteConnection(readOnly.ToString()))
        {
            await snapshot.OpenAsync(cancellationToken);
            await using SqliteCommand journal = snapshot.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode;";
            Assert.Equal(
                "delete",
                Assert.IsType<string>(
                    await journal.ExecuteScalarAsync(
                        cancellationToken)),
                ignoreCase: true);
            await using SqliteCommand integrity =
                snapshot.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            Assert.Equal(
                "ok",
                Assert.IsType<string>(
                    await integrity.ExecuteScalarAsync(
                        cancellationToken)));
        }

        await using (var source =
            new SqliteConnection(databaseOptions.ConnectionString))
        {
            await source.OpenAsync(cancellationToken);
            await using SqliteCommand change = source.CreateCommand();
            change.CommandText =
                """
                INSERT INTO schema_migrations (
                    version, name, applied_at_utc
                )
                VALUES (
                    999, 'test-change', '2026-07-29T10:00:00Z'
                );
                """;
            await change.ExecuteNonQueryAsync(cancellationToken);
        }

        Assert.Equal(
            "published",
            await publisher.PublishOnceAsync(cancellationToken));
        await using var updated =
            new SqliteConnection(readOnly.ToString());
        await updated.OpenAsync(cancellationToken);
        await using SqliteCommand latest = updated.CreateCommand();
        latest.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Assert.Equal(
            999L,
            await latest.ExecuteScalarAsync(cancellationToken));
    }

    [Fact]
    public void Options_are_disabled_without_interval_and_validate_path()
    {
        AnalyticsSnapshotOptions disabled =
            AnalyticsSnapshotOptions.FromConfiguration(
                new ConfigurationBuilder().Build());
        Assert.False(disabled.Enabled);

        IConfiguration invalid =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [
                            "AutoFpl:Analytics:"
                            + "SnapshotPollIntervalMinutes"
                        ] = "0",
                        ["AutoFpl:Analytics:SnapshotPath"] =
                            Path.Combine(_root, "snapshot.db"),
                    })
                .Build();
        Assert.Throws<InvalidOperationException>(
            () => AnalyticsSnapshotOptions.FromConfiguration(invalid));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static DatabaseOptions Options(string databasePath)
    {
        IConfiguration configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["AutoFpl:DatabasePath"] = databasePath,
                    })
                .Build();
        return DatabaseOptions.FromConfiguration(configuration);
    }

    private static AnalyticsSnapshotOptions SnapshotOptions(
        string path,
        string interval)
    {
        IConfiguration configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [
                            "AutoFpl:Analytics:"
                            + "SnapshotPollIntervalMinutes"
                        ] = interval,
                        ["AutoFpl:Analytics:SnapshotPath"] = path,
                    })
                .Build();
        return AnalyticsSnapshotOptions.FromConfiguration(configuration);
    }
}
