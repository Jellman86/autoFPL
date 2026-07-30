namespace AutoFpl.Api.Intelligence;

internal sealed record FbrefPlayingTimeSource(
    string SourceKey,
    string CompetitionSeason,
    string FbrefSeason,
    bool IsCurrentTargetSource,
    ResearchSourceDefinition Definition);

internal static class FbrefPlayingTimeSources
{
    public const string SourceKeyPrefix =
        "fbref-championship-playing-time-";
    public const string CurrentCompetitionSeason = "2025-26";

    public static readonly IReadOnlyList<FbrefPlayingTimeSource> All =
    [
        Create("2021-22", "2021-2022"),
        Create("2022-23", "2022-2023"),
        Create("2023-24", "2023-2024"),
        Create("2024-25", "2024-2025"),
        Create(
            CurrentCompetitionSeason,
            "2025-2026",
            isCurrentTargetSource: true),
    ];

    public static FbrefPlayingTimeSource Current =>
        All.Single(source => source.IsCurrentTargetSource);

    public static FbrefPlayingTimeSource Get(string sourceKey) =>
        All.SingleOrDefault(source => StringComparer.Ordinal.Equals(
                source.SourceKey,
                sourceKey))
            ?? throw new ResearchSourceSnapshotException(
                "The snapshot is not a registered FBref Championship "
                + "playing-time source.");

    private static FbrefPlayingTimeSource Create(
        string competitionSeason,
        string fbrefSeason,
        bool isCurrentTargetSource = false)
    {
        string sourceKey = $"{SourceKeyPrefix}{competitionSeason}";
        var canonicalUri = new Uri(
            $"https://fbref.com/en/comps/10/{fbrefSeason}/playingtime/"
            + $"{fbrefSeason}-Championship-Stats",
            UriKind.Absolute);
        IReadOnlyList<Uri> allowedFinalUris = isCurrentTargetSource
            ? [
                canonicalUri,
                new Uri(
                    "https://fbref.com/en/comps/10/playingtime/"
                    + "Championship-Stats",
                    UriKind.Absolute),
            ]
            : [canonicalUri];
        string requiredTitle = isCurrentTargetSource
            ? "Championship Playing Time | FBref.com"
            : $"{fbrefSeason} Championship Playing Time | FBref.com";
        var definition = new ResearchSourceDefinition(
            sourceKey,
            "prior-competition-playing-time",
            canonicalUri,
            "sports-reference-fbref",
            ["identity", "appearances", "starts", "minutes"],
            true,
            [
                "This aggregate establishes prior-season exposure, not match-order temporal form.",
                "FBref identities require an explicit reviewed bridge to official FPL player codes.",
                "The source can be corrected after publication; retrieval time is retained.",
                "The source remains shadow-only until point-in-time coverage and predictive gain are evaluated.",
            ],
            ByparrClient.TransportKey,
            allowedFinalUris,
            [requiredTitle],
            PollAutomatically: false);
        return new(
            sourceKey,
            competitionSeason,
            fbrefSeason,
            isCurrentTargetSource,
            definition);
    }
}
