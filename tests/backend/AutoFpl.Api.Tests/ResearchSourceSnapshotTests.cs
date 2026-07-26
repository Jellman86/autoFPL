using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Intelligence;
using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Intelligence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class ResearchSourceSnapshotTests
{
    private static readonly DateTimeOffset CaptureTime =
        new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RetrievalTime =
        new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Spider_capture_is_fixed_origin_immutable_deduplicated_and_shadow_only()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var handler = new SpiderMcpHandler(
            """
            ## Arsenal
            * Raya
            * Saliba
            Last Updated Sun 26th Jul
            """);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://spider-mcp:8080/mcp"),
        };
        var importer = new ResearchSourceSnapshotImporter(
            new SpiderMcpClient(httpClient),
            new ResearchSourceSnapshotStore(
                options,
                new FixedTimeProvider(RetrievalTime)));

        ResearchSourceSnapshotDocument first = await importer.ImportAsync(
            "ffscout-predicted-lineups",
            TestContext.Current.CancellationToken);
        ResearchSourceSnapshotDocument duplicate = await importer.ImportAsync(
            "ffscout-predicted-lineups",
            TestContext.Current.CancellationToken);

        Assert.Equal(first, duplicate);
        Assert.Equal("shadow-only", first.Status);
        Assert.Equal("specialist-predicted-lineup", first.SourceClass);
        Assert.Equal("ffscout-editorial-lineup", first.DependenceGroup);
        Assert.Equal("spider-mcp", first.TransportKey);
        Assert.Equal(1, first.SourceRevision);
        Assert.Equal(1, first.Gameweek);
        Assert.Equal(1, first.IdentityCaptureId);
        Assert.True(first.IsPreDeadline);
        Assert.Equal(64, first.ContentSha256.Length);
        Assert.True(first.ContentBytes > 0);
        Assert.Equal(2, handler.ScrapeCalls);
        Assert.Equal(2, handler.DeleteCalls);

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM research_source_snapshots;";
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync(
            TestContext.Current.CancellationToken))!);

        await using SqliteCommand readRaw = connection.CreateCommand();
        readRaw.CommandText =
            "SELECT content_brotli FROM research_source_snapshots WHERE snapshot_id = 1;";
        Assert.NotEmpty((byte[])(await readRaw.ExecuteScalarAsync(
            TestContext.Current.CancellationToken))!);

        await using SqliteCommand mutate = connection.CreateCommand();
        mutate.CommandText =
            "UPDATE research_source_snapshots SET status = 'changed' WHERE snapshot_id = 1;";
        SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
            async () => await mutate.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "research source snapshots are immutable",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ffscout_extractor_uses_official_photo_code_and_is_idempotent()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var snapshotStore = new ResearchSourceSnapshotStore(
            options,
            new FixedTimeProvider(RetrievalTime));
        ResearchSourceDefinition source =
            ResearchSourceRegistry.Get("ffscout-predicted-lineups");
        ResearchSourceSnapshotDocument snapshot = await snapshotStore.PersistAsync(
            source,
            new SpiderScrapeResult(
                source.CanonicalUri,
                200,
                """
                FPL 2026/27 - Predicted Line-ups
                Our Team News page will house predicted line-ups for all 20 Premier League teams.
                ![Home badge](https://example.test/home.png)##
                Home
                **Next Match:** Away (H)
                * ![Avatar of Test Player](https://resources.premierleague.com/premierleague25/photos/players/110x140/1001.png)Test Player
                * **Out:**
                * Mvom Onana
                * **Doubts:**
                *
                Carvalho 25%
                * **Banned:**
                * Test Player
                """,
                "untrusted_remote_content"),
            TestContext.Current.CancellationToken);
        var extractor = new ResearchSourceClaimExtractor(
            snapshotStore,
            new EvidenceClaimStore(
                options,
                new FixedTimeProvider(RetrievalTime.AddMinutes(1))));

        ResearchSourceClaimExtractionDocument first =
            await extractor.ExtractAsync(
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken);
        ResearchSourceClaimExtractionDocument duplicate =
            await extractor.ExtractAsync(
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken);

        Assert.Equal(1, first.CandidateCount);
        Assert.Equal(1, first.StartClaimCount);
        Assert.Equal(2, first.AvailabilityCandidateCount);
        Assert.Equal(2, first.AvailabilityClaimCount);
        Assert.Equal(0, first.UnresolvedAvailabilityCount);
        Assert.Equal(3, first.ClaimCount);
        Assert.Empty(first.UnresolvedPlayerCodes);
        Assert.Equal(first, duplicate);

        EvidenceClaimSetDocument claims =
            await new EvidenceClaimStore(options, TimeProvider.System)
                .GetForGameweekAsync(
                    "2026-27",
                    1,
                    RetrievalTime,
                    TestContext.Current.CancellationToken);
        Assert.Equal(3, claims.Claims.Count);
        EvidenceClaimDocument claim =
            Assert.Single(
                claims.Claims,
                item => item.ClaimType == "start");
        Assert.Equal(101, claim.PlayerId);
        Assert.Equal("start", claim.ClaimType);
        Assert.Equal("starts", claim.StartStatus);
        Assert.Equal("model-forecast", claim.Directness);
        Assert.Equal(
            ResearchSourceClaimExtractor.FfScoutLineupExtractionVersion,
            claim.ExtractionVersion);
        Assert.Equal("Home predicted XI: Test Player", claim.SourceSpan);
        Assert.Equal(1m, claim.ExtractionConfidence);
        EvidenceClaimDocument unavailable =
            Assert.Single(
                claims.Claims,
                item => item.AvailabilityStatus == "unavailable");
        Assert.Equal(102, unavailable.PlayerId);
        Assert.Equal("reported", unavailable.Directness);
        Assert.Null(unavailable.ForecastProbability);
        Assert.Equal(0.95m, unavailable.ExtractionConfidence);
        Assert.Equal(
            ResearchSourceClaimExtractor.FfScoutAvailabilityExtractionVersion,
            unavailable.ExtractionVersion);
        EvidenceClaimDocument doubtful =
            Assert.Single(
                claims.Claims,
                item => item.AvailabilityStatus == "doubtful");
        Assert.Equal(103, doubtful.PlayerId);
        Assert.Equal(0.25m, doubtful.ForecastProbability);
        Assert.Equal(1m, doubtful.ExtractionConfidence);

        await using var connection =
            new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM evidence_claims;";
        Assert.Equal(
            3L,
            (long)(await count.ExecuteScalarAsync(
                TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task Ffscout_extractor_reports_unresolved_photo_codes_without_claims()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var snapshotStore = new ResearchSourceSnapshotStore(
            options,
            new FixedTimeProvider(RetrievalTime));
        ResearchSourceDefinition source =
            ResearchSourceRegistry.Get("ffscout-predicted-lineups");
        ResearchSourceSnapshotDocument snapshot = await snapshotStore.PersistAsync(
            source,
            new SpiderScrapeResult(
                source.CanonicalUri,
                200,
                """
                Our Team News page will house predicted line-ups for all 20 Premier League teams.
                ![Home badge](https://example.test/home.png)##
                Home
                * ![Avatar of Unknown](https://resources.premierleague.com/premierleague25/photos/players/110x140/9999.png)Unknown
                * **Out:**
                * Unknown
                * Smith
                * **Doubts:**
                * **Banned:**
                """,
                "untrusted_remote_content"),
            TestContext.Current.CancellationToken);
        var extractor = new ResearchSourceClaimExtractor(
            snapshotStore,
            new EvidenceClaimStore(options, TimeProvider.System));

        ResearchSourceClaimExtractionDocument extraction =
            await extractor.ExtractAsync(
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken);

        Assert.Equal(1, extraction.CandidateCount);
        Assert.Equal(0, extraction.StartClaimCount);
        Assert.Equal(2, extraction.AvailabilityCandidateCount);
        Assert.Equal(0, extraction.AvailabilityClaimCount);
        Assert.Equal(2, extraction.UnresolvedAvailabilityCount);
        Assert.Equal(0, extraction.ClaimCount);
        Assert.Equal([9999], extraction.UnresolvedPlayerCodes);
    }

    [Fact]
    public async Task Ffscout_extractor_falls_back_to_a_unique_team_scoped_name()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var snapshotStore = new ResearchSourceSnapshotStore(
            options,
            new FixedTimeProvider(RetrievalTime));
        ResearchSourceDefinition source =
            ResearchSourceRegistry.Get("ffscout-predicted-lineups");
        ResearchSourceSnapshotDocument snapshot = await snapshotStore.PersistAsync(
            source,
            new SpiderScrapeResult(
                source.CanonicalUri,
                200,
                """
                Our Team News page will house predicted line-ups for all 20 Premier League teams.
                ![Home badge](https://example.test/home.png)##
                Home
                * ![Avatar of Test Player](https://resources.premierleague.com/premierleague25/photos/players/110x140/9999.png)Test Player
                * **Out:**
                * **Doubts:**
                * **Banned:**
                """,
                "untrusted_remote_content"),
            TestContext.Current.CancellationToken);
        var extractor = new ResearchSourceClaimExtractor(
            snapshotStore,
            new EvidenceClaimStore(options, TimeProvider.System));

        ResearchSourceClaimExtractionDocument extraction =
            await extractor.ExtractAsync(
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken);

        Assert.Equal(1, extraction.StartClaimCount);
        Assert.Empty(extraction.UnresolvedPlayerCodes);
        EvidenceClaimSetDocument claims =
            await new EvidenceClaimStore(options, TimeProvider.System)
                .GetForGameweekAsync(
                    "2026-27",
                    1,
                    RetrievalTime,
                    TestContext.Current.CancellationToken);
        EvidenceClaimDocument claim = Assert.Single(claims.Claims);
        Assert.Equal(101, claim.PlayerId);
        Assert.Equal(0.95m, claim.ExtractionConfidence);
    }

    [Fact]
    public async Task Ffscout_extractor_infers_non_starters_only_from_a_complete_resolved_xi()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        await AddHomePlayersAsync(options);
        var snapshotStore = new ResearchSourceSnapshotStore(
            options,
            new FixedTimeProvider(RetrievalTime));
        ResearchSourceDefinition source =
            ResearchSourceRegistry.Get("ffscout-predicted-lineups");
        ResearchSourceSnapshotDocument snapshot = await snapshotStore.PersistAsync(
            source,
            new SpiderScrapeResult(
                source.CanonicalUri,
                200,
                """
                Our Team News page will house predicted line-ups for all 20 Premier League teams.
                ![Home badge](https://example.test/home.png)##
                Home
                * ![Avatar of Test Player](https://resources.premierleague.com/premierleague25/photos/players/110x140/1001.png)Test Player
                * ![Avatar of Onana](https://resources.premierleague.com/premierleague25/photos/players/110x140/1002.png)Onana
                * ![Avatar of Carvalho](https://resources.premierleague.com/premierleague25/photos/players/110x140/1003.png)Carvalho
                * ![Avatar of Alice Smith](https://resources.premierleague.com/premierleague25/photos/players/110x140/1004.png)Alice Smith
                * ![Avatar of Bob Smith](https://resources.premierleague.com/premierleague25/photos/players/110x140/1005.png)Bob Smith
                * ![Avatar of Extra 106](https://resources.premierleague.com/premierleague25/photos/players/110x140/1106.png)Extra 106
                * ![Avatar of Extra 107](https://resources.premierleague.com/premierleague25/photos/players/110x140/1107.png)Extra 107
                * ![Avatar of Extra 108](https://resources.premierleague.com/premierleague25/photos/players/110x140/1108.png)Extra 108
                * ![Avatar of Extra 109](https://resources.premierleague.com/premierleague25/photos/players/110x140/1109.png)Extra 109
                * ![Avatar of Extra 110](https://resources.premierleague.com/premierleague25/photos/players/110x140/1110.png)Extra 110
                * ![Avatar of Extra 111](https://resources.premierleague.com/premierleague25/photos/players/110x140/1111.png)Extra 111
                * **Out:**
                * **Doubts:**
                * **Banned:**
                """,
                "untrusted_remote_content"),
            TestContext.Current.CancellationToken);
        var extractor = new ResearchSourceClaimExtractor(
            snapshotStore,
            new EvidenceClaimStore(options, TimeProvider.System));

        ResearchSourceClaimExtractionDocument extraction =
            await extractor.ExtractAsync(
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken);

        Assert.Equal(11, extraction.CandidateCount);
        Assert.Equal(12, extraction.StartClaimCount);
        Assert.Equal(12, extraction.ClaimCount);
        Assert.Empty(extraction.UnresolvedPlayerCodes);
        EvidenceClaimSetDocument claims =
            await new EvidenceClaimStore(options, TimeProvider.System)
                .GetForGameweekAsync(
                    "2026-27",
                    1,
                    RetrievalTime,
                    TestContext.Current.CancellationToken);
        EvidenceClaimDocument omitted = Assert.Single(
            claims.Claims,
            claim => claim.StartStatus == "does-not-start");
        Assert.Equal(112, omitted.PlayerId);
        Assert.Equal(
            ResearchSourceClaimExtractor.FfScoutLineupComplementExtractionVersion,
            omitted.ExtractionVersion);
        Assert.Equal(
            "Home complete predicted XI omits: Extra 112",
            omitted.SourceSpan);
    }

    [Fact]
    public async Task Straightred_extractor_retains_dependent_consensus_probabilities()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var snapshotStore = new ResearchSourceSnapshotStore(
            options,
            new FixedTimeProvider(RetrievalTime));
        ResearchSourceDefinition source =
            ResearchSourceRegistry.Get("straightred-lineup-consensus");
        ResearchSourceSnapshotDocument snapshot = await snapshotStore.PersistAsync(
            source,
            new SpiderScrapeResult(
                source.CanonicalUri,
                200,
                """
                strAIghtred - Premier League Predicted Lineups
                Built for FPL managers. Powered by multiple prediction sources.
                🔵 Home vs Away · 2 sources
                Test Player
                100%
                Carvalho
                50%
                3-5-2
                ### Recently Updated
                """,
                "untrusted_remote_content"),
            TestContext.Current.CancellationToken);
        var extractor = new ResearchSourceClaimExtractor(
            snapshotStore,
            new EvidenceClaimStore(options, TimeProvider.System));

        ResearchSourceClaimExtractionDocument extraction =
            await extractor.ExtractAsync(
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken);
        ResearchSourceClaimExtractionDocument duplicate =
            await extractor.ExtractAsync(
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken);

        Assert.Equal(extraction, duplicate);
        Assert.Equal(
            ResearchSourceClaimExtractor.StraightredExtractionVersion,
            extraction.ExtractionVersion);
        Assert.Equal(2, extraction.CandidateCount);
        Assert.Equal(2, extraction.StartClaimCount);
        Assert.Equal(0, extraction.UnresolvedStartCount);
        Assert.Equal(0, extraction.AvailabilityCandidateCount);
        Assert.Equal(2, extraction.ClaimCount);
        Assert.Empty(extraction.UnresolvedPlayerCodes);

        EvidenceClaimSetDocument claims =
            await new EvidenceClaimStore(options, TimeProvider.System)
                .GetForGameweekAsync(
                    "2026-27",
                    1,
                    RetrievalTime,
                    TestContext.Current.CancellationToken);
        Assert.Equal(2, claims.Claims.Count);
        Assert.All(
            claims.Claims,
            claim =>
            {
                Assert.Equal("straightred-lineup-consensus", claim.SourceKey);
                Assert.Equal("start", claim.ClaimType);
                Assert.Equal("starts", claim.StartStatus);
                Assert.Equal("model-forecast", claim.Directness);
                Assert.Equal(
                    ResearchSourceClaimExtractor.StraightredExtractionVersion,
                    claim.ExtractionVersion);
                Assert.NotNull(claim.DuplicateClusterKey);
            });
        Assert.Equal(
            [0.5m, 1m],
            claims.Claims
                .Select(claim => claim.ForecastProbability!.Value)
                .Order()
                .ToArray());
        Assert.Contains(
            claims.Claims,
            claim => claim.SourceSpan
                == "Home consensus (2 sources): Test Player 100%");
    }

    [Fact]
    public async Task Inventory_api_exposes_diverse_registry_and_metadata_but_not_source_text()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var source = ResearchSourceRegistry.Get("premier-league-injuries");
        await new ResearchSourceSnapshotStore(
                options,
                new FixedTimeProvider(RetrievalTime))
            .PersistAsync(
                source,
                new SpiderScrapeResult(
                    source.CanonicalUri,
                    200,
                    "Private retained source text.",
                    "untrusted_remote_content"),
                TestContext.Current.CancellationToken);

        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(
                    builder =>
                    {
                        builder.UseSetting(
                            "AutoFpl:DatabasePath",
                            files.DatabasePath);
                        builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");
                    });
        using HttpClient client = factory.CreateClient();
        ResearchSourceInventoryDocument? inventory =
            await client.GetFromJsonAsync<ResearchSourceInventoryDocument>(
                "/api/v1/research/sources",
                TestContext.Current.CancellationToken);

        Assert.NotNull(inventory);
        Assert.Equal(3, inventory.Sources.Count);
        Assert.Contains(
            inventory.Sources,
            item => item.SourceClass == "official-availability-aggregation");
        Assert.Contains(
            inventory.Sources,
            item => item.SourceClass == "specialist-predicted-lineup");
        Assert.Contains(
            inventory.Sources,
            item => item.SourceClass == "derived-predicted-lineup-consensus");
        ResearchSourceSnapshotDocument latest =
            Assert.Single(inventory.LatestSnapshots);
        Assert.Equal("premier-league-injuries", latest.SourceKey);
        string json = JsonSerializer.Serialize(
            inventory,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(
            "Private retained source text.",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spider_capture_rejects_wrong_server_and_redirected_resource()
    {
        ResearchSourceDefinition source =
            ResearchSourceRegistry.Get("ffscout-predicted-lineups");
        using var wrongServerClient = new HttpClient(
            new SpiderMcpHandler("content", serverName: "unexpected"))
        {
            BaseAddress = new Uri("http://spider-mcp:8080/mcp"),
        };
        await Assert.ThrowsAsync<ResearchSourceSnapshotException>(
            async () => await new SpiderMcpClient(wrongServerClient).ScrapeAsync(
                source,
                TestContext.Current.CancellationToken));

        using var wrongResourceClient = new HttpClient(
            new SpiderMcpHandler(
                "content",
                finalUrl: "https://example.com/copied-page"))
        {
            BaseAddress = new Uri("http://spider-mcp:8080/mcp"),
        };
        await Assert.ThrowsAsync<ResearchSourceSnapshotException>(
            async () => await new SpiderMcpClient(wrongResourceClient).ScrapeAsync(
                source,
                TestContext.Current.CancellationToken));
    }

    private static async Task<DatabaseOptions> CreateDatabaseAsync(
        string databasePath)
    {
        DatabaseOptions options = CreateOptions(databasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        await new OfficialFplImporter(
                httpClient,
                new OfficialFplCaptureStore(options),
                TimeProvider.System)
            .ImportCapturedPayloadAsync(
                CreateBootstrap(),
                CreateFixtures(),
                CaptureTime,
                TestContext.Current.CancellationToken);
        return options;
    }

    private static async Task AddHomePlayersAsync(DatabaseOptions options)
    {
        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH RECURSIVE extra(player_id) AS (
                VALUES (106)
                UNION ALL
                SELECT player_id + 1
                FROM extra
                WHERE player_id < 112
            )
            INSERT INTO official_fpl_players (
                capture_id,
                player_id,
                code,
                team_id,
                position,
                first_name,
                second_name,
                web_name,
                price_tenths,
                status,
                news,
                news_added_utc,
                chance_next_round,
                selected_by_percent,
                total_points,
                minutes,
                starts,
                photo_identifier,
                expected_points_next
            )
            SELECT
                1,
                player_id,
                1000 + player_id,
                1,
                'midfielder',
                'Extra',
                CAST(player_id AS TEXT),
                'Extra ' || player_id,
                50,
                'a',
                '',
                NULL,
                NULL,
                '0.0',
                0,
                0,
                0,
                (1000 + player_id) || '.png',
                '2.0'
            FROM extra;
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static byte[] CreateBootstrap() =>
        JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                events = new object[]
                {
                    new
                    {
                        id = 1,
                        name = "Gameweek 1",
                        deadline_time = "2026-08-01T12:00:00Z",
                        finished = false,
                        data_checked = false,
                        is_current = false,
                        is_next = true,
                    },
                },
                teams = new object[]
                {
                    new
                    {
                        id = 1,
                        code = 10,
                        name = "Home",
                        short_name = "HOM",
                    },
                    new
                    {
                        id = 2,
                        code = 20,
                        name = "Away",
                        short_name = "AWY",
                    },
                },
                elements = new object[]
                {
                    new
                    {
                        id = 101,
                        code = 1001,
                        team = 1,
                        element_type = 3,
                        first_name = "Test",
                        second_name = "Player",
                        web_name = "Player",
                        photo = "1001.png",
                        now_cost = 75,
                        status = "a",
                        news = string.Empty,
                        news_added = (string?)null,
                        chance_of_playing_next_round = (int?)null,
                        selected_by_percent = "12.5",
                        ep_next = "4.2",
                        total_points = 0,
                        minutes = 0,
                        starts = 0,
                    },
                    new
                    {
                        id = 102,
                        code = 1002,
                        team = 1,
                        element_type = 3,
                        first_name = "Amadou",
                        second_name = "Onana",
                        web_name = "Onana",
                        photo = "1002.png",
                        now_cost = 55,
                        status = "a",
                        news = string.Empty,
                        news_added = (string?)null,
                        chance_of_playing_next_round = (int?)null,
                        selected_by_percent = "1.0",
                        ep_next = "2.0",
                        total_points = 0,
                        minutes = 0,
                        starts = 0,
                    },
                    new
                    {
                        id = 103,
                        code = 1003,
                        team = 1,
                        element_type = 3,
                        first_name = "Fábio",
                        second_name = "Freitas Gouveia Carvalho",
                        web_name = "Carvalho",
                        photo = "1003.png",
                        now_cost = 50,
                        status = "a",
                        news = string.Empty,
                        news_added = (string?)null,
                        chance_of_playing_next_round = (int?)null,
                        selected_by_percent = "1.0",
                        ep_next = "2.0",
                        total_points = 0,
                        minutes = 0,
                        starts = 0,
                    },
                    new
                    {
                        id = 104,
                        code = 1004,
                        team = 1,
                        element_type = 3,
                        first_name = "Alice",
                        second_name = "Smith",
                        web_name = "Smith",
                        photo = "1004.png",
                        now_cost = 50,
                        status = "a",
                        news = string.Empty,
                        news_added = (string?)null,
                        chance_of_playing_next_round = (int?)null,
                        selected_by_percent = "1.0",
                        ep_next = "2.0",
                        total_points = 0,
                        minutes = 0,
                        starts = 0,
                    },
                    new
                    {
                        id = 105,
                        code = 1005,
                        team = 1,
                        element_type = 3,
                        first_name = "Bob",
                        second_name = "Smith",
                        web_name = "Smith",
                        photo = "1005.png",
                        now_cost = 50,
                        status = "a",
                        news = string.Empty,
                        news_added = (string?)null,
                        chance_of_playing_next_round = (int?)null,
                        selected_by_percent = "1.0",
                        ep_next = "2.0",
                        total_points = 0,
                        minutes = 0,
                        starts = 0,
                    },
                },
            });

    private static byte[] CreateFixtures() =>
        JsonSerializer.SerializeToUtf8Bytes(
            new object[]
            {
                new
                {
                    id = 1,
                    @event = 1,
                    team_h = 1,
                    team_a = 2,
                    kickoff_time = "2026-08-02T14:00:00Z",
                    started = false,
                    finished = false,
                    finished_provisional = false,
                    team_h_score = (int?)null,
                    team_a_score = (int?)null,
                },
            });

    private static DatabaseOptions CreateOptions(string databasePath)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AutoFpl:DatabasePath"] = databasePath,
                })
            .Build();
        return DatabaseOptions.FromConfiguration(configuration);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class SpiderMcpHandler(
        string content,
        string serverName = "rmcp",
        string? finalUrl = null) : HttpMessageHandler
    {
        public int ScrapeCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete)
            {
                DeleteCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            using JsonDocument body = JsonDocument.Parse(
                await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            JsonElement root = body.RootElement;
            string method = root.GetProperty("method").GetString()!;
            if (method == "initialize")
            {
                var response = Json(
                    new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        result = new
                        {
                            protocolVersion = "2025-03-26",
                            capabilities = new { tools = new { listChanged = false } },
                            serverInfo = new { name = serverName, version = "test" },
                        },
                    });
                response.Headers.TryAddWithoutValidation(
                    "Mcp-Session-Id",
                    "test-session");
                return response;
            }

            if (method == "notifications/initialized")
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            Assert.Equal("tools/call", method);
            JsonElement parameters = root.GetProperty("params");
            Assert.Equal(
                "spider_scrape",
                parameters.GetProperty("name").GetString());
            JsonElement arguments = parameters.GetProperty("arguments");
            Assert.Equal(
                "https://cdn.fantasyfootballscout.co.uk/team-news",
                arguments.GetProperty("url").GetString());
            Assert.False(arguments.GetProperty("headless").GetBoolean());
            ScrapeCalls++;
            string scrape = JsonSerializer.Serialize(
                new
                {
                    url = finalUrl
                        ?? "https://cdn.fantasyfootballscout.co.uk/team-news",
                    status_code = 200,
                    content,
                    links = Array.Empty<string>(),
                    content_trust = "untrusted_remote_content",
                });
            return Json(
                new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    result = new
                    {
                        content = new[]
                        {
                            new { type = "text", text = scrape },
                        },
                        isError = false,
                    },
                });
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(value),
                    Encoding.UTF8,
                    "application/json"),
            };
    }

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-research-source-tests-{Guid.NewGuid():N}");

        public TemporaryDatabaseFiles()
        {
            Directory.CreateDirectory(_directory);
            DatabasePath = Path.Combine(_directory, "autofpl.db");
        }

        public string DatabasePath { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
