namespace AutoFpl.Api.Sources;

internal sealed record OfficialFplOutcomePayload(
    byte[] LiveJson,
    string LiveSha256,
    IReadOnlyList<OfficialFplPlayerOutcome> Players);

internal sealed record OfficialFplPlayerOutcome(
    int PlayerId,
    int Minutes,
    int Starts,
    int TotalPoints,
    int GoalsScored,
    int Assists,
    int CleanSheets,
    int GoalsConceded,
    int Saves,
    int Bonus,
    int YellowCards,
    int RedCards,
    int OwnGoals,
    int PenaltiesSaved,
    int PenaltiesMissed,
    int Bps,
    decimal Influence,
    decimal Creativity,
    decimal Threat,
    decimal IctIndex,
    int ClearancesBlocksInterceptions,
    int Recoveries,
    int Tackles,
    int DefensiveContribution,
    decimal ExpectedGoals,
    decimal ExpectedAssists,
    decimal ExpectedGoalInvolvements,
    decimal ExpectedGoalsConceded);
