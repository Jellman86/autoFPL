using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using AutoFpl.Api.Advice;
using AutoFpl.Api.Errors;
using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Health;
using AutoFpl.Api.Intelligence;
using AutoFpl.Api.Mcp;
using AutoFpl.Api.Persistence;
using AutoFpl.Api.Selections;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Advice;
using AutoFpl.Contracts.Forecasts;
using AutoFpl.Contracts.Intelligence;
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

using ModelContextProtocol.Protocol;

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
bool requestedHistoricalFplSeasonImport =
    args.Length > 0
    && StringComparer.Ordinal.Equals(args[0], "--import-historical-fpl-season");
string historicalFplSeasonCode =
    args.Length == 2
        ? args[1]
        : HistoricalFplSeasonRegistry.DefaultSeasonCode;
bool runHistoricalFplSeasonImport =
    requestedHistoricalFplSeasonImport
    && args.Length is 1 or 2
    && HistoricalFplSeasonRegistry.TryGet(historicalFplSeasonCode, out _);
if (requestedHistoricalFplSeasonImport && !runHistoricalFplSeasonImport)
{
    await Console.Error.WriteLineAsync(
        "Usage: --import-historical-fpl-season [2024-25|2025-26]");
    return 2;
}
bool runFplFormForecastImport =
    args.Length == 1
    && StringComparer.Ordinal.Equals(args[0], "--import-fpl-form-forecast");
bool requestedEvidenceClaimImport =
    args.Length > 0
    && StringComparer.Ordinal.Equals(args[0], "--import-evidence-claim");
bool runEvidenceClaimImport =
    requestedEvidenceClaimImport
    && args.Length == 2
    && !string.IsNullOrWhiteSpace(args[1]);
if (requestedEvidenceClaimImport && !runEvidenceClaimImport)
{
    await Console.Error.WriteLineAsync(
        "Usage: --import-evidence-claim <json-file>");
    return 2;
}
bool requestedResearchSourceCapture =
    args.Length > 0
    && StringComparer.Ordinal.Equals(args[0], "--capture-research-source");
bool runResearchSourceCapture =
    requestedResearchSourceCapture
    && args.Length == 2
    && !string.IsNullOrWhiteSpace(args[1])
    && args[1].Length <= 100;
if (requestedResearchSourceCapture && !runResearchSourceCapture)
{
    await Console.Error.WriteLineAsync(
        "Usage: --capture-research-source <source-key>");
    return 2;
}
bool requestedResearchSourceClaimExtraction =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--extract-research-source-claims");
long researchSnapshotId = 0;
bool runResearchSourceClaimExtraction =
    requestedResearchSourceClaimExtraction
    && args.Length == 2
    && long.TryParse(args[1], out researchSnapshotId)
    && researchSnapshotId > 0;
if (requestedResearchSourceClaimExtraction
    && !runResearchSourceClaimExtraction)
{
    await Console.Error.WriteLineAsync(
        "Usage: --extract-research-source-claims <snapshot-id>");
    return 2;
}
bool requestedFbrefPlayingTimeExtraction =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--extract-fbref-playing-time");
long fbrefPlayingTimeSnapshotId = 0;
bool runFbrefPlayingTimeExtraction =
    requestedFbrefPlayingTimeExtraction
    && args.Length == 2
    && long.TryParse(args[1], out fbrefPlayingTimeSnapshotId)
    && fbrefPlayingTimeSnapshotId > 0;
if (requestedFbrefPlayingTimeExtraction
    && !runFbrefPlayingTimeExtraction)
{
    await Console.Error.WriteLineAsync(
        "Usage: --extract-fbref-playing-time <snapshot-id>");
    return 2;
}
bool requestedFbrefTeamScheduleExtraction =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--extract-fbref-team-schedule");
long fbrefTeamScheduleSnapshotId = 0;
bool runFbrefTeamScheduleExtraction =
    requestedFbrefTeamScheduleExtraction
    && args.Length == 2
    && long.TryParse(args[1], out fbrefTeamScheduleSnapshotId)
    && fbrefTeamScheduleSnapshotId > 0;
if (requestedFbrefTeamScheduleExtraction
    && !runFbrefTeamScheduleExtraction)
{
    await Console.Error.WriteLineAsync(
        "Usage: --extract-fbref-team-schedule <snapshot-id>");
    return 2;
}
bool requestedFbrefPlayerMatchOpportunityExtraction =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--extract-fbref-player-match-opportunities");
long fbrefOpportunityPlayerSnapshotId = 0;
long fbrefOpportunityScheduleSnapshotId = 0;
bool runFbrefPlayerMatchOpportunityExtraction =
    requestedFbrefPlayerMatchOpportunityExtraction
    && args.Length == 3
    && long.TryParse(args[1], out fbrefOpportunityPlayerSnapshotId)
    && fbrefOpportunityPlayerSnapshotId > 0
    && long.TryParse(args[2], out fbrefOpportunityScheduleSnapshotId)
    && fbrefOpportunityScheduleSnapshotId > 0;
if (requestedFbrefPlayerMatchOpportunityExtraction
    && !runFbrefPlayerMatchOpportunityExtraction)
{
    await Console.Error.WriteLineAsync(
        "Usage: --extract-fbref-player-match-opportunities "
        + "<player-match-log-snapshot-id> <team-schedule-snapshot-id>");
    return 2;
}
bool requestedFbrefPlayerMatchLogCapture =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--capture-fbref-player-match-log");
int fbrefPlayerMatchLogOfficialCode = 0;
bool runFbrefPlayerMatchLogCapture =
    requestedFbrefPlayerMatchLogCapture
    && args.Length == 2
    && int.TryParse(args[1], out fbrefPlayerMatchLogOfficialCode)
    && fbrefPlayerMatchLogOfficialCode > 0;
if (requestedFbrefPlayerMatchLogCapture
    && !runFbrefPlayerMatchLogCapture)
{
    await Console.Error.WriteLineAsync(
        "Usage: --capture-fbref-player-match-log <official-player-code>");
    return 2;
}
bool requestedFbrefPlayerMatchLogExtraction =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--extract-fbref-player-match-log");
long fbrefPlayerMatchLogSnapshotId = 0;
bool runFbrefPlayerMatchLogExtraction =
    requestedFbrefPlayerMatchLogExtraction
    && args.Length == 2
    && long.TryParse(args[1], out fbrefPlayerMatchLogSnapshotId)
    && fbrefPlayerMatchLogSnapshotId > 0;
if (requestedFbrefPlayerMatchLogExtraction
    && !runFbrefPlayerMatchLogExtraction)
{
    await Console.Error.WriteLineAsync(
        "Usage: --extract-fbref-player-match-log <snapshot-id>");
    return 2;
}
bool requestedFbrefMatchLogBatchCapture =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--capture-fbref-reviewed-match-logs");
int fbrefMatchLogBatchLimit = 0;
bool runFbrefMatchLogBatchCapture =
    requestedFbrefMatchLogBatchCapture
    && args.Length == 2
    && int.TryParse(args[1], out fbrefMatchLogBatchLimit)
    && fbrefMatchLogBatchLimit
        is >= FbrefMatchLogBatchCapture.MinimumCaptureLimit
        and <= FbrefMatchLogBatchCapture.MaximumCaptureLimit;
if (requestedFbrefMatchLogBatchCapture
    && !runFbrefMatchLogBatchCapture)
{
    await Console.Error.WriteLineAsync(
        "Usage: --capture-fbref-reviewed-match-logs <limit 1-5>");
    return 2;
}
bool requestedEvidenceClaimEvaluation =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--evaluate-evidence-claims");
bool runEvidenceClaimEvaluation =
    requestedEvidenceClaimEvaluation
    && args.Length is 1 or 2
    && (args.Length == 1
        || (!string.IsNullOrWhiteSpace(args[1])
            && args[1].Length is >= 4 and <= 16));
if (requestedEvidenceClaimEvaluation && !runEvidenceClaimEvaluation)
{
    await Console.Error.WriteLineAsync(
        "Usage: --evaluate-evidence-claims [season-code]");
    return 2;
}
bool requestedFplFormForecastEvaluation =
    args.Length > 0
    && StringComparer.Ordinal.Equals(args[0], "--evaluate-fpl-form-forecast");
bool runFplFormForecastEvaluation =
    requestedFplFormForecastEvaluation
    && args.Length is 1 or 2
    && (args.Length == 1
        || (!string.IsNullOrWhiteSpace(args[1]) && args[1].Length <= 16));
if (requestedFplFormForecastEvaluation && !runFplFormForecastEvaluation)
{
    await Console.Error.WriteLineAsync(
        "Usage: --evaluate-fpl-form-forecast [season-code]");
    return 2;
}
bool requestedOfficialExpectedPointsEvaluation =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--evaluate-official-fpl-expected-points");
bool runOfficialExpectedPointsEvaluation =
    requestedOfficialExpectedPointsEvaluation
    && args.Length is 1 or 2
    && (args.Length == 1
        || (!string.IsNullOrWhiteSpace(args[1]) && args[1].Length <= 16));
if (requestedOfficialExpectedPointsEvaluation
    && !runOfficialExpectedPointsEvaluation)
{
    await Console.Error.WriteLineAsync(
        "Usage: --evaluate-official-fpl-expected-points [season-code]");
    return 2;
}
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
bool requestedPreseasonPlayerForecastImport =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--import-preseason-player-forecast");
bool runPreseasonPlayerForecastImport =
    requestedPreseasonPlayerForecastImport
    && args.Length == 2
    && !string.IsNullOrWhiteSpace(args[1]);
if (requestedPreseasonPlayerForecastImport
    && !runPreseasonPlayerForecastImport)
{
    await Console.Error.WriteLineAsync(
        "Usage: --import-preseason-player-forecast <json-file>");
    return 2;
}
bool requestedMultiSeasonPlayerForecastImport =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--import-multi-season-player-forecast");
bool runMultiSeasonPlayerForecastImport =
    requestedMultiSeasonPlayerForecastImport
    && args.Length == 2
    && !string.IsNullOrWhiteSpace(args[1]);
if (requestedMultiSeasonPlayerForecastImport
    && !runMultiSeasonPlayerForecastImport)
{
    await Console.Error.WriteLineAsync(
        "Usage: --import-multi-season-player-forecast <json-file>");
    return 2;
}
bool requestedJointScenarioImport =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--import-joint-scenario-shadow");
bool runJointScenarioImport =
    requestedJointScenarioImport
    && args.Length == 2
    && !string.IsNullOrWhiteSpace(args[1]);
if (requestedJointScenarioImport && !runJointScenarioImport)
{
    await Console.Error.WriteLineAsync(
        "Usage: --import-joint-scenario-shadow <json-file>");
    return 2;
}
bool requestedSelectionScenarioScoreImport =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--import-selection-scenario-score-shadow");
bool runSelectionScenarioScoreImport =
    requestedSelectionScenarioScoreImport
    && args.Length == 2
    && !string.IsNullOrWhiteSpace(args[1]);
if (requestedSelectionScenarioScoreImport
    && !runSelectionScenarioScoreImport)
{
    await Console.Error.WriteLineAsync(
        "Usage: --import-selection-scenario-score-shadow <json-file>");
    return 2;
}
bool requestedSelectionRoleStrategyImport =
    args.Length > 0
    && StringComparer.Ordinal.Equals(
        args[0],
        "--import-selection-role-strategy-shadow");
bool runSelectionRoleStrategyImport =
    requestedSelectionRoleStrategyImport
    && args.Length == 2
    && !string.IsNullOrWhiteSpace(args[1]);
if (requestedSelectionRoleStrategyImport
    && !runSelectionRoleStrategyImport)
{
    await Console.Error.WriteLineAsync(
        "Usage: --import-selection-role-strategy-shadow <json-file>");
    return 2;
}

bool runNonWebCommand =
    runIntegrityCheck
    || runBackup
    || runOfficialFplImport
    || runHistoricalFplSeasonImport
    || runFplFormForecastImport
    || runEvidenceClaimImport
    || runResearchSourceCapture
    || runResearchSourceClaimExtraction
    || runFbrefPlayingTimeExtraction
    || runFbrefTeamScheduleExtraction
    || runFbrefPlayerMatchOpportunityExtraction
    || runFbrefPlayerMatchLogCapture
    || runFbrefPlayerMatchLogExtraction
    || runFbrefMatchLogBatchCapture
    || runEvidenceClaimEvaluation
    || runFplFormForecastEvaluation
    || runOfficialExpectedPointsEvaluation
    || runOfficialFplOutcomeImport
    || runPreseasonPlayerForecastImport
    || runMultiSeasonPlayerForecastImport
    || runJointScenarioImport
    || runSelectionScenarioScoreImport
    || runSelectionRoleStrategyImport;

WebApplicationBuilder builder = WebApplication.CreateBuilder(
    runNonWebCommand ? [] : args);
builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
if (runNonWebCommand)
{
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "autofpl",
            Version = "0.1.0",
        };
        options.ServerInstructions =
            "autoFPL returns read-only, cutoff-correct football evidence. "
            + "Research claims are quarantined and must never be described as "
            + "influencing Baseline v0. Preserve stable IDs, timestamps, evidence "
            + "status and source URLs. Never describe a provisional or shadow "
            + "artifact as calibrated, promoted or globally optimal.";
    })
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<PlayerDossierMcpTools>()
    .WithTools<CurrentPredictionMcpTools>()
    .WithTools<CurrentStrategyMcpTools>();
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
builder.Services.AddSingleton(serviceProvider =>
    new HistoricalFplSeasonStore(
        serviceProvider.GetRequiredService<DatabaseOptions>()));
builder.Services.AddSingleton(serviceProvider =>
    new OfficialFplPlayerDossierStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<OfficialFplCaptureStore>()));
builder.Services.AddSingleton(serviceProvider =>
    new OfficialDecisionRoomPreviewStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<OfficialFplCaptureStore>()));
builder.Services.AddSingleton(serviceProvider =>
    new BaselineForecastArtifactStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<OfficialDecisionRoomPreviewStore>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(serviceProvider =>
    new PlayerGameweekForecastArtifactStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<OfficialDecisionRoomPreviewStore>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(serviceProvider =>
    new PreseasonPlayerForecastStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<PreseasonPlayerForecastImporter>();
builder.Services.AddSingleton(serviceProvider =>
    new MultiSeasonPlayerForecastStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<MultiSeasonPlayerForecastImporter>();
builder.Services.AddSingleton(serviceProvider =>
    new JointScenarioShadowStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<JointScenarioShadowImporter>();
builder.Services.AddSingleton(serviceProvider =>
    new SelectionScenarioScoreShadowStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<SelectionScenarioScoreShadowImporter>();
builder.Services.AddSingleton(serviceProvider =>
    new SelectionRoleStrategyShadowStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<SelectionRoleStrategyShadowImporter>();
builder.Services.AddSingleton(serviceProvider =>
    new SelectionRevisionStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(serviceProvider =>
    new FplFormForecastStore(
        serviceProvider.GetRequiredService<DatabaseOptions>()));
builder.Services.AddSingleton(serviceProvider =>
    new FplFormIdentityCoverageStore(
        serviceProvider.GetRequiredService<DatabaseOptions>()));
builder.Services.AddSingleton(serviceProvider =>
    new FplFormForecastEvaluationStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<FplFormIdentityCoverageStore>()));
builder.Services.AddSingleton(serviceProvider =>
    new OfficialFplExpectedPointsEvaluationStore(
        serviceProvider.GetRequiredService<DatabaseOptions>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(serviceProvider =>
    new EvidenceClaimStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(serviceProvider =>
    new EvidenceClaimEvaluationStore(
        serviceProvider.GetRequiredService<DatabaseOptions>()));
builder.Services.AddSingleton<EvidenceClaimImporter>();
builder.Services.AddSingleton(serviceProvider =>
    new ResearchSourceSnapshotStore(
        serviceProvider.GetRequiredService<DatabaseOptions>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
FplFormForecastPollingOptions fplFormPollingOptions =
    FplFormForecastPollingOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(fplFormPollingOptions);
OfficialFplPollingOptions officialFplPollingOptions =
    OfficialFplPollingOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(officialFplPollingOptions);
ResearchSourcePollingOptions researchSourcePollingOptions =
    ResearchSourcePollingOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(researchSourcePollingOptions);
FbrefMatchLogPollingOptions fbrefMatchLogPollingOptions =
    FbrefMatchLogPollingOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(fbrefMatchLogPollingOptions);
ShadowForecastInboxOptions shadowForecastInboxOptions =
    ShadowForecastInboxOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(shadowForecastInboxOptions);
AnalyticsSnapshotOptions analyticsSnapshotOptions =
    AnalyticsSnapshotOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(analyticsSnapshotOptions);
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
builder.Services
    .AddHttpClient<HistoricalFplSeasonImporter>(
        client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
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
    .AddHttpClient<PlaywrightMcpFplFormCollector>(
        (serviceProvider, client) =>
        {
            string configured =
                serviceProvider.GetRequiredService<IConfiguration>()[
                    "AutoFpl:Research:PlaywrightMcpUrl"]
                ?? "http://playwright-mcp:8931/mcp";
            if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri? endpoint)
                || endpoint.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException(
                    "AutoFpl:Research:PlaywrightMcpUrl must be an absolute HTTP(S) URL.");
            }

            client.BaseAddress = endpoint;
            client.Timeout = TimeSpan.FromSeconds(180);
        })
    .ConfigurePrimaryHttpMessageHandler(
        () => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxConnectionsPerServer = 1,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        });
builder.Services
    .AddHttpClient<SpiderMcpClient>(
        (serviceProvider, client) =>
        {
            string configured =
                serviceProvider.GetRequiredService<IConfiguration>()[
                    "AutoFpl:Research:SpiderMcpUrl"]
                ?? "http://spider-mcp:8080/mcp";
            if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri? endpoint)
                || endpoint.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException(
                    "AutoFpl:Research:SpiderMcpUrl must be an absolute HTTP(S) URL.");
            }

            client.BaseAddress = endpoint;
            client.Timeout = TimeSpan.FromSeconds(150);
        })
    .ConfigurePrimaryHttpMessageHandler(
        () => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxConnectionsPerServer = 1,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        });
builder.Services
    .AddHttpClient<ByparrClient>(
        (serviceProvider, client) =>
        {
            string configured =
                serviceProvider.GetRequiredService<IConfiguration>()[
                    "AutoFpl:Research:ByparrUrl"]
                ?? "http://byparr:8191/";
            if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri? endpoint)
                || endpoint.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(endpoint.UserInfo)
                || !string.IsNullOrEmpty(endpoint.Query)
                || !string.IsNullOrEmpty(endpoint.Fragment)
                || !StringComparer.Ordinal.Equals(endpoint.AbsolutePath, "/"))
            {
                throw new InvalidOperationException(
                    "AutoFpl:Research:ByparrUrl must be an absolute HTTP(S) origin.");
            }

            client.BaseAddress = endpoint;
            client.Timeout = TimeSpan.FromSeconds(90);
        })
    .ConfigurePrimaryHttpMessageHandler(
        () => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxConnectionsPerServer = 1,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseProxy = false,
        });
builder.Services.AddTransient<FplFormForecastImporter>();
builder.Services.AddTransient<ResearchSourceSnapshotImporter>();
builder.Services.AddTransient<ResearchSourceClaimExtractor>();
builder.Services.AddTransient<FbrefPlayingTimeExtractor>();
builder.Services.AddTransient<FbrefTeamScheduleExtractor>();
builder.Services.AddTransient<FbrefPlayerMatchLogImporter>();
builder.Services.AddTransient<FbrefPlayerMatchLogExtractor>();
builder.Services.AddTransient<FbrefPlayerMatchOpportunityExtractor>();
builder.Services.AddTransient<FbrefMatchLogCoverageReader>();
builder.Services.AddTransient<FbrefMatchOpportunityCoverageReader>();
builder.Services.AddTransient<FbrefMatchOpportunityFeatureTableReader>();
builder.Services.AddTransient<FbrefMatchLogBatchCapture>();
if (fplFormPollingOptions.Enabled)
{
    builder.Services.AddHostedService<FplFormForecastPoller>();
}
if (officialFplPollingOptions.Enabled)
{
    builder.Services.AddHostedService<OfficialFplPoller>();
}
if (researchSourcePollingOptions.Enabled)
{
    builder.Services.AddHostedService<ResearchSourcePoller>();
}
if (fbrefMatchLogPollingOptions.Enabled)
{
    builder.Services.AddHostedService<FbrefMatchLogPoller>();
}
if (shadowForecastInboxOptions.Enabled)
{
    builder.Services.AddHostedService<ShadowForecastInboxPoller>();
    builder.Services.AddHostedService<JointScenarioInboxPoller>();
    builder.Services.AddHostedService<SelectionScenarioScoreInboxPoller>();
    builder.Services.AddHostedService<SelectionRoleStrategyInboxPoller>();
}
if (analyticsSnapshotOptions.Enabled)
{
    builder.Services.AddHostedService<AnalyticsSnapshotPublisher>();
}
builder.Services.AddExceptionHandler<DecisionSnapshotPersistenceExceptionHandler>();
builder.Services.AddExceptionHandler<DecisionSnapshotValidationExceptionHandler>();
builder.Services.AddExceptionHandler<SquadValidationExceptionHandler>();
builder.Services.AddExceptionHandler<LineupValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GameweekSelectionValidationExceptionHandler>();
builder.Services.AddExceptionHandler<SelectionWorkflowExceptionHandler>();
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
    await app.Services
        .GetRequiredService<BaselineForecastArtifactStore>()
        .RefreshLatestAsync();
    await app.Services
        .GetRequiredService<PlayerGameweekForecastArtifactStore>()
        .RefreshLatestAsync();
    await Console.Out.WriteLineAsync(
        JsonSerializer.Serialize(
            capture,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return 0;
}

if (runHistoricalFplSeasonImport)
{
    try
    {
        HistoricalFplSeasonCaptureDocument capture =
            await app.Services
                .GetRequiredService<HistoricalFplSeasonImporter>()
                .ImportAsync(historicalFplSeasonCode);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                capture,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (Exception exception)
        when (exception is HistoricalFplSeasonPayloadException
            or HttpRequestException
            or TaskCanceledException)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runFplFormForecastImport)
{
    try
    {
        FplFormForecastCaptureDocument capture =
            await app.Services
                .GetRequiredService<FplFormForecastImporter>()
                .ImportLatestAsync();
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                capture,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (FplFormForecastPayloadException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runEvidenceClaimImport)
{
    try
    {
        EvidenceClaimDocument claim =
            await app.Services
                .GetRequiredService<EvidenceClaimImporter>()
                .ImportFileAsync(args[1]);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                claim,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (Exception exception)
        when (exception is EvidenceClaimValidationException
            or FileNotFoundException
            or InvalidDataException
            or JsonException)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runPreseasonPlayerForecastImport)
{
    try
    {
        PreseasonPlayerForecastDocument forecast =
            await app.Services
                .GetRequiredService<PreseasonPlayerForecastImporter>()
                .ImportFileAsync(args[1]);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                forecast,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (Exception exception)
        when (exception is PreseasonPlayerForecastValidationException
            or FileNotFoundException
            or InvalidDataException
            or JsonException)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runMultiSeasonPlayerForecastImport)
{
    try
    {
        MultiSeasonPlayerForecastDocument forecast =
            await app.Services
                .GetRequiredService<MultiSeasonPlayerForecastImporter>()
                .ImportFileAsync(args[1]);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                forecast,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (Exception exception)
        when (exception is MultiSeasonPlayerForecastValidationException
            or FileNotFoundException
            or InvalidDataException
            or JsonException)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runJointScenarioImport)
{
    try
    {
        JointScenarioShadowDocument scenario =
            await app.Services
                .GetRequiredService<JointScenarioShadowImporter>()
                .ImportFileAsync(args[1]);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                scenario,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (Exception exception)
        when (exception is JointScenarioValidationException
            or FileNotFoundException
            or InvalidDataException
            or JsonException)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runSelectionScenarioScoreImport)
{
    try
    {
        SelectionScenarioScoreShadowDocument score =
            await app.Services
                .GetRequiredService<
                    SelectionScenarioScoreShadowImporter>()
                .ImportFileAsync(args[1]);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                score,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (Exception exception)
        when (exception is SelectionScenarioScoreValidationException
            or FileNotFoundException
            or InvalidDataException
            or JsonException)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runSelectionRoleStrategyImport)
{
    try
    {
        SelectionRoleStrategyShadowDocument strategy =
            await app.Services
                .GetRequiredService<
                    SelectionRoleStrategyShadowImporter>()
                .ImportFileAsync(args[1]);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                strategy,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (Exception exception)
        when (exception is SelectionRoleStrategyValidationException
            or FileNotFoundException
            or InvalidDataException
            or JsonException)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runResearchSourceCapture)
{
    try
    {
        ResearchSourceSnapshotDocument snapshot =
            await app.Services
                .GetRequiredService<ResearchSourceSnapshotImporter>()
                .ImportAsync(args[1]);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                snapshot,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (ResearchSourceSnapshotException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runResearchSourceClaimExtraction)
{
    try
    {
        ResearchSourceClaimExtractionDocument extraction =
            await app.Services
                .GetRequiredService<ResearchSourceClaimExtractor>()
                .ExtractAsync(researchSnapshotId);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                extraction,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (ResearchSourceSnapshotException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runFbrefPlayingTimeExtraction)
{
    try
    {
        FbrefPlayingTimeDocument? extraction =
            await app.Services
                .GetRequiredService<FbrefPlayingTimeExtractor>()
                .GetAsync(fbrefPlayingTimeSnapshotId);
        if (extraction is null)
        {
            await Console.Error.WriteLineAsync(
                $"Research source snapshot {fbrefPlayingTimeSnapshotId} was not found.");
            return 2;
        }
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                extraction,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (ResearchSourceSnapshotException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runFbrefTeamScheduleExtraction)
{
    try
    {
        FbrefTeamScheduleDocument? extraction =
            await app.Services
                .GetRequiredService<FbrefTeamScheduleExtractor>()
                .GetAsync(fbrefTeamScheduleSnapshotId);
        if (extraction is null)
        {
            await Console.Error.WriteLineAsync(
                $"Research source snapshot {fbrefTeamScheduleSnapshotId} was not found.");
            return 2;
        }
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                extraction,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (ResearchSourceSnapshotException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runFbrefPlayerMatchOpportunityExtraction)
{
    try
    {
        FbrefPlayerMatchOpportunityDocument? extraction =
            await app.Services
                .GetRequiredService<FbrefPlayerMatchOpportunityExtractor>()
                .GetAsync(
                    fbrefOpportunityPlayerSnapshotId,
                    fbrefOpportunityScheduleSnapshotId);
        if (extraction is null)
        {
            await Console.Error.WriteLineAsync(
                "One or both FBref source snapshots were not found.");
            return 2;
        }
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                extraction,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (ResearchSourceSnapshotException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runFbrefPlayerMatchLogCapture)
{
    try
    {
        ResearchSourceSnapshotDocument snapshot =
            await app.Services
                .GetRequiredService<FbrefPlayerMatchLogImporter>()
                .ImportAsync(fbrefPlayerMatchLogOfficialCode);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                snapshot,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (ResearchSourceSnapshotException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runFbrefPlayerMatchLogExtraction)
{
    try
    {
        FbrefPlayerMatchLogDocument? extraction =
            await app.Services
                .GetRequiredService<FbrefPlayerMatchLogExtractor>()
                .GetAsync(fbrefPlayerMatchLogSnapshotId);
        if (extraction is null)
        {
            await Console.Error.WriteLineAsync(
                $"Research source snapshot {fbrefPlayerMatchLogSnapshotId} was not found.");
            return 2;
        }
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                extraction,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (ResearchSourceSnapshotException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runFbrefMatchLogBatchCapture)
{
    try
    {
        FbrefMatchLogBatchCaptureDocument batch =
            await app.Services
                .GetRequiredService<FbrefMatchLogBatchCapture>()
                .RunAsync(fbrefMatchLogBatchLimit);
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(
                batch,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return batch.FailedCount == 0 ? 0 : 2;
    }
    catch (ResearchSourceSnapshotException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 2;
    }
}

if (runEvidenceClaimEvaluation)
{
    EvidenceClaimEvaluationDocument evaluation =
        await app.Services
            .GetRequiredService<EvidenceClaimEvaluationStore>()
            .EvaluateAsync(args.Length == 2 ? args[1] : null);
    await Console.Out.WriteLineAsync(
        JsonSerializer.Serialize(
            evaluation,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return evaluation.Status == "complete" ? 0 : 2;
}

if (runFplFormForecastEvaluation)
{
    FplFormForecastEvaluationDocument evaluation =
        await app.Services
            .GetRequiredService<FplFormForecastEvaluationStore>()
            .EvaluateAsync(args.Length == 2 ? args[1] : null);
    await Console.Out.WriteLineAsync(
        JsonSerializer.Serialize(
            evaluation,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return evaluation.Status == "complete" ? 0 : 2;
}

if (runOfficialExpectedPointsEvaluation)
{
    OfficialFplExpectedPointsEvaluationDocument evaluation =
        await app.Services
            .GetRequiredService<OfficialFplExpectedPointsEvaluationStore>()
            .EvaluateAsync(args.Length == 2 ? args[1] : null);
    await Console.Out.WriteLineAsync(
        JsonSerializer.Serialize(
            evaluation,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return evaluation.Status == "complete" ? 0 : 2;
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
await app.Services
    .GetRequiredService<BaselineForecastArtifactStore>()
    .RefreshLatestAsync();
await app.Services
    .GetRequiredService<PlayerGameweekForecastArtifactStore>()
    .RefreshLatestAsync();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapMcp("/mcp")
    .WithMetadata(new MachineProtocolEndpointMetadata());
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
    async (
        DecisionSnapshotStore store,
        BaselineForecastArtifactStore forecastStore,
        CancellationToken cancellationToken) =>
    {
        DecisionSnapshotDocument? snapshot = await store.GetLatestSnapshotAsync(
            SyntheticDecisionSnapshotSeeder.SeasonCode,
            SyntheticDecisionSnapshotSeeder.Gameweek,
            cancellationToken);
        GameweekAdviceDocument? officialAdvice =
            await forecastStore.GetLatestAsync(cancellationToken);
        GameweekAdviceDocument advice = officialAdvice is null
            ? DemoGameweekAdvice.Create(snapshot)
            : officialAdvice;
        return Results.Ok(advice);
    })
    .WithName("GetDemoGameweekAdvice")
    .WithSummary(
        "Return the latest persisted Baseline v0 forecast or the synthetic acceptance fixture.")
    .WithTags("Advice")
    .Produces<GameweekAdviceDocument>();
app.MapGet(
    "/api/v1/forecasts/player-gameweek/latest",
    async (
        PlayerGameweekForecastArtifactStore store,
        CancellationToken cancellationToken) =>
    {
        PlayerGameweekForecastDocument? forecast =
            await store.GetLatestAsync(cancellationToken);
        return forecast is null ? Results.NotFound() : Results.Ok(forecast);
    })
    .WithName("GetLatestPlayerGameweekForecast")
    .WithSummary(
        "Read the latest immutable provisional forecast for every eligible player.")
    .WithDescription(
        "Baseline v0 currently exposes an uncalibrated interval and availability-weighted "
        + "minutes proxy. Missing fitted start and 60-minute probabilities remain null.")
    .WithTags("Forecasts")
    .Produces<PlayerGameweekForecastDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/forecasts/{forecastArtifactId:long:min(1)}/player-pool",
    async (
        long forecastArtifactId,
        PlayerGameweekForecastArtifactStore store,
        CancellationToken cancellationToken) =>
    {
        PlayerGameweekForecastDocument? forecast =
            await store.GetForBaselineArtifactAsync(
                forecastArtifactId,
                cancellationToken);
        return forecast is null ? Results.NotFound() : Results.Ok(forecast);
    })
    .WithName("GetPlayerPoolForForecast")
    .WithSummary(
        "Read every eligible player forecast linked to one Baseline forecast artifact.")
    .WithDescription(
        "This exact-capture player pool is the authoritative source for legal user squad "
        + "revisions and like-for-like model comparison.")
    .WithTags("Forecasts")
    .Produces<PlayerGameweekForecastDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/forecasts/preseason-challenger/latest",
    async (
        PreseasonPlayerForecastStore store,
        CancellationToken cancellationToken) =>
    {
        PreseasonPlayerForecastDocument? forecast =
            await store.GetLatestAsync(cancellationToken);
        return forecast is null ? Results.NotFound() : Results.Ok(forecast);
    })
    .WithName("GetLatestPreseasonPlayerForecast")
    .WithSummary(
        "Read the latest immutable provisional preseason player challenger.")
    .WithDescription(
        "The fixed archive-trained point means are comparison evidence only. "
        + "Baseline v0 still drives advice, and no calibrated distribution, "
        + "appearance probability or expected-minutes model is implied.")
    .WithTags("Forecasts")
    .Produces<PreseasonPlayerForecastDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/forecasts/multi-season-shadow/latest",
    async (
        MultiSeasonPlayerForecastStore store,
        CancellationToken cancellationToken) =>
    {
        MultiSeasonPlayerForecastDocument? forecast =
            await store.GetLatestAsync(cancellationToken);
        return forecast is null ? Results.NotFound() : Results.Ok(forecast);
    })
    .WithName("GetLatestMultiSeasonPlayerForecast")
    .WithSummary(
        "Read the latest immutable two-season player shadow forecast.")
    .WithDescription(
        "The retrospectively selected point means are prospective comparison "
        + "evidence only. Baseline v0 still drives advice; the shadow has no "
        + "calibrated distribution, appearance probability or expected-minutes "
        + "model.")
    .WithTags("Forecasts")
    .Produces<MultiSeasonPlayerForecastDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/forecasts/multi-season-shadow/readiness",
    async (
        MultiSeasonPlayerForecastStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(
            await store.GetReadinessAsync(cancellationToken)))
    .WithName("GetMultiSeasonPlayerForecastReadiness")
    .WithSummary(
        "Report whether the latest two-season shadow matches official evidence.")
    .WithDescription(
        "Returns current only when the shadow targets the latest official "
        + "capture. Missing or stale status never falls back to older evidence, "
        + "and the shadow never influences advice.")
    .WithTags("Forecasts")
    .Produces<MultiSeasonPlayerForecastReadinessDocument>();
app.MapGet(
    "/api/v1/forecasts/joint-scenario-shadow/latest",
    async (
        JointScenarioShadowStore store,
        CancellationToken cancellationToken) =>
    {
        JointScenarioShadowDocument? scenario =
            await store.GetLatestAsync(cancellationToken);
        return scenario is null ? Results.NotFound() : Results.Ok(scenario);
    })
    .WithName("GetLatestJointScenarioShadow")
    .WithSummary(
        "Read the latest immutable joint player-Gameweek scenario shadow.")
    .WithDescription(
        "The complete point and appearance rows are prospective research "
        + "evidence only. They are unscored, unpromoted and cannot influence "
        + "advice or mutate a user selection.")
    .WithTags("Forecasts")
    .Produces<JointScenarioShadowDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/forecasts/joint-scenario-shadow/readiness",
    async (
        JointScenarioShadowStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.GetReadinessAsync(cancellationToken)))
    .WithName("GetJointScenarioShadowReadiness")
    .WithSummary(
        "Report whether the latest joint scenario matches official evidence.")
    .WithDescription(
        "Returns current only for an exact latest official-capture match. "
        + "Missing or stale matrices never fall back and never influence "
        + "advice.")
    .WithTags("Forecasts")
    .Produces<JointScenarioReadinessDocument>();
app.MapGet(
    "/api/v1/selections/current",
    async (
        SelectionRevisionStore store,
        CancellationToken cancellationToken) =>
    {
        SelectionRevisionDocument? revision =
            await store.GetCurrentAsync(cancellationToken);
        return revision is null ? Results.NotFound() : Results.Ok(revision);
    })
    .WithName("GetCurrentSelectionRevision")
    .WithSummary(
        "Read the latest user-owned selection revision for the current forecast target.")
    .WithDescription(
        "The status is computed from the immutable revision, its one-time lock event "
        + "and the current deadline. A locked revision becomes frozen at the deadline.")
    .WithTags("Selections")
    .Produces<SelectionRevisionDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/selections/current/scenario-score-shadow",
    async (
        SelectionScenarioScoreShadowStore store,
        CancellationToken cancellationToken) =>
    {
        SelectionScenarioScoreShadowDocument? score =
            await store.GetCurrentAsync(cancellationToken);
        return score is null ? Results.NotFound() : Results.Ok(score);
    })
    .WithName("GetCurrentSelectionScenarioScoreShadow")
    .WithSummary(
        "Read the current selection's immutable paired scenario distribution.")
    .WithDescription(
        "Returns only an exact current scenario, forecast and latest selection "
        + "revision match. The distribution is prospective shadow evidence, "
        + "is not promoted and cannot influence advice.")
    .WithTags("Selections")
    .Produces<SelectionScenarioScoreShadowDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/selections/current/role-strategies-shadow",
    async (
        SelectionRoleStrategyShadowStore store,
        CancellationToken cancellationToken) =>
    {
        SelectionRoleStrategyShadowDocument? strategies =
            await store.GetCurrentAsync(cancellationToken);
        return strategies is null
            ? Results.NotFound()
            : Results.Ok(strategies);
    })
    .WithName("GetCurrentSelectionRoleStrategiesShadow")
    .WithSummary(
        "Read bounded balanced, safer and higher-ceiling role strategies.")
    .WithDescription(
        "Returns only an exact current selection-score match. Strategies "
        + "change XI, bench order and captaincy on the fixed 15-player squad; "
        + "they are prospective shadow evidence and cannot mutate a selection.")
    .WithTags("Selections")
    .Produces<SelectionRoleStrategyShadowDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/selections/{selectionRevisionId:long:min(1)}",
    async (
        long selectionRevisionId,
        SelectionRevisionStore store,
        CancellationToken cancellationToken) =>
    {
        SelectionRevisionDocument? revision =
            await store.GetAsync(selectionRevisionId, cancellationToken);
        return revision is null ? Results.NotFound() : Results.Ok(revision);
    })
    .WithName("GetSelectionRevision")
    .WithSummary("Read one immutable user-owned selection revision.")
    .WithTags("Selections")
    .Produces<SelectionRevisionDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapPost(
    "/api/v1/selections/drafts",
    async (
        SelectionDraftFromForecastRequest request,
        SelectionRevisionStore store,
        CancellationToken cancellationToken) =>
    {
        if (request.ForecastArtifactId is null or <= 0)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["forecastArtifactId"] =
                    [
                        "forecastArtifactId must identify a persisted forecast artifact.",
                    ],
                });
        }

        SelectionRevisionDocument? revision =
            await store.CreateDraftFromForecastAsync(
                request.ForecastArtifactId.Value,
                cancellationToken);
        return revision is null
            ? Results.NotFound()
            : Results.Created(
                $"/api/v1/selections/{revision.SelectionRevisionId}",
                revision);
    })
    .WithName("CreateSelectionDraftFromForecast")
    .WithSummary(
        "Preserve a persisted forecast selection as an immutable user-owned draft.")
    .WithDescription(
        "This does not lock, submit or write to an FPL account. Repeating the exact "
        + "forecast draft is idempotent; later changed selections create new revisions.")
    .WithTags("Selections")
    .Produces<SelectionRevisionDocument>(StatusCodes.Status201Created)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);
app.MapPost(
    "/api/v1/selections/{selectionRevisionId:long:min(1)}/revisions",
    async (
        long selectionRevisionId,
        SelectionRevisionEditRequest request,
        SelectionRevisionStore store,
        CancellationToken cancellationToken) =>
    {
        if (request.StartingPlayerIds is null
            || request.CaptainPlayerId is null
            || request.ViceCaptainPlayerId is null
            || request.ReplacementGoalkeeperPlayerId is null
            || request.OutfieldSubstitutePlayerIds is null
            || request.StartingPlayerIds.Any(playerId => playerId is null)
            || request.OutfieldSubstitutePlayerIds.Any(playerId => playerId is null))
        {
            return Results.BadRequest();
        }

        var selection = new LockedSelectionDocument(
            [.. request.StartingPlayerIds.Select(playerId => playerId!.Value)],
            request.CaptainPlayerId.Value,
            request.ViceCaptainPlayerId.Value,
            request.ReplacementGoalkeeperPlayerId.Value,
            [
                .. request.OutfieldSubstitutePlayerIds.Select(
                    playerId => playerId!.Value),
            ]);
        SelectionRevisionDocument? revision =
            await store.CreateEditedRevisionAsync(
                selectionRevisionId,
                selection,
                cancellationToken);
        return revision is null
            ? Results.NotFound()
            : Results.Created(
                $"/api/v1/selections/{revision.SelectionRevisionId}",
                revision);
    })
    .WithName("CreateEditedSelectionRevision")
    .WithSummary(
        "Create an immutable pre-deadline revision of the latest user selection.")
    .WithDescription(
        "The starting XI, bench order and captaincy must partition the same forecast "
        + "squad and satisfy FPL formation rules. Editing a locked choice creates a "
        + "new unlocked revision; no FPL account action occurs.")
    .WithTags("Selections")
    .Produces<SelectionRevisionDocument>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapGet(
    "/api/v1/selections/{selectionRevisionId:long:min(1)}/comparison",
    async (
        long selectionRevisionId,
        SelectionRevisionStore store,
        CancellationToken cancellationToken) =>
    {
        SelectionComparisonDocument? comparison =
            await store.GetComparisonAsync(
                selectionRevisionId,
                cancellationToken);
        return comparison is null ? Results.NotFound() : Results.Ok(comparison);
    })
    .WithName("GetSelectionComparison")
    .WithSummary(
        "Compare a user selection with the model squad on the same immutable forecast.")
    .WithDescription(
        "Both selections use the same player point estimates and decision cutoff. "
        + "The delta is not a calibrated probability or realised score.")
    .WithTags("Selections")
    .Produces<SelectionComparisonDocument>()
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);
app.MapPut(
    "/api/v1/selections/{selectionRevisionId:long:min(1)}/lock",
    async (
        long selectionRevisionId,
        SelectionRevisionStore store,
        CancellationToken cancellationToken) =>
    {
        SelectionRevisionDocument? revision =
            await store.LockAsync(selectionRevisionId, cancellationToken);
        return revision is null ? Results.NotFound() : Results.Ok(revision);
    })
    .WithName("LockSelectionRevision")
    .WithSummary("Explicitly lock the latest user-owned selection revision.")
    .WithDescription(
        "Locking is idempotent and available only before the recorded deadline. "
        + "A newer revision makes an older draft stale; no FPL account action occurs.")
    .WithTags("Selections")
    .Produces<SelectionRevisionDocument>()
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);
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
    "/api/v1/data/fpl-form-forecast/latest",
    async (
        FplFormForecastStore store,
        CancellationToken cancellationToken) =>
    {
        FplFormForecastCaptureDocument? capture =
            await store.GetLatestAsync(cancellationToken);
        return capture is null ? Results.NotFound() : Results.Ok(capture);
    })
    .WithName("GetLatestFplFormForecastCapture")
    .WithSummary(
        "Read provenance and counts for the latest immutable public FPL Form forecast capture.")
    .WithDescription(
        "The fixed-origin operator import uses the existing isolated Playwright MCP service "
        + "to record bounded active next-Gameweek fixture forecasts. Provider publication "
        + "time is unknown; availableAtUtc is the completed retrieval time.")
    .WithTags("Data")
    .Produces<FplFormForecastCaptureDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/evidence/claims/{seasonCode}/{gameweek:int:min(1):max(38)}",
    async (
        string seasonCode,
        int gameweek,
        DateTimeOffset? decisionCutoffUtc,
        EvidenceClaimStore store,
        CancellationToken cancellationToken) =>
    {
        if (seasonCode.Length is < 4 or > 16 || decisionCutoffUtc is null)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [decisionCutoffUtc is null
                        ? "decisionCutoffUtc"
                        : "seasonCode"] =
                    [
                        decisionCutoffUtc is null
                            ? "decisionCutoffUtc is required."
                            : "seasonCode must contain 4 to 16 characters.",
                    ],
                });
        }

        EvidenceClaimSetDocument claims = await store.GetForGameweekAsync(
            seasonCode,
            gameweek,
            decisionCutoffUtc.Value,
            cancellationToken);
        return Results.Ok(claims);
    })
    .WithName("GetEvidenceClaimsForGameweek")
    .WithSummary(
        "Read quarantined, point-in-time evidence claims available by a decision cutoff.")
    .WithDescription(
        "Claims are immutable source-linked candidates with verified official player "
        + "identity. They do not influence forecasts or authoritative squad state.")
    .WithTags("Evidence")
    .Produces<EvidenceClaimSetDocument>()
    .ProducesValidationProblem();
app.MapGet(
    "/api/v1/research/sources",
    async (
        ResearchSourceSnapshotStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.GetInventoryAsync(cancellationToken)))
    .WithName("GetResearchSourceInventory")
    .WithSummary(
        "Read the fixed shadow-source inventory and latest immutable capture metadata.")
    .WithDescription(
        "The inventory deliberately spans official availability, specialist predicted "
        + "lineups, prior-competition playing time and fixed team schedules. Latest "
        + "FFScout coverage reports "
        + "only exact snapshot-linked start classifications; partial and missing clubs "
        + "remain unknown. Captures remain shadow-only and expose no third-party article "
        + "text or forecast influence.")
    .WithTags("Research")
    .Produces<ResearchSourceInventoryDocument>();
app.MapGet(
    "/api/v1/research/snapshots/{snapshotId:long}/fbref-playing-time",
    async (
        long snapshotId,
        FbrefPlayingTimeExtractor extractor,
        CancellationToken cancellationToken) =>
    {
        try
        {
            FbrefPlayingTimeDocument? extraction =
                await extractor.GetAsync(snapshotId, cancellationToken);
            return extraction is null
                ? Results.NotFound()
                : Results.Ok(extraction);
        }
        catch (ResearchSourceSnapshotException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The retained FBref snapshot could not be extracted.");
        }
    })
    .WithName("GetFbrefPlayingTime")
    .WithSummary(
        "Extract bounded prior-season appearances, starts and minutes from one snapshot.")
    .WithDescription(
        "The deterministic parser exposes stable FBref player and match-log identities. "
        + "The versioned bridge is bound to a reviewed snapshot content hash and validates "
        + "every source ID against its expected official stable code and current team. New "
        + "exact matches remain proposals, unresolved identities remain explicit and the "
        + "result cannot influence forecasts.")
    .WithTags("Research")
    .Produces<FbrefPlayingTimeDocument>()
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapGet(
    "/api/v1/research/snapshots/{snapshotId:long}/fbref-team-schedule",
    async (
        long snapshotId,
        FbrefTeamScheduleExtractor extractor,
        CancellationToken cancellationToken) =>
    {
        try
        {
            FbrefTeamScheduleDocument? extraction =
                await extractor.GetAsync(snapshotId, cancellationToken);
            return extraction is null
                ? Results.NotFound()
                : Results.Ok(extraction);
        }
        catch (ResearchSourceSnapshotException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The retained FBref team schedule could not be extracted.");
        }
    })
    .WithName("GetFbrefTeamSchedule")
    .WithSummary(
        "Extract one promoted club's prior-season Championship match schedule.")
    .WithDescription(
        "The deterministic parser exposes exact match IDs, dates, kickoffs, opponents "
        + "and results from one fixed source. These rows define match opportunities "
        + "for missing-aware player-history joins; they do not infer availability or "
        + "influence forecasts.")
    .WithTags("Research")
    .Produces<FbrefTeamScheduleDocument>()
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapGet(
    "/api/v1/research/fbref-player-match-opportunities",
    async (
        long playerMatchLogSnapshotId,
        long teamScheduleSnapshotId,
        FbrefPlayerMatchOpportunityExtractor extractor,
        CancellationToken cancellationToken) =>
    {
        try
        {
            FbrefPlayerMatchOpportunityDocument? extraction =
                await extractor.GetAsync(
                    playerMatchLogSnapshotId,
                    teamScheduleSnapshotId,
                    cancellationToken);
            return extraction is null
                ? Results.NotFound()
                : Results.Ok(extraction);
        }
        catch (ResearchSourceSnapshotException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The FBref player and schedule snapshots could not be joined.");
        }
    })
    .WithName("GetFbrefPlayerMatchOpportunities")
    .WithSummary(
        "Join one reviewed player chronology to every prior-season team match.")
    .WithDescription(
        "Requires explicit immutable player-log and team-schedule snapshot IDs. "
        + "Stable match IDs define the join; absent player rows retain null minutes "
        + "and explicit missingness. Last-3, last-6 and last-8 summaries remain "
        + "shadow feature candidates and cannot influence forecasts.")
    .WithTags("Research")
    .Produces<FbrefPlayerMatchOpportunityDocument>()
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapGet(
    "/api/v1/research/fbref-player-match-opportunity-coverage",
    async (
        FbrefMatchOpportunityCoverageReader reader,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await reader.GetAsync(cancellationToken));
        }
        catch (ResearchSourceSnapshotException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "FBref match-opportunity readiness could not be read.");
        }
    })
    .WithName("GetFbrefPlayerMatchOpportunityCoverage")
    .WithSummary(
        "Read player-log and team-schedule source-pair readiness for the reviewed cohort.")
    .WithDescription(
        "Reports exact immutable snapshot pairs for all 60 reviewed identities. "
        + "A ready source pair is not a validated feature or evidence of predictive "
        + "gain; incomplete capture keeps shadow evaluation blocked.")
    .WithTags("Research")
    .Produces<FbrefMatchOpportunityCoverageDocument>()
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapGet(
    "/api/v1/research/fbref-player-match-opportunity-features",
    async (
        FbrefMatchOpportunityFeatureTableReader reader,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await reader.GetAsync(cancellationToken));
        }
        catch (ResearchSourceSnapshotException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "FBref match-opportunity features could not be read.");
        }
    })
    .WithName("GetFbrefPlayerMatchOpportunityFeatures")
    .WithSummary(
        "Read cutoff-bound shadow match-opportunity features for the reviewed cohort.")
    .WithDescription(
        "Returns all 60 reviewed identities, preserving missing source pairs and "
        + "no-player-row counts. Raw season and last-3/6/8 counts remain exploratory, "
        + "unpromoted and unable to influence forecasts.")
    .WithTags("Research")
    .Produces<FbrefMatchOpportunityFeatureTableDocument>()
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapGet(
    "/api/v1/research/snapshots/{snapshotId:long}/fbref-player-match-log",
    async (
        long snapshotId,
        FbrefPlayerMatchLogExtractor extractor,
        CancellationToken cancellationToken) =>
    {
        try
        {
            FbrefPlayerMatchLogDocument? extraction =
                await extractor.GetAsync(snapshotId, cancellationToken);
            return extraction is null
                ? Results.NotFound()
                : Results.Ok(extraction);
        }
        catch (ResearchSourceSnapshotException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The retained FBref player match log could not be extracted.");
        }
    })
    .WithName("GetFbrefPlayerMatchLog")
    .WithSummary(
        "Extract bounded prior-season match chronology for one reviewed player.")
    .WithDescription(
        "The deterministic parser exposes dated competition, opponent, start, minutes "
        + "and bounded performance rows only after revalidating the source against the "
        + "snapshot-bound FBref identity bridge. Raw HTML remains private and no row "
        + "can influence forecasts.")
    .WithTags("Research")
    .Produces<FbrefPlayerMatchLogDocument>()
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapGet(
    "/api/v1/research/fbref-player-match-log-coverage",
    async (
        FbrefMatchLogCoverageReader reader,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await reader.GetAsync(cancellationToken));
        }
        catch (ResearchSourceSnapshotException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Reviewed FBref match-log coverage could not be read.");
        }
    })
    .WithName("GetFbrefPlayerMatchLogCoverage")
    .WithSummary(
        "Read capture coverage for every reviewed FBref player identity.")
    .WithDescription(
        "Reports captured and missing immutable match-log snapshots by player and "
        + "promoted club. It exposes metadata only, accepts no URL and does not start "
        + "collection or influence forecasts.")
    .WithTags("Research")
    .Produces<FbrefMatchLogCoverageDocument>()
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapGet(
    "/api/v1/data/historical-fpl/{seasonCode}",
    async (
        string seasonCode,
        HistoricalFplSeasonStore store,
        CancellationToken cancellationToken) =>
    {
        HistoricalFplSeasonCaptureDocument? capture =
            await store.GetLatestAsync(seasonCode, cancellationToken);
        return capture is null ? Results.NotFound() : Results.Ok(capture);
    })
    .WithName("GetHistoricalFplSeasonCapture")
    .WithSummary(
        "Read provenance and coverage for one pinned historical FPL season archive.")
    .WithDescription(
        "The normalized prior-season performance and final availability archive is "
        + "identified through stable official player codes. Raw CSV is retained "
        + "privately, and the source xP field is deliberately excluded.")
    .WithTags("Data")
    .Produces<HistoricalFplSeasonCaptureDocument>()
    .Produces(StatusCodes.Status404NotFound);
app.MapGet(
    "/api/v1/data/historical-fpl/identity-coverage/{fromSeasonCode}/{toSeasonCode}",
    async (
        string fromSeasonCode,
        string toSeasonCode,
        HistoricalFplSeasonStore store,
        CancellationToken cancellationToken) =>
    {
        try
        {
            HistoricalFplIdentityCoverageDocument? coverage =
                await store.GetIdentityCoverageAsync(
                    fromSeasonCode,
                    toSeasonCode,
                    cancellationToken);
            return coverage is null ? Results.NotFound() : Results.Ok(coverage);
        }
        catch (HistoricalFplSeasonCoverageException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Historical FPL identity coverage could not be audited.");
        }
    })
    .WithName("GetHistoricalFplIdentityCoverage")
    .WithSummary(
        "Audit stable player identity coverage across two pinned FPL seasons.")
    .WithDescription(
        "The audit revalidates both exact archive identities and normalized row "
        + "coverage, then compares official player codes without a name fallback. "
        + "Missing, ambiguous or non-pinned evidence fails closed.")
    .WithTags("Data")
    .Produces<HistoricalFplIdentityCoverageDocument>()
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
app.MapGet(
    "/api/v1/data/fpl-form-forecast/status",
    async (
        FplFormForecastStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.GetStatusAsync(cancellationToken)))
    .WithName("GetFplFormForecastStatus")
    .WithSummary(
        "Read the latest FPL Form collection check and retained forecast state.")
    .WithDescription(
        "Distinguishes an active captured forecast from a provider with no active "
        + "Gameweek, a failed collection, and a source that has not been checked. "
        + "A failed or waiting check never removes an earlier immutable capture.")
    .WithTags("Data")
    .Produces<FplFormForecastStatusDocument>();
app.MapGet(
    "/api/v1/data/fpl-form-forecast/{captureId:long:min(1)}/identity-coverage",
    async (
        long captureId,
        FplFormIdentityCoverageStore store,
        CancellationToken cancellationToken) =>
    {
        FplFormIdentityCoverageDocument? coverage =
            await store.GetAsync(captureId, cancellationToken);
        return coverage is null ? Results.NotFound() : Results.Ok(coverage);
    })
    .WithName("GetFplFormIdentityCoverage")
    .WithSummary(
        "Prove one FPL Form capture's player and fixture identities against official FPL.")
    .WithDescription(
        "Uses only an official catalogue available no later than the forecast capture and "
        + "Gameweek deadline. Source IDs must agree with attributes; absent IDs may use only "
        + "a unique normalized player or team-and-kickoff match. Ambiguity fails closed.")
    .WithTags("Data")
    .Produces<FplFormIdentityCoverageDocument>()
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
    "/api/v1/data/official-fpl/replays/{seasonCode}/{gameweek:int:min(1):max(38)}/players/{playerId:int:min(1)}",
    async (
        string seasonCode,
        int gameweek,
        int playerId,
        OfficialFplPlayerDossierStore store,
        CancellationToken cancellationToken) =>
    {
        OfficialFplPlayerDossierDocument? dossier = await store.GetAsync(
            seasonCode,
            gameweek,
            playerId,
            cancellationToken);
        return dossier is null ? Results.NotFound() : Results.Ok(dossier);
    })
    .WithName("GetOfficialFplPlayerDossier")
    .WithSummary(
        "Read one cutoff-correct player identity, outcomes, fixtures and research evidence.")
    .WithDescription(
        "Selects the same latest official capture available before the target Gameweek "
        + "deadline, excludes later outcome corrections, derives photo URLs only from "
        + "validated official bootstrap identifiers and exposes admitted research claims "
        + "as quarantined evidence that does not influence the forecast.")
    .WithTags("Data")
    .Produces<OfficialFplPlayerDossierDocument>()
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
    "/api/v1/data/official-fpl/outcomes/readiness",
    async (
        OfficialFplCaptureStore captureStore,
        OfficialFplOutcomeStore outcomeStore,
        CancellationToken cancellationToken) =>
    {
        OfficialFplOutcomeReadinessDocument? readiness =
            await outcomeStore.GetReadinessAsync(
                captureStore,
                cancellationToken);
        return readiness is null ? Results.NotFound() : Results.Ok(readiness);
    })
    .WithName("GetOfficialFplOutcomeReadiness")
    .WithSummary("Read automatic final-outcome capture and replay-pairing readiness.")
    .WithDescription(
        "Reports completed Gameweeks with captured immutable outcomes, complete "
        + "pre-deadline replay pairs and explicit missing or identity-blocked gaps. "
        + "The enabled official poller imports at most three outcome endpoints per "
        + "cycle and rechecks the latest completed Gameweek for corrections.")
    .WithTags("Data")
    .Produces<OfficialFplOutcomeReadinessDocument>()
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
