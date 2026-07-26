using System.Net.Http.Headers;
using System.Security.Cryptography;

using AutoFpl.Contracts.Sources;

namespace AutoFpl.Api.Sources;

public sealed class HistoricalFplSeasonImporter
{
    public const string SourceKey = "vaastav-fpl-historical/v1";
    public const string SeasonCode = "2025-26";
    public const string SourceRevision = "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a";
    public const string ExpectedPlayersSha256 =
        "412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b";
    public const string ExpectedGameweeksSha256 =
        "0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d";
    public const int ExpectedPlayerCount = 841;
    public const int ExpectedPlayerGameweekCount = 29_747;
    public const int MaximumResponseBytes = 6 * 1024 * 1024;

    public static readonly Uri PlayersUri =
        new(
            $"https://raw.githubusercontent.com/vaastav/Fantasy-Premier-League/"
            + $"{SourceRevision}/data/{SeasonCode}/players_raw.csv");
    public static readonly Uri GameweeksUri =
        new(
            $"https://raw.githubusercontent.com/vaastav/Fantasy-Premier-League/"
            + $"{SourceRevision}/data/{SeasonCode}/gws/merged_gw.csv");
    public static readonly DateTimeOffset PublishedAtUtc =
        new(2026, 6, 17, 12, 19, 44, TimeSpan.Zero);

    private readonly HttpClient _httpClient;
    private readonly HistoricalFplSeasonStore _store;
    private readonly TimeProvider _timeProvider;

    public HistoricalFplSeasonImporter(
        HttpClient httpClient,
        HistoricalFplSeasonStore store,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<HistoricalFplSeasonCaptureDocument> ImportAsync(
        CancellationToken cancellationToken = default)
    {
        Task<byte[]> playersTask = FetchCsvAsync(PlayersUri, cancellationToken);
        Task<byte[]> gameweeksTask = FetchCsvAsync(GameweeksUri, cancellationToken);
        await Task.WhenAll(playersTask, gameweeksTask);
        return await ImportCapturedPayloadAsync(
            await playersTask,
            await gameweeksTask,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    internal async Task<HistoricalFplSeasonCaptureDocument> ImportCapturedPayloadAsync(
        byte[] playersCsv,
        byte[] gameweeksCsv,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (retrievedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The retrieval time must be expressed as UTC.",
                nameof(retrievedAtUtc));
        }

        if (retrievedAtUtc < PublishedAtUtc)
        {
            throw new HistoricalFplSeasonPayloadException(
                "Historical FPL archive retrieval predates the pinned publication.");
        }

        if (playersCsv.Length > MaximumResponseBytes
            || gameweeksCsv.Length > MaximumResponseBytes)
        {
            throw new HistoricalFplSeasonPayloadException(
                "Historical FPL archive exceeds the supported response size.");
        }

        string playersSha256 =
            Convert.ToHexString(SHA256.HashData(playersCsv)).ToLowerInvariant();
        string gameweeksSha256 =
            Convert.ToHexString(SHA256.HashData(gameweeksCsv)).ToLowerInvariant();
        if (!StringComparer.Ordinal.Equals(
                playersSha256,
                ExpectedPlayersSha256)
            || !StringComparer.Ordinal.Equals(
                gameweeksSha256,
                ExpectedGameweeksSha256))
        {
            throw new HistoricalFplSeasonPayloadException(
                "Historical FPL archive does not match the pinned file hashes.");
        }

        HistoricalFplSeasonPayload payload =
            HistoricalFplSeasonPayloadParser.Parse(playersCsv, gameweeksCsv);
        if (payload.Players.Count != ExpectedPlayerCount
            || payload.PlayerGameweeks.Count != ExpectedPlayerGameweekCount)
        {
            throw new HistoricalFplSeasonPayloadException(
                "Historical FPL archive does not match the pinned row counts.");
        }

        return await _store.SaveAsync(payload, retrievedAtUtc, cancellationToken);
    }

    private async Task<byte[]> FetchCsvAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/csv"));
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null
            && !StringComparer.OrdinalIgnoreCase.Equals(mediaType, "text/csv")
            && !StringComparer.OrdinalIgnoreCase.Equals(mediaType, "text/plain")
            && !StringComparer.OrdinalIgnoreCase.Equals(
                mediaType,
                "application/octet-stream"))
        {
            throw new HistoricalFplSeasonPayloadException(
                "Historical FPL archive returned an unsupported content type.");
        }

        long? declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength is > MaximumResponseBytes)
        {
            throw new HistoricalFplSeasonPayloadException(
                "Historical FPL archive exceeds the supported response size.");
        }

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
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
                throw new HistoricalFplSeasonPayloadException(
                    "Historical FPL archive exceeds the supported response size.");
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return content.ToArray();
    }
}
