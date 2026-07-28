using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.VisualBasic.FileIO;

namespace AutoFpl.Api.Sources;

internal static class HistoricalFplSeasonPayloadParser
{
    private static readonly string[] PlayerHeaders =
    [
        "id",
        "code",
        "first_name",
        "second_name",
        "web_name",
        "element_type",
        "team",
        "status",
        "chance_of_playing_next_round",
        "news",
        "news_added",
    ];

    private static readonly string[] GameweekHeaders =
    [
        "element",
        "GW",
        "fixture",
        "kickoff_time",
        "team",
        "opponent_team",
        "was_home",
        "minutes",
        "starts",
        "total_points",
        "goals_scored",
        "assists",
        "clean_sheets",
        "goals_conceded",
        "saves",
        "bonus",
        "yellow_cards",
        "red_cards",
        "bps",
        "influence",
        "creativity",
        "threat",
        "ict_index",
        "expected_goals",
        "expected_assists",
        "expected_goal_involvements",
        "expected_goals_conceded",
    ];

    public static HistoricalFplSeasonPayload Parse(
        byte[] playersCsv,
        byte[] gameweeksCsv)
    {
        ArgumentNullException.ThrowIfNull(playersCsv);
        ArgumentNullException.ThrowIfNull(gameweeksCsv);

        try
        {
            IReadOnlyList<HistoricalFplPlayer> players =
                ParsePlayers(playersCsv, out IReadOnlySet<int> excludedElementIds);
            var byElement = players.ToDictionary(player => player.SeasonElementId);
            IReadOnlyList<HistoricalFplPlayerGameweek> gameweeks =
                ParseGameweeks(gameweeksCsv, byElement, excludedElementIds);
            return new(
                playersCsv,
                gameweeksCsv,
                Convert.ToHexString(SHA256.HashData(playersCsv)).ToLowerInvariant(),
                Convert.ToHexString(SHA256.HashData(gameweeksCsv)).ToLowerInvariant(),
                players,
                gameweeks);
        }
        catch (HistoricalFplSeasonPayloadException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is FormatException
                or OverflowException
                or MalformedLineException
                or DecoderFallbackException
                or ArgumentException)
        {
            throw Invalid("the CSV payload is malformed.", exception);
        }
    }

    private static IReadOnlyList<HistoricalFplPlayer> ParsePlayers(
        byte[] csv,
        out IReadOnlySet<int> excludedElementIds)
    {
        using CsvRows rows = CsvRows.Open(csv, PlayerHeaders);
        var players = new List<HistoricalFplPlayer>();
        var excluded = new HashSet<int>();
        var elementIds = new HashSet<int>();
        var playerCodes = new HashSet<int>();
        while (rows.Read() is IReadOnlyDictionary<string, string> row)
        {
            int elementId = PositiveInt(row, "id");
            int playerCode = PositiveInt(row, "code");
            if (!elementIds.Add(elementId) || !playerCodes.Add(playerCode))
            {
                throw Invalid("player identities must be unique within the archive.");
            }

            string elementType = Required(row, "element_type", 1);
            if (StringComparer.Ordinal.Equals(elementType, "5"))
            {
                excluded.Add(elementId);
                continue;
            }

            players.Add(
                new(
                    elementId,
                    playerCode,
                    Required(row, "first_name", 100),
                    Required(row, "second_name", 100),
                    Required(row, "web_name", 100),
                    Position(elementType),
                    PositiveInt(row, "team"),
                    Required(row, "status", 8),
                    NullablePercent(row, "chance_of_playing_next_round"),
                    Convert.ToHexString(
                            SHA256.HashData(
                                Encoding.UTF8.GetBytes(row["news"])))
                        .ToLowerInvariant(),
                    NullableUtc(row, "news_added")));
        }

        if (players.Count is < 1 or > 2000)
        {
            throw Invalid("the player archive contains an unsupported row count.");
        }

        excludedElementIds = excluded;
        return players;
    }

    private static IReadOnlyList<HistoricalFplPlayerGameweek> ParseGameweeks(
        byte[] csv,
        IReadOnlyDictionary<int, HistoricalFplPlayer> players,
        IReadOnlySet<int> excludedElementIds)
    {
        using CsvRows rows = CsvRows.Open(csv, GameweekHeaders);
        var gameweeks = new List<HistoricalFplPlayerGameweek>();
        var byKey =
            new Dictionary<
                (int ElementId, int Gameweek, int FixtureId),
                HistoricalFplPlayerGameweek>();
        while (rows.Read() is IReadOnlyDictionary<string, string> row)
        {
            int elementId = PositiveInt(row, "element");
            if (excludedElementIds.Contains(elementId))
            {
                continue;
            }

            if (!players.TryGetValue(elementId, out HistoricalFplPlayer? player))
            {
                throw Invalid(
                    "a player Gameweek row references an unknown season element.");
            }

            int gameweek = BoundedInt(row, "GW", 1, 38);
            int fixtureId = PositiveInt(row, "fixture");
            var item = new HistoricalFplPlayerGameweek(
                elementId,
                player.PlayerCode,
                gameweek,
                fixtureId,
                Utc(row, "kickoff_time"),
                Required(row, "team", 100),
                PositiveInt(row, "opponent_team"),
                Boolean(row, "was_home"),
                BoundedInt(row, "minutes", 0, 180),
                BoundedInt(row, "starts", 0, 2),
                BoundedInt(row, "total_points", -50, 100),
                BoundedInt(row, "goals_scored", 0, 10),
                BoundedInt(row, "assists", 0, 10),
                BoundedInt(row, "clean_sheets", 0, 2),
                BoundedInt(row, "goals_conceded", 0, 20),
                BoundedInt(row, "saves", 0, 30),
                BoundedInt(row, "bonus", 0, 6),
                BoundedInt(row, "yellow_cards", 0, 2),
                BoundedInt(row, "red_cards", 0, 2),
                BoundedInt(row, "bps", -100, 300),
                Decimal(row, "influence", 0, 1000),
                Decimal(row, "creativity", 0, 1000),
                Decimal(row, "threat", 0, 1000),
                Decimal(row, "ict_index", 0, 1000),
                Decimal(row, "expected_goals", 0, 20),
                Decimal(row, "expected_assists", 0, 20),
                Decimal(row, "expected_goal_involvements", 0, 20),
                Decimal(row, "expected_goals_conceded", 0, 30),
                NullableBoundedInt(
                    row,
                    "clearances_blocks_interceptions",
                    0,
                    100),
                NullableBoundedInt(row, "defensive_contribution", 0, 100),
                NullableBoundedInt(row, "recoveries", 0, 100),
                NullableBoundedInt(row, "tackles", 0, 100));
            var key = (elementId, gameweek, fixtureId);
            if (byKey.TryGetValue(key, out HistoricalFplPlayerGameweek? existing))
            {
                if (existing != item)
                {
                    throw Invalid(
                        "duplicate player Gameweek identities contain conflicting values.");
                }

                continue;
            }

            byKey.Add(key, item);
            gameweeks.Add(item);
        }

        if (gameweeks.Count is < 1 or > 100000)
        {
            throw Invalid("the player Gameweek archive contains an unsupported row count.");
        }

        return gameweeks;
    }

    private static string Required(
        IReadOnlyDictionary<string, string> row,
        string name,
        int maximumLength)
    {
        string value = row[name].Trim();
        if (value.Length is 0 || value.Length > maximumLength)
        {
            throw Invalid($"field '{name}' is invalid.");
        }

        return value;
    }

    private static int PositiveInt(
        IReadOnlyDictionary<string, string> row,
        string name) => BoundedInt(row, name, 1, int.MaxValue);

    private static int BoundedInt(
        IReadOnlyDictionary<string, string> row,
        string name,
        int minimum,
        int maximum)
    {
        if (!int.TryParse(
                row[name],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
            || value < minimum
            || value > maximum)
        {
            throw Invalid($"field '{name}' is outside the supported range.");
        }

        return value;
    }

    private static decimal Decimal(
        IReadOnlyDictionary<string, string> row,
        string name,
        decimal minimum,
        decimal maximum)
    {
        if (!decimal.TryParse(
                row[name],
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal value)
            || value < minimum
            || value > maximum)
        {
            throw Invalid($"field '{name}' is outside the supported range.");
        }

        return value;
    }

    private static bool Boolean(
        IReadOnlyDictionary<string, string> row,
        string name)
    {
        if (!bool.TryParse(row[name], out bool value))
        {
            throw Invalid($"field '{name}' is not Boolean.");
        }

        return value;
    }

    private static int? NullablePercent(
        IReadOnlyDictionary<string, string> row,
        string name)
    {
        string value = row[name].Trim();
        return value.Length == 0 || StringComparer.OrdinalIgnoreCase.Equals(value, "None")
            ? null
            : BoundedInt(row, name, 0, 100);
    }

    private static int? NullableBoundedInt(
        IReadOnlyDictionary<string, string> row,
        string name,
        int minimum,
        int maximum)
    {
        if (!row.TryGetValue(name, out string? raw))
        {
            return null;
        }

        string value = raw.Trim();
        return value.Length == 0
            || StringComparer.OrdinalIgnoreCase.Equals(value, "None")
                ? null
                : BoundedInt(row, name, minimum, maximum);
    }

    private static DateTimeOffset? NullableUtc(
        IReadOnlyDictionary<string, string> row,
        string name)
    {
        string value = row[name].Trim();
        return value.Length == 0 || StringComparer.OrdinalIgnoreCase.Equals(value, "None")
            ? null
            : ParseUtc(value, name);
    }

    private static DateTimeOffset Utc(
        IReadOnlyDictionary<string, string> row,
        string name) => ParseUtc(row[name], name);

    private static DateTimeOffset ParseUtc(string value, string name)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed)
            || parsed.Offset != TimeSpan.Zero)
        {
            throw Invalid($"field '{name}' is not a UTC timestamp.");
        }

        return parsed;
    }

    private static string Position(string value) =>
        value switch
        {
            "1" => "goalkeeper",
            "2" => "defender",
            "3" => "midfielder",
            "4" => "forward",
            _ => throw Invalid("field 'element_type' is outside the supported range."),
        };

    private static HistoricalFplSeasonPayloadException Invalid(
        string reason,
        Exception? innerException = null)
    {
        string message = $"Historical FPL archive rejected: {reason}";
        return innerException is null
            ? new(message)
            : new(message, innerException);
    }

    private sealed class CsvRows : IDisposable
    {
        private readonly TextFieldParser _parser;
        private readonly IReadOnlyList<string> _headers;
        private int _lineNumber = 1;

        private CsvRows(TextFieldParser parser, IReadOnlyList<string> headers)
        {
            _parser = parser;
            _headers = headers;
        }

        public static CsvRows Open(byte[] csv, IReadOnlyCollection<string> requiredHeaders)
        {
            if (csv.Length == 0)
            {
                throw Invalid("a CSV payload is empty.");
            }

            var stream = new MemoryStream(csv, writable: false);
            var parser = new TextFieldParser(
                stream,
                Encoding.UTF8,
                detectEncoding: false,
                leaveOpen: false)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = false,
            };
            parser.SetDelimiters(",");
            string[] headers = parser.ReadFields()
                ?? throw Invalid("a CSV header is missing.");
            if (headers.Length == 0 || headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
            {
                parser.Dispose();
                throw Invalid("CSV headers must be present and unique.");
            }

            string[] missing = requiredHeaders
                .Where(required => !headers.Contains(required, StringComparer.Ordinal))
                .ToArray();
            if (missing.Length > 0)
            {
                parser.Dispose();
                throw Invalid($"required CSV header '{missing[0]}' is missing.");
            }

            return new(parser, headers);
        }

        public IReadOnlyDictionary<string, string>? Read()
        {
            if (_parser.EndOfData)
            {
                return null;
            }

            string[] fields = _parser.ReadFields()
                ?? throw Invalid("a CSV row is missing.");
            _lineNumber++;
            if (fields.Length != _headers.Count)
            {
                throw Invalid($"CSV row {_lineNumber} has the wrong number of fields.");
            }

            var row = new Dictionary<string, string>(
                _headers.Count,
                StringComparer.Ordinal);
            for (int index = 0; index < _headers.Count; index++)
            {
                row.Add(_headers[index], fields[index]);
            }

            return row;
        }

        public void Dispose() => _parser.Dispose();
    }
}
