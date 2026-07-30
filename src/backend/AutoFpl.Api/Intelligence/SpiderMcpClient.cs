using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AutoFpl.Api.Intelligence;

public sealed record SpiderScrapeResult(
    Uri FinalUri,
    int StatusCode,
    string Content,
    string ContentTrust);

public sealed class ResearchSourceSnapshotException : Exception
{
    public ResearchSourceSnapshotException(string message)
        : base(message)
    {
    }

    public ResearchSourceSnapshotException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class SpiderMcpClient
{
    public const string TransportKey = "spider-mcp";
    public const string TransportVersion =
        "spider-mcp/de35b3a9dd740542070fa2ee0e70bc804dde07ee";

    private const string ProtocolVersion = "2025-03-26";
    private const string SessionHeader = "Mcp-Session-Id";
    private const int MaximumMcpResponseBytes = 512 * 1024;
    private const int MaximumScrapeContentBytes = 128 * 1024;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
        AllowDuplicateProperties = false,
    };

    private readonly HttpClient _httpClient;

    public SpiderMcpClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (_httpClient.BaseAddress is null)
        {
            throw new ArgumentException(
                "The Spider MCP HTTP client requires a base address.",
                nameof(httpClient));
        }
    }

    public async Task<SpiderScrapeResult> ScrapeAsync(
        ResearchSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        string? sessionId = null;
        try
        {
            sessionId = await InitializeAsync(cancellationToken);
            await SendInitializedAsync(sessionId, cancellationToken);
            string output = await CallToolAsync(
                sessionId,
                "spider_scrape",
                new
                {
                    url = source.CanonicalUri.AbsoluteUri,
                    return_format = "markdown",
                    headless = source.RequiresRendering,
                    wait_for = source.WaitForSelector,
                },
                cancellationToken);
            return ParseScrapeResult(output, source);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
                "Spider MCP could not capture the research source.",
                exception);
        }
        finally
        {
            if (sessionId is not null)
            {
                await TryDeleteSessionAsync(sessionId);
            }
        }
    }

    private async Task<string> InitializeAsync(CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = ProtocolVersion,
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "autofpl-research-source-collector",
                        version = "1.0",
                    },
                },
            });
        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Post,
            payload,
            sessionId: null,
            cancellationToken);
        RequireStatus(response, HttpStatusCode.OK);
        if (!response.Headers.TryGetValues(
                SessionHeader,
                out IEnumerable<string>? values))
        {
            throw Invalid("Spider MCP did not establish a session.");
        }

        string sessionId = values.SingleOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 200)
        {
            throw Invalid("Spider MCP returned an invalid session identifier.");
        }

        using JsonDocument envelope = await ReadEnvelopeAsync(
            response,
            expectedId: 1,
            cancellationToken);
        JsonElement result = RequireResult(envelope.RootElement, expectedId: 1);
        if (!result.TryGetProperty("protocolVersion", out JsonElement protocol)
            || protocol.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(
                protocol.GetString(),
                ProtocolVersion)
            || !result.TryGetProperty("serverInfo", out JsonElement serverInfo)
            || serverInfo.ValueKind != JsonValueKind.Object
            || !serverInfo.TryGetProperty("name", out JsonElement name)
            || name.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(name.GetString(), "rmcp"))
        {
            throw Invalid(
                "The research MCP endpoint is not the expected hardened Spider server.");
        }

        return sessionId;
    }

    private async Task SendInitializedAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
            });
        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Post,
            payload,
            sessionId,
            cancellationToken);
        RequireStatus(response, HttpStatusCode.Accepted);
    }

    private async Task<string> CallToolAsync(
        string sessionId,
        string toolName,
        object arguments,
        CancellationToken cancellationToken)
    {
        const int requestId = 2;
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                jsonrpc = "2.0",
                id = requestId,
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments,
                },
            });
        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Post,
            payload,
            sessionId,
            cancellationToken);
        RequireStatus(response, HttpStatusCode.OK);
        using JsonDocument envelope = await ReadEnvelopeAsync(
            response,
            requestId,
            cancellationToken);
        JsonElement result = RequireResult(envelope.RootElement, requestId);
        if (result.TryGetProperty("isError", out JsonElement isError)
            && isError.ValueKind == JsonValueKind.True)
        {
            throw Invalid($"Spider MCP {toolName} failed.");
        }

        if (!result.TryGetProperty("content", out JsonElement content)
            || content.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"Spider MCP {toolName} returned no content.");
        }

        string? text = content
            .EnumerateArray()
            .Where(
                item => item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("type", out JsonElement type)
                    && type.ValueKind == JsonValueKind.String
                    && StringComparer.Ordinal.Equals(type.GetString(), "text")
                    && item.TryGetProperty("text", out JsonElement value)
                    && value.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("text").GetString())
            .FirstOrDefault(value => value is not null);
        return text
            ?? throw Invalid($"Spider MCP {toolName} returned no text result.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        byte[]? payload,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, string.Empty);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation(
            "MCP-Protocol-Version",
            ProtocolVersion);
        if (sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation(SessionHeader, sessionId);
        }

        if (payload is not null)
        {
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/json");
        }

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static async Task<JsonDocument> ReadEnvelopeAsync(
        HttpResponseMessage response,
        int expectedId,
        CancellationToken cancellationToken)
    {
        byte[] body = await ReadBoundedAsync(response.Content, cancellationToken);
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (StringComparer.OrdinalIgnoreCase.Equals(
                mediaType,
                "application/json"))
        {
            if (body.Length == 0)
            {
                throw Invalid("Spider MCP returned an empty JSON response.");
            }

            return JsonDocument.Parse(body, DocumentOptions);
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(
                mediaType,
                "text/event-stream"))
        {
            throw Invalid(
                "Spider MCP returned an unsupported response content type.");
        }

        string eventStream = Encoding.UTF8.GetString(body)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (string item in eventStream.Split("\n\n"))
        {
            string data = string.Join(
                "\n",
                item.Split('\n')
                    .Select(line => line.TrimEnd('\r'))
                    .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                    .Select(line => line["data:".Length..].TrimStart()));
            if (data.Length == 0)
            {
                continue;
            }

            JsonDocument envelope = JsonDocument.Parse(data, DocumentOptions);
            JsonElement root = envelope.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("id", out JsonElement id)
                && id.ValueKind == JsonValueKind.Number
                && id.TryGetInt32(out int actualId)
                && actualId == expectedId)
            {
                return envelope;
            }

            envelope.Dispose();
        }

        throw Invalid("Spider MCP returned no matching JSON-RPC response.");
    }

    private static JsonElement RequireResult(JsonElement root, int expectedId)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("jsonrpc", out JsonElement jsonrpc)
            || jsonrpc.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(jsonrpc.GetString(), "2.0")
            || !root.TryGetProperty("id", out JsonElement id)
            || id.ValueKind != JsonValueKind.Number
            || !id.TryGetInt32(out int actualId)
            || actualId != expectedId)
        {
            throw Invalid("Spider MCP returned an invalid JSON-RPC envelope.");
        }

        if (root.TryGetProperty("error", out _))
        {
            throw Invalid("Spider MCP returned a JSON-RPC error.");
        }

        if (!root.TryGetProperty("result", out JsonElement result)
            || result.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("Spider MCP returned no JSON-RPC result.");
        }

        return result;
    }

    private static SpiderScrapeResult ParseScrapeResult(
        string output,
        ResearchSourceDefinition source)
    {
        using JsonDocument document = JsonDocument.Parse(output, DocumentOptions);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("url", out JsonElement url)
            || url.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? finalUri)
            || !source.AllowsFinalUri(finalUri)
            || !root.TryGetProperty("status_code", out JsonElement statusCode)
            || statusCode.ValueKind != JsonValueKind.Number
            || !statusCode.TryGetInt32(out int parsedStatusCode)
            || parsedStatusCode != StatusCodes.Status200OK
            || !root.TryGetProperty(
                "content_trust",
                out JsonElement contentTrust)
            || contentTrust.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(
                contentTrust.GetString(),
                "untrusted_remote_content")
            || !root.TryGetProperty("content", out JsonElement content)
            || content.ValueKind != JsonValueKind.String)
        {
            throw Invalid(
                "Spider MCP returned an invalid or unexpected scrape result.");
        }

        string value = content.GetString() ?? string.Empty;
        int contentBytes = Encoding.UTF8.GetByteCount(value);
        if (contentBytes is <= 0 or > MaximumScrapeContentBytes
            || !source.HasRequiredContent(value))
        {
            throw Invalid(
                "Spider MCP returned unsupported research content dimensions.");
        }

        return new(
            finalUri,
            parsedStatusCode,
            value,
            contentTrust.GetString()!);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
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

            if (output.Length + read > MaximumMcpResponseBytes)
            {
                throw Invalid("Spider MCP response exceeded the supported size.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private async Task TryDeleteSessionAsync(string sessionId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                payload: null,
                sessionId,
                timeout.Token);
        }
        catch
        {
            // The server also expires sessions; do not hide the capture result.
        }
    }

    private static void RequireStatus(
        HttpResponseMessage response,
        HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            throw Invalid("Spider MCP returned an unexpected HTTP status.");
        }
    }

    private static ResearchSourceSnapshotException Invalid(string message) =>
        new(message);
}
