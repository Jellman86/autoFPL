using AutoFpl.Api.Forecasts;

namespace AutoFpl.Api.Selections;

public sealed class SelectionScenarioScoreInboxPoller : BackgroundService
{
    public const string FilePrefix = "selection-scenario-score-capture-";
    public const string PendingSuffix = ".json";
    public const string ImportedSuffix = ".imported";
    public const string RejectedSuffix = ".rejected";

    private readonly SelectionScenarioScoreShadowImporter _importer;
    private readonly ShadowForecastInboxOptions _options;
    private readonly TimeProvider _timeProvider;

    public SelectionScenarioScoreInboxPoller(
        SelectionScenarioScoreShadowImporter importer,
        ShadowForecastInboxOptions options,
        TimeProvider timeProvider)
    {
        _importer =
            importer ?? throw new ArgumentNullException(nameof(importer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = _options.Interval
            ?? throw new InvalidOperationException(
                "The score inbox poller cannot run without an interval.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ImportOnceAsync(stoppingToken);
            }
            catch (Exception exception)
                when (exception is IOException
                    or UnauthorizedAccessException)
            {
                // The serving application remains available while the private
                // inbox is temporarily unavailable.
            }
            await Task.Delay(interval, _timeProvider, stoppingToken);
        }
    }

    public async Task<string> ImportOnceAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.InboxPath);
        string? path = Directory
            .EnumerateFiles(
                _options.InboxPath,
                $"{FilePrefix}*{PendingSuffix}",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        if (path is null)
        {
            return "empty";
        }
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return TryArchive(path, RejectedSuffix)
                ? "rejected"
                : "rejected-pending-archive";
        }
        try
        {
            await _importer.ImportFileAsync(path, cancellationToken);
            return TryArchive(path, ImportedSuffix)
                ? "imported"
                : "imported-pending-archive";
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return TryArchive(path, RejectedSuffix)
                ? "rejected"
                : "rejected-pending-archive";
        }
    }

    private static bool TryArchive(string path, string suffix)
    {
        string target = path + suffix;
        try
        {
            if (File.Exists(target))
            {
                return false;
            }
            File.Move(path, target);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
