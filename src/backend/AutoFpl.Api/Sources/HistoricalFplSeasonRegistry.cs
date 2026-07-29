using System.Diagnostics.CodeAnalysis;

namespace AutoFpl.Api.Sources;

internal sealed record HistoricalFplSeasonDefinition(
    string SeasonCode,
    string SourceRevision,
    string ExpectedPlayersSha256,
    string ExpectedGameweeksSha256,
    int ExpectedPlayerCount,
    int ExpectedPlayerGameweekCount,
    DateTimeOffset PublishedAtUtc)
{
    public Uri PlayersUri =>
        new(
            $"https://raw.githubusercontent.com/vaastav/Fantasy-Premier-League/"
            + $"{SourceRevision}/data/{SeasonCode}/players_raw.csv");

    public Uri GameweeksUri =>
        new(
            $"https://raw.githubusercontent.com/vaastav/Fantasy-Premier-League/"
            + $"{SourceRevision}/data/{SeasonCode}/gws/merged_gw.csv");
}

internal static class HistoricalFplSeasonRegistry
{
    public const string SourceKey = "vaastav-fpl-historical/v1";
    public const string DefaultSeasonCode = "2025-26";

    private const string SourceRevision =
        "f9ed3e8839b0f970e0d5d4a83c5628f6eaee755a";

    public static IReadOnlyList<HistoricalFplSeasonDefinition> Seasons { get; } =
    [
        new(
            "2022-23",
            SourceRevision,
            "a874c12817bbf4d454e60a5765629742b528d4ad0c2f39f56e4fc89640605f7f",
            "b78a6d0456141f9033c32fc4122931baae780ac4a4938683451b1fce7a4fdd15",
            778,
            26_505,
            new(2026, 6, 17, 12, 19, 44, TimeSpan.Zero)),
        new(
            "2023-24",
            SourceRevision,
            "43d8cf5efb3d901f2499558c40b392b7f0ee22afe28030f36266ad29024c63d9",
            "e9c09c8856f1c86b4f920f46ddd5033af83409439dfda53be925df2a3e7c8a9e",
            865,
            29_725,
            new(2026, 6, 17, 12, 19, 44, TimeSpan.Zero)),
        new(
            "2024-25",
            SourceRevision,
            "75686051b265cbe7755ac71213ecaad21b26ee1cc46a8bafbba19c39ce894b05",
            "5bbbcba6353b4c72ad273adcc8e3aa451946a826564679788f45b1cb3325b84e",
            784,
            27_283,
            new(2026, 6, 17, 12, 19, 44, TimeSpan.Zero)),
        new(
            DefaultSeasonCode,
            SourceRevision,
            "412ce0172016f8f98f25177dc6de9f3cd2a8ec7a6135f9aa638d7fdee784d67b",
            "0d09f1f1cb1b5520ec8e2f25238aa652efe2a263d8ca7cb2b6538b27bf86727d",
            841,
            29_747,
            new(2026, 6, 17, 12, 19, 44, TimeSpan.Zero)),
    ];

    public static bool TryGet(
        string seasonCode,
        [NotNullWhen(true)] out HistoricalFplSeasonDefinition? definition)
    {
        definition = Seasons.SingleOrDefault(
            candidate =>
                StringComparer.Ordinal.Equals(candidate.SeasonCode, seasonCode));
        return definition is not null;
    }

    public static HistoricalFplSeasonDefinition GetRequired(string seasonCode) =>
        TryGet(seasonCode, out HistoricalFplSeasonDefinition? definition)
            ? definition!
            : throw new HistoricalFplSeasonPayloadException(
                $"Historical FPL season '{seasonCode}' is not registered.");
}
