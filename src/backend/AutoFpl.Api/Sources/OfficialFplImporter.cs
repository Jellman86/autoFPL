using System.Net.Http.Headers;

using AutoFpl.Contracts.Sources;

namespace AutoFpl.Api.Sources;

public sealed class OfficialFplImporter
{
    public const string SourceKey = "official-fpl-api/v1";
    public const int MaximumResponseBytes = 4 * 1024 * 1024;

    public static readonly Uri BootstrapUri =
        new("https://fantasy.premierleague.com/api/bootstrap-static/");
    public static readonly Uri FixturesUri =
        new("https://fantasy.premierleague.com/api/fixtures/");

    private readonly HttpClient _httpClient;
    private readonly OfficialFplCaptureStore _store;
    private readonly TimeProvider _timeProvider;

    public OfficialFplImporter(
        HttpClient httpClient,
        OfficialFplCaptureStore store,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<OfficialFplCaptureDocument> ImportLatestAsync(
        CancellationToken cancellationToken = default)
    {
        Task<byte[]> bootstrapTask = FetchJsonAsync(BootstrapUri, cancellationToken);
        Task<byte[]> fixturesTask = FetchJsonAsync(FixturesUri, cancellationToken);
        await Task.WhenAll(bootstrapTask, fixturesTask);

        return await ImportCapturedPayloadAsync(
            await bootstrapTask,
            await fixturesTask,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public async Task<OfficialFplCaptureDocument> ImportCapturedPayloadAsync(
        byte[] bootstrapJson,
        byte[] fixturesJson,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (retrievedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The retrieval time must be expressed as UTC.",
                nameof(retrievedAtUtc));
        }

        if (bootstrapJson.Length > MaximumResponseBytes
            || fixturesJson.Length > MaximumResponseBytes)
        {
            throw new OfficialFplPayloadException(
                "Official FPL payload exceeds the supported response size.");
        }

        OfficialFplPayload payload = OfficialFplPayloadParser.Parse(
            bootstrapJson,
            fixturesJson);
        return await _store.SaveAsync(payload, retrievedAtUtc, cancellationToken);
    }

    private async Task<byte[]> FetchJsonAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (!StringComparer.OrdinalIgnoreCase.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "application/json"))
        {
            throw new OfficialFplPayloadException(
                "Official FPL returned an unsupported content type.");
        }

        long? declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength is > MaximumResponseBytes)
        {
            throw new OfficialFplPayloadException(
                "Official FPL payload exceeds the supported response size.");
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
                throw new OfficialFplPayloadException(
                    "Official FPL payload exceeds the supported response size.");
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return content.ToArray();
    }
}
