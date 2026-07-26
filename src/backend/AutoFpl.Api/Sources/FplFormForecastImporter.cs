using AutoFpl.Contracts.Sources;

namespace AutoFpl.Api.Sources;

public sealed class FplFormForecastImporter
{
    public const string SourceKey = "fpl-form-public-forecast/v1";
    public const int MaximumResponseBytes = 128 * 1024 * 1024;

    public static readonly Uri ForecastUri =
        new("https://www.fplform.com/fpl-predicted-points.php");

    private readonly PlaywrightMcpFplFormCollector _collector;
    private readonly FplFormForecastStore _store;
    private readonly TimeProvider _timeProvider;

    public FplFormForecastImporter(
        PlaywrightMcpFplFormCollector collector,
        FplFormForecastStore store,
        TimeProvider timeProvider)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<FplFormForecastCaptureDocument> ImportLatestAsync(
        CancellationToken cancellationToken = default)
    {
        byte[] evidence = await _collector.CaptureAsync(cancellationToken);
        return await ImportExtractedEvidenceAsync(
            evidence,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public async Task<FplFormForecastCaptureDocument> ImportExtractedEvidenceAsync(
        byte[] evidence,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ValidateRetrievalTime(retrievedAtUtc);
        FplFormForecastPayload payload =
            FplFormForecastPayloadParser.ParseExtractedEvidence(evidence);
        return await _store.SaveAsync(payload, retrievedAtUtc, cancellationToken);
    }

    public async Task<FplFormForecastCaptureDocument> ImportCapturedHtmlAsync(
        byte[] html,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ValidateRetrievalTime(retrievedAtUtc);

        if (html.Length > MaximumResponseBytes)
        {
            throw new FplFormForecastPayloadException(
                "FPL Form response exceeds the supported response size.");
        }

        FplFormForecastPayload payload = FplFormForecastPayloadParser.Parse(html);
        return await _store.SaveAsync(payload, retrievedAtUtc, cancellationToken);
    }

    private static void ValidateRetrievalTime(DateTimeOffset retrievedAtUtc)
    {
        if (retrievedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The retrieval time must be expressed as UTC.",
                nameof(retrievedAtUtc));
        }
    }
}
