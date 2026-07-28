namespace AutoFpl.Api.Intelligence;

internal sealed record FbrefTeamScheduleSource(
    string SourceKey,
    string SourceTeamId,
    string TeamName,
    ResearchSourceDefinition Definition);

internal static class FbrefTeamScheduleSources
{
    public const string SourceKeyPrefix = "fbref-team-schedule-";
    public const string SourceKeySuffix = "-2025-26";

    public static readonly IReadOnlyList<FbrefTeamScheduleSource> All =
    [
        Create("f7e3dfe9", "Coventry City", "Coventry-City"),
        Create("bd8769d1", "Hull City", "Hull-City"),
        Create("b74092de", "Ipswich Town", "Ipswich-Town"),
    ];

    public static FbrefTeamScheduleSource Get(string sourceKey) =>
        All.SingleOrDefault(source => StringComparer.Ordinal.Equals(
                source.SourceKey,
                sourceKey))
            ?? throw new ResearchSourceSnapshotException(
                "The snapshot is not a registered FBref team schedule.");

    private static FbrefTeamScheduleSource Create(
        string sourceTeamId,
        string teamName,
        string teamSlug)
    {
        string sourceKey =
            $"{SourceKeyPrefix}{sourceTeamId}{SourceKeySuffix}";
        var canonicalUri = new Uri(
            $"https://fbref.com/en/squads/{sourceTeamId}"
            + $"/2025-2026/matchlogs/c10/schedule/{teamSlug}"
            + "-Scores-and-Fixtures-Championship",
            UriKind.Absolute);
        var definition = new ResearchSourceDefinition(
            sourceKey,
            "prior-competition-team-schedule",
            canonicalUri,
            "sports-reference-fbref",
            [
                "match-date",
                "kickoff",
                "match-identity",
                "opponent",
                "venue",
                "result",
            ],
            true,
            [
                "The schedule establishes team match opportunities, not player availability.",
                "Player absence is explicit only after a stable match-ID join to a reviewed player log.",
                "The source can be corrected after publication; retrieval time is retained.",
                "The source remains shadow-only until identical-fold predictive gain is measured.",
            ],
            ByparrClient.TransportKey,
            [canonicalUri],
            [
                $"{teamName} Scores and Fixtures, Championship | FBref.com",
                "id=\"matchlogs_for\"",
            ],
            PollAutomatically: false);
        return new(sourceKey, sourceTeamId, teamName, definition);
    }
}
