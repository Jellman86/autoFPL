using System.Net.Http.Headers;

using AutoFpl.Contracts.Sources;

namespace AutoFpl.Api.Sources;

public sealed class OfficialFplOutcomeImporter
{
    public const string SourceKey = "official-fpl-api-event-live/v1";
    public const int MaximumResponseBytes = 4 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly OfficialFplImporter _referenceImporter;
    private readonly OfficialFplCaptureStore _captureStore;
    private readonly OfficialFplOutcomeStore _outcomeStore;
    private readonly TimeProvider _timeProvider;

    public OfficialFplOutcomeImporter(
        HttpClient httpClient,
        OfficialFplImporter referenceImporter,
        OfficialFplCaptureStore captureStore,
        OfficialFplOutcomeStore outcomeStore,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _referenceImporter =
            referenceImporter ?? throw new ArgumentNullException(nameof(referenceImporter));
        _captureStore =
            captureStore ?? throw new ArgumentNullException(nameof(captureStore));
        _outcomeStore =
            outcomeStore ?? throw new ArgumentNullException(nameof(outcomeStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public static Uri GetLiveUri(int gameweek)
    {
        if (gameweek is < 1 or > 38)
        {
            throw new ArgumentOutOfRangeException(nameof(gameweek));
        }

        return new($"https://fantasy.premierleague.com/api/event/{gameweek}/live/");
    }

    public async Task<OfficialFplOutcomeCaptureDocument> ImportLatestAsync(
        int gameweek,
        CancellationToken cancellationToken = default)
    {
        OfficialFplCaptureDocument referenceCapture =
            await _referenceImporter.ImportLatestAsync(cancellationToken);
        return await ImportForReferenceAsync(
            referenceCapture,
            gameweek,
            cancellationToken);
    }

    public async Task<OfficialFplOutcomeCaptureDocument> ImportForReferenceAsync(
        OfficialFplCaptureDocument referenceCapture,
        int gameweek,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referenceCapture);
        OfficialFplOutcomeContext context =
            await _captureStore.GetOutcomeContextAsync(
                referenceCapture.CaptureId,
                gameweek,
                cancellationToken)
            ?? throw Invalid("the latest reference capture does not contain the requested Gameweek.");
        EnsureFinal(context);

        Uri liveUri = GetLiveUri(gameweek);
        byte[] liveJson = await FetchJsonAsync(liveUri, cancellationToken);
        return await ImportCapturedPayloadAsync(
            referenceCapture,
            gameweek,
            liveJson,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public async Task<OfficialFplOutcomeCaptureDocument> ImportCapturedPayloadAsync(
        OfficialFplCaptureDocument referenceCapture,
        int gameweek,
        byte[] liveJson,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referenceCapture);
        ArgumentNullException.ThrowIfNull(liveJson);
        if (retrievedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The retrieval time must be expressed as UTC.",
                nameof(retrievedAtUtc));
        }

        if (liveJson.Length > MaximumResponseBytes)
        {
            throw Invalid("payload exceeds the supported response size.");
        }

        OfficialFplOutcomePayload payload = OfficialFplOutcomePayloadParser.Parse(liveJson);
        return await _outcomeStore.SaveAsync(
            referenceCapture,
            gameweek,
            payload,
            GetLiveUri(gameweek),
            retrievedAtUtc,
            cancellationToken);
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
            throw Invalid("provider returned an unsupported content type.");
        }

        long? declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength is > MaximumResponseBytes)
        {
            throw Invalid("payload exceeds the supported response size.");
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
                throw Invalid("payload exceeds the supported response size.");
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return content.ToArray();
    }

    private static void EnsureFinal(OfficialFplOutcomeContext context)
    {
        if (!context.EventFinished || !context.DataChecked)
        {
            throw Invalid("the official event is not finished and data-checked.");
        }

        if (context.FixtureCount == 0)
        {
            throw Invalid("the latest reference capture has no Gameweek fixtures.");
        }

        if (context.FinishedFixtureCount != context.FixtureCount)
        {
            throw Invalid("not all official Gameweek fixtures are finished.");
        }
    }

    private static OfficialFplPayloadException Invalid(string message) =>
        new($"Official FPL outcome validation failed: {message}");
}
