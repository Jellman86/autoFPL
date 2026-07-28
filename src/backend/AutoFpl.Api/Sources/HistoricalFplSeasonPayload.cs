namespace AutoFpl.Api.Sources;

internal sealed record HistoricalFplSeasonPayload(
    byte[] PlayersCsv,
    byte[] GameweeksCsv,
    string PlayersSha256,
    string GameweeksSha256,
    IReadOnlyList<HistoricalFplPlayer> Players,
    IReadOnlyList<HistoricalFplPlayerGameweek> PlayerGameweeks);

internal sealed record HistoricalFplPlayer(
    int SeasonElementId,
    int PlayerCode,
    string FirstName,
    string SecondName,
    string WebName,
    string Position,
    int FinalTeamId,
    string FinalStatus,
    int? FinalChanceNextRound,
    string FinalNewsSha256,
    DateTimeOffset? FinalNewsAddedUtc);

internal sealed record HistoricalFplPlayerGameweek(
    int SeasonElementId,
    int PlayerCode,
    int Gameweek,
    int FixtureId,
    DateTimeOffset KickoffUtc,
    string TeamName,
    int OpponentTeamId,
    bool WasHome,
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
    int Bps,
    decimal Influence,
    decimal Creativity,
    decimal Threat,
    decimal IctIndex,
    decimal ExpectedGoals,
    decimal ExpectedAssists,
    decimal ExpectedGoalInvolvements,
    decimal ExpectedGoalsConceded,
    int? ClearancesBlocksInterceptions,
    int? DefensiveContribution,
    int? Recoveries,
    int? Tackles);

public sealed class HistoricalFplSeasonPayloadException : Exception
{
    public HistoricalFplSeasonPayloadException(string message)
        : base(message)
    {
    }

    public HistoricalFplSeasonPayloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
