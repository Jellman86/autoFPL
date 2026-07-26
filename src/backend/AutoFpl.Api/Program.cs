using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using AutoFpl.Api.Advice;
using AutoFpl.Api.Errors;
using AutoFpl.Api.Health;
using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Advice;
using AutoFpl.Contracts.Lineups;
using AutoFpl.Contracts.Outcomes;
using AutoFpl.Contracts.Selections;
using AutoFpl.Contracts.Snapshots;
using AutoFpl.Contracts.Sources;
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

bool runIntegrityCheck =
    args.Length == 1
    && StringComparer.Ordinal.Equals(args[0], "--database-integrity-check");
bool runBackup =
    args.Length == 2
    && StringComparer.Ordinal.Equals(args[0], "--database-backup");
bool runOfficialFplImport =
    args.Length == 1
    && StringComparer.Ordinal.Equals(args[0], "--import-official-fpl");
bool requestedOfficialFplOutcomeImport =
    args.Length > 0
    && StringComparer.Ordinal.Equals(args[0], "--import-official-fpl-outcome");
int outcomeGameweek = 0;
bool runOfficialFplOutcomeImport =
    requestedOfficialFplOutcomeImport
    && args.Length == 2
    && int.TryParse(args[1], out outcomeGameweek)
    && outcomeGameweek is >= 1 and <= 38;
if (requestedOfficialFplOutcomeImport && !runOfficialFplOutcomeImport)
{
    await Console.Error.WriteLineAsync(
        "Usage: --import-official-fpl-outcome <gameweek 1-38>");
    return 2;
}

bool runNonWebCommand =
    runIntegrityCheck
    || runBackup
    || runOfficialFplImport
    || runOfficialFplOutcomeImport;

WebApplicationBuilder builder = WebApplication.CreateBuilder(
    runNonWebCommand ? [] : args);
if (runNonWebCommand)
{
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");
builder.Services.AddSingleton(serviceProvider =>
    DatabaseOptions.FromConfiguration(
        serviceProvider.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(serviceProvider =>
    new DecisionSnapshotStore(
        serviceProvider.GetRequiredService<DatabaseOptions>()));
builder.Services.AddSingleton(serviceProvider =>
    new OfficialFplCaptureStore(
        serviceProvider.GetRequiredService<DatabaseOptions>()));
builder.Services.AddSingleton(serviceProvider =>
    new OfficialFplOutcomeStore(
        serviceProvider.GetRequiredService<DatabaseOptions>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddHttpClient<OfficialFplImporter>(
        client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "autoFPL-private-research/0.1");
        })
    .ConfigurePrimaryHttpMessageHandler(
        () => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxConnectionsPerServer = 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        });
builder.Services
    .AddHttpClient<OfficialFplOutcomeImporter>(
        client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "autoFPL-private-research/0.1");
        })
    .ConfigurePrimaryHttpMessageHandler(
        () => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxConnectionsPerServer = 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        });
builder.Services.AddExceptionHandler<DecisionSnapshotPersistenceExceptionHandler>();
builder.Services.AddExceptionHandler<DecisionSnapshotValidationExceptionHandler>();
builder.Services.AddExceptionHandler<SquadValidationExceptionHandler>();
builder.Services.AddExceptionHandler<LineupValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GameweekSelectionValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GameweekCaptaincyResolutionValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GameweekSubstitutionResolutionValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GameweekScoreResolutionValidationExceptionHandler>();
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
DecisionSnapshotStore snapshotStore =
    app.Services.GetRequiredService<DecisionSnapshotStore>();
if ((runIntegrityCheck || runBackup) && !File.Exists(snapshotStore.DatabasePath))
{
    await Console.Error.WriteLineAsync(
        $"Database does not exist: {snapshotStore.DatabasePath}");
    return 2;
}

await snapshotStore.MigrateAsync();

if (runIntegrityCheck)
{
    string result = await snapshotStore.IntegrityCheckAsync();
    await Console.Out.WriteLineAsync(result);
    return StringComparer.Ordinal.Equals(result, "ok") ? 0 : 1;
}

if (runBackup)
{
    await snapshotStore.BackupAsync(args[1]);
    await Console.Out.WriteLineAsync(Path.GetFullPath(args[1]));
    return 0;
}

if (runOfficialFplImport)
{
    OfficialFplCaptureDocument capture =
        await app.Services
            .GetRequiredService<OfficialFplImporter>()
            .ImportLatestAsync();
    await Console.Out.WriteLineAsync(
        JsonSerializer.Serialize(
            capture,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return 0;
}

if (runOfficialFplOutcomeImport)
{
    OfficialFplOutcomeCaptureDocument outcome =
        await app.Services
            .GetRequiredService<OfficialFplOutcomeImporter>()
            .ImportLatestAsync(outcomeGameweek);
    await Console.Out.WriteLineAsync(
        JsonSerializer.Serialize(
            outcome,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return 0;
}

if (!StringComparer.OrdinalIgnoreCase.Equals(
    app.Configuration["AutoFpl:SeedDemoSnapshot"],
    "false"))
{
    await SyntheticDecisionSnapshotSeeder.EnsureSeededAsync(snapshotStore);
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapGet("/healthz", () => Results.Ok(new ProbeResponse("healthy")))
    .WithName("GetHealth")
    .WithSummary("Report whether the application process is alive.")
    .WithTags("Operations")
    .Produces<ProbeResponse>();
app.MapGet(
    "/readyz",
    async (DecisionSnapshotStore store, CancellationToken cancellationToken) =>
        await store.IsReadyAsync(cancellationToken)
            ? Results.Ok(new ProbeResponse("ready"))
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .WithName("GetReadiness")
    .WithSummary("Report whether the application and SQLite schema are ready.")
    .WithTags("Operations")
    .Produces<ProbeResponse>()
    .Produces(StatusCodes.Status503ServiceUnavailable);
app.MapGet(
    "/api/v1/advice/demo",
    async (DecisionSnapshotStore store, CancellationToken cancellationToken) =>
    {
        DecisionSnapshotDocument? snapshot = await store.GetLatestSnapshotAsync(
            SyntheticDecisionSnapshotSeeder.SeasonCode,
            SyntheticDecisionSnapshotSeeder.Gameweek,
            cancellationToken);
        GameweekAdviceDocument advice = DemoGameweekAdvice.Create(snapshot);
        return Results.Ok(advice);
    })
    .WithName("GetDemoGameweekAdvice")
    .WithSummary("Return the synthetic Gameweek advice fixture used by the decision-room preview.")
    .WithTags("Advice")
    .Produces<GameweekAdviceDocument>();
app.MapGet(
    "/api/v1/data/official-fpl/latest",
    async (
        OfficialFplCaptureStore store,
        CancellationToken cancellationToken) =>
    {
        OfficialFplCaptureDocument? capture = await store.GetLatestAsync(cancellationToken);
        return capture is null ? Results.NotFound() : Results.Ok(capture);
    })
    .WithName("GetLatestOfficialFplCapture")
    .WithSummary(
        "Read provenance and counts for the latest immutable official FPL reference capture.")
    .WithDescription(
        "The fixed-origin operator import captures bootstrap and fixture JSON atomically. "
        + "Provider publication time is unknown; availableAtUtc is the completed retrieval time.")
    .WithTags("Data")
    .Produces<OfficialFplCaptureDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek:int:min(1):max(38)}/pre-deadline",
    async (
        string seasonCode,
        int gameweek,
        OfficialFplCaptureStore store,
        CancellationToken cancellationToken) =>
    {
        OfficialFplReplayDocument? replay =
            await store.GetLatestPreDeadlineReplayAsync(
                seasonCode,
                gameweek,
                cancellationToken);
        return replay is null ? Results.NotFound() : Results.Ok(replay);
    })
    .WithName("GetOfficialFplPreDeadlineReplay")
    .WithSummary(
        "Select the latest immutable official FPL capture available before a Gameweek deadline.")
    .WithDescription(
        "The selected capture is ordered by retrieval-time availability and must have been "
        + "available no later than the deadline recorded in that same capture.")
    .WithTags("Data")
    .Produces<OfficialFplReplayDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/data/official-fpl/outcomes/{seasonCode}/{gameweek:int:min(1):max(38)}/latest",
    async (
        string seasonCode,
        int gameweek,
        OfficialFplOutcomeStore store,
        CancellationToken cancellationToken) =>
    {
        OfficialFplOutcomeCaptureDocument? outcome =
            await store.GetLatestAsync(seasonCode, gameweek, cancellationToken);
        return outcome is null ? Results.NotFound() : Results.Ok(outcome);
    })
    .WithName("GetLatestOfficialFplOutcome")
    .WithSummary("Read the latest immutable official per-player Gameweek outcome capture.")
    .WithDescription(
        "Outcomes are operator-imported only after the official event is finished and "
        + "data-checked, every Gameweek fixture is finished, and player coverage is complete.")
    .WithTags("Data")
    .Produces<OfficialFplOutcomeCaptureDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek:int:min(1):max(38)}/outcome",
    async (
        string seasonCode,
        int gameweek,
        OfficialFplCaptureStore captureStore,
        OfficialFplOutcomeStore outcomeStore,
        CancellationToken cancellationToken) =>
    {
        OfficialFplReplayOutcomeDocument? pair =
            await outcomeStore.GetReplayOutcomeAsync(
                captureStore,
                seasonCode,
                gameweek,
                cancellationToken);
        return pair is null ? Results.NotFound() : Results.Ok(pair);
    })
    .WithName("GetOfficialFplReplayOutcome")
    .WithSummary("Pair cutoff-safe pre-deadline evidence with a complete official outcome.")
    .WithDescription(
        "The pair is returned only when every player in the selected pre-deadline capture "
        + "has a matching row in the latest final Gameweek outcome.")
    .WithTags("Data")
    .Produces<OfficialFplReplayOutcomeDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapPost(
    "/api/v1/decision-snapshots",
    async (
        DecisionSnapshotPersistenceRequest request,
        DecisionSnapshotStore store,
        CancellationToken cancellationToken) =>
    {
        if (!DecisionSnapshotRequestMapper.TryMap(request, out DecisionSnapshotCommand? command))
        {
            return Results.BadRequest();
        }

        DecisionSnapshotDocument snapshot = await store.CreateSnapshotAsync(
            command!,
            cancellationToken);
        return Results.Created($"/api/v1/decision-snapshots/{snapshot.SnapshotId}", snapshot);
    })
    .WithName("CreateDecisionSnapshot")
    .WithSummary("Persist authoritative squad state and create a cutoff-correct decision snapshot.")
    .WithTags("Decision snapshots")
    .Produces<DecisionSnapshotDocument>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapGet(
    "/api/v1/decision-snapshots/{snapshotId:long}",
    async (
        long snapshotId,
        DecisionSnapshotStore store,
        CancellationToken cancellationToken) =>
    {
        if (snapshotId <= 0)
        {
            return Results.NotFound();
        }

        DecisionSnapshotDocument? snapshot = await store.GetSnapshotAsync(
            snapshotId,
            cancellationToken);
        return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
    })
    .WithName("GetDecisionSnapshot")
    .WithSummary("Read one immutable decision snapshot by ID.")
    .WithTags("Decision snapshots")
    .Produces<DecisionSnapshotDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapPost(
    "/api/v1/decision-snapshot-metadata/validation",
    (DecisionSnapshotMetadataRequest request) =>
    {
        DecisionSnapshotMetadata metadata = DecisionSnapshotMetadata.Create(
            request.SchemaVersion ?? string.Empty,
            request.SourceType ?? string.Empty);

        return Results.Ok(DecisionSnapshotMetadataDocument.FromDomain(metadata));
    })
    .WithName("ValidateDecisionSnapshotMetadata")
    .WithSummary("Validate and canonicalise decision-snapshot metadata.")
    .WithTags("Validation")
    .Produces<DecisionSnapshotMetadataDocument>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
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
    })
    .WithName("ValidateSquad")
    .WithSummary("Validate a complete manually supplied FPL squad.")
    .WithTags("Validation")
    .Produces<SquadValidationDocument>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
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
    })
    .WithName("ValidateLineup")
    .WithSummary("Validate a starting XI, formation, captain and vice-captain.")
    .WithTags("Validation")
    .Produces<LineupValidationDocument>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
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
    })
    .WithName("ValidateGameweekSelection")
    .WithSummary("Validate a complete Gameweek selection and ordered bench.")
    .WithTags("Validation")
    .Produces<GameweekSelectionValidationDocument>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
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
    })
    .WithName("ResolveGameweekCaptaincy")
    .WithSummary("Resolve captaincy from a valid selection and supplied minutes evidence.")
    .WithTags("Outcomes")
    .Produces<GameweekCaptaincyResolutionDocument>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
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
    })
    .WithName("ResolveGameweekSubstitutions")
    .WithSummary("Resolve automatic substitutions from a valid selection and supplied play evidence.")
    .WithTags("Outcomes")
    .Produces<GameweekSubstitutionResolutionDocument>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapPost(
    "/api/v1/gameweek-outcomes/effective-resolution",
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
        GameweekOutcomeResolution resolution = GameweekOutcomeResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed);
        return Results.Ok(GameweekOutcomeResolutionDocument.FromDomain(resolution));
    })
    .WithName("ResolveEffectiveGameweekOutcome")
    .WithSummary("Resolve substitutions and captaincy from one supplied play-evidence snapshot.")
    .WithTags("Outcomes")
    .Produces<GameweekOutcomeResolutionDocument>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapPost(
    "/api/v1/gameweek-outcomes/effective-score",
    (GameweekScoreResolutionRequest request) =>
    {
        if (request.BudgetTenths is null
            || request.Players is null
            || request.StartingPlayerIds is null
            || request.CaptainPlayerId is null
            || request.ViceCaptainPlayerId is null
            || request.ReplacementGoalkeeperPlayerId is null
            || request.OutfieldSubstitutePlayerIds is null
            || request.PlayerIdsWhoPlayed is null
            || request.PlayerPoints is null
            || request.Players.Any(PlayerRequestIsMalformed)
            || request.StartingPlayerIds.Any(playerId => playerId is null)
            || request.OutfieldSubstitutePlayerIds.Any(playerId => playerId is null)
            || request.PlayerIdsWhoPlayed.Any(playerId => playerId is null)
            || request.PlayerPoints.Any(
                entry => entry is null || entry.PlayerId is null || entry.Points is null))
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
        PlayerGameweekPoints[] playerPoints = request.PlayerPoints
            .Select(entry => new PlayerGameweekPoints(entry!.PlayerId!.Value, entry.Points!.Value))
            .ToArray();
        GameweekSelection selection = GameweekSelection.Create(
            squad,
            startingPlayerIds,
            request.CaptainPlayerId.Value,
            request.ViceCaptainPlayerId.Value,
            request.ReplacementGoalkeeperPlayerId.Value,
            outfieldSubstitutePlayerIds);
        GameweekScoreResolution resolution = GameweekScoreResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed,
            playerPoints);
        return Results.Ok(GameweekScoreResolutionDocument.FromDomain(resolution));
    })
    .WithName("ResolveEffectiveGameweekScore")
    .WithSummary("Score an effective Gameweek outcome from complete supplied points evidence.")
    .WithTags("Outcomes")
    .Produces<GameweekScoreResolutionDocument>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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
