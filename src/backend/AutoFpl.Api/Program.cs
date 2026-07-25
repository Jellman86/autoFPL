using System.Text.Json.Serialization;

using AutoFpl.Api.Errors;
using AutoFpl.Api.Health;
using AutoFpl.Contracts.Lineups;
using AutoFpl.Contracts.Outcomes;
using AutoFpl.Contracts.Selections;
using AutoFpl.Contracts.Snapshots;
using AutoFpl.Contracts.Squads;
using AutoFpl.Domain.Lineups;
using AutoFpl.Domain.Outcomes;
using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Snapshots;
using AutoFpl.Domain.Squads;

if (args.Length == 1 && StringComparer.Ordinal.Equals(args[0], "--health-check"))
{
    return await HealthProbe.CheckAsync();
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DecisionSnapshotValidationExceptionHandler>();
builder.Services.AddExceptionHandler<SquadValidationExceptionHandler>();
builder.Services.AddExceptionHandler<LineupValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GameweekSelectionValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GameweekCaptaincyResolutionValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GameweekSubstitutionResolutionValidationExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options =>
{
    options.ThrowOnBadRequest = false;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.AllowDuplicateProperties = false;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(
        new DecisionSnapshotMetadataRequestJsonConverter());
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 16 * 1024;
});

WebApplication app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/healthz", () => Results.Ok(new ProbeResponse("healthy")));
app.MapGet("/readyz", () => Results.Ok(new ProbeResponse("ready")));
app.MapPost(
    "/api/v1/decision-snapshot-metadata/validation",
    (DecisionSnapshotMetadataRequest request) =>
    {
        DecisionSnapshotMetadata metadata = DecisionSnapshotMetadata.Create(
            request.SchemaVersion ?? string.Empty,
            request.SourceType ?? string.Empty);

        return Results.Ok(DecisionSnapshotMetadataDocument.FromDomain(metadata));
    });
app.MapPost(
    "/api/v1/squads/validation",
    (SquadValidationRequest request) =>
    {
        if (request.BudgetTenths is null
            || request.Players is null
            || request.Players.Any(PlayerRequestIsMalformed))
        {
            return Results.BadRequest();
        }

        SquadPlayer[] players = CreateSquadPlayers(request.Players);
        Squad squad = Squad.Create(request.BudgetTenths.Value, players);
        return Results.Ok(SquadValidationDocument.FromDomain(squad));
    });
app.MapPost(
    "/api/v1/lineups/validation",
    (LineupValidationRequest request) =>
    {
        if (request.BudgetTenths is null
            || request.Players is null
            || request.StartingPlayerIds is null
            || request.CaptainPlayerId is null
            || request.ViceCaptainPlayerId is null
            || request.Players.Any(PlayerRequestIsMalformed)
            || request.StartingPlayerIds.Any(playerId => playerId is null))
        {
            return Results.BadRequest();
        }

        SquadPlayer[] players = CreateSquadPlayers(request.Players);
        Squad squad = Squad.Create(request.BudgetTenths.Value, players);
        int[] startingPlayerIds = request.StartingPlayerIds
            .Select(playerId => playerId!.Value)
            .ToArray();
        Lineup lineup = Lineup.Create(
            squad,
            startingPlayerIds,
            request.CaptainPlayerId.Value,
            request.ViceCaptainPlayerId.Value);
        return Results.Ok(LineupValidationDocument.FromDomain(lineup));
    });
app.MapPost(
    "/api/v1/gameweek-selections/validation",
    (GameweekSelectionValidationRequest request) =>
    {
        if (request.BudgetTenths is null
            || request.Players is null
            || request.StartingPlayerIds is null
            || request.CaptainPlayerId is null
            || request.ViceCaptainPlayerId is null
            || request.ReplacementGoalkeeperPlayerId is null
            || request.OutfieldSubstitutePlayerIds is null
            || request.Players.Any(PlayerRequestIsMalformed)
            || request.StartingPlayerIds.Any(playerId => playerId is null)
            || request.OutfieldSubstitutePlayerIds.Any(playerId => playerId is null))
        {
            return Results.BadRequest();
        }

        SquadPlayer[] players = CreateSquadPlayers(request.Players);
        Squad squad = Squad.Create(request.BudgetTenths.Value, players);
        int[] startingPlayerIds = request.StartingPlayerIds
            .Select(playerId => playerId!.Value)
            .ToArray();
        int[] outfieldSubstitutePlayerIds = request.OutfieldSubstitutePlayerIds
            .Select(playerId => playerId!.Value)
            .ToArray();
        GameweekSelection selection = GameweekSelection.Create(
            squad,
            startingPlayerIds,
            request.CaptainPlayerId.Value,
            request.ViceCaptainPlayerId.Value,
            request.ReplacementGoalkeeperPlayerId.Value,
            outfieldSubstitutePlayerIds);
        return Results.Ok(GameweekSelectionValidationDocument.FromDomain(selection));
    });
app.MapPost(
    "/api/v1/gameweek-outcomes/captaincy-resolution",
    (GameweekCaptaincyResolutionRequest request) =>
    {
        if (request.BudgetTenths is null
            || request.Players is null
            || request.StartingPlayerIds is null
            || request.CaptainPlayerId is null
            || request.ViceCaptainPlayerId is null
            || request.ReplacementGoalkeeperPlayerId is null
            || request.OutfieldSubstitutePlayerIds is null
            || request.PlayerIdsWithMinutes is null
            || request.Players.Any(PlayerRequestIsMalformed)
            || request.StartingPlayerIds.Any(playerId => playerId is null)
            || request.OutfieldSubstitutePlayerIds.Any(playerId => playerId is null)
            || request.PlayerIdsWithMinutes.Any(playerId => playerId is null))
        {
            return Results.BadRequest();
        }

        SquadPlayer[] players = CreateSquadPlayers(request.Players);
        Squad squad = Squad.Create(request.BudgetTenths.Value, players);
        int[] startingPlayerIds = request.StartingPlayerIds
            .Select(playerId => playerId!.Value)
            .ToArray();
        int[] outfieldSubstitutePlayerIds = request.OutfieldSubstitutePlayerIds
            .Select(playerId => playerId!.Value)
            .ToArray();
        int[] playerIdsWithMinutes = request.PlayerIdsWithMinutes
            .Select(playerId => playerId!.Value)
            .ToArray();
        GameweekSelection selection = GameweekSelection.Create(
            squad,
            startingPlayerIds,
            request.CaptainPlayerId.Value,
            request.ViceCaptainPlayerId.Value,
            request.ReplacementGoalkeeperPlayerId.Value,
            outfieldSubstitutePlayerIds);
        GameweekCaptaincyResolution resolution = GameweekCaptaincyResolution.Resolve(
            squad,
            selection,
            playerIdsWithMinutes);
        return Results.Ok(GameweekCaptaincyResolutionDocument.FromDomain(resolution));
    });
app.MapPost(
    "/api/v1/gameweek-outcomes/substitution-resolution",
    (GameweekSubstitutionResolutionRequest request) =>
    {
        if (request.BudgetTenths is null
            || request.Players is null
            || request.StartingPlayerIds is null
            || request.CaptainPlayerId is null
            || request.ViceCaptainPlayerId is null
            || request.ReplacementGoalkeeperPlayerId is null
            || request.OutfieldSubstitutePlayerIds is null
            || request.PlayerIdsWhoPlayed is null
            || request.Players.Any(PlayerRequestIsMalformed)
            || request.StartingPlayerIds.Any(playerId => playerId is null)
            || request.OutfieldSubstitutePlayerIds.Any(playerId => playerId is null)
            || request.PlayerIdsWhoPlayed.Any(playerId => playerId is null))
        {
            return Results.BadRequest();
        }

        SquadPlayer[] players = CreateSquadPlayers(request.Players);
        Squad squad = Squad.Create(request.BudgetTenths.Value, players);
        int[] startingPlayerIds = request.StartingPlayerIds
            .Select(playerId => playerId!.Value)
            .ToArray();
        int[] outfieldSubstitutePlayerIds = request.OutfieldSubstitutePlayerIds
            .Select(playerId => playerId!.Value)
            .ToArray();
        int[] playerIdsWhoPlayed = request.PlayerIdsWhoPlayed
            .Select(playerId => playerId!.Value)
            .ToArray();
        GameweekSelection selection = GameweekSelection.Create(
            squad,
            startingPlayerIds,
            request.CaptainPlayerId.Value,
            request.ViceCaptainPlayerId.Value,
            request.ReplacementGoalkeeperPlayerId.Value,
            outfieldSubstitutePlayerIds);
        GameweekSubstitutionResolution resolution = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed);
        return Results.Ok(GameweekSubstitutionResolutionDocument.FromDomain(resolution));
    });

static bool PlayerRequestIsMalformed(SquadPlayerRequest? playerRequest) =>
    playerRequest is null
    || playerRequest.PlayerId is null
    || playerRequest.ClubId is null
    || playerRequest.Position is null
    || playerRequest.PriceTenths is null;

static SquadPlayer[] CreateSquadPlayers(IReadOnlyList<SquadPlayerRequest?> playerRequests)
{
    var players = new SquadPlayer[playerRequests.Count];
    for (int index = 0; index < playerRequests.Count; index++)
    {
        SquadPlayerRequest playerRequest = playerRequests[index]!;
        players[index] = SquadPlayer.Create(
            playerRequest.PlayerId!.Value,
            playerRequest.ClubId!.Value,
            playerRequest.Position!,
            playerRequest.PriceTenths!.Value);
    }

    return players;
}

app.Run();
return 0;

public sealed record DecisionSnapshotMetadataRequest(
    [property: JsonPropertyName("schemaVersion")] string? SchemaVersion,
    [property: JsonPropertyName("sourceType")] string? SourceType);

public sealed record ProbeResponse([property: JsonPropertyName("status")] string Status);

public partial class Program;
