using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AutoFpl.Api.Intelligence;

public sealed record ByparrCaptureResult(
    Uri FinalUri,
    int StatusCode,
    string Content,
    string ContentTrust,
    string TransportVersion);

public sealed class ByparrClient
{
    public const string TransportKey = "byparr";

    private const int ChallengeTimeoutSeconds = 60;
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private const int MaximumContentBytes = 6 * 1024 * 1024;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
        AllowDuplicateProperties = false,
    };

    private readonly HttpClient _httpClient;

    public ByparrClient(HttpClient httpClient)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (_httpClient.BaseAddress is null)
        {
            throw new ArgumentException(
                "The Byparr HTTP client requires a base address.",
                nameof(httpClient));
        }
    }

    public async Task<ByparrCaptureResult> CaptureAsync(
        ResearchSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!StringComparer.Ordinal.Equals(
                source.TransportKey,
                TransportKey))
        {
            throw Invalid("Byparr cannot capture a source assigned to another transport.");
        }

        try
        {
            byte[] requestBody = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    cmd = "request.get",
                    url = source.CanonicalUri.AbsoluteUri,
                    max_timeout = ChallengeTimeoutSeconds,
                });
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1");
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new ByteArrayContent(requestBody);
            request.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/json");

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "application/json"))
            {
                throw Invalid("Byparr returned an unexpected HTTP response.");
            }

            byte[] body = await ReadBoundedAsync(
                response.Content,
                cancellationToken);
            using JsonDocument document = JsonDocument.Parse(
                body,
                DocumentOptions);
            return Parse(document.RootElement, source);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ResearchSourceSnapshotException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or InvalidOperationException)
        {
            throw new ResearchSourceSnapshotException(
                "Byparr could not capture the research source.",
                exception);
        }
    }

    private static ByparrCaptureResult Parse(
        JsonElement root,
        ResearchSourceDefinition source)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("status", out JsonElement transportStatus)
            || transportStatus.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(
                transportStatus.GetString(),
                "ok")
            || !root.TryGetProperty("version", out JsonElement versionElement)
            || versionElement.ValueKind != JsonValueKind.String
            || !IsValidVersion(versionElement.GetString())
            || !root.TryGetProperty("solution", out JsonElement solution)
            || solution.ValueKind != JsonValueKind.Object
            || !solution.TryGetProperty("url", out JsonElement url)
            || url.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? finalUri)
            || !source.AllowsFinalUri(finalUri)
            || !solution.TryGetProperty("status", out JsonElement sourceStatus)
            || sourceStatus.ValueKind != JsonValueKind.Number
            || !sourceStatus.TryGetInt32(out int parsedStatus)
            || parsedStatus != StatusCodes.Status200OK
            || !solution.TryGetProperty("response", out JsonElement content)
            || content.ValueKind != JsonValueKind.String)
        {
            throw Invalid("Byparr returned an invalid or unexpected capture result.");
        }

        string value = content.GetString() ?? string.Empty;
        int contentBytes = Encoding.UTF8.GetByteCount(value);
        if (contentBytes is <= 0 or > MaximumContentBytes
            || !source.HasRequiredContent(value))
        {
            throw Invalid("Byparr returned unsupported research content.");
        }

        return new(
            finalUri,
            parsedStatus,
            value,
            "untrusted_remote_content",
            $"{TransportKey}/{versionElement.GetString()}");
    }

    private static bool IsValidVersion(string? version) =>
        version is { Length: >= 1 and <= 40 }
        && version.All(
            character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '+');

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw Invalid("Byparr response exceeded the supported size.");
        }

        await using Stream stream =
            await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > MaximumResponseBytes)
            {
                throw Invalid("Byparr response exceeded the supported size.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private static ResearchSourceSnapshotException Invalid(string message) =>
        new(message);
}
