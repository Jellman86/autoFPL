using System.Text.Json.Serialization;

namespace AutoFpl.Contracts.Sources;

public sealed record OfficialFplPlayerDossierDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("seasonCode")] string SeasonCode,
    [property: JsonPropertyName("targetGameweek")] int TargetGameweek,
    [property: JsonPropertyName("decisionCutoffUtc")] DateTimeOffset DecisionCutoffUtc,
    [property: JsonPropertyName("selectedCaptureId")] long SelectedCaptureId,
    [property: JsonPropertyName("captureAvailableAtUtc")] DateTimeOffset CaptureAvailableAtUtc,
    [property: JsonPropertyName("player")] OfficialFplPlayerIdentityDocument Player,
    [property: JsonPropertyName("recentOutcomes")]
        IReadOnlyList<OfficialFplPlayerOutcomeDocument> RecentOutcomes,
    [property: JsonPropertyName("upcomingFixtures")]
        IReadOnlyList<OfficialFplPlayerFixtureDocument> UpcomingFixtures);

public sealed record OfficialFplPlayerIdentityDocument(
    [property: JsonPropertyName("playerId")] int PlayerId,
    [property: JsonPropertyName("playerCode")] int PlayerCode,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("webName")] string WebName,
    [property: JsonPropertyName("teamId")] int TeamId,
    [property: JsonPropertyName("teamName")] string TeamName,
    [property: JsonPropertyName("teamShortName")] string TeamShortName,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("priceTenths")] int PriceTenths,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("news")] string News,
    [property: JsonPropertyName("photoIdentifier")] string? PhotoIdentifier,
    [property: JsonPropertyName("photoUrl")] string? PhotoUrl);

public sealed record OfficialFplPlayerOutcomeDocument(
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("outcomeCaptureId")] long OutcomeCaptureId,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc,
    [property: JsonPropertyName("isGameweekAggregate")] bool IsGameweekAggregate,
    [property: JsonPropertyName("fixtures")]
        IReadOnlyList<OfficialFplPlayerFixtureDocument> Fixtures,
    [property: JsonPropertyName("minutes")] int Minutes,
    [property: JsonPropertyName("starts")] int Starts,
    [property: JsonPropertyName("totalPoints")] int TotalPoints,
    [property: JsonPropertyName("goalsScored")] int GoalsScored,
    [property: JsonPropertyName("assists")] int Assists,
    [property: JsonPropertyName("cleanSheets")] int CleanSheets,
    [property: JsonPropertyName("goalsConceded")] int GoalsConceded,
    [property: JsonPropertyName("saves")] int Saves,
    [property: JsonPropertyName("bonus")] int Bonus,
    [property: JsonPropertyName("yellowCards")] int YellowCards,
    [property: JsonPropertyName("redCards")] int RedCards);

public sealed record OfficialFplPlayerFixtureDocument(
    [property: JsonPropertyName("fixtureId")] int FixtureId,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("kickoffUtc")] DateTimeOffset? KickoffUtc,
    [property: JsonPropertyName("opponentTeamId")] int OpponentTeamId,
    [property: JsonPropertyName("opponentName")] string OpponentName,
    [property: JsonPropertyName("opponentShortName")] string OpponentShortName,
    [property: JsonPropertyName("isHome")] bool IsHome,
    [property: JsonPropertyName("started")] bool Started,
    [property: JsonPropertyName("finished")] bool Finished);
