namespace AutoFpl.Api.Sources;

internal sealed record OfficialFplPayload(
    string SeasonCode,
    byte[] BootstrapJson,
    byte[] FixturesJson,
    string BootstrapSha256,
    string FixturesSha256,
    IReadOnlyList<OfficialFplEvent> Events,
    IReadOnlyList<OfficialFplTeam> Teams,
    IReadOnlyList<OfficialFplPlayer> Players,
    IReadOnlyList<OfficialFplFixture> Fixtures,
    int? NextGameweekNumber,
    DateTimeOffset? NextDeadlineUtc,
    int? LatestCompletedGameweek);

internal sealed record OfficialFplEvent(
    int Id,
    string Name,
    DateTimeOffset DeadlineUtc,
    bool Finished,
    bool DataChecked,
    bool IsCurrent,
    bool IsNext);

internal sealed record OfficialFplTeam(
    int Id,
    int Code,
    string Name,
    string ShortName);

internal sealed record OfficialFplPlayer(
    int Id,
    int Code,
    int TeamId,
    string Position,
    string FirstName,
    string SecondName,
    string WebName,
    string PhotoIdentifier,
    int PriceTenths,
    string Status,
    string News,
    DateTimeOffset? NewsAddedUtc,
    int? ChanceNextRound,
    decimal SelectedByPercent,
    int TotalPoints,
    int Minutes,
    int Starts);

internal sealed record OfficialFplFixture(
    int Id,
    int? EventId,
    int HomeTeamId,
    int AwayTeamId,
    DateTimeOffset? KickoffUtc,
    bool Started,
    bool Finished,
    bool FinishedProvisional,
    int? HomeScore,
    int? AwayScore);
