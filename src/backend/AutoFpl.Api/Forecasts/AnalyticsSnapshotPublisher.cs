using AutoFpl.Api.Persistence;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Forecasts;

public sealed class AnalyticsSnapshotPublisher : BackgroundService
{
    private readonly DecisionSnapshotStore _store;
    private readonly DatabaseOptions _databaseOptions;
    private readonly AnalyticsSnapshotOptions _options;
    private readonly TimeProvider _timeProvider;
    private string? _publishedSourceIdentity;

    public AnalyticsSnapshotPublisher(
        DecisionSnapshotStore store,
        DatabaseOptions databaseOptions,
        AnalyticsSnapshotOptions options,
        TimeProvider timeProvider)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
        _databaseOptions = databaseOptions
            ?? throw new ArgumentNullException(nameof(databaseOptions));
        _options = options
            ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        TimeSpan interval = _options.Interval
            ?? throw new InvalidOperationException(
                "The analytics snapshot publisher cannot run without "
                + "an interval.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishOnceAsync(stoppingToken);
            }
            catch (Exception exception)
                when (exception is IOException
                    or InvalidDataException
                    or UnauthorizedAccessException
                    or SqliteException)
            {
                // Serving remains available while the private analytics
                // snapshot path is temporarily unavailable.
            }
            await Task.Delay(interval, _timeProvider, stoppingToken);
        }
    }

    public async Task<string> PublishOnceAsync(
        CancellationToken cancellationToken = default)
    {
        string sourceIdentity =
            await ReadSourceIdentityAsync(cancellationToken);
        if (StringComparer.Ordinal.Equals(
            sourceIdentity,
            _publishedSourceIdentity)
            && File.Exists(_options.SnapshotPath))
        {
            return "unchanged";
        }

        string? directory = Path.GetDirectoryName(
            _options.SnapshotPath);
        if (directory is null)
        {
            throw new InvalidOperationException(
                "The analytics snapshot path must identify a file.");
        }
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".autofpl-snapshot-{Guid.NewGuid():N}.tmp");
        try
        {
            await _store.BackupAsync(
                temporaryPath,
                cancellationToken);
            await MakeStandaloneAsync(
                temporaryPath,
                cancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporaryPath,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead);
            }
            File.Move(
                temporaryPath,
                _options.SnapshotPath,
                overwrite: true);
            _publishedSourceIdentity = sourceIdentity;
            return "published";
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<string> ReadSourceIdentityAsync(
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_databaseOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                CAST(COALESCE((
                    SELECT MAX(version) FROM schema_migrations
                ), 0) AS TEXT)
                || ':' || CAST(COALESCE((
                    SELECT MAX(capture_id) FROM official_fpl_captures
                ), 0) AS TEXT)
                || ':' || CAST(COALESCE((
                    SELECT MAX(artifact_id)
                    FROM baseline_forecast_artifacts
                ), 0) AS TEXT)
                || ':' || CAST(COALESCE((
                    SELECT MAX(forecast_artifact_id)
                    FROM multi_season_player_forecast_artifacts
                ), 0) AS TEXT)
                || ':' || CAST(COALESCE((
                    SELECT MAX(scenario_artifact_id)
                    FROM joint_scenario_shadow_artifacts
                ), 0) AS TEXT)
                || ':' || CAST(COALESCE((
                    SELECT MAX(selection_revision_id)
                    FROM selection_revisions
                ), 0) AS TEXT)
                || ':' || CAST(COALESCE((
                    SELECT MAX(score_artifact_id)
                    FROM selection_scenario_score_shadow_artifacts
                ), 0) AS TEXT)
                || ':' || CAST(COALESCE((
                    SELECT MAX(strategy_artifact_id)
                    FROM selection_role_strategy_shadow_artifacts
                ), 0) AS TEXT)
                || ':' || CAST(COALESCE((
                    SELECT MAX(initial_squad_artifact_id)
                    FROM initial_squad_quality_shadow_artifacts
                ), 0) AS TEXT)
                || ':' || CAST(COALESCE((
                    SELECT MAX(outcome_capture_id)
                    FROM official_fpl_outcome_captures
                ), 0) AS TEXT)
                || ':' || CAST(COALESCE((
                    SELECT MAX(capture_id)
                    FROM historical_fpl_season_captures
                ), 0) AS TEXT)
                || ':' || COALESCE((
                    SELECT locked_at_utc
                    FROM selection_revisions
                    ORDER BY selection_revision_id DESC
                    LIMIT 1
                ), '');
            """;
        return Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException(
                "SQLite did not return an analytics source identity.");
    }

    private static async Task MakeStandaloneAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = DatabaseOptions.BusyTimeoutSeconds,
        };
        await using var connection =
            new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand journal = connection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode = DELETE;";
        string journalMode =
            Convert.ToString(
                await journal.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
        if (!StringComparer.OrdinalIgnoreCase.Equals(
            journalMode,
            "delete"))
        {
            throw new InvalidDataException(
                "The analytics snapshot is not standalone.");
        }

        await using SqliteCommand integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        string result =
            Convert.ToString(
                await integrity.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
        if (!StringComparer.Ordinal.Equals(result, "ok"))
        {
            throw new InvalidDataException(
                "The analytics snapshot failed its integrity check.");
        }
    }
}
