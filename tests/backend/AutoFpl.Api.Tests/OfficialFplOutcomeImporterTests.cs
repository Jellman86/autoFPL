using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Sources;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class OfficialFplOutcomeImporterTests
{
    private static readonly DateTimeOffset PreDeadlineUtc =
        new(2026, 8, 21, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FinalRetrievalUtc =
        new(2026, 8, 24, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Final_outcome_is_fixed_origin_immutable_idempotent_and_replayable()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await MigrateAsync(files.DatabasePath);
        var captureStore = new OfficialFplCaptureStore(options);
        var outcomeStore = new OfficialFplOutcomeStore(options);

        using var unusedClient = new HttpClient(new RejectingHandler());
        var preDeadlineImporter = new OfficialFplImporter(
            unusedClient,
            captureStore,
            new FixedTimeProvider(PreDeadlineUtc));
        OfficialFplCaptureDocument preDeadline =
            await preDeadlineImporter.ImportCapturedPayloadAsync(
                CreateBootstrap(isFinal: false),
                CreateFixtures(isFinal: false),
                PreDeadlineUtc,
                TestContext.Current.CancellationToken);

        byte[] live = CreateLivePayload();
        var handler = new OutcomeHandler(
            CreateBootstrap(isFinal: true),
            CreateFixtures(isFinal: true),
            live);
        using var client = new HttpClient(handler);
        var referenceImporter = new OfficialFplImporter(
            client,
            captureStore,
            new FixedTimeProvider(FinalRetrievalUtc));
        var importer = new OfficialFplOutcomeImporter(
            client,
            referenceImporter,
            captureStore,
            outcomeStore,
            new FixedTimeProvider(FinalRetrievalUtc));

        OfficialFplOutcomeCaptureDocument first =
            await importer.ImportLatestAsync(1, TestContext.Current.CancellationToken);
        OfficialFplOutcomeCaptureDocument duplicate =
            await importer.ImportCapturedPayloadAsync(
                (await captureStore.GetLatestAsync(TestContext.Current.CancellationToken))!,
                1,
                live,
                FinalRetrievalUtc.AddMinutes(15),
                TestContext.Current.CancellationToken);

        Assert.Equal(first, duplicate);
        Assert.Equal(1, first.OutcomeCaptureId);
        Assert.Equal("official-fpl-api-event-live/v1", first.SourceKey);
        Assert.Equal("2026-27", first.SeasonCode);
        Assert.Equal(1, first.Gameweek);
        Assert.NotEqual(preDeadline.CaptureId, first.ReferenceCaptureId);
        Assert.Equal(OfficialFplOutcomeImporter.GetLiveUri(1).AbsoluteUri, first.LiveUrl);
        Assert.Null(first.PublishedAtUtc);
        Assert.Equal(FinalRetrievalUtc, first.RetrievedAtUtc);
        Assert.Equal(FinalRetrievalUtc, first.AvailableAtUtc);
        Assert.Equal(64, first.LiveSha256.Length);
        Assert.Equal(4, first.PlayerCount);
        Assert.Equal(1, first.GameweekFixtureCount);
        Assert.Equal(
            [
                OfficialFplImporter.BootstrapUri,
                OfficialFplOutcomeImporter.GetLiveUri(1),
                OfficialFplImporter.FixturesUri,
            ],
            handler.Requests.OrderBy(uri => uri.AbsoluteUri).ToArray());

        OfficialFplReplayOutcomeDocument? pair =
            await outcomeStore.GetReplayOutcomeAsync(
                captureStore,
                "2026-27",
                1,
                TestContext.Current.CancellationToken);
        Assert.NotNull(pair);
        Assert.Equal(preDeadline.CaptureId, pair.Replay.SelectedCaptureId);
        Assert.Equal(first, pair.Outcome);
        Assert.Equal(4, pair.MatchedPlayerCount);
        await AssertOutcomeRowsAsync(files.DatabasePath, 1, 4);

        await using WebApplicationFactory<Program> factory =
            CreateFactory(files.DatabasePath);
        using HttpClient api = factory.CreateClient();
        OfficialFplOutcomeCaptureDocument? servedOutcome =
            await api.GetFromJsonAsync<OfficialFplOutcomeCaptureDocument>(
                "/api/v1/data/official-fpl/outcomes/2026-27/1/latest",
                TestContext.Current.CancellationToken);
        OfficialFplReplayOutcomeDocument? servedPair =
            await api.GetFromJsonAsync<OfficialFplReplayOutcomeDocument>(
                "/api/v1/data/official-fpl/replays/2026-27/1/outcome",
                TestContext.Current.CancellationToken);
        Assert.Equal(first, servedOutcome);
        Assert.Equal(pair, servedPair);
    }

    [Fact]
    public async Task Corrected_live_content_creates_a_new_immutable_outcome()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await MigrateAsync(files.DatabasePath);
        var captureStore = new OfficialFplCaptureStore(options);
        var outcomeStore = new OfficialFplOutcomeStore(options);
        using var client = new HttpClient(new RejectingHandler());
        var referenceImporter = new OfficialFplImporter(
            client,
            captureStore,
            new FixedTimeProvider(FinalRetrievalUtc));
        OfficialFplCaptureDocument reference =
            await referenceImporter.ImportCapturedPayloadAsync(
                CreateBootstrap(isFinal: true),
                CreateFixtures(isFinal: true),
                FinalRetrievalUtc,
                TestContext.Current.CancellationToken);
        var importer = new OfficialFplOutcomeImporter(
            client,
            referenceImporter,
            captureStore,
            outcomeStore,
            new FixedTimeProvider(FinalRetrievalUtc));

        OfficialFplOutcomeCaptureDocument first =
            await importer.ImportCapturedPayloadAsync(
                reference,
                1,
                CreateLivePayload(),
                FinalRetrievalUtc,
                TestContext.Current.CancellationToken);
        OfficialFplOutcomeCaptureDocument corrected =
            await importer.ImportCapturedPayloadAsync(
                reference,
                1,
                CreateLivePayload(firstPlayerPoints: 11),
                FinalRetrievalUtc.AddHours(1),
                TestContext.Current.CancellationToken);

        Assert.Equal(1, first.OutcomeCaptureId);
        Assert.Equal(2, corrected.OutcomeCaptureId);
        Assert.NotEqual(first.LiveSha256, corrected.LiveSha256);
        Assert.Equal(
            corrected,
            await outcomeStore.GetLatestAsync(
                "2026-27",
                1,
                TestContext.Current.CancellationToken));
        await AssertOutcomeRowsAsync(files.DatabasePath, 2, 8);
    }

    [Fact]
    public async Task Player_dossier_uses_official_photo_prior_outcome_and_cutoff_safe_fixtures()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await MigrateAsync(files.DatabasePath);
        var captureStore = new OfficialFplCaptureStore(options);
        var outcomeStore = new OfficialFplOutcomeStore(options);
        using var client = new HttpClient(new RejectingHandler());
        var referenceImporter = new OfficialFplImporter(
            client,
            captureStore,
            new FixedTimeProvider(FinalRetrievalUtc));
        OfficialFplCaptureDocument reference =
            await referenceImporter.ImportCapturedPayloadAsync(
                CreateBootstrap(isFinal: true),
                CreateFixtures(isFinal: true),
                FinalRetrievalUtc,
                TestContext.Current.CancellationToken);
        var outcomeImporter = new OfficialFplOutcomeImporter(
            client,
            referenceImporter,
            captureStore,
            outcomeStore,
            new FixedTimeProvider(FinalRetrievalUtc));
        OfficialFplOutcomeCaptureDocument inTime =
            await outcomeImporter.ImportCapturedPayloadAsync(
                reference,
                1,
                CreateLivePayload(),
                FinalRetrievalUtc,
                TestContext.Current.CancellationToken);
        await outcomeImporter.ImportCapturedPayloadAsync(
            reference,
            1,
            CreateLivePayload(firstPlayerPoints: 11),
            new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        var store = new OfficialFplPlayerDossierStore(options, captureStore);
        OfficialFplPlayerDossierDocument? dossier = await store.GetAsync(
            "2026-27",
            2,
            1,
            TestContext.Current.CancellationToken);

        Assert.NotNull(dossier);
        Assert.Equal("1.1", dossier.SchemaVersion);
        Assert.Equal("2026-27", dossier.SeasonCode);
        Assert.Equal(2, dossier.TargetGameweek);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 29, 14, 0, 0, TimeSpan.Zero),
            dossier.DecisionCutoffUtc);
        Assert.Equal(reference.CaptureId, dossier.SelectedCaptureId);
        Assert.Equal(1, dossier.Player.PlayerId);
        Assert.Equal(101, dossier.Player.PlayerCode);
        Assert.Equal("Ada Keeper", dossier.Player.FullName);
        Assert.Equal("North Town", dossier.Player.TeamName);
        Assert.Equal("goalkeeper", dossier.Player.Position);
        Assert.Equal("101.jpg", dossier.Player.PhotoIdentifier);
        Assert.Equal(
            $"{OfficialFplPlayerDossierStore.PhotoBaseUrl}p101.png",
            dossier.Player.PhotoUrl);
        Assert.NotNull(dossier.PublishedExpectedPoints);
        Assert.Equal(
            OfficialFplImporter.SourceKey,
            dossier.PublishedExpectedPoints.SourceKey);
        Assert.Equal(2, dossier.PublishedExpectedPoints.TargetGameweek);
        Assert.Equal(4.2m, dossier.PublishedExpectedPoints.ExpectedPoints);
        Assert.Equal(
            "published-challenger-not-promoted",
            dossier.PublishedExpectedPoints.EvidenceStatus);

        OfficialFplPlayerOutcomeDocument outcome = Assert.Single(dossier.RecentOutcomes);
        Assert.Equal(1, outcome.Gameweek);
        Assert.Equal(inTime.OutcomeCaptureId, outcome.OutcomeCaptureId);
        Assert.Equal(6, outcome.TotalPoints);
        Assert.Equal(90, outcome.Minutes);
        Assert.Equal(4, outcome.Saves);
        Assert.Equal(2, outcome.Bonus);
        Assert.Equal(32, outcome.Bps);
        Assert.Equal(44.2m, outcome.Influence);
        Assert.Equal(12.3m, outcome.Creativity);
        Assert.Equal(8.4m, outcome.Threat);
        Assert.Equal(6.5m, outcome.IctIndex);
        Assert.Equal(10, outcome.DefensiveContribution);
        Assert.Equal(0.31m, outcome.ExpectedGoals);
        Assert.Equal(0.22m, outcome.ExpectedAssists);
        Assert.Equal(0.53m, outcome.ExpectedGoalInvolvements);
        Assert.Equal(0.74m, outcome.ExpectedGoalsConceded);
        Assert.False(outcome.IsGameweekAggregate);
        OfficialFplPlayerFixtureDocument priorFixture = Assert.Single(outcome.Fixtures);
        Assert.Equal(1, priorFixture.FixtureId);
        Assert.Equal("South City", priorFixture.OpponentName);
        Assert.True(priorFixture.IsHome);

        OfficialFplPlayerFixtureDocument upcoming =
            Assert.Single(dossier.UpcomingFixtures);
        Assert.Equal(2, upcoming.FixtureId);
        Assert.Equal(2, upcoming.Gameweek);
        Assert.Equal("South City", upcoming.OpponentName);
        Assert.False(upcoming.IsHome);
        Assert.False(upcoming.Started);

        await using WebApplicationFactory<Program> factory =
            CreateFactory(files.DatabasePath);
        using HttpClient api = factory.CreateClient();
        OfficialFplPlayerDossierDocument? served =
            await api.GetFromJsonAsync<OfficialFplPlayerDossierDocument>(
                "/api/v1/data/official-fpl/replays/2026-27/2/players/1",
                TestContext.Current.CancellationToken);
        Assert.Equal(
            JsonSerializer.Serialize(dossier),
            JsonSerializer.Serialize(served));
    }

    [Fact]
    public async Task Incomplete_event_is_rejected_before_live_fetch()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await MigrateAsync(files.DatabasePath);
        var captureStore = new OfficialFplCaptureStore(options);
        var outcomeStore = new OfficialFplOutcomeStore(options);
        var handler = new OutcomeHandler(
            CreateBootstrap(isFinal: false),
            CreateFixtures(isFinal: false),
            CreateLivePayload());
        using var client = new HttpClient(handler);
        var referenceImporter = new OfficialFplImporter(
            client,
            captureStore,
            new FixedTimeProvider(PreDeadlineUtc));
        var importer = new OfficialFplOutcomeImporter(
            client,
            referenceImporter,
            captureStore,
            outcomeStore,
            new FixedTimeProvider(PreDeadlineUtc));

        OfficialFplPayloadException exception =
            await Assert.ThrowsAsync<OfficialFplPayloadException>(
                () => importer.ImportLatestAsync(
                    1,
                    TestContext.Current.CancellationToken));

        Assert.Contains("not finished and data-checked", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(OfficialFplOutcomeImporter.GetLiveUri(1), handler.Requests);
        Assert.Null(
            await outcomeStore.GetLatestAsync(
                "2026-27",
                1,
                TestContext.Current.CancellationToken));
        await AssertOutcomeRowsAsync(files.DatabasePath, 0, 0);
    }

    [Fact]
    public async Task Empty_live_payload_is_rejected_atomically()
    {
        await AssertInvalidLivePayloadAsync(
            """{"elements":[]}"""u8.ToArray(),
            "elements count");
    }

    [Fact]
    public async Task Incomplete_player_coverage_is_rejected_atomically()
    {
        await AssertInvalidLivePayloadAsync(
            CreateLivePayload(playerIds: [1, 2, 3]),
            "exactly cover");
    }

    [Fact]
    public async Task Invalid_underlying_stat_is_rejected_atomically()
    {
        await AssertInvalidLivePayloadAsync(
            CreateLivePayload(firstExpectedGoals: "NaN"),
            "expected_goals");
    }

    private static async Task AssertInvalidLivePayloadAsync(
        byte[] live,
        string expectedMessage)
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await MigrateAsync(files.DatabasePath);
        var captureStore = new OfficialFplCaptureStore(options);
        var outcomeStore = new OfficialFplOutcomeStore(options);
        using var client = new HttpClient(new RejectingHandler());
        var referenceImporter = new OfficialFplImporter(
            client,
            captureStore,
            new FixedTimeProvider(FinalRetrievalUtc));
        OfficialFplCaptureDocument reference =
            await referenceImporter.ImportCapturedPayloadAsync(
                CreateBootstrap(isFinal: true),
                CreateFixtures(isFinal: true),
                FinalRetrievalUtc,
                TestContext.Current.CancellationToken);
        var importer = new OfficialFplOutcomeImporter(
            client,
            referenceImporter,
            captureStore,
            outcomeStore,
            new FixedTimeProvider(FinalRetrievalUtc));

        OfficialFplPayloadException exception =
            await Assert.ThrowsAsync<OfficialFplPayloadException>(
                () => importer.ImportCapturedPayloadAsync(
                    reference,
                    1,
                    live,
                    FinalRetrievalUtc,
                    TestContext.Current.CancellationToken));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        await AssertOutcomeRowsAsync(files.DatabasePath, 0, 0);
    }

    private static byte[] CreateBootstrap(bool isFinal)
    {
        object[] players =
        [
            Player(1, 101, 1, 1, "Ada", "Keeper"),
            Player(2, 102, 1, 2, "Bea", "Back"),
            Player(3, 103, 2, 3, "Mia", "Middle"),
            Player(4, 104, 2, 4, "Fran", "Forward"),
        ];
        return JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                events = new object[]
                {
                    new
                    {
                        id = 1,
                        name = "Gameweek 1",
                        deadline_time = "2026-08-21T17:30:00Z",
                        finished = isFinal,
                        data_checked = isFinal,
                        is_current = !isFinal,
                        is_next = false,
                    },
                    new
                    {
                        id = 2,
                        name = "Gameweek 2",
                        deadline_time = "2026-08-29T14:00:00Z",
                        finished = false,
                        data_checked = false,
                        is_current = false,
                        is_next = isFinal,
                    },
                },
                teams = new object[]
                {
                    new { id = 1, code = 10, name = "North Town", short_name = "NTH" },
                    new { id = 2, code = 20, name = "South City", short_name = "STH" },
                },
                elements = players,
            });
    }

    private static object Player(
        int id,
        int code,
        int team,
        int elementType,
        string firstName,
        string webName) =>
        new
        {
            id,
            code,
            team,
            element_type = elementType,
            first_name = firstName,
            second_name = webName,
            web_name = webName,
            photo = $"{code}.jpg",
            now_cost = 50,
            status = "a",
            news = string.Empty,
            news_added = (string?)null,
            chance_of_playing_next_round = (int?)null,
            selected_by_percent = "10.5",
            ep_next = "4.2",
            total_points = 0,
            minutes = 0,
            starts = 0,
        };

    private static byte[] CreateFixtures(bool isFinal) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new object[]
            {
                new
                {
                    id = 1,
                    @event = 1,
                    team_h = 1,
                    team_a = 2,
                    kickoff_time = "2026-08-22T14:00:00Z",
                    started = isFinal,
                    finished = isFinal,
                    finished_provisional = isFinal,
                    team_h_score = isFinal ? 2 : (int?)null,
                    team_a_score = isFinal ? 1 : (int?)null,
                },
                new
                {
                    id = 2,
                    @event = 2,
                    team_h = 2,
                    team_a = 1,
                    kickoff_time = "2026-08-29T15:00:00Z",
                    started = false,
                    finished = false,
                    finished_provisional = false,
                    team_h_score = (int?)null,
                    team_a_score = (int?)null,
                },
            });

    private static byte[] CreateLivePayload(
        int firstPlayerPoints = 6,
        int[]? playerIds = null,
        string firstExpectedGoals = "0.31")
    {
        int[] ids = playerIds ?? [1, 2, 3, 4];
        return JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                elements = ids.Select(
                    id => new
                    {
                        id,
                        stats = new
                        {
                            minutes = id == 1 ? 90 : 60,
                            starts = 1,
                            total_points = id == 1 ? firstPlayerPoints : id,
                            goals_scored = id == 4 ? 1 : 0,
                            assists = id == 3 ? 1 : 0,
                            clean_sheets = id == 1 ? 1 : 0,
                            goals_conceded = id <= 2 ? 1 : 2,
                            saves = id == 1 ? 4 : 0,
                            bonus = id == 1 ? 2 : 0,
                            yellow_cards = 0,
                            red_cards = 0,
                            own_goals = 0,
                            penalties_saved = 0,
                            penalties_missed = 0,
                            bps = id == 1 ? 32 : id,
                            influence = id == 1 ? "44.2" : "1.0",
                            creativity = id == 1 ? "12.3" : "1.0",
                            threat = id == 1 ? "8.4" : "1.0",
                            ict_index = id == 1 ? "6.5" : "0.3",
                            clearances_blocks_interceptions = id == 1 ? 4 : 1,
                            recoveries = id == 1 ? 6 : 1,
                            tackles = id == 1 ? 2 : 1,
                            defensive_contribution = id == 1 ? 10 : 2,
                            expected_goals = id == 1 ? firstExpectedGoals : "0.10",
                            expected_assists = id == 1 ? "0.22" : "0.10",
                            expected_goal_involvements = id == 1 ? "0.53" : "0.20",
                            expected_goals_conceded = id == 1 ? "0.74" : "1.10",
                        },
                    }),
            });
    }

    private static async Task<DatabaseOptions> MigrateAsync(string databasePath)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:DatabasePath"] = databasePath,
                })
            .Build();
        DatabaseOptions options = DatabaseOptions.FromConfiguration(configuration);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        return options;
    }

    private static WebApplicationFactory<Program> CreateFactory(string databasePath) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(
                builder =>
                {
                    builder.UseSetting("AutoFpl:DatabasePath", databasePath);
                    builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");
                });

    private static async Task AssertOutcomeRowsAsync(
        string databasePath,
        int expectedCaptures,
        int expectedPlayers)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM official_fpl_outcome_captures),
                (SELECT COUNT(*) FROM official_fpl_player_outcomes);
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(expectedCaptures, reader.GetInt32(0));
        Assert.Equal(expectedPlayers, reader.GetInt32(1));
    }

    private sealed class OutcomeHandler : HttpMessageHandler
    {
        private readonly byte[] _bootstrap;
        private readonly byte[] _fixtures;
        private readonly byte[] _live;

        public OutcomeHandler(byte[] bootstrap, byte[] fixtures, byte[] live)
        {
            _bootstrap = bootstrap;
            _fixtures = fixtures;
            _live = live;
        }

        public ConcurrentBag<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Uri uri = Assert.IsType<Uri>(request.RequestUri);
            Requests.Add(uri);
            byte[] content = uri == OfficialFplImporter.BootstrapUri
                ? _bootstrap
                : uri == OfficialFplImporter.FixturesUri
                    ? _fixtures
                    : uri == OfficialFplOutcomeImporter.GetLiveUri(1)
                        ? _live
                        : throw new InvalidOperationException($"Unexpected URI: {uri}");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No network request was expected.");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-outcome-tests-{Guid.NewGuid():N}");

        public TemporaryDatabaseFiles()
        {
            Directory.CreateDirectory(_directory);
            DatabasePath = Path.Combine(_directory, "autofpl.db");
        }

        public string DatabasePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
