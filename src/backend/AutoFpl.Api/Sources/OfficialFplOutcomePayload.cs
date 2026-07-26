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
    int RedCards);
