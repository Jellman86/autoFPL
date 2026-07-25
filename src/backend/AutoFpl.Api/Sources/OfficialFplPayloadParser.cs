using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace AutoFpl.Api.Sources;

internal static class OfficialFplPayloadParser
{
    private const int MaximumEvents = 38;
    private const int MaximumTeams = 40;
    private const int MaximumPlayers = 2_000;
    private const int MaximumFixtures = 1_000;

    private static readonly IReadOnlyDictionary<int, string> Positions =
        new Dictionary<int, string>
        {
            [1] = "goalkeeper",
            [2] = "defender",
            [3] = "midfielder",
            [4] = "forward",
        };

    public static OfficialFplPayload Parse(byte[] bootstrapJson, byte[] fixturesJson)
    {
        ArgumentNullException.ThrowIfNull(bootstrapJson);
        ArgumentNullException.ThrowIfNull(fixturesJson);

        try
        {
            RejectDuplicateProperties(bootstrapJson, "bootstrap");
            RejectDuplicateProperties(fixturesJson, "fixtures");
            using JsonDocument bootstrap = JsonDocument.Parse(
                bootstrapJson,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            using JsonDocument fixtures = JsonDocument.Parse(
                fixturesJson,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });

            JsonElement bootstrapRoot = RequireObject(bootstrap.RootElement, "bootstrap");
            IReadOnlyList<OfficialFplEvent> events = ParseEvents(
                RequireArray(bootstrapRoot, "events"));
            IReadOnlyList<OfficialFplTeam> teams = ParseTeams(
                RequireArray(bootstrapRoot, "teams"));
            IReadOnlyList<OfficialFplPlayer> players = ParsePlayers(
                RequireArray(bootstrapRoot, "elements"),
                teams);
            IReadOnlyList<OfficialFplFixture> parsedFixtures = ParseFixtures(
                fixtures.RootElement,
                events,
                teams);

            OfficialFplEvent? next = events.SingleOrDefault(item => item.IsNext);
            int? latestCompleted = events
                .Where(item => item.Finished && item.DataChecked)
                .Select(item => (int?)item.Id)
                .Max();

            return new(
                DeriveSeasonCode(events[0].DeadlineUtc),
                bootstrapJson.ToArray(),
                fixturesJson.ToArray(),
                Convert.ToHexString(SHA256.HashData(bootstrapJson)).ToLowerInvariant(),
                Convert.ToHexString(SHA256.HashData(fixturesJson)).ToLowerInvariant(),
                events,
                teams,
                players,
                parsedFixtures,
                next?.Id,
                next?.DeadlineUtc,
                latestCompleted);
        }
        catch (OfficialFplPayloadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new OfficialFplPayloadException(
                "Official FPL returned malformed JSON.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new OfficialFplPayloadException(
                "Official FPL returned an internally inconsistent payload.",
                exception);
        }
    }

    private static IReadOnlyList<OfficialFplEvent> ParseEvents(JsonElement array)
    {
        EnsureCount(array, "events", minimum: 1, MaximumEvents);
        var results = new List<OfficialFplEvent>(array.GetArrayLength());
        var ids = new HashSet<int>();
        int nextCount = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            JsonElement value = RequireObject(item, "event");
            int id = RequireInt32(value, "id", minimum: 1, maximum: MaximumEvents);
            if (!ids.Add(id))
            {
                throw Invalid("events contains a duplicate id.");
            }

            bool isNext = RequireBoolean(value, "is_next");
            nextCount += isNext ? 1 : 0;
            results.Add(
                new(
                    id,
                    RequireText(value, "name", maximumLength: 100),
                    RequireUtcInstant(value, "deadline_time"),
                    RequireBoolean(value, "finished"),
                    RequireBoolean(value, "data_checked"),
                    RequireBoolean(value, "is_current"),
                    isNext));
        }

        if (nextCount > 1)
        {
            throw Invalid("events contains more than one next Gameweek.");
        }

        return results.OrderBy(item => item.Id).ToArray();
    }

    private static IReadOnlyList<OfficialFplTeam> ParseTeams(JsonElement array)
    {
        EnsureCount(array, "teams", minimum: 1, MaximumTeams);
        var results = new List<OfficialFplTeam>(array.GetArrayLength());
        var ids = new HashSet<int>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            JsonElement value = RequireObject(item, "team");
            int id = RequireInt32(value, "id", minimum: 1, maximum: int.MaxValue);
            if (!ids.Add(id))
            {
                throw Invalid("teams contains a duplicate id.");
            }

            results.Add(
                new(
                    id,
                    RequireInt32(value, "code", minimum: 1, maximum: int.MaxValue),
                    RequireText(value, "name", maximumLength: 100),
                    RequireText(value, "short_name", maximumLength: 8)));
        }

        return results.OrderBy(item => item.Id).ToArray();
    }

    private static IReadOnlyList<OfficialFplPlayer> ParsePlayers(
        JsonElement array,
        IReadOnlyList<OfficialFplTeam> teams)
    {
        EnsureCount(array, "elements", minimum: 1, MaximumPlayers);
        HashSet<int> teamIds = teams.Select(item => item.Id).ToHashSet();
        var results = new List<OfficialFplPlayer>(array.GetArrayLength());
        var ids = new HashSet<int>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            JsonElement value = RequireObject(item, "player");
            int id = RequireInt32(value, "id", minimum: 1, maximum: int.MaxValue);
            if (!ids.Add(id))
            {
                throw Invalid("elements contains a duplicate id.");
            }

            int teamId = RequireInt32(value, "team", minimum: 1, maximum: int.MaxValue);
            if (!teamIds.Contains(teamId))
            {
                throw Invalid("elements references an unknown team.");
            }

            int elementType = RequireInt32(value, "element_type", minimum: 1, maximum: 4);
            if (!Positions.TryGetValue(elementType, out string? position))
            {
                throw Invalid("elements contains an unsupported position.");
            }

            results.Add(
                new(
                    id,
                    RequireInt32(value, "code", minimum: 1, maximum: int.MaxValue),
                    teamId,
                    position,
                    RequireText(value, "first_name", maximumLength: 100),
                    RequireText(value, "second_name", maximumLength: 100),
                    RequireText(value, "web_name", maximumLength: 100),
                    RequireInt32(value, "now_cost", minimum: 1, maximum: 2_000),
                    RequireText(value, "status", maximumLength: 8),
                    RequireText(value, "news", maximumLength: 4_000, allowEmpty: true),
                    OptionalUtcInstant(value, "news_added"),
                    OptionalInt32(value, "chance_of_playing_next_round", 0, 100),
                    RequireDecimalString(value, "selected_by_percent", 0m, 100m),
                    RequireInt32(value, "total_points", int.MinValue, int.MaxValue),
                    RequireInt32(value, "minutes", 0, int.MaxValue),
                    RequireInt32(value, "starts", 0, int.MaxValue)));
        }

        return results.OrderBy(item => item.Id).ToArray();
    }

    private static IReadOnlyList<OfficialFplFixture> ParseFixtures(
        JsonElement root,
        IReadOnlyList<OfficialFplEvent> events,
        IReadOnlyList<OfficialFplTeam> teams)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("fixtures must be an array.");
        }

        EnsureCount(root, "fixtures", minimum: 0, MaximumFixtures);
        HashSet<int> eventIds = events.Select(item => item.Id).ToHashSet();
        HashSet<int> teamIds = teams.Select(item => item.Id).ToHashSet();
        var ids = new HashSet<int>();
        var results = new List<OfficialFplFixture>(root.GetArrayLength());
        foreach (JsonElement item in root.EnumerateArray())
        {
            JsonElement value = RequireObject(item, "fixture");
            int id = RequireInt32(value, "id", minimum: 1, maximum: int.MaxValue);
            if (!ids.Add(id))
            {
                throw Invalid("fixtures contains a duplicate id.");
            }

            int? eventId = OptionalInt32(value, "event", 1, MaximumEvents);
            if (eventId is not null && !eventIds.Contains(eventId.Value))
            {
                throw Invalid("fixtures references an unknown event.");
            }

            int homeTeamId = RequireInt32(value, "team_h", 1, int.MaxValue);
            int awayTeamId = RequireInt32(value, "team_a", 1, int.MaxValue);
            if (!teamIds.Contains(homeTeamId) || !teamIds.Contains(awayTeamId))
            {
                throw Invalid("fixtures references an unknown team.");
            }

            if (homeTeamId == awayTeamId)
            {
                throw Invalid("fixture teams must differ.");
            }

            int? homeScore = OptionalInt32(value, "team_h_score", 0, int.MaxValue);
            int? awayScore = OptionalInt32(value, "team_a_score", 0, int.MaxValue);
            if ((homeScore is null) != (awayScore is null))
            {
                throw Invalid("fixture scores must both be present or both be null.");
            }

            results.Add(
                new(
                    id,
                    eventId,
                    homeTeamId,
                    awayTeamId,
                    OptionalUtcInstant(value, "kickoff_time"),
                    RequireBoolean(value, "started"),
                    RequireBoolean(value, "finished"),
                    RequireBoolean(value, "finished_provisional"),
                    homeScore,
                    awayScore));
        }

        return results.OrderBy(item => item.Id).ToArray();
    }

    private static JsonElement RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{name} must be an object.");
        }

        return value;
    }

    private static JsonElement RequireArray(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{name} must be an array.");
        }

        return property;
    }

    private static void EnsureCount(
        JsonElement array,
        string name,
        int minimum,
        int maximum)
    {
        int count = array.GetArrayLength();
        if (count < minimum || count > maximum)
        {
            throw Invalid($"{name} count is outside the supported range.");
        }
    }

    private static string RequireText(
        JsonElement value,
        string name,
        int maximumLength,
        bool allowEmpty = false)
    {
        if (!value.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{name} must be a string.");
        }

        string result = property.GetString() ?? string.Empty;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(result))
            || result.Length > maximumLength)
        {
            throw Invalid($"{name} is outside the supported length.");
        }

        return result;
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

    private static int? OptionalInt32(
        JsonElement value,
        string name,
        int minimum,
        int maximum)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
        {
            throw Invalid($"{name} is required.");
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out int result)
            || result < minimum
            || result > maximum)
        {
            throw Invalid($"{name} must be null or an integer in the supported range.");
        }

        return result;
    }

    private static bool RequireBoolean(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out JsonElement property)
            || (property.ValueKind != JsonValueKind.True
                && property.ValueKind != JsonValueKind.False))
        {
            throw Invalid($"{name} must be a boolean.");
        }

        return property.GetBoolean();
    }

    private static decimal RequireDecimalString(
        JsonElement value,
        string name,
        decimal minimum,
        decimal maximum)
    {
        if (!value.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.String
            || !decimal.TryParse(
                property.GetString(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out decimal result)
            || result < minimum
            || result > maximum)
        {
            throw Invalid($"{name} must be a decimal string in the supported range.");
        }

        return result;
    }

    private static DateTimeOffset RequireUtcInstant(JsonElement value, string name) =>
        OptionalUtcInstant(value, name)
        ?? throw Invalid($"{name} must be a UTC timestamp.");

    private static DateTimeOffset? OptionalUtcInstant(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
        {
            throw Invalid($"{name} is required.");
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParseExact(
                property.GetString(),
                ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset result)
            || result.Offset != TimeSpan.Zero)
        {
            throw Invalid($"{name} must be null or a UTC timestamp.");
        }

        return result;
    }

    private static string DeriveSeasonCode(DateTimeOffset firstDeadlineUtc)
    {
        int startYear = firstDeadlineUtc.Year;
        int endYear = (startYear + 1) % 100;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{startYear:D4}-{endYear:D2}");
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> json, string resource)
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
                throw Invalid($"{resource} contains a duplicate JSON property.");
            }
        }
    }

    private static OfficialFplPayloadException Invalid(string message) =>
        new($"Official FPL payload validation failed: {message}");
}

public sealed class OfficialFplPayloadException : Exception
{
    public OfficialFplPayloadException(string message)
        : base(message)
    {
    }

    public OfficialFplPayloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
