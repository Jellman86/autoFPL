using System.Text;
using System.Text.Json;

using AutoFpl.Api.Sources;

namespace AutoFpl.Api.Intelligence;

public sealed record PlaywrightResearchSourceCaptureResult(
    Uri FinalUri,
    int StatusCode,
    string Content,
    string ContentTrust,
    string TransportVersion);

public sealed class PremierLeagueInjuryPlaywrightCollector
{
    public const string TransportKey = "playwright-mcp";
    public const string TransportVersion = "playwright-mcp/v0.0.78";

    private const string EvaluationPrefix = "AUTOFPL_PL_INJURY_V1:";
    private const string SchemaVersion = "premier-league-injury-dom/v1";
    private const int ExpectedClubCount = 20;
    private const int MaximumRowCount = 200;
    private const int MaximumPayloadBytes = 128 * 1024;

    private const string CollectionCode =
        """
        async (page) => {
          const prefix = "AUTOFPL_PL_INJURY_V1:";
          const sourceUrl =
            "https://www.premierleague.com/en/latest-player-injuries";
          const response = await page.goto(
            sourceUrl,
            { waitUntil: "domcontentloaded", timeout: 30000 });
          if (response === null
              || response.status() !== 200
              || response.url() !== sourceUrl) {
            throw new Error("Premier League returned an unexpected response");
          }

          await page.waitForFunction(
            () => {
              const articles = Array.from(
                document.querySelectorAll(".injury-news__article"));
              const rows = Array.from(
                document.querySelectorAll(
                  ".injury-news__table-body .injury-news__table-row"));
              const realRows = rows.filter(row => {
                const cells = row.querySelectorAll(
                  ".injury-news__table-data");
                return (cells[0]?.textContent ?? "").trim() !== "-";
              });
              return articles.length === 20
                && realRows.length >= 1
                && realRows.length <= 200
                && articles.every(article => {
                  const team = article.querySelector(".injury-news__team");
                  const body = article.querySelector(
                    ".injury-news__table-body");
                  return (team?.textContent ?? "").trim().length > 0
                    && body !== null;
                })
                && rows.every(row => {
                  const cells = row.querySelectorAll(
                    ".injury-news__table-data");
                  const player = (cells[0]?.textContent ?? "").trim();
                  const injury = (cells[1]?.textContent ?? "").trim();
                  const details = (cells[2]?.textContent ?? "").trim();
                  const link = row.querySelector(
                    "a.injury-news__table-link");
                  const isEmptyClub =
                    cells.length === 3
                    && player === "-"
                    && injury === "-"
                    && (cells[2]?.textContent ?? "").trim() === "-"
                    && link === null;
                  const isInjury =
                    cells.length === 3
                    && player.length > 0
                    && player !== "-"
                    && injury.length > 0
                    && (
                      (
                        link instanceof HTMLAnchorElement
                        && link.href.startsWith("https://")
                      )
                      || (link === null && details === "-")
                    );
                  return isEmptyClub || isInjury;
                });
            },
            null,
            { timeout: 20000 });
          await page.waitForTimeout(1000);

          const extracted = await page
            .locator(".injury-news__article")
            .evaluateAll(async articles => {
              const compareText = (left, right) =>
                left < right ? -1 : left > right ? 1 : 0;
              const widgetHtml = articles
                .map(article => article.outerHTML)
                .join("\n");
              const renderedWidgetDigest = await crypto.subtle.digest(
                "SHA-256",
                new TextEncoder().encode(widgetHtml));
              const renderedWidgetSha256 = Array.from(
                new Uint8Array(renderedWidgetDigest),
                byte => byte.toString(16).padStart(2, "0"))
                .join("");
              const clubs = articles.map(article => {
                const teamName = (
                  article.querySelector(".injury-news__team")
                    ?.textContent ?? ""
                ).trim();
                const rows = Array.from(
                  article.querySelectorAll(
                    ".injury-news__table-body .injury-news__table-row"))
                  .map(row => {
                    const cells = row.querySelectorAll(
                      ".injury-news__table-data");
                    const link = row.querySelector(
                      "a.injury-news__table-link");
                    return {
                      playerName: (cells[0]?.textContent ?? "").trim(),
                      injury: (cells[1]?.textContent ?? "").trim(),
                      updateUrl:
                        link instanceof HTMLAnchorElement ? link.href : null
                    };
                  })
                  .filter(row => row.playerName !== "-")
                  .sort(
                    (left, right) =>
                      compareText(left.playerName, right.playerName)
                      || compareText(left.injury, right.injury)
                      || compareText(left.updateUrl, right.updateUrl));
                return { teamName, rows };
              }).sort(
                (left, right) =>
                  compareText(left.teamName, right.teamName));
              return {
                pageTitle: document.title,
                renderedWidgetSha256,
                clubs
              };
            });

          return prefix + JSON.stringify({
            schemaVersion: "premier-league-injury-dom/v1",
            sourceUrl,
            pageTitle: extracted.pageTitle,
            renderedWidgetSha256: extracted.renderedWidgetSha256,
            clubs: extracted.clubs
          });
        }
        """;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
        AllowDuplicateProperties = false,
    };

    private readonly PlaywrightMcpFplFormCollector _playwright;

    public PremierLeagueInjuryPlaywrightCollector(
        PlaywrightMcpFplFormCollector playwright)
    {
        _playwright =
            playwright ?? throw new ArgumentNullException(nameof(playwright));
    }

    public async Task<PlaywrightResearchSourceCaptureResult> CaptureAsync(
        ResearchSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!StringComparer.Ordinal.Equals(source.SourceKey, "premier-league-injuries")
            || !StringComparer.Ordinal.Equals(source.TransportKey, TransportKey))
        {
            throw Invalid("The Playwright injury collector received an unsupported source.");
        }

        byte[] payload;
        try
        {
            payload = await _playwright.CaptureFixedPageAsync(
                CollectionCode,
                EvaluationPrefix,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is FplFormForecastPayloadException
            or HttpRequestException
            or TaskCanceledException
            or JsonException
            or InvalidOperationException)
        {
            throw new ResearchSourceSnapshotException(
                "Playwright MCP could not capture the Premier League injury source.",
                exception);
        }

        if (payload.Length is <= 0 or > MaximumPayloadBytes)
        {
            throw Invalid("The Premier League injury payload has unsupported dimensions.");
        }

        string content = Encoding.UTF8.GetString(payload);
        ValidatePayload(content, source);
        return new(
            source.CanonicalUri,
            StatusCodes.Status200OK,
            content,
            "untrusted_remote_content",
            TransportVersion);
    }

    private static void ValidatePayload(
        string content,
        ResearchSourceDefinition source)
    {
        using JsonDocument document = JsonDocument.Parse(content, DocumentOptions);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !ReadExactString(root, "schemaVersion", SchemaVersion)
            || !ReadExactString(
                root,
                "sourceUrl",
                source.CanonicalUri.AbsoluteUri)
            || !TryReadBoundedString(root, "pageTitle", 1, 200, out _)
            || !TryReadBoundedString(
                root,
                "renderedWidgetSha256",
                64,
                64,
                out string? renderedHash)
            || !IsLowerHex(renderedHash)
            || !root.TryGetProperty("clubs", out JsonElement clubs)
            || clubs.ValueKind != JsonValueKind.Array
            || clubs.GetArrayLength() != ExpectedClubCount)
        {
            throw Invalid("The Premier League injury payload is structurally invalid.");
        }

        var teamNames = new HashSet<string>(StringComparer.Ordinal);
        int rowCount = 0;
        foreach (JsonElement club in clubs.EnumerateArray())
        {
            if (club.ValueKind != JsonValueKind.Object
                || !TryReadBoundedString(
                    club,
                    "teamName",
                    1,
                    100,
                    out string? teamName)
                || !teamNames.Add(teamName)
                || !club.TryGetProperty("rows", out JsonElement rows)
                || rows.ValueKind != JsonValueKind.Array)
            {
                throw Invalid("The Premier League injury club is invalid.");
            }

            var playerNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement row in rows.EnumerateArray())
            {
                rowCount++;
                if (row.ValueKind != JsonValueKind.Object
                    || !TryReadBoundedString(
                        row,
                        "playerName",
                        1,
                        120,
                        out string? playerName)
                    || StringComparer.Ordinal.Equals(playerName, "-")
                    || !playerNames.Add(playerName)
                    // Clubs sometimes list a player without disclosing the
                    // injury type. The claim is that the player is listed, so an
                    // empty type is preserved as-is rather than rejecting the
                    // whole payload or inventing a value.
                    || !TryReadBoundedString(
                        row,
                        "injury",
                        0,
                        160,
                        out _)
                    || !IsOptionalSafeUpdateUri(row))
                {
                    throw Invalid("The Premier League injury row is invalid.");
                }
            }
        }

        if (rowCount is < 1 or > MaximumRowCount
            || !source.HasRequiredContent(content))
        {
            throw Invalid("The Premier League injury payload is incomplete.");
        }
    }

    private static bool ReadExactString(
        JsonElement element,
        string propertyName,
        string expected) =>
        element.TryGetProperty(propertyName, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
        && StringComparer.Ordinal.Equals(property.GetString(), expected);

    private static bool TryReadBoundedString(
        JsonElement element,
        string propertyName,
        int minimumLength,
        int maximumLength,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length >= minimumLength
            && value.Length <= maximumLength
            && !value.Any(char.IsControl);
    }

    private static bool IsLowerHex(string value) =>
        value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    private static bool IsSafeUpdateUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment);

    private static bool IsOptionalSafeUpdateUri(JsonElement row)
    {
        if (!row.TryGetProperty("updateUrl", out JsonElement updateUrl))
        {
            return false;
        }

        return updateUrl.ValueKind == JsonValueKind.Null
            || (
                updateUrl.ValueKind == JsonValueKind.String
                && TryReadBoundedString(
                    row,
                    "updateUrl",
                    1,
                    2048,
                    out string value)
                && IsSafeUpdateUri(value)
            );
    }

    private static ResearchSourceSnapshotException Invalid(string message) =>
        new(message);
}
