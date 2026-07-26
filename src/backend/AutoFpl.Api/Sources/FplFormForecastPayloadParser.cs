using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace AutoFpl.Api.Sources;

internal static class FplFormForecastPayloadParser
{
    private const int MaximumPlayers = 2_000;
    private const int MaximumPredictions = 4_000;

    private static ReadOnlySpan<byte> NextGameweekMarker => "data-nw=\""u8;

    private static ReadOnlySpan<byte> PlayersMarker => "data-players=\""u8;

    private static readonly IReadOnlyDictionary<string, string> Positions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Goalkeeper"] = "goalkeeper",
            ["Defender"] = "defender",
            ["Midfielder"] = "midfielder",
            ["Forward"] = "forward",
        };

    public static FplFormForecastPayload Parse(byte[] html)
    {
        ArgumentNullException.ThrowIfNull(html);

        try
        {
            int gameweek = ParseNextGameweek(html);
            ReadOnlySpan<byte> encodedPlayers = ReadAttribute(
                html,
                PlayersMarker,
                "data-players");
            (byte[] playersJson, int playersJsonLength) =
                DecodeHtmlAttribute(encodedPlayers);
            using JsonDocument players = JsonDocument.Parse(
                playersJson.AsMemory(0, playersJsonLength),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                    AllowDuplicateProperties = false,
                });

            if (players.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("data-players must be a JSON object.");
            }

            int season = FindLatestSeason(players.RootElement);
            IReadOnlyList<FplFormFixturePrediction> predictions =
                ParsePredictions(players.RootElement, season, gameweek);
            if (predictions.Count == 0)
            {
                throw Invalid("The active Gameweek contains no published predictions.");
            }

            int playerCount = predictions
                .Select(item => item.SourcePlayerId)
                .Distinct()
                .Count();
            if (playerCount > MaximumPlayers || predictions.Count > MaximumPredictions)
            {
                throw Invalid("Prediction counts are outside the supported range.");
            }

            return new(
                DeriveSeasonCode(season),
                gameweek,
                html.ToArray(),
                Convert.ToHexString(SHA256.HashData(html)).ToLowerInvariant(),
                predictions);
        }
        catch (FplFormForecastPayloadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new FplFormForecastPayloadException(
                "FPL Form returned malformed embedded prediction JSON.",
                exception);
        }
    }

    private static int ParseNextGameweek(byte[] document)
    {
        ReadOnlySpan<byte> raw = ReadAttribute(
            document,
            NextGameweekMarker,
            "data-nw");
        if (!System.Buffers.Text.Utf8Parser.TryParse(raw, out int result, out int consumed)
            || consumed != raw.Length
            || result is < 1 or > 38)
        {
            throw Invalid("FPL Form has no active next-Gameweek forecast.");
        }

        return result;
    }

    private static ReadOnlySpan<byte> ReadAttribute(
        byte[] document,
        ReadOnlySpan<byte> marker,
        string name)
    {
        ReadOnlySpan<byte> bytes = document;
        int start = bytes.IndexOf(marker);
        if (start < 0)
        {
            throw Invalid($"FPL Form HTML is missing {name}.");
        }

        start += marker.Length;
        int relativeEnd = bytes[start..].IndexOf((byte)'"');
        int end = relativeEnd < 0 ? -1 : start + relativeEnd;
        if (end < 0)
        {
            throw Invalid($"FPL Form HTML contains an unterminated {name}.");
        }

        return bytes[start..end];
    }

    private static (byte[] Buffer, int Length) DecodeHtmlAttribute(
        ReadOnlySpan<byte> encoded)
    {
        byte[] decoded = GC.AllocateUninitializedArray<byte>(encoded.Length);
        int output = 0;
        for (int input = 0; input < encoded.Length;)
        {
            if (encoded[input] != (byte)'&')
            {
                decoded[output++] = encoded[input++];
                continue;
            }

            int semicolonOffset = encoded[input..Math.Min(encoded.Length, input + 16)]
                .IndexOf((byte)';');
            if (semicolonOffset < 0)
            {
                throw Invalid("data-players contains an unterminated HTML entity.");
            }

            ReadOnlySpan<byte> entity = encoded.Slice(input, semicolonOffset + 1);
            if (entity.SequenceEqual("&quot;"u8))
            {
                decoded[output++] = (byte)'"';
            }
            else if (entity.SequenceEqual("&amp;"u8))
            {
                decoded[output++] = (byte)'&';
            }
            else if (entity.SequenceEqual("&lt;"u8))
            {
                decoded[output++] = (byte)'<';
            }
            else if (entity.SequenceEqual("&gt;"u8))
            {
                decoded[output++] = (byte)'>';
            }
            else if (entity.SequenceEqual("&apos;"u8)
                || entity.SequenceEqual("&#39;"u8)
                || entity.SequenceEqual("&#039;"u8))
            {
                decoded[output++] = (byte)'\'';
            }
            else
            {
                throw Invalid("data-players contains an unsupported HTML entity.");
            }

            input += entity.Length;
        }

        return (decoded, output);
    }

    private static int FindLatestSeason(JsonElement players)
    {
        int? latest = null;
        foreach (JsonProperty playerProperty in players.EnumerateObject())
        {
            JsonElement player = RequireObject(playerProperty.Value, "player");
            JsonElement fixtures = RequireObjectProperty(player, "fixtures");
            foreach (JsonProperty seasonProperty in fixtures.EnumerateObject())
            {
                if (int.TryParse(
                        seasonProperty.Name,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int season)
                    && season is >= 20 and <= 99)
                {
                    latest = Math.Max(latest ?? season, season);
                }
            }
        }

        return latest
            ?? throw Invalid("FPL Form HTML contains no supported season.");
    }

    private static IReadOnlyList<FplFormFixturePrediction> ParsePredictions(
        JsonElement players,
        int season,
        int gameweek)
    {
        var results = new List<FplFormFixturePrediction>();
        var identities = new HashSet<(int PlayerId, int FixtureId)>();
        foreach (JsonProperty playerProperty in players.EnumerateObject())
        {
            if (!int.TryParse(
                    playerProperty.Name,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int sourcePlayerId)
                || sourcePlayerId <= 0)
            {
                throw Invalid("data-players contains an unsupported player id.");
            }

            JsonElement player = RequireObject(playerProperty.Value, "player");
            string playerName = RequireText(player, "name", 150);
            string teamName = RequireText(player, "team_name", 100);
            string providerPosition = RequireText(player, "position", 32);
            if (!Positions.TryGetValue(providerPosition, out string? position))
            {
                throw Invalid("data-players contains an unsupported position.");
            }

            JsonElement fixtures = RequireObjectProperty(player, "fixtures");
            if (!fixtures.TryGetProperty(
                    season.ToString(CultureInfo.InvariantCulture),
                    out JsonElement seasonFixtures))
            {
                continue;
            }

            seasonFixtures = RequireObject(seasonFixtures, "season fixtures");
            if (!seasonFixtures.TryGetProperty(
                    gameweek.ToString(CultureInfo.InvariantCulture),
                    out JsonElement gameweekFixtures))
            {
                continue;
            }

            gameweekFixtures = RequireObject(gameweekFixtures, "Gameweek fixtures");
            foreach (JsonProperty kickoffProperty in gameweekFixtures.EnumerateObject())
            {
                JsonElement prediction = RequireObject(kickoffProperty.Value, "prediction");
                decimal? predictedPoints = OptionalDecimalString(
                    prediction,
                    "predicted_points",
                    -20m,
                    100m);
                if (predictedPoints is null)
                {
                    continue;
                }

                RequireExactIntegerString(prediction, "season", season);
                RequireExactIntegerString(prediction, "event", gameweek);
                int fixtureId = RequireIntegerString(
                    prediction,
                    "fixture",
                    minimum: 1,
                    maximum: int.MaxValue);
                if (!identities.Add((sourcePlayerId, fixtureId)))
                {
                    throw Invalid("data-players contains a duplicate player/fixture prediction.");
                }

                string kickoff = RequireText(prediction, "kickoff", 32);
                if (!StringComparer.Ordinal.Equals(kickoff, kickoffProperty.Name))
                {
                    throw Invalid("Prediction kickoff identity is inconsistent.");
                }

                results.Add(
                    new(
                        sourcePlayerId,
                        fixtureId,
                        playerName,
                        teamName,
                        position,
                        kickoff,
                        predictedPoints.Value,
                        OptionalDecimalString(
                            prediction,
                            "probability_of_playing",
                            0m,
                            1m)));
            }
        }

        return results
            .OrderBy(item => item.SourcePlayerId)
            .ThenBy(item => item.FixtureId)
            .ToArray();
    }

    private static JsonElement RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{name} must be an object.");
        }

        return value;
    }

    private static JsonElement RequireObjectProperty(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{name} must be an object.");
        }

        return property;
    }

    private static string RequireText(JsonElement value, string name, int maximumLength)
    {
        if (!value.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{name} must be a string.");
        }

        string result = property.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(result) || result.Length > maximumLength)
        {
            throw Invalid($"{name} is outside the supported length.");
        }

        return result;
    }

    private static int RequireIntegerString(
        JsonElement value,
        string name,
        int minimum,
        int maximum)
    {
        string raw = RequireText(value, name, 16);
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int result)
            || result < minimum
            || result > maximum)
        {
            throw Invalid($"{name} must be an integer string in the supported range.");
        }

        return result;
    }

    private static void RequireExactIntegerString(
        JsonElement value,
        string name,
        int expected)
    {
        if (RequireIntegerString(value, name, 1, 99) != expected)
        {
            throw Invalid($"{name} does not match the selected forecast.");
        }
    }

    private static decimal? OptionalDecimalString(
        JsonElement value,
        string name,
        decimal minimum,
        decimal maximum)
    {
        if (!value.TryGetProperty(name, out JsonElement property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String
            || !decimal.TryParse(
                property.GetString(),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out decimal result)
            || result < minimum
            || result > maximum)
        {
            throw Invalid($"{name} must be a decimal string in the supported range.");
        }

        return result;
    }

    private static string DeriveSeasonCode(int season)
    {
        int startYear = 2000 + season;
        int endYear = startYear + 1;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{startYear:D4}-{endYear % 100:D2}");
    }

    private static FplFormForecastPayloadException Invalid(string message) => new(message);
}

public sealed class FplFormForecastPayloadException : Exception
{
    public FplFormForecastPayloadException(string message)
        : base(message)
    {
    }

    public FplFormForecastPayloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
