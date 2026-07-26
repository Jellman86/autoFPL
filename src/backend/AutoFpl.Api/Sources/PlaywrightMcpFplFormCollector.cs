using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AutoFpl.Api.Sources;

public sealed class PlaywrightMcpFplFormCollector
{
    private const string ProtocolVersion = "2025-03-26";
    private const string SessionHeader = "Mcp-Session-Id";
    private const string EvaluationPrefix = "AUTOFPL_FPL_FORM_V1:";
    private const int MaximumMcpResponseBytes = 6 * 1024 * 1024;

    private const string CollectionCode =
        """
        async (page) => {
          await page.goto(
            "https://fplform.com/fpl-predicted-points",
            { waitUntil: "domcontentloaded", timeout: 30000 });
          return await page.evaluate(async () => {
            const prefix = "AUTOFPL_FPL_FORM_V1:";
            const node = document.querySelector("#php-data");
            if (!node) {
              throw new Error("FPL Form page is missing #php-data");
            }

            const parsedGameweek = Number(node.getAttribute("data-nw"));
            const gameweek = Number.isInteger(parsedGameweek) ? parsedGameweek : 99;
            if (gameweek < 1 || gameweek > 38) {
              return prefix + JSON.stringify({
                schemaVersion: "fpl-form-dom/v1",
                sourceUrl: location.href,
                season: 20,
                gameweek,
                providerPayloadSha256: null,
                predictions: []
              });
            }

            const rawPlayers = node.getAttribute("data-players");
            if (!rawPlayers) {
              throw new Error("FPL Form page is missing data-players");
            }

            const players = JSON.parse(rawPlayers);
            let season = 0;
            for (const player of Object.values(players)) {
              for (const candidate of Object.keys(player.fixtures ?? {})) {
                const parsed = Number(candidate);
                if (Number.isInteger(parsed) && parsed >= 20 && parsed <= 99) {
                  season = Math.max(season, parsed);
                }
              }
            }

            const predictions = [];
            for (const [sourcePlayerId, player] of Object.entries(players)) {
              const fixtures =
                player.fixtures?.[String(season)]?.[String(gameweek)] ?? {};
              for (const [kickoffIdentity, prediction] of Object.entries(fixtures)) {
                if (prediction.predicted_points === null
                    || prediction.predicted_points === undefined) {
                  continue;
                }

                if (Number(prediction.season) !== season
                    || Number(prediction.event) !== gameweek
                    || String(prediction.kickoff) !== kickoffIdentity) {
                  throw new Error("FPL Form prediction identity is inconsistent");
                }

                predictions.push({
                  sourcePlayerId: Number(sourcePlayerId),
                  fixtureId: Number(prediction.fixture),
                  playerName: player.name,
                  teamName: player.team_name,
                  position: player.position,
                  kickoffLocal: prediction.kickoff,
                  predictedPoints: String(prediction.predicted_points),
                  appearanceProbability:
                    prediction.probability_of_playing === null
                    || prediction.probability_of_playing === undefined
                      ? null
                      : String(prediction.probability_of_playing)
                });
              }
            }

            predictions.sort(
              (left, right) =>
                left.sourcePlayerId - right.sourcePlayerId
                || left.fixtureId - right.fixtureId);
            const digest = await crypto.subtle.digest(
              "SHA-256",
              new TextEncoder().encode(rawPlayers));
            const providerPayloadSha256 = Array.from(new Uint8Array(digest))
              .map(value => value.toString(16).padStart(2, "0"))
              .join("");
            return prefix + JSON.stringify({
              schemaVersion: "fpl-form-dom/v1",
              sourceUrl: location.href,
              season,
              gameweek,
              providerPayloadSha256,
              predictions
            });
          });
        }
        """;

    private readonly HttpClient _httpClient;

    public PlaywrightMcpFplFormCollector(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (_httpClient.BaseAddress is null)
        {
            throw new ArgumentException(
                "The Playwright MCP HTTP client requires a base address.",
                nameof(httpClient));
        }
    }

    public async Task<byte[]> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        string? sessionId = null;
        try
        {
            sessionId = await InitializeAsync(cancellationToken);
            await SendInitializedAsync(sessionId, cancellationToken);
            string evaluated = await CallToolAsync(
                sessionId,
                requestId: 2,
                "browser_run_code_unsafe",
                new { code = CollectionCode },
                cancellationToken);
            return ParseEvaluation(evaluated);
        }
        catch (FplFormForecastPayloadException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or InvalidOperationException)
        {
            throw new FplFormForecastPayloadException(
                "Playwright MCP could not collect the FPL Form forecast.",
                exception);
        }
        finally
        {
            if (sessionId is not null)
            {
                await TryCloseAsync(sessionId);
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
                        name = "autofpl-fpl-form-collector",
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
        if (!response.Headers.TryGetValues(SessionHeader, out IEnumerable<string>? values))
        {
            throw Invalid("Playwright MCP did not establish a session.");
        }

        string sessionId = values.SingleOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 200)
        {
            throw Invalid("Playwright MCP returned an invalid session identifier.");
        }

        using JsonDocument envelope = await ReadEnvelopeAsync(
            response,
            expectedId: 1,
            cancellationToken);
        JsonElement result = RequireResult(envelope.RootElement, expectedId: 1);
        if (!result.TryGetProperty("protocolVersion", out JsonElement protocol)
            || protocol.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(protocol.GetString(), ProtocolVersion)
            || !result.TryGetProperty("serverInfo", out JsonElement serverInfo)
            || serverInfo.ValueKind != JsonValueKind.Object
            || !serverInfo.TryGetProperty("name", out JsonElement name)
            || name.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(name.GetString(), "Playwright"))
        {
            throw Invalid("The research MCP endpoint is not the expected Playwright server.");
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
        int requestId,
        string toolName,
        object arguments,
        CancellationToken cancellationToken)
    {
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
            throw Invalid($"Playwright MCP {toolName} failed.");
        }

        if (!result.TryGetProperty("content", out JsonElement content)
            || content.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"Playwright MCP {toolName} returned no content.");
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
            ?? throw Invalid($"Playwright MCP {toolName} returned no text result.");
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
        if (StringComparer.OrdinalIgnoreCase.Equals(mediaType, "application/json"))
        {
            if (body.Length == 0)
            {
                throw Invalid("Playwright MCP returned an empty JSON response.");
            }

            return ParseJson(body);
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(mediaType, "text/event-stream"))
        {
            throw Invalid("Playwright MCP returned an unsupported response content type.");
        }

        string eventStream = Encoding.UTF8.GetString(body);
        var data = new StringBuilder();
        foreach (string rawLine in eventStream.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                JsonDocument? envelope = ParseMatchingEvent(data, expectedId);
                if (envelope is not null)
                {
                    return envelope;
                }

                data.Clear();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            if (data.Length > 0)
            {
                data.Append('\n');
            }

            ReadOnlySpan<char> value = line.AsSpan("data:".Length);
            if (!value.IsEmpty && value[0] == ' ')
            {
                value = value[1..];
            }

            data.Append(value);
        }

        JsonDocument? finalEnvelope = ParseMatchingEvent(data, expectedId);
        return finalEnvelope
            ?? throw Invalid("Playwright MCP returned no matching JSON-RPC response.");
    }

    private static JsonDocument? ParseMatchingEvent(
        StringBuilder data,
        int expectedId)
    {
        if (data.Length == 0)
        {
            return null;
        }

        JsonDocument envelope;
        try
        {
            envelope = ParseJson(Encoding.UTF8.GetBytes(data.ToString()));
        }
        catch (JsonException)
        {
            throw Invalid("Playwright MCP returned an invalid event stream.");
        }

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
        return null;
    }

    private static JsonDocument ParseJson(ReadOnlyMemory<byte> body) =>
        JsonDocument.Parse(
            body,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
                AllowDuplicateProperties = false,
            });

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
            throw Invalid("Playwright MCP returned an invalid JSON-RPC envelope.");
        }

        if (root.TryGetProperty("error", out _))
        {
            throw Invalid("Playwright MCP returned a JSON-RPC error.");
        }

        if (!root.TryGetProperty("result", out JsonElement result)
            || result.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("Playwright MCP returned no JSON-RPC result.");
        }

        return result;
    }

    private static byte[] ParseEvaluation(string output)
    {
        int resultMarker = output.IndexOf("### Result", StringComparison.Ordinal);
        int codeMarker = output.IndexOf(
            "### Ran Playwright code",
            StringComparison.Ordinal);
        if (resultMarker < 0 || codeMarker <= resultMarker)
        {
            throw Invalid("Playwright MCP returned an unsupported evaluation result.");
        }

        string literal = output[
            (resultMarker + "### Result".Length)..codeMarker].Trim();
        string? value = JsonSerializer.Deserialize<string>(literal);
        if (value is null
            || !value.StartsWith(EvaluationPrefix, StringComparison.Ordinal))
        {
            throw Invalid("Playwright MCP returned an unsupported extraction payload.");
        }

        return Encoding.UTF8.GetBytes(value[EvaluationPrefix.Length..]);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
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
                throw Invalid("Playwright MCP response exceeded the supported size.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private async Task TryCloseAsync(string sessionId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await CallToolAsync(
                sessionId,
                requestId: 3,
                "browser_close",
                new { },
                timeout.Token);
        }
        catch
        {
            // Deleting the isolated MCP session remains the authoritative cleanup.
        }
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
            // The server also expires isolated sessions; do not hide the primary result.
        }
    }

    private static void RequireStatus(
        HttpResponseMessage response,
        HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            throw Invalid("Playwright MCP returned an unexpected HTTP status.");
        }
    }

    private static FplFormForecastPayloadException Invalid(string message) => new(message);
}
