using System.Net.Http.Headers;

using AutoFpl.Contracts.Sources;

namespace AutoFpl.Api.Sources;

public sealed class FplFormForecastImporter
{
    public const string SourceKey = "fpl-form-public-forecast/v1";
    public const int MaximumResponseBytes = 128 * 1024 * 1024;

    public static readonly Uri ForecastUri =
        new("https://www.fplform.com/fpl-predicted-points.php");

    private readonly HttpClient _httpClient;
    private readonly FplFormForecastStore _store;
    private readonly TimeProvider _timeProvider;

    public FplFormForecastImporter(
        HttpClient httpClient,
        FplFormForecastStore store,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<FplFormForecastCaptureDocument> ImportLatestAsync(
        CancellationToken cancellationToken = default)
    {
        byte[] html = await FetchHtmlAsync(cancellationToken);
        return await ImportCapturedHtmlAsync(
            html,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public async Task<FplFormForecastCaptureDocument> ImportCapturedHtmlAsync(
        byte[] html,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (retrievedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The retrieval time must be expressed as UTC.",
                nameof(retrievedAtUtc));
        }

        if (html.Length > MaximumResponseBytes)
        {
            throw new FplFormForecastPayloadException(
                "FPL Form response exceeds the supported response size.");
        }

        FplFormForecastPayload payload = FplFormForecastPayloadParser.Parse(html);
        return await _store.SaveAsync(payload, retrievedAtUtc, cancellationToken);
    }

    private async Task<byte[]> FetchHtmlAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ForecastUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (!StringComparer.OrdinalIgnoreCase.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "text/html"))
        {
            throw new FplFormForecastPayloadException(
                "FPL Form returned an unsupported content type.");
        }

        long? declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength is > MaximumResponseBytes)
        {
            throw new FplFormForecastPayloadException(
                "FPL Form response exceeds the supported response size.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var content = new MemoryStream(
            declaredLength is > 0
                ? checked((int)declaredLength.Value)
                : 0);
        var buffer = new byte[64 * 1024];
        int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > MaximumResponseBytes)
            {
                throw new FplFormForecastPayloadException(
                    "FPL Form response exceeds the supported response size.");
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return content.ToArray();
    }
}
