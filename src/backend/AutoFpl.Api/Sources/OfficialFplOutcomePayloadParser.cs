using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;

namespace AutoFpl.Api.Sources;

internal static class OfficialFplOutcomePayloadParser
{
    private const int MaximumPlayers = 2_000;

    public static OfficialFplOutcomePayload Parse(byte[] liveJson)
    {
        ArgumentNullException.ThrowIfNull(liveJson);

        try
        {
            RejectDuplicateProperties(liveJson);
            using JsonDocument document = JsonDocument.Parse(
                liveJson,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(
                    "elements",
                    out JsonElement elements)
                || elements.ValueKind != JsonValueKind.Array)
            {
                throw Invalid("elements must be an array.");
            }

            int count = elements.GetArrayLength();
            if (count is < 1 or > MaximumPlayers)
            {
                throw Invalid("elements count is outside the supported range.");
            }

            var players = new List<OfficialFplPlayerOutcome>(count);
            var playerIds = new HashSet<int>();
            foreach (JsonElement element in elements.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid("element must be an object.");
                }

                int playerId = RequireInt32(element, "id", 1, int.MaxValue);
                if (!playerIds.Add(playerId))
                {
                    throw Invalid("elements contains a duplicate player id.");
                }

                if (!element.TryGetProperty("stats", out JsonElement stats)
                    || stats.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid("stats must be an object.");
                }

                players.Add(
                    new(
                        playerId,
                        RequireInt32(stats, "minutes", 0, 400),
                        RequireInt32(stats, "starts", 0, 4),
                        RequireInt32(stats, "total_points", -100, 500),
                        RequireInt32(stats, "goals_scored", 0, 20),
                        RequireInt32(stats, "assists", 0, 20),
                        RequireInt32(stats, "clean_sheets", 0, 4),
                        RequireInt32(stats, "goals_conceded", 0, 50),
                        RequireInt32(stats, "saves", 0, 100),
                        RequireInt32(stats, "bonus", 0, 30),
                        RequireInt32(stats, "yellow_cards", 0, 4),
                        RequireInt32(stats, "red_cards", 0, 4),
                        RequireInt32(stats, "own_goals", 0, 20),
                        RequireInt32(stats, "penalties_saved", 0, 20),
                        RequireInt32(stats, "penalties_missed", 0, 20),
                        RequireInt32(stats, "bps", -500, 2_000),
                        RequireDecimal(stats, "influence", 0m, 10_000m),
                        RequireDecimal(stats, "creativity", 0m, 10_000m),
                        RequireDecimal(stats, "threat", 0m, 10_000m),
                        RequireDecimal(stats, "ict_index", 0m, 10_000m),
                        RequireInt32(
                            stats,
                            "clearances_blocks_interceptions",
                            0,
                            1_000),
                        RequireInt32(stats, "recoveries", 0, 1_000),
                        RequireInt32(stats, "tackles", 0, 1_000),
                        RequireInt32(
                            stats,
                            "defensive_contribution",
                            0,
                            1_000),
                        RequireDecimal(
                            stats,
                            "expected_goals",
                            0m,
                            100m),
                        RequireDecimal(
                            stats,
                            "expected_assists",
                            0m,
                            100m),
                        RequireDecimal(
                            stats,
                            "expected_goal_involvements",
                            0m,
                            100m),
                        RequireDecimal(
                            stats,
                            "expected_goals_conceded",
                            0m,
                            100m)));
            }

            return new(
                liveJson.ToArray(),
                Convert.ToHexString(SHA256.HashData(liveJson)).ToLowerInvariant(),
                players.OrderBy(player => player.PlayerId).ToArray());
        }
        catch (OfficialFplPayloadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new OfficialFplPayloadException(
                "Official FPL outcome returned malformed JSON.",
                exception);
        }
    }

    private static int RequireInt32(
        JsonElement value,
        string name,
        int minimum,
        int maximum)
    {
        if (!value.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out int result)
            || result < minimum
            || result > maximum)
        {
            throw Invalid($"{name} must be an integer in the supported range.");
        }

        return result;
    }

    private static decimal RequireDecimal(
        JsonElement value,
        string name,
        decimal minimum,
        decimal maximum)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
        {
            throw Invalid($"{name} must be a decimal in the supported range.");
        }

        decimal result;
        if (property.ValueKind == JsonValueKind.String)
        {
            if (!decimal.TryParse(
                    property.GetString(),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out result))
            {
                throw Invalid($"{name} must be a decimal in the supported range.");
            }
        }
        else if (property.ValueKind == JsonValueKind.Number)
        {
            if (!property.TryGetDecimal(out result))
            {
                throw Invalid($"{name} must be a decimal in the supported range.");
            }
        }
        else
        {
            throw Invalid($"{name} must be a decimal in the supported range.");
        }

        if (result < minimum || result > maximum)
        {
            throw Invalid($"{name} must be a decimal in the supported range.");
        }

        return result;
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(
            json,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
        var objectProperties = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                objectProperties.Push(new(StringComparer.Ordinal));
                continue;
            }

            if (reader.TokenType == JsonTokenType.EndObject)
            {
                objectProperties.Pop();
                continue;
            }

            if (reader.TokenType == JsonTokenType.PropertyName
                && !objectProperties.Peek().Add(reader.GetString() ?? string.Empty))
            {
                throw Invalid("live payload contains a duplicate JSON property.");
            }
        }
    }

    private static OfficialFplPayloadException Invalid(string message) =>
        new($"Official FPL outcome validation failed: {message}");
}
