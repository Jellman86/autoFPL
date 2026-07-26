using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed record ResearchSourceDefinition(
    string SourceKey,
    string SourceClass,
    Uri CanonicalUri,
    string DependenceGroup,
    IReadOnlyList<string> TargetTypes,
    bool RequiresRendering,
    IReadOnlyList<string> Limitations)
{
    public ResearchSourceDefinitionDocument ToDocument() =>
        new(
            SourceKey,
            SourceClass,
            CanonicalUri.AbsoluteUri,
            DependenceGroup,
            TargetTypes,
            RequiresRendering ? "javascript" : "static",
            "shadow-only",
            Limitations);
}

public static class ResearchSourceRegistry
{
    private static readonly IReadOnlyList<ResearchSourceDefinition> Definitions =
    [
        new(
            "premier-league-injuries",
            "official-availability-aggregation",
            new Uri(
                "https://www.premierleague.com/en/latest-player-injuries",
                UriKind.Absolute),
            "official-premier-league-and-club-reporting",
            ["availability"],
            true,
            [
                "The page aggregates club reporting and may lag a direct manager statement.",
                "An injury listing does not by itself quantify start or minutes probability.",
            ]),
        new(
            "ffscout-predicted-lineups",
            "specialist-predicted-lineup",
            new Uri(
                "https://cdn.fantasyfootballscout.co.uk/team-news",
                UriKind.Absolute),
            "ffscout-editorial-lineup",
            ["start", "availability"],
            false,
            [
                "A named predicted XI is a categorical forecast, not a calibrated probability.",
                "Early or partial pages may omit clubs; missing coverage is not a negative forecast.",
                "Its upstream reporting may overlap official and other specialist sources.",
            ]),
        new(
            "straightred-lineup-consensus",
            "derived-predicted-lineup-consensus",
            new Uri("https://www.straightred.ai/", UriKind.Absolute),
            "public-lineup-aggregators",
            ["start"],
            false,
            [
                "This is a derived consensus and must not be counted as independent of its inputs.",
                "Displayed percentages describe source agreement, not calibrated start probability.",
            ]),
    ];

    public static IReadOnlyList<ResearchSourceDefinition> All => Definitions;

    public static ResearchSourceDefinition Get(string sourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        return Definitions.SingleOrDefault(
                source => StringComparer.Ordinal.Equals(
                    source.SourceKey,
                    sourceKey))
            ?? throw new ResearchSourceSnapshotException(
                $"Unknown research source key: {sourceKey}");
    }
}
