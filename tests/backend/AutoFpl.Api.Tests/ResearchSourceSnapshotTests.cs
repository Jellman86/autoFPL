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
    public async Task Rendered_injury_capture_uses_playwright_and_rejects_incomplete_clubs()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var handler = new PlaywrightInjuryMcpHandler(
            CreatePremierLeagueInjuryEvidence(
                emptyFirstClub: true,
                missingUpdateUrl: true));
        using var playwrightHttpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://playwright-mcp:8931/mcp"),
        };
        using var spiderHttpClient = new HttpClient(
            new SpiderMcpHandler("unused"))
        {
            BaseAddress = new Uri("http://spider-mcp:8080/mcp"),
        };
        var importer = new ResearchSourceSnapshotImporter(
            new SpiderMcpClient(spiderHttpClient),
            new ResearchSourceSnapshotStore(
                options,
                new FixedTimeProvider(RetrievalTime)),
            new PremierLeagueInjuryPlaywrightCollector(
                new PlaywrightMcpFplFormCollector(playwrightHttpClient)));

        ResearchSourceSnapshotDocument snapshot = await importer.ImportAsync(
            "premier-league-injuries",
            TestContext.Current.CancellationToken);

        Assert.Equal("playwright-mcp", snapshot.TransportKey);
        Assert.Equal(
            PremierLeagueInjuryPlaywrightCollector.TransportVersion,
            snapshot.TransportVersion);
        Assert.True(snapshot.ContentBytes >= 1000);
        Assert.Contains(
            ".injury-news__article",
            handler.CollectionCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "articles.length === 20",
            handler.CollectionCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "renderedWidgetSha256",
            handler.CollectionCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "crypto.subtle.digest",
            handler.CollectionCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "import(\"node:crypto\")",
            handler.CollectionCode,
            StringComparison.Ordinal);

        var incompleteHandler = new PlaywrightInjuryMcpHandler(
            CreatePremierLeagueInjuryEvidence(clubCount: 19));
        using var incompleteClient = new HttpClient(incompleteHandler)
        {
            BaseAddress = new Uri("http://playwright-mcp:8931/mcp"),
        };
        await Assert.ThrowsAsync<ResearchSourceSnapshotException>(
            async () => await new ResearchSourceSnapshotImporter(
                    new SpiderMcpClient(spiderHttpClient),
                    new ResearchSourceSnapshotStore(
                        options,
                        new FixedTimeProvider(RetrievalTime.AddMinutes(1))),
                    new PremierLeagueInjuryPlaywrightCollector(
                        new PlaywrightMcpFplFormCollector(incompleteClient)))
                .ImportAsync(
                    "premier-league-injuries",
                    TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Byparr_capture_uses_only_the_registered_url_and_retains_transport_provenance()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        ResearchSourceDefinition source = ResearchSourceRegistry.Get(
            "fbref-championship-playing-time-2025-26");
        var byparrHandler = new ByparrHandler(
            source,
            """
            <html><head><title>Championship Playing Time | FBref.com</title></head>
            <body>retained prior-season evidence</body></html>
            """);
        using var byparrHttpClient = new HttpClient(byparrHandler)
        {
            BaseAddress = new Uri("http://192.168.213.101:8191/"),
        };
        using var spiderHttpClient = new HttpClient(
            new SpiderMcpHandler("unused"))
        {
            BaseAddress = new Uri("http://spider-mcp:8080/mcp"),
        };
        var importer = new ResearchSourceSnapshotImporter(
            new SpiderMcpClient(spiderHttpClient),
            new ByparrClient(byparrHttpClient),
            new ResearchSourceSnapshotStore(
                options,
                new FixedTimeProvider(RetrievalTime)));

        ResearchSourceSnapshotDocument snapshot = await importer.ImportAsync(
            source.SourceKey,
            TestContext.Current.CancellationToken);

        Assert.False(source.PollAutomatically);
        Assert.Equal("byparr", snapshot.TransportKey);
        Assert.Equal("byparr/2.1.0", snapshot.TransportVersion);
        Assert.Equal(source.CanonicalUri.AbsoluteUri, snapshot.CanonicalUrl);
        Assert.Equal(
            "https://fbref.com/en/comps/10/playingtime/Championship-Stats",
            snapshot.FinalUrl);
        Assert.Equal(1, byparrHandler.RequestCount);
        Assert.False(byparrHandler.SawProxyOverrideHeader);
    }

    [Theory]
    [InlineData(
        "https://example.com/copied-page",
        "<title>Championship Playing Time | FBref.com</title>")]
    [InlineData(
        "https://fbref.com/en/comps/10/playingtime/Championship-Stats",
        "<title>Just a moment...</title>")]
    public async Task Byparr_capture_rejects_unregistered_redirects_and_challenge_pages(
        string finalUrl,
        string content)
    {
        ResearchSourceDefinition source = ResearchSourceRegistry.Get(
            "fbref-championship-playing-time-2025-26");
        using var httpClient = new HttpClient(
            new ByparrHandler(source, content, finalUrl))
        {
            BaseAddress = new Uri("http://192.168.213.101:8191/"),
        };

        await Assert.ThrowsAsync<ResearchSourceSnapshotException>(
            async () => await new ByparrClient(httpClient).CaptureAsync(
                source,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Fbref_playing_time_sources_cover_all_registered_opening_folds()
    {
        FbrefPlayingTimeSource[] sources =
            FbrefPlayingTimeSources.All.ToArray();

        Assert.Equal(
            ["2021-22", "2022-23", "2023-24", "2024-25", "2025-26"],
            sources.Select(source => source.CompetitionSeason));
        Assert.Equal(
            5,
            sources.Select(source => source.SourceKey).Distinct().Count());
        Assert.Single(sources, source => source.IsCurrentTargetSource);
        Assert.All(
            sources,
            source =>
            {
                Assert.Equal(
                    ByparrClient.TransportKey,
                    source.Definition.TransportKey);
                Assert.False(source.Definition.PollAutomatically);
                Assert.Equal(
                    "prior-competition-playing-time",
                    source.Definition.SourceClass);
                Assert.Contains(
                    $"/{source.FbrefSeason}/playingtime/",
                    source.Definition.CanonicalUri.AbsolutePath,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Historical_fbref_playing_time_capture_extracts_population_without_current_identity()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        FbrefPlayingTimeSource historical =
            FbrefPlayingTimeSources.Get(
                "fbref-championship-playing-time-2022-23");
        string content =
            "<html><head><title>2022-2023 Championship Playing Time "
            + "| FBref.com</title></head><body>"
            + CreateFbrefPlayingTimeHtml(fbrefSeason: historical.FbrefSeason)
            + "</body></html>";
        var handler = new ByparrHandler(
            historical.Definition,
            content,
            historical.Definition.CanonicalUri.AbsoluteUri);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://192.168.213.101:8191/"),
        };
        using var spiderHttpClient = new HttpClient(
            new SpiderMcpHandler("unused"))
        {
            BaseAddress = new Uri("http://spider-mcp:8080/mcp"),
        };
        var store = new ResearchSourceSnapshotStore(
            options,
            new FixedTimeProvider(RetrievalTime));
        var importer = new ResearchSourceSnapshotImporter(
            new SpiderMcpClient(spiderHttpClient),
            new ByparrClient(httpClient),
            store);

        ResearchSourceSnapshotDocument snapshot =
            await importer.ImportAsync(
                historical.SourceKey,
                TestContext.Current.CancellationToken);
        FbrefPlayingTimeDocument? extraction =
            await new FbrefPlayingTimeExtractor(store).GetAsync(
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken);

        Assert.NotNull(extraction);
        Assert.Equal("2022-23", extraction.CompetitionSeason);
        Assert.Equal(500, extraction.RowCount);
        Assert.Null(extraction.IdentityBridgeVersion);
        Assert.Equal(0, extraction.ExactCurrentTeamMatchCount);
        Assert.Equal(0, extraction.ReviewedIdentityCount);
        Assert.Equal(0, extraction.ExactCurrentTeamProposalCount);
        Assert.Empty(extraction.CurrentTeamCoverage);
        Assert.All(
            extraction.Players,
            player => Assert.Equal("not-in-scope", player.IdentityStatus));
        Assert.All(
            extraction.Players,
            player => Assert.Contains(
                "/matchlogs/2022-2023/summary/",
                player.MatchLogsUrl,
                StringComparison.Ordinal));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public void Fbref_playing_time_parser_is_bounded_and_identity_bridge_is_exact()
    {
        string content = CreateFbrefPlayingTimeHtml(
            duplicateCommentedTable: true);

        IReadOnlyList<FbrefPlayingTimeExtractor.FbrefPlayingTimeRow> rows =
            FbrefPlayingTimeExtractor.Parse(content);
        var snapshot = new ResearchSourceSnapshotDocument(
            27,
            "1.0",
            "shadow-only",
            FbrefPlayingTimeExtractor.SourceKey,
            "prior-competition-playing-time",
            "https://fbref.com/source",
            "https://fbref.com/final",
            "sports-reference-fbref",
            "byparr",
            "byparr/2.1.0",
            "2026-27",
            1,
            new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            13,
            RetrievalTime,
            RetrievalTime,
            true,
            1,
            new string('a', 64),
            content.Length,
            RetrievalTime);
        ResearchOfficialPlayerIdentity[] identities =
        [
            new(101, 1001, "Coventry City", "Test", "Player", "Player"),
            new(102, 1002, "Coventry City", "Missing", "Current", "Current"),
            new(201, 2001, "Hull City", "Hull", "Match", "Match"),
            new(301, 3001, "Ipswich Town", "Ipswich", "Match", "Match"),
        ];

        FbrefPlayingTimeDocument extraction =
            FbrefPlayingTimeExtractor.BuildDocument(
                snapshot,
                rows,
                identities);

        Assert.Equal(500, extraction.RowCount);
        Assert.Equal(500, extraction.SourcePlayerCount);
        Assert.Equal(3, extraction.ExactCurrentTeamMatchCount);
        Assert.Null(extraction.IdentityBridgeVersion);
        Assert.Equal(0, extraction.ReviewedIdentityCount);
        Assert.Equal(3, extraction.ExactCurrentTeamProposalCount);
        Assert.Equal(
            FbrefPlayingTimeExtractor.ExtractionVersion,
            extraction.ExtractionVersion);
        FbrefPlayingTimePlayerDocument player = Assert.Single(
            extraction.Players,
            candidate => candidate.OfficialPlayerCode == 1001);
        Assert.Equal("00000001", player.SourcePlayerId);
        Assert.Equal(46, player.Appearances);
        Assert.Equal(40, player.Starts);
        Assert.Equal(3600, player.Minutes);
        Assert.Equal("exact-current-team-proposal", player.IdentityStatus);
        FbrefPlayingTimeTeamCoverageDocument coventry = Assert.Single(
            extraction.CurrentTeamCoverage,
            team => team.TeamName == "Coventry City");
        Assert.Equal(2, coventry.OfficialPlayerCount);
        Assert.Equal(15, coventry.SourceRowCount);
        Assert.Equal(1, coventry.ExactMatchCount);
        Assert.Equal(0, coventry.ReviewedMatchCount);
        Assert.Equal(1, coventry.ExactProposalCount);
        Assert.Equal([1002], coventry.UnmatchedOfficialPlayerCodes);
        Assert.Equal(14, coventry.UnmatchedSourcePlayerIds.Count);
    }

    [Fact]
    public void Fbref_reviewed_bridge_is_snapshot_bound_unique_and_complete()
    {
        Assert.Equal(60, FbrefPlayerIdentityBridge.All.Count);
        Assert.Equal(
            22,
            FbrefPlayerIdentityBridge.All.Count(
                entry => entry.TeamName == "Coventry City"));
        Assert.Equal(
            18,
            FbrefPlayerIdentityBridge.All.Count(
                entry => entry.TeamName == "Hull City"));
        Assert.Equal(
            20,
            FbrefPlayerIdentityBridge.All.Count(
                entry => entry.TeamName == "Ipswich Town"));
        Assert.All(
            FbrefPlayerIdentityBridge.All,
            entry =>
            {
                Assert.Matches("^[0-9a-f]{8}$", entry.SourcePlayerId);
                Assert.Matches("^[0-9a-f]{8}$", entry.SourceTeamId);
                Assert.True(entry.OfficialPlayerCode > 0);
            });

        var snapshot = new ResearchSourceSnapshotDocument(
            FbrefPlayerIdentityBridge.ReviewedSnapshotId,
            "1.0",
            "shadow-only",
            FbrefPlayingTimeExtractor.SourceKey,
            "prior-competition-playing-time",
            "https://fbref.com/source",
            "https://fbref.com/final",
            "sports-reference-fbref",
            "byparr",
            "byparr/2.1.0",
            "2026-27",
            1,
            new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            13,
            RetrievalTime,
            RetrievalTime,
            true,
            1,
            FbrefPlayerIdentityBridge.ReviewedContentSha256,
            4_280_848,
            RetrievalTime);
        FbrefPlayingTimeExtractor.FbrefPlayingTimeRow[] rows =
            FbrefPlayerIdentityBridge.All
                .Select(
                    entry => new FbrefPlayingTimeExtractor.FbrefPlayingTimeRow(
                        entry.SourcePlayerId,
                        entry.SourcePlayerName,
                        entry.SourceTeamId,
                        entry.TeamName,
                        20,
                        10,
                        900,
                        $"https://fbref.com/en/players/{entry.SourcePlayerId}"
                            + "/matchlogs/2025-2026/summary/Player-Match-Logs"))
                .ToArray();
        ResearchOfficialPlayerIdentity[] identities =
            FbrefPlayerIdentityBridge.All
                .Select(
                    (entry, index) =>
                    {
                        int separator = entry.SourcePlayerName.IndexOf(' ');
                        return new ResearchOfficialPlayerIdentity(
                            index + 1,
                            entry.OfficialPlayerCode,
                            entry.TeamName,
                            entry.SourcePlayerName[..separator],
                            entry.SourcePlayerName[(separator + 1)..],
                            entry.SourcePlayerName[(separator + 1)..]);
                    })
                .ToArray();

        FbrefPlayingTimeDocument extraction =
            FbrefPlayingTimeExtractor.BuildDocument(
                snapshot,
                rows,
                identities);

        Assert.Equal(
            FbrefPlayerIdentityBridge.Version,
            extraction.IdentityBridgeVersion);
        Assert.Equal(60, extraction.ReviewedIdentityCount);
        Assert.Equal(0, extraction.ExactCurrentTeamProposalCount);
        Assert.All(
            extraction.Players,
            player => Assert.Equal("reviewed-v1", player.IdentityStatus));
    }

    [Fact]
    public void Fbref_team_schedule_sources_are_fixed_and_manual()
    {
        ResearchSourceDefinition[] sources = ResearchSourceRegistry.All
            .Where(source => StringComparer.Ordinal.Equals(
                source.SourceClass,
                "prior-competition-team-schedule"))
            .ToArray();

        Assert.Equal(3, sources.Length);
        Assert.Equal(
            [
                "fbref-team-schedule-f7e3dfe9-2025-26",
                "fbref-team-schedule-bd8769d1-2025-26",
                "fbref-team-schedule-b74092de-2025-26",
            ],
            sources.Select(source => source.SourceKey));
        Assert.All(
            sources,
            source =>
            {
                Assert.Equal("fbref.com", source.CanonicalUri.Host);
                Assert.Contains(
                    "/2025-2026/matchlogs/c10/schedule/",
                    source.CanonicalUri.AbsolutePath,
                    StringComparison.Ordinal);
                Assert.Equal(ByparrClient.TransportKey, source.TransportKey);
                Assert.False(source.PollAutomatically);
            });
    }

    [Fact]
    public async Task Fbref_team_schedule_capture_persists_and_extracts()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        ResearchSourceDefinition source = ResearchSourceRegistry.Get(
            "fbref-team-schedule-f7e3dfe9-2025-26");
        string content = CreateFbrefTeamScheduleHtml();
        var handler = new ByparrHandler(
            source,
            content,
            source.CanonicalUri.AbsoluteUri);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://192.168.213.101:8191/"),
        };
        var store = new ResearchSourceSnapshotStore(
            options,
            new FixedTimeProvider(RetrievalTime));

        ByparrCaptureResult capture = await new ByparrClient(httpClient)
            .CaptureAsync(source, TestContext.Current.CancellationToken);
        ResearchSourceSnapshotDocument snapshot = await store.PersistAsync(
            source,
            capture,
            TestContext.Current.CancellationToken);
        FbrefTeamScheduleDocument? extraction =
            await new FbrefTeamScheduleExtractor(store).GetAsync(
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken);

        Assert.Equal(source.SourceKey, snapshot.SourceKey);
        Assert.Equal(
            "prior-competition-team-schedule",
            snapshot.SourceClass);
        Assert.NotNull(extraction);
        Assert.Equal("f7e3dfe9", extraction.SourceTeamId);
        Assert.Equal("Coventry City", extraction.TeamName);
        Assert.Equal(46, extraction.MatchCount);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public void Fbref_team_schedule_parser_retains_exact_match_opportunities()
    {
        string content = CreateFbrefTeamScheduleHtml();

        IReadOnlyList<FbrefTeamScheduleRowDocument> matches =
            FbrefTeamScheduleExtractor.Parse(content);

        Assert.Equal(46, matches.Count);
        Assert.Equal("00000001", matches[0].SourceMatchId);
        Assert.Equal(new DateOnly(2025, 8, 1), matches[0].MatchDate);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1_754_046_000),
            matches[0].KickoffUtc);
        Assert.Equal("matchweek 1", matches[0].Round.ToLowerInvariant());
        Assert.Equal("home", matches[0].Venue);
        Assert.Equal("W", matches[0].Result);
        Assert.Equal(2, matches[0].GoalsFor);
        Assert.Equal(1, matches[0].GoalsAgainst);
        Assert.Equal("aaaaaaaa", matches[0].SourceOpponentId);
        Assert.Equal("Test Opponent", matches[0].OpponentName);
    }

    [Fact]
    public void Fbref_team_schedule_parser_rejects_duplicate_matches()
    {
        string content = CreateFbrefTeamScheduleHtml(
            duplicateFinalMatch: true);

        Assert.Throws<ResearchSourceSnapshotException>(
            () => FbrefTeamScheduleExtractor.Parse(content));
    }

    [Fact]
    public void Fbref_player_match_opportunities_preserve_missing_rows_and_windows()
    {
        FbrefTeamScheduleDocument schedule =
            CreateFbrefTeamScheduleDocument();
        FbrefPlayerMatchLogDocument player =
            CreateFbrefPlayerMatchLogDocument(schedule);

        FbrefPlayerMatchOpportunityDocument artifact =
            FbrefPlayerMatchOpportunityExtractor.Build(player, schedule);

        Assert.Equal(
            FbrefPlayerMatchOpportunityExtractor.ExtractionVersion,
            artifact.ExtractionVersion);
        Assert.Equal(8, artifact.ScheduledMatchCount);
        Assert.Equal(3, artifact.ObservedPlayerRowCount);
        Assert.Equal(1, artifact.ExcludedOtherCompetitionRowCount);
        Assert.Equal(5, artifact.NoPlayerRowCount);
        Assert.Equal(
            "before-first-observed",
            artifact.Opportunities[0].ObservedRangeStatus);
        Assert.Equal(
            "within-observed-range",
            artifact.Opportunities[2].ObservedRangeStatus);
        Assert.Equal(
            "after-last-observed",
            artifact.Opportunities[6].ObservedRangeStatus);
        Assert.Equal(
            "no-player-row",
            artifact.Opportunities[2].PlayerEvidenceStatus);
        Assert.Null(artifact.Opportunities[2].Minutes);
        Assert.Equal(
            "unused-bench",
            artifact.Opportunities[3].PlayerEvidenceStatus);
        Assert.Equal(0, artifact.Opportunities[3].Minutes);
        FbrefPlayerMatchOpportunityWindowDocument lastThree =
            Assert.Single(
                artifact.RollingWindows,
                window => window.WindowSize == 3);
        Assert.Equal(
            "complete-with-missing-player-rows",
            lastThree.WindowStatus);
        Assert.Equal(3, lastThree.ScheduledMatchCount);
        Assert.Equal(1, lastThree.ObservedPlayerRowCount);
        Assert.Equal(1, lastThree.AppearanceCount);
        Assert.Equal(0, lastThree.StartCount);
        Assert.Equal(0, lastThree.UnusedBenchCount);
        Assert.Equal(2, lastThree.NoPlayerRowCount);
        Assert.Equal(30, lastThree.ObservedMinutes);
    }

    [Fact]
    public void Fbref_match_opportunity_features_preserve_counts_and_missingness()
    {
        FbrefTeamScheduleDocument schedule =
            CreateFbrefTeamScheduleDocument();
        FbrefPlayerMatchOpportunityDocument opportunity =
            FbrefPlayerMatchOpportunityExtractor.Build(
                CreateFbrefPlayerMatchLogDocument(schedule),
                schedule);
        var targetIdentity = new ResearchOfficialPlayerIdentity(
            999,
            opportunity.OfficialPlayerCode,
            "Hull City",
            "Semi",
            "Ajayi",
            "Ajayi");
        ResearchSourceSnapshotDocument playerSnapshot =
            CreateResearchSnapshot(
                opportunity.PlayerMatchLogSnapshotId,
                "fbref-player-match-log-d6192210-2025-26",
                opportunity.PlayerMatchLogContentSha256,
                RetrievalTime);
        ResearchSourceSnapshotDocument scheduleSnapshot =
            CreateResearchSnapshot(
                opportunity.TeamScheduleSnapshotId,
                schedule.SourceKey,
                opportunity.TeamScheduleContentSha256,
                RetrievalTime.AddMinutes(1));

        FbrefMatchOpportunityPlayerFeatureDocument feature =
            FbrefMatchOpportunityFeatureTableReader.CreateReady(
                opportunity,
                targetIdentity,
                playerSnapshot,
                scheduleSnapshot);

        Assert.Equal("shadow-feature-ready", feature.FeatureStatus);
        Assert.Equal(999, feature.OfficialPlayerId);
        Assert.Equal(
            scheduleSnapshot.AvailableAtUtc,
            feature.AvailableAtUtc);
        Assert.NotNull(feature.Season);
        Assert.Equal(8, feature.Season.ScheduledMatchCount);
        Assert.Equal(3, feature.Season.ObservedPlayerRowCount);
        Assert.Equal(2, feature.Season.AppearanceCount);
        Assert.Equal(1, feature.Season.StartCount);
        Assert.Equal(1, feature.Season.UnusedBenchCount);
        Assert.Equal(5, feature.Season.NoPlayerRowCount);
        Assert.Equal(120, feature.Season.ObservedMinutes);
        FbrefMatchOpportunityFeatureWindowDocument lastThree =
            Assert.Single(
                feature.RollingWindows,
                window => window.WindowSize == 3);
        Assert.Equal(1, lastThree.ObservedPlayerRowCount);
        Assert.Equal(1, lastThree.AppearanceCount);
        Assert.Equal(2, lastThree.NoPlayerRowCount);
        Assert.Equal(30, lastThree.ObservedMinutes);

        FbrefMatchOpportunityPlayerFeatureDocument missing =
            FbrefMatchOpportunityFeatureTableReader.CreateMissing(
                new(
                    opportunity.SourcePlayerId,
                    opportunity.PlayerName,
                    opportunity.SourceTeamId,
                    opportunity.TeamName,
                    opportunity.OfficialPlayerId,
                    opportunity.OfficialPlayerCode,
                    playerSnapshot.SourceKey,
                    "missing",
                    null,
                    null,
                    null,
                    null),
                targetIdentity,
                null,
                scheduleSnapshot);
        Assert.Equal("missing-player-log", missing.FeatureStatus);
        Assert.Null(missing.Season);
        Assert.Empty(missing.RollingWindows);

        FbrefMatchOpportunityPlayerFeatureDocument rejected =
            FbrefMatchOpportunityFeatureTableReader.CreateRejected(
                new(
                    opportunity.SourcePlayerId,
                    opportunity.PlayerName,
                    opportunity.SourceTeamId,
                    opportunity.TeamName,
                    opportunity.OfficialPlayerId,
                    opportunity.OfficialPlayerCode,
                    playerSnapshot.SourceKey,
                    "captured",
                    playerSnapshot.SnapshotId,
                    1,
                    playerSnapshot.RetrievedAtUtc,
                    playerSnapshot.ContentSha256),
                targetIdentity,
                playerSnapshot,
                scheduleSnapshot);
        Assert.Equal(
            "rejected-incompatible-source-pair",
            rejected.FeatureStatus);
        Assert.Equal(
            playerSnapshot.SnapshotId,
            rejected.PlayerMatchLogSnapshotId);
        Assert.Null(rejected.Season);
        Assert.Empty(rejected.RollingWindows);
    }

    [Fact]
    public void Fbref_player_match_opportunities_reject_inconsistent_stable_match()
    {
        FbrefTeamScheduleDocument schedule =
            CreateFbrefTeamScheduleDocument();
        FbrefPlayerMatchLogDocument player =
            CreateFbrefPlayerMatchLogDocument(schedule);
        FbrefPlayerMatchLogRowDocument changed =
            player.Matches[0] with { SourceOpponentId = "bbbbbbbb" };
        player = player with
        {
            Matches = [changed, .. player.Matches.Skip(1)],
        };

        Assert.Throws<ResearchSourceSnapshotException>(
            () => FbrefPlayerMatchOpportunityExtractor.Build(
                player,
                schedule));
    }

    [Fact]
    public void Fbref_match_opportunity_coverage_reports_source_pair_readiness()
    {
        FbrefMatchLogPlayerCoverageDocument[] players =
            FbrefPlayerIdentityBridge.All
                .Select(
                    (entry, index) =>
                    {
                        long? snapshotId = index < 15 ? 50 + index : null;
                        return new FbrefMatchLogPlayerCoverageDocument(
                            entry.SourcePlayerId,
                            entry.SourcePlayerName,
                            entry.SourceTeamId,
                            entry.TeamName,
                            index + 1,
                            entry.OfficialPlayerCode,
                            $"{FbrefPlayerMatchLogImporter.SourceKeyPrefix}"
                                + $"{entry.SourcePlayerId}"
                                + $"{FbrefPlayerMatchLogImporter.SourceKeySuffix}",
                            snapshotId is null ? "missing" : "captured",
                            snapshotId,
                            snapshotId is null ? null : 1,
                            snapshotId is null ? null : RetrievalTime,
                            snapshotId is null
                                ? null
                                : new string('a', 64));
                    })
                .ToArray();
        var matchLogs = new FbrefMatchLogCoverageDocument(
            "1.0",
            FbrefPlayerIdentityBridge.Version,
            FbrefPlayerIdentityBridge.ReviewedSnapshotId,
            players.Length,
            15,
            45,
            [],
            players);
        ResearchSourceSnapshotDocument[] schedules =
            FbrefTeamScheduleSources.All
                .Select(
                    (source, index) => CreateScheduleSnapshot(
                        source,
                        47 + index))
                .ToArray();
        var inventory = new ResearchSourceInventoryDocument(
            "1.0",
            [],
            schedules,
            []);

        FbrefMatchOpportunityCoverageDocument coverage =
            FbrefMatchOpportunityCoverageReader.Build(
                matchLogs,
                inventory);

        Assert.Equal(
            "blocked-incomplete-player-logs",
            coverage.ReadinessStatus);
        Assert.Equal(60, coverage.ReviewedPlayerCount);
        Assert.Equal(15, coverage.CapturedPlayerLogCount);
        Assert.Equal(45, coverage.MissingPlayerLogCount);
        Assert.Equal(3, coverage.CapturedTeamScheduleCount);
        Assert.Equal(0, coverage.MissingTeamScheduleCount);
        Assert.Equal(15, coverage.SourcePairReadyPlayerCount);
        Assert.Equal(3, coverage.Teams.Count);
        Assert.All(
            coverage.Teams,
            team => Assert.Equal("captured", team.ScheduleCaptureStatus));
        Assert.Equal(
            15,
            coverage.Players.Count(player =>
                player.SourcePairStatus == "source-pair-ready"));

        FbrefMatchOpportunityCoverageDocument missingSchedule =
            FbrefMatchOpportunityCoverageReader.Build(
                matchLogs,
                inventory with
                {
                    LatestSnapshots = schedules[1..],
                });
        Assert.Equal(
            "blocked-incomplete-team-schedules",
            missingSchedule.ReadinessStatus);
        Assert.Equal(2, missingSchedule.CapturedTeamScheduleCount);
        Assert.Equal(1, missingSchedule.MissingTeamScheduleCount);
    }

    [Fact]
    public async Task Fbref_match_log_capture_is_reviewed_allowlisted_and_immutable()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var player = new FbrefPlayingTimePlayerDocument(
            "d6192210",
            "Semi Ajayi",
            "bd8769d1",
            "Hull City",
            14,
            9,
            843,
            "https://fbref.com/en/players/d6192210/matchlogs/2025-2026/summary/Semi-Ajayi-Match-Logs",
            "reviewed-v1",
            101,
            146426);
        ResearchSourceDefinition source =
            FbrefPlayerMatchLogImporter.CreateSourceDefinition(player);
        const string finalUrl =
            "https://fbref.com/en/players/d6192210/matchlogs/2025-2026/Semi-Ajayi-Match-Logs";
        const string content =
            """
            <html><head><title>2025-2026 Semi Ajayi Match Logs | FBref.com</title></head>
            <body><table id="matchlogs_all"><tbody></tbody></table></body></html>
            """;
        var handler = new ByparrHandler(source, content, finalUrl);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://192.168.213.101:8191/"),
        };
        var store = new ResearchSourceSnapshotStore(
            options,
            new FixedTimeProvider(RetrievalTime));

        ByparrCaptureResult capture = await new ByparrClient(httpClient)
            .CaptureAsync(source, TestContext.Current.CancellationToken);
        ResearchSourceSnapshotDocument snapshot = await store.PersistAsync(
            source,
            capture,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "fbref-player-match-log-d6192210-2025-26",
            snapshot.SourceKey);
        Assert.True(
            FbrefPlayerMatchLogImporter.IsMatchLogSourceKey(
                snapshot.SourceKey));
        Assert.Equal("prior-competition-player-match-log", snapshot.SourceClass);
        Assert.Equal("byparr/2.1.0", snapshot.TransportVersion);
        Assert.Equal(player.MatchLogsUrl, snapshot.CanonicalUrl);
        Assert.Equal(finalUrl, snapshot.FinalUrl);
        Assert.False(source.PollAutomatically);
        Assert.Equal(1, handler.RequestCount);

        await using var connection =
            new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand mutate = connection.CreateCommand();
        mutate.CommandText =
            "UPDATE research_source_snapshots SET source_revision = 2 WHERE snapshot_id = $snapshotId;";
        mutate.Parameters.AddWithValue("$snapshotId", snapshot.SnapshotId);
        SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
            async () => await mutate.ExecuteNonQueryAsync(
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "research source snapshots are immutable",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fbref-player-match-log-d6192210-2025-26", true)]
    [InlineData("fbref-player-match-log-D6192210-2025-26", false)]
    [InlineData("fbref-player-match-log-d6192210-2026-27", false)]
    [InlineData("fbref-player-match-log-d61922100-2025-26", false)]
    [InlineData("https://fbref.com/arbitrary", false)]
    public void Fbref_match_log_source_key_is_exact(
        string sourceKey,
        bool expected)
    {
        Assert.Equal(
            expected,
            FbrefPlayerMatchLogImporter.IsMatchLogSourceKey(sourceKey));
    }

    [Fact]
    public void Fbref_match_log_source_rejects_unreviewed_or_changed_urls()
    {
        var unreviewed = new FbrefPlayingTimePlayerDocument(
            "d6192210",
            "Semi Ajayi",
            "bd8769d1",
            "Hull City",
            14,
            9,
            843,
            "https://fbref.com/en/players/d6192210/matchlogs/2025-2026/summary/Semi-Ajayi-Match-Logs",
            "exact-current-team-proposal",
            101,
            146426);
        var changedUrl = unreviewed with
        {
            IdentityStatus = "reviewed-v1",
            MatchLogsUrl =
                "https://example.com/en/players/d6192210/matchlogs/2025-2026/summary/Semi-Ajayi-Match-Logs",
        };

        Assert.Throws<ResearchSourceSnapshotException>(
            () => FbrefPlayerMatchLogImporter.CreateSourceDefinition(unreviewed));
        Assert.Throws<ResearchSourceSnapshotException>(
            () => FbrefPlayerMatchLogImporter.CreateSourceDefinition(changedUrl));
    }

    [Fact]
    public void Fbref_match_log_parser_retains_dated_appearances_and_bench_rows()
    {
        const string content =
            """
            <html><body>
            <table id="matchlogs_all"><tbody>
            <tr>
              <th data-stat="date">2025-08-09</th>
              <td data-stat="comp">Championship</td>
              <td data-stat="round">Regular season</td>
              <td data-stat="venue">Away</td>
              <td data-stat="result">D 0–0</td>
              <td data-stat="team"><a href="/en/squads/aaaaaaaa/Hull-City">Hull City</a></td>
              <td data-stat="opponent"><a href="/en/squads/bbbbbbbb/Coventry-City">Coventry City</a></td>
              <td data-stat="game_started">Y</td>
              <td data-stat="minutes">90</td>
              <td data-stat="goals">1</td>
              <td data-stat="assists">0</td>
              <td data-stat="cards_yellow">1</td>
              <td data-stat="cards_red">0</td>
              <td data-stat="match_report"><a href="/en/matches/11111111/Report">Match Report</a></td>
            </tr>
            <tr>
              <th data-stat="date"></th>
            </tr>
            <tr class="thead">
              <th data-stat="date">Date</th>
              <td data-stat="comp">Comp</td>
              <td data-stat="round">Round</td>
              <td data-stat="venue">Venue</td>
              <td data-stat="result">Result</td>
              <td data-stat="team">Squad</td>
              <td data-stat="opponent">Opponent</td>
              <td data-stat="game_started">Start</td>
              <td data-stat="minutes">Min</td>
              <td data-stat="match_report">Match Report</td>
            </tr>
            <tr>
              <th data-stat="date">2025-08-16</th>
              <td data-stat="comp">Championship</td>
              <td data-stat="round">Regular season</td>
              <td data-stat="venue">Home</td>
              <td data-stat="result">W 1–0</td>
              <td data-stat="team"><a href="/en/squads/aaaaaaaa/Hull-City">Hull City</a></td>
              <td data-stat="opponent"><a href="/en/squads/cccccccc/Ipswich-Town">Ipswich Town</a></td>
              <td data-stat="game_started">N</td>
              <td data-stat="bench_explain">On matchday squad, but did not play</td>
              <td data-stat="match_report"><a href="/en/matches/22222222/Report">Match Report</a></td>
            </tr>
            </tbody></table>
            </body></html>
            """;

        IReadOnlyList<FbrefPlayerMatchLogRowDocument> rows =
            FbrefPlayerMatchLogExtractor.Parse(content);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2025, 8, 9), rows[0].MatchDate);
        Assert.Equal("11111111", rows[0].SourceMatchId);
        Assert.True(rows[0].Started);
        Assert.Equal(90, rows[0].Minutes);
        Assert.Equal(1, rows[0].Goals);
        Assert.Equal("away", rows[0].Venue);
        Assert.Equal("bbbbbbbb", rows[0].SourceOpponentId);
        Assert.False(rows[1].Started);
        Assert.Equal(0, rows[1].Minutes);
        Assert.Equal(0, rows[1].Goals);
        Assert.Equal("home", rows[1].Venue);
    }

    [Fact]
    public void Fbref_match_log_parser_rejects_duplicate_matches()
    {
        const string row =
            """
            <tr>
              <th data-stat="date">2025-08-09</th>
              <td data-stat="comp">Championship</td>
              <td data-stat="round">Regular season</td>
              <td data-stat="venue">Away</td>
              <td data-stat="result">D 0–0</td>
              <td data-stat="team"><a href="/en/squads/aaaaaaaa/Hull-City">Hull City</a></td>
              <td data-stat="opponent"><a href="/en/squads/bbbbbbbb/Coventry-City">Coventry City</a></td>
              <td data-stat="game_started">Y</td>
              <td data-stat="minutes">90</td>
              <td data-stat="match_report"><a href="/en/matches/11111111/Report">Match Report</a></td>
            </tr>
            """;
        string content =
            $"<table id=\"matchlogs_all\"><tbody>{row}{row}</tbody></table>";

        Assert.Throws<ResearchSourceSnapshotException>(
            () => FbrefPlayerMatchLogExtractor.Parse(content));
    }

    [Fact]
    public void Fbref_match_log_coverage_reports_all_reviewed_identities()
    {
        FbrefPlayingTimePlayerDocument[] players =
            FbrefPlayerIdentityBridge.All
                .Select(
                    (entry, index) => new FbrefPlayingTimePlayerDocument(
                        entry.SourcePlayerId,
                        entry.SourcePlayerName,
                        entry.SourceTeamId,
                        entry.TeamName,
                        20,
                        10,
                        900,
                        $"https://fbref.com/en/players/{entry.SourcePlayerId}"
                            + "/matchlogs/2025-2026/summary/Player-Match-Logs",
                        "reviewed-v1",
                        index + 1,
                        entry.OfficialPlayerCode))
                .ToArray();
        var playingTime = new FbrefPlayingTimeDocument(
            "1.0",
            FbrefPlayerIdentityBridge.ReviewedSnapshotId,
            FbrefPlayingTimeExtractor.SourceKey,
            FbrefPlayingTimeExtractor.ExtractionVersion,
            FbrefPlayerIdentityBridge.Version,
            FbrefPlayerIdentityBridge.ReviewedContentSha256,
            13,
            RetrievalTime,
            "Championship",
            "2025-26",
            players.Length,
            players.Length,
            players.Length,
            players.Length,
            0,
            players,
            []);
        FbrefPlayingTimePlayerDocument first = players[0];
        var snapshot = new ResearchSourceSnapshotDocument(
            31,
            "1.0",
            "shadow-only",
            $"{FbrefPlayerMatchLogImporter.SourceKeyPrefix}"
                + $"{first.SourcePlayerId}"
                + $"{FbrefPlayerMatchLogImporter.SourceKeySuffix}",
            "prior-competition-player-match-log",
            first.MatchLogsUrl,
            first.MatchLogsUrl.Replace(
                "/summary/",
                "/",
                StringComparison.Ordinal),
            "sports-reference-fbref",
            "byparr",
            "byparr/2.1.0",
            "2026-27",
            1,
            new DateTimeOffset(2026, 8, 21, 17, 30, 0, TimeSpan.Zero),
            13,
            RetrievalTime,
            RetrievalTime,
            true,
            1,
            new string('a', 64),
            100,
            RetrievalTime);
        var inventory = new ResearchSourceInventoryDocument(
            "1.0",
            [],
            [snapshot],
            []);

        FbrefMatchLogCoverageDocument coverage =
            FbrefMatchLogCoverageReader.Build(playingTime, inventory);

        Assert.Equal(60, coverage.ReviewedPlayerCount);
        Assert.Equal(1, coverage.CapturedPlayerCount);
        Assert.Equal(59, coverage.MissingPlayerCount);
        Assert.Equal(3, coverage.Teams.Count);
        FbrefMatchLogPlayerCoverageDocument captured = Assert.Single(
            coverage.Players,
            player => player.CaptureStatus == "captured");
        Assert.Equal(first.OfficialPlayerCode, captured.OfficialPlayerCode);
        Assert.Equal(31, captured.SnapshotId);
    }

    [Fact]
    public async Task Fbref_match_log_batch_is_bounded_skips_captured_and_isolates_failures()
    {
        FbrefMatchLogPlayerCoverageDocument[] players =
        [
            CreateCoveragePlayer(1, "captured", 30),
            CreateCoveragePlayer(2, "missing", null),
            CreateCoveragePlayer(3, "missing", null),
            CreateCoveragePlayer(4, "missing", null),
        ];
        var coverage = new FbrefMatchLogCoverageDocument(
            "1.0",
            FbrefPlayerIdentityBridge.Version,
            FbrefPlayerIdentityBridge.ReviewedSnapshotId,
            4,
            1,
            3,
            [],
            players);
        var playingTime = new FbrefPlayingTimeDocument(
            "1.0",
            FbrefPlayerIdentityBridge.ReviewedSnapshotId,
            FbrefPlayingTimeExtractor.SourceKey,
            FbrefPlayingTimeExtractor.ExtractionVersion,
            FbrefPlayerIdentityBridge.Version,
            FbrefPlayerIdentityBridge.ReviewedContentSha256,
            13,
            RetrievalTime,
            "Championship",
            "2025-26",
            players.Length,
            players.Length,
            players.Length,
            players.Length,
            0,
            [],
            []);
        var context = new FbrefMatchLogCoverageContext(
            coverage,
            playingTime);
        int contextReadCount = 0;
        var importedDocuments = new List<FbrefPlayingTimeDocument>();
        var attemptedCodes = new List<int>();
        var batch = new FbrefMatchLogBatchCapture(
            _ =>
            {
                contextReadCount++;
                return Task.FromResult(context);
            },
            (document, code, _) =>
            {
                importedDocuments.Add(document);
                attemptedCodes.Add(code);
                if (code == 2)
                {
                    throw new ResearchSourceSnapshotException("expected");
                }
                return Task.FromResult(
                    new ResearchSourceSnapshotDocument(
                        30 + code,
                        "1.0",
                        "shadow-only",
                        $"fbref-player-match-log-{code:x8}-2025-26",
                        "prior-competition-player-match-log",
                        "https://fbref.com/source",
                        "https://fbref.com/final",
                        "sports-reference-fbref",
                        "byparr",
                        "byparr/2.1.0",
                        "2026-27",
                        1,
                        new DateTimeOffset(
                            2026,
                            8,
                            21,
                            17,
                            30,
                            0,
                            TimeSpan.Zero),
                        13,
                        RetrievalTime,
                        RetrievalTime,
                        true,
                        1,
                        new string('b', 64),
                        100,
                        RetrievalTime));
            });

        FbrefMatchLogBatchCaptureDocument result = await batch.RunAsync(
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, contextReadCount);
        Assert.Equal([2, 3], attemptedCodes);
        Assert.All(
            importedDocuments,
            document => Assert.Same(playingTime, document));
        Assert.Equal(1, result.AlreadyCapturedCount);
        Assert.Equal(2, result.AttemptedCount);
        Assert.Equal(1, result.CapturedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(2, result.RemainingCount);
        Assert.Equal("partial", result.Status);
        Assert.Equal("capture-failed", result.Results[0].FailureCode);
        Assert.Equal(33, result.Results[1].SnapshotId);
        await Assert.ThrowsAsync<ResearchSourceSnapshotException>(
            async () => await batch.RunAsync(
                6,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Fbref_playing_time_parser_rejects_duplicate_player_team_rows()
    {
        string content = CreateFbrefPlayingTimeHtml(
            duplicatePlayerTeamRow: true);

        Assert.Throws<ResearchSourceSnapshotException>(
            () => FbrefPlayingTimeExtractor.Parse(content));
    }

    [Fact]
    public async Task Premier_league_injury_extractor_is_team_scoped_and_idempotent()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var snapshotStore = new ResearchSourceSnapshotStore(
            options,
            new FixedTimeProvider(RetrievalTime));
        ResearchSourceDefinition source =
            ResearchSourceRegistry.Get("premier-league-injuries");
        ResearchSourceSnapshotDocument snapshot = await snapshotStore.PersistAsync(
            source,
            new PlaywrightResearchSourceCaptureResult(
                source.CanonicalUri,
                200,
                CreatePremierLeagueInjuryExtractionEvidence(),
                "untrusted_remote_content",
                PremierLeagueInjuryPlaywrightCollector.TransportVersion),
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

        Assert.Equal(first, duplicate);
        Assert.Equal(
            ResearchSourceClaimExtractor.PremierLeagueInjuryExtractionVersion,
            first.ExtractionVersion);
        Assert.Equal(3, first.CandidateCount);
        Assert.Equal(0, first.StartClaimCount);
        Assert.Equal(3, first.AvailabilityCandidateCount);
        Assert.Equal(2, first.AvailabilityClaimCount);
        Assert.Equal(1, first.UnresolvedAvailabilityCount);
        Assert.Equal(2, first.ClaimCount);
        Assert.Empty(first.UnresolvedPlayerCodes);

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
                Assert.Equal("premier-league-injuries", claim.SourceKey);
                Assert.Equal("Premier League", claim.Author);
                Assert.Equal("availability", claim.ClaimType);
                Assert.Equal("doubtful", claim.AvailabilityStatus);
                Assert.Null(claim.ForecastProbability);
                Assert.Equal("reported", claim.Directness);
                Assert.Equal("deterministic", claim.ExtractionMethod);
                Assert.Equal(
                    ResearchSourceClaimExtractor
                        .PremierLeagueInjuryExtractionVersion,
                    claim.ExtractionVersion);
                Assert.NotNull(claim.DuplicateClusterKey);
            });
        Assert.Contains(
            claims.Claims,
            claim => claim.PlayerId == 101
                && claim.SourceSpan
                    == "Home injury list: Test Player — Back"
                && claim.ExtractionConfidence == 1m);
        Assert.Contains(
            claims.Claims,
            claim => claim.PlayerId == 102
                && claim.SourceSpan
                    == "Home injury list: Amadou — Knee"
                && claim.ExtractionConfidence == 0.95m);
    }

    [Fact]
    public async Task Refresh_health_reports_a_source_that_has_stopped_collecting()
    {
        // The failure this exists to surface: on 2026-08-20 the injury source
        // stopped collecting while the rest of the portfolio carried on. The
        // poller isolates a failing source and the logging boundary keeps the
        // failure out of application logs, so nothing signalled it for two days.
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        ResearchSourcePollingOptions pollingOptions =
            ResearchSourcePollingOptions.FromConfiguration(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["AutoFpl:Research:ResearchSourcePollIntervalMinutes"] =
                                "360",
                        })
                    .Build());

        ResearchSourceDefinition source =
            ResearchSourceRegistry.Get("premier-league-injuries");
        await new ResearchSourceSnapshotStore(
                options,
                new FixedTimeProvider(RetrievalTime))
            .PersistAsync(
                source,
                new PlaywrightResearchSourceCaptureResult(
                    source.CanonicalUri,
                    200,
                    CreatePremierLeagueInjuryExtractionEvidence(),
                    "untrusted_remote_content",
                    PremierLeagueInjuryPlaywrightCollector.TransportVersion),
                TestContext.Current.CancellationToken);

        // One missed cycle is late, not stopped.
        ResearchSourceRefreshHealthDocument late =
            await new ResearchSourceSnapshotStore(
                    options,
                    new FixedTimeProvider(RetrievalTime.AddMinutes(500)))
                .GetRefreshHealthAsync(
                    pollingOptions,
                    TestContext.Current.CancellationToken);
        Assert.Equal(360, late.ExpectedIntervalMinutes);
        Assert.Equal(
            "fresh",
            Assert.Single(
                late.Sources,
                state => state.SourceKey == "premier-league-injuries").Status);
        Assert.DoesNotContain("premier-league-injuries", late.StaleSourceKeys);

        // Past two intervals it has stopped. 2026-08-22 was roughly 52 hours
        // after the last real capture, well beyond this boundary.
        ResearchSourceRefreshHealthDocument stopped =
            await new ResearchSourceSnapshotStore(
                    options,
                    new FixedTimeProvider(RetrievalTime.AddMinutes(800)))
                .GetRefreshHealthAsync(
                    pollingOptions,
                    TestContext.Current.CancellationToken);
        ResearchSourceRefreshStateDocument injuries = Assert.Single(
            stopped.Sources,
            state => state.SourceKey == "premier-league-injuries");
        Assert.Equal("stale", injuries.Status);
        Assert.Equal(800, injuries.AgeMinutes);
        Assert.Equal(RetrievalTime, injuries.LastRetrievedAtUtc);
        Assert.Contains("premier-league-injuries", stopped.StaleSourceKeys);
    }

    [Fact]
    public async Task Refresh_health_separates_never_collected_from_not_polled()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        ResearchSourcePollingOptions pollingOptions =
            ResearchSourcePollingOptions.FromConfiguration(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["AutoFpl:Research:ResearchSourcePollIntervalMinutes"] =
                                "360",
                        })
                    .Build());

        ResearchSourceRefreshHealthDocument health =
            await new ResearchSourceSnapshotStore(
                    options,
                    new FixedTimeProvider(RetrievalTime))
                .GetRefreshHealthAsync(
                    pollingOptions,
                    TestContext.Current.CancellationToken);

        Assert.Equal("1.0", health.SchemaVersion);
        Assert.Equal(RetrievalTime, health.GeneratedAtUtc);
        Assert.Equal(ResearchSourceRegistry.All.Count, health.Sources.Count);

        // Nothing has been captured, so every automatically polled source is
        // never-collected and every manual source is simply not polled. A
        // manual source must never be reported as a fault.
        foreach (ResearchSourceRefreshStateDocument state in health.Sources)
        {
            Assert.Null(state.LastRetrievedAtUtc);
            Assert.Null(state.AgeMinutes);
            Assert.Equal(
                state.PollAutomatically ? "never-collected" : "not-polled",
                state.Status);
        }

        Assert.All(
            health.StaleSourceKeys,
            key => Assert.True(
                ResearchSourceRegistry.Get(key).PollAutomatically));
        Assert.Equal(
            ResearchSourceRegistry.All.Count(
                definition => definition.PollAutomatically),
            health.StaleSourceKeys.Count);
    }

    [Fact]
    public void Premier_league_injury_parser_accepts_an_undisclosed_injury_type()
    {
        // Observed live on 2026-08-20: Newcastle United listed Joelinton, Dan
        // Burn and Lewis Miley with an empty injury cell. The row bound required
        // at least one character, so three blank cells rejected the whole
        // twenty-club payload and the source stopped collecting for two days.
        IReadOnlyList<ResearchSourceClaimExtractor.PremierLeagueInjuryCandidate>
            candidates =
                ResearchSourceClaimExtractor.ExtractPremierLeagueInjuryCandidates(
                    CreatePremierLeagueInjuryEvidence(
                        undisclosedInjuryType: true));

        ResearchSourceClaimExtractor.PremierLeagueInjuryCandidate undisclosed =
            Assert.Single(
                candidates,
                candidate => candidate.Injury.Length == 0);
        Assert.Equal("Club 03", undisclosed.TeamName);
        Assert.Equal("Player 03-01", undisclosed.PlayerName);

        // The absence is recorded as an absence. No diagnosis is invented and
        // the span carries no dangling separator.
        Assert.Equal(
            "Club 03 injury list: Player 03-01",
            undisclosed.SourceSpan);

        // Every other row is unaffected.
        Assert.Equal(40, candidates.Count);
        Assert.Contains(
            candidates,
            candidate => candidate.SourceSpan
                == "Club 01 injury list: Player 01-01 — Back");
    }

    [Fact]
    public void Premier_league_injury_parser_rejects_incomplete_club_coverage()
    {
        Assert.Throws<ResearchSourceSnapshotException>(
            () => ResearchSourceClaimExtractor
                .ExtractPremierLeagueInjuryCandidates(
                    CreatePremierLeagueInjuryEvidence(clubCount: 19)));
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

        ResearchSourceInventoryDocument inventory =
            await snapshotStore.GetInventoryAsync(
                TestContext.Current.CancellationToken);
        ResearchSourceStartCoverageDocument coverage =
            Assert.Single(inventory.LatestStartCoverage);
        Assert.Equal(snapshot.SnapshotId, coverage.SnapshotId);
        Assert.Equal(12, coverage.PlayerCount);
        Assert.Equal(12, coverage.ClassifiedPlayerCount);
        Assert.Equal(11, coverage.PredictedStarterCount);
        Assert.Equal(1, coverage.PredictedNonStarterCount);
        Assert.Equal(1, coverage.CompleteTeamCount);
        Assert.Equal(0, coverage.PartialTeamCount);
        Assert.Equal(0, coverage.MissingTeamCount);
        ResearchSourceTeamStartCoverageDocument team =
            Assert.Single(coverage.Teams);
        Assert.Equal("HOM", team.TeamShortName);
        Assert.Equal("complete", team.Status);
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
    public async Task Background_refresh_captures_the_portfolio_and_extracts_supported_sources()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = await CreateDatabaseAsync(files.DatabasePath);
        var snapshotStore = new ResearchSourceSnapshotStore(
            options,
            new FixedTimeProvider(RetrievalTime));
        var handler = new SpiderMcpHandler(
            """
            Our Team News page will house predicted line-ups for all 20 Premier League teams.
            ![Home badge](https://example.test/home.png)##
            Home
            * ![Avatar of Test Player](https://resources.premierleague.com/premierleague25/photos/players/110x140/1001.png)Test Player
            * **Out:**
            * **Doubts:**
            * **Banned:**
            """,
            acceptRegisteredSources: true,
            contentByUrl: new Dictionary<string, string>
            {
                ["https://www.straightred.ai/"] =
                    """
                    strAIghtred - Premier League Predicted Lineups
                    Built for FPL managers. Powered by multiple prediction sources.
                    🔵 Home vs Away · 2 sources
                    Test Player
                    100%
                    3-5-2
                    ### Recently Updated
                    """,
                ["https://fpl.solioanalytics.com/api/data/latest.json"] =
                    """
                    {"gameweek":1,"deadlineIso":"2026-08-21T17:30:00.000Z","generatedAt":"2026-07-26T09:00:00.000Z","source":"https://fpl.solioanalytics.com/api/data/latest","topProjected":[{"name":"Test Player","team":"HOM","position":"MID","price":50,"prPoints":4.5}],"bestAttackingFixtures":[],"bestCleanSheets":[]}
                    """ + new string(' ', 1000),
            });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://spider-mcp:8080/mcp"),
        };
        var playwrightHandler = new PlaywrightInjuryMcpHandler(
            CreatePremierLeagueInjuryEvidence());
        using var playwrightHttpClient = new HttpClient(playwrightHandler)
        {
            BaseAddress = new Uri("http://playwright-mcp:8931/mcp"),
        };
        var claimStore = new EvidenceClaimStore(
            options,
            new FixedTimeProvider(RetrievalTime.AddMinutes(1)));
        ResearchSourcePollingOptions pollingOptions =
            ResearchSourcePollingOptions.FromConfiguration(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["AutoFpl:Research:ResearchSourcePollIntervalMinutes"] =
                                "360",
                        })
                    .Build());
        var poller = new ResearchSourcePoller(
            new ResearchSourceSnapshotImporter(
                new SpiderMcpClient(httpClient),
                snapshotStore,
                new PremierLeagueInjuryPlaywrightCollector(
                    new PlaywrightMcpFplFormCollector(
                        playwrightHttpClient))),
            new ResearchSourceClaimExtractor(snapshotStore, claimStore),
            pollingOptions,
            new ResearchSourceRefreshSignal(
                OfficialFplPollingOptions.FromConfiguration(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["AutoFpl:Research:OfficialFplPollIntervalMinutes"] =
                                    "360",
                            })
                        .Build()),
                pollingOptions),
            new FixedTimeProvider(RetrievalTime));

        await poller.RefreshOnceAsync(TestContext.Current.CancellationToken);

        ResearchSourceInventoryDocument inventory =
            await snapshotStore.GetInventoryAsync(
                TestContext.Current.CancellationToken);
        Assert.Equal(4, inventory.LatestSnapshots.Count);
        Assert.Single(inventory.LatestStartCoverage);
        EvidenceClaimSetDocument claims = await claimStore.GetForGameweekAsync(
            "2026-27",
            1,
            RetrievalTime.AddMinutes(1),
            TestContext.Current.CancellationToken);
        Assert.Contains(
            claims.Claims,
            claim => claim.SourceKey
                == ResearchSourceClaimExtractor.FfScoutSourceKey);
        Assert.Contains(
            claims.Claims,
            claim => claim.SourceKey
                == ResearchSourceClaimExtractor.StraightredSourceKey);
        Assert.Equal(3, handler.ScrapeCalls);
        Assert.Equal(3, handler.DeleteCalls);
        Assert.NotNull(playwrightHandler.CollectionCode);
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
        Assert.Equal(12, inventory.Sources.Count);
        Assert.Contains(
            inventory.Sources,
            item => item.SourceClass == "official-availability-aggregation");
        Assert.Contains(
            inventory.Sources,
            item => item.SourceClass == "specialist-predicted-lineup");
        Assert.Contains(
            inventory.Sources,
            item => item.SourceClass == "derived-predicted-lineup-consensus");
        Assert.Contains(
            inventory.Sources,
            item => item.SourceClass
                == "public-quantitative-market-projection");
        Assert.Equal(
            5,
            inventory.Sources.Count(
                item => item.SourceClass
                    == "prior-competition-playing-time"));
        Assert.Equal(
            3,
            inventory.Sources.Count(
                item => item.SourceClass
                    == "prior-competition-team-schedule"));
        ResearchSourceSnapshotDocument latest =
            Assert.Single(inventory.LatestSnapshots);
        Assert.Equal("premier-league-injuries", latest.SourceKey);
        Assert.Empty(inventory.LatestStartCoverage);
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

    private static FbrefMatchLogPlayerCoverageDocument CreateCoveragePlayer(
        int code,
        string status,
        long? snapshotId) =>
        new(
            code.ToString("x8"),
            $"Player {code}",
            "aaaaaaaa",
            "Test Team",
            code,
            code,
            $"fbref-player-match-log-{code:x8}-2025-26",
            status,
            snapshotId,
            snapshotId is null ? null : 1,
            snapshotId is null ? null : RetrievalTime,
            snapshotId is null ? null : new string('a', 64));

    private static string CreateFbrefTeamScheduleHtml(
        bool duplicateFinalMatch = false)
    {
        var table = new StringBuilder(
            """
            <html><head>
            <title>Coventry City Scores and Fixtures, Championship | FBref.com</title>
            </head><body><table id="matchlogs_for"><tbody>
            <tr class="thead"><th data-stat="date">Date</th></tr>
            """);
        var firstDate = new DateOnly(2025, 8, 1);
        const long firstEpoch = 1_754_046_000;
        for (int index = 0; index < 46; index++)
        {
            int matchNumber =
                duplicateFinalMatch && index == 45 ? 1 : index + 1;
            string matchId = matchNumber.ToString("x8");
            DateOnly matchDate = firstDate.AddDays(index);
            long epoch = firstEpoch + index * 86_400L;
            table.Append(
                $"""
                <tr>
                  <th data-stat="date"><a href="/en/matches/{matchId}/Test-Match">{matchDate:yyyy-MM-dd}</a></th>
                  <td data-stat="start_time"><span data-venue-epoch="{epoch}">12:00</span></td>
                  <td data-stat="round">Matchweek {index + 1}</td>
                  <td data-stat="venue">Home</td>
                  <td data-stat="result">W</td>
                  <td data-stat="goals_for">2</td>
                  <td data-stat="goals_against">1</td>
                  <td data-stat="opponent"><a href="/en/squads/aaaaaaaa/Test-Opponent">Test Opponent</a></td>
                  <td data-stat="match_report"><a href="/en/matches/{matchId}/Test-Match">Match Report</a></td>
                </tr>
                """);
        }
        table.Append("</tbody></table></body></html>");
        return table.ToString();
    }

    private static FbrefTeamScheduleDocument
        CreateFbrefTeamScheduleDocument()
    {
        var firstDate = new DateOnly(2025, 8, 1);
        FbrefTeamScheduleRowDocument[] matches = Enumerable.Range(1, 8)
            .Select(
                index => new FbrefTeamScheduleRowDocument(
                    index.ToString("x8"),
                    firstDate.AddDays(index - 1),
                    new DateTimeOffset(
                        firstDate.AddDays(index - 1),
                        new TimeOnly(12, 0),
                        TimeSpan.Zero),
                    $"Matchweek {index}",
                    index % 2 == 0 ? "away" : "home",
                    "W",
                    2,
                    1,
                    "aaaaaaaa",
                    "Test Opponent"))
            .ToArray();
        return new(
            "1.0",
            48,
            "fbref-team-schedule-bd8769d1-2025-26",
            FbrefTeamScheduleExtractor.ExtractionVersion,
            new string('b', 64),
            13,
            RetrievalTime.AddMinutes(1),
            "bd8769d1",
            "Hull City",
            "Championship",
            "2025-26",
            matches.Length,
            matches);
    }

    private static ResearchSourceSnapshotDocument CreateScheduleSnapshot(
        FbrefTeamScheduleSource source,
        long snapshotId) =>
        new(
            snapshotId,
            "1.0",
            "shadow-only",
            source.SourceKey,
            source.Definition.SourceClass,
            source.Definition.CanonicalUri.AbsoluteUri,
            source.Definition.CanonicalUri.AbsoluteUri,
            source.Definition.DependenceGroup,
            ByparrClient.TransportKey,
            "byparr/2.1.0",
            "2026-27",
            1,
            new DateTimeOffset(2026, 8, 21, 17, 30, 0, TimeSpan.Zero),
            13,
            RetrievalTime,
            RetrievalTime,
            true,
            1,
            new string('b', 64),
            100,
            RetrievalTime);

    private static ResearchSourceSnapshotDocument CreateResearchSnapshot(
        long snapshotId,
        string sourceKey,
        string contentSha256,
        DateTimeOffset availableAtUtc) =>
        new(
            snapshotId,
            "1.0",
            "shadow-only",
            sourceKey,
            "public-statistics",
            "https://fbref.com/",
            "https://fbref.com/",
            "fbref",
            ByparrClient.TransportKey,
            "byparr/2.1.0",
            "2026-27",
            1,
            new DateTimeOffset(2026, 8, 21, 17, 30, 0, TimeSpan.Zero),
            14,
            availableAtUtc,
            availableAtUtc,
            true,
            1,
            contentSha256,
            100,
            availableAtUtc);

    private static FbrefPlayerMatchLogDocument
        CreateFbrefPlayerMatchLogDocument(
            FbrefTeamScheduleDocument schedule)
    {
        int[] observedIndexes = [2, 4, 6];
        FbrefPlayerMatchLogRowDocument[] championshipRows =
            observedIndexes.Select(
                index =>
                {
                    FbrefTeamScheduleRowDocument match =
                        schedule.Matches[index - 1];
                    int minutes = index == 4 ? 0 : index == 2 ? 90 : 30;
                    return new FbrefPlayerMatchLogRowDocument(
                        match.SourceMatchId,
                        match.MatchDate,
                        "Championship",
                        match.Round,
                        match.Venue,
                        "W 2–1",
                        schedule.SourceTeamId,
                        schedule.TeamName,
                        match.SourceOpponentId,
                        match.OpponentName,
                        index == 2,
                        minutes,
                        0,
                        0,
                        0,
                        0);
                })
                .ToArray();
        var cupRow = new FbrefPlayerMatchLogRowDocument(
            "ffffffff",
            new DateOnly(2026, 1, 10),
            "FA Cup",
            "Third round",
            "home",
            "W 1–0",
            schedule.SourceTeamId,
            schedule.TeamName,
            "cccccccc",
            "Cup Opponent",
            true,
            90,
            0,
            0,
            0,
            0);
        return new(
            "1.0",
            31,
            "fbref-player-match-log-d6192210-2025-26",
            FbrefPlayerMatchLogExtractor.ExtractionVersion,
            FbrefPlayerIdentityBridge.Version,
            new string('a', 64),
            13,
            RetrievalTime,
            "d6192210",
            "Semi Ajayi",
            schedule.SourceTeamId,
            schedule.TeamName,
            101,
            146426,
            schedule.TeamName,
            schedule.CompetitionSeason,
            "source-revision-mismatch",
            4,
            3,
            210,
            4,
            3,
            2,
            120,
            [.. championshipRows, cupRow]);
    }

    private static string CreateFbrefPlayingTimeHtml(
        bool duplicateCommentedTable = false,
        bool duplicatePlayerTeamRow = false,
        string fbrefSeason = "2025-2026")
    {
        var table = new StringBuilder(
            """
            <table id="stats_playing_time"><tbody>
            """);
        for (int index = 0; index < 500; index++)
        {
            string teamName;
            string teamId;
            string playerName;
            if (index < 15)
            {
                teamName = "Coventry City";
                teamId = "f7e3dfe9";
                playerName = index == 0 ? "Test Player" : $"Coventry Source {index}";
            }
            else if (index < 30)
            {
                teamName = "Hull City";
                teamId = "bd8769d1";
                playerName = index == 15 ? "Hull Match" : $"Hull Source {index}";
            }
            else if (index < 45)
            {
                teamName = "Ipswich Town";
                teamId = "b74092de";
                playerName = index == 30 ? "Ipswich Match" : $"Ipswich Source {index}";
            }
            else
            {
                teamName = "Birmingham City";
                teamId = "ec79b7c2";
                playerName = $"Other Source {index}";
            }

            int identifierValue =
                duplicatePlayerTeamRow && index == 1 ? 1 : index + 1;
            string playerId = identifierValue.ToString("x8");
            table.Append(
                System.Globalization.CultureInfo.InvariantCulture,
                $"""
                 <tr>
                 <th data-stat="player"><a href="/en/players/{playerId}/Player">{playerName}</a></th>
                 <td data-stat="team"><a href="/en/squads/{teamId}/Team">{teamName}</a></td>
                 <td data-stat="games">46</td>
                 <td data-stat="minutes">3,600</td>
                 <td data-stat="games_starts">40</td>
                 <td data-stat="matches"><a href="/en/players/{playerId}/matchlogs/{fbrefSeason}/summary/Player-Match-Logs">Matches</a></td>
                 </tr>
                 """);
        }
        table.Append("</tbody></table>");
        string value = table.ToString();
        return duplicateCommentedTable
            ? $"<html><body><!--{value}-->{value}</body></html>"
            : $"<html><body>{value}</body></html>";
    }

    private static string CreatePremierLeagueInjuryEvidence(
        int clubCount = 20,
        bool emptyFirstClub = false,
        bool missingUpdateUrl = false,
        bool undisclosedInjuryType = false) =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = "premier-league-injury-dom/v1",
                sourceUrl =
                    "https://www.premierleague.com/en/latest-player-injuries",
                pageTitle =
                    "Premier League Latest Injury News - Club by Club Updates",
                renderedWidgetSha256 = new string('a', 64),
                clubs = Enumerable.Range(1, clubCount)
                    .Select(
                        club => new
                        {
                            teamName = $"Club {club:D2}",
                            rows = Enumerable.Range(
                                    1,
                                    emptyFirstClub && club == 1 ? 0 : 2)
                                .Select(
                                    player => new
                                    {
                                        playerName =
                                            $"Player {club:D2}-{player:D2}",
                                        injury =
                                            undisclosedInjuryType
                                                && club == 3
                                                && player == 1
                                                ? string.Empty
                                                : player == 1 ? "Back" : "Knee",
                                        updateUrl =
                                            missingUpdateUrl
                                                && club == 2
                                                && player == 1
                                                ? null
                                                : $"https://club{club:D2}.example/"
                                                    + $"news/player-{player:D2}",
                                    })
                                .ToArray(),
                        })
                    .ToArray(),
            });

    private static string CreatePremierLeagueInjuryExtractionEvidence() =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = "premier-league-injury-dom/v1",
                sourceUrl =
                    "https://www.premierleague.com/en/latest-player-injuries",
                pageTitle =
                    "Premier League Latest Injury News - Club by Club Updates",
                renderedWidgetSha256 = new string('b', 64),
                clubs = Enumerable.Range(1, 20)
                    .Select(
                        club => new
                        {
                            teamName = club == 1 ? "Home" : $"Club {club:D2}",
                            rows = club == 1
                                ? (object[])
                                [
                                    (object)new
                                    {
                                        playerName = "Test Player",
                                        injury = "Back",
                                        updateUrl = (string?)null,
                                    },
                                    new
                                    {
                                        playerName = "Amadou",
                                        injury = "Knee",
                                        updateUrl =
                                            "https://home.example/update",
                                    },
                                    new
                                    {
                                        playerName = "Unknown Trialist",
                                        injury = "Knock",
                                        updateUrl =
                                            "https://home.example/unknown",
                                    },
                                ]
                                : Array.Empty<object>(),
                        })
                    .ToArray(),
            });

    private sealed class PlaywrightInjuryMcpHandler(
        string evidence) : HttpMessageHandler
    {
        public string? CollectionCode { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            string body =
                await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(body);
            string method =
                document.RootElement.GetProperty("method").GetString()!;
            if (StringComparer.Ordinal.Equals(method, "initialize"))
            {
                return Response(
                    1,
                    new
                    {
                        protocolVersion = "2025-03-26",
                        capabilities = new { tools = new { } },
                        serverInfo = new
                        {
                            name = "Playwright",
                            version = "test",
                        },
                    },
                    includeSession: true);
            }

            if (StringComparer.Ordinal.Equals(
                    method,
                    "notifications/initialized"))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            JsonElement parameters =
                document.RootElement.GetProperty("params");
            string tool = parameters.GetProperty("name").GetString()!;
            if (StringComparer.Ordinal.Equals(
                    tool,
                    "browser_run_code_unsafe"))
            {
                CollectionCode = parameters
                    .GetProperty("arguments")
                    .GetProperty("code")
                    .GetString();
                string literal = JsonSerializer.Serialize(
                    $"AUTOFPL_PL_INJURY_V1:{evidence}");
                return Response(
                    2,
                    new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text =
                                    "### Result\n"
                                    + literal
                                    + "\n### Ran Playwright code\n```js\nprobe\n```",
                            },
                        },
                    });
            }

            Assert.Equal("browser_close", tool);
            return Response(
                3,
                new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = "closed",
                        },
                    },
                });
        }

        private static HttpResponseMessage Response(
            int id,
            object result,
            bool includeSession = false)
        {
            string envelope = JsonSerializer.Serialize(
                new
                {
                    result,
                    jsonrpc = "2.0",
                    id,
                });
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    envelope,
                    Encoding.UTF8,
                    "application/json"),
            };
            if (includeSession)
            {
                response.Headers.TryAddWithoutValidation(
                    "Mcp-Session-Id",
                    "test-session");
            }

            return response;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class SpiderMcpHandler(
        string content,
        string serverName = "rmcp",
        string? finalUrl = null,
        bool acceptRegisteredSources = false,
        IReadOnlyDictionary<string, string>? contentByUrl = null) : HttpMessageHandler
    {
        public int ScrapeCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public bool? LastHeadless { get; private set; }

        public string? LastWaitForSelector { get; private set; }

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
            string requestedUrl = arguments.GetProperty("url").GetString()!;
            LastHeadless = arguments.GetProperty("headless").GetBoolean();
            LastWaitForSelector =
                arguments.GetProperty("wait_for").ValueKind
                    == JsonValueKind.Null
                ? null
                : arguments.GetProperty("wait_for").GetString();
            if (acceptRegisteredSources)
            {
                Assert.Contains(
                    ResearchSourceRegistry.All,
                    source => source.CanonicalUri.AbsoluteUri == requestedUrl);
            }
            else
            {
                Assert.Equal(
                    "https://cdn.fantasyfootballscout.co.uk/team-news",
                    requestedUrl);
                Assert.False(arguments.GetProperty("headless").GetBoolean());
            }
            ScrapeCalls++;
            string scrape = JsonSerializer.Serialize(
                new
                {
                    url = finalUrl
                        ?? requestedUrl,
                    status_code = 200,
                    content = contentByUrl?.GetValueOrDefault(requestedUrl)
                        ?? content,
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

    private sealed class ByparrHandler(
        ResearchSourceDefinition source,
        string content,
        string? finalUrl = null) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public bool SawProxyOverrideHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1", request.RequestUri!.AbsolutePath);
            SawProxyOverrideHeader = request.Headers.Any(
                header => header.Key.StartsWith(
                    "X-Proxy-",
                    StringComparison.OrdinalIgnoreCase));
            using JsonDocument body = JsonDocument.Parse(
                await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            Assert.Equal(
                "request.get",
                body.RootElement.GetProperty("cmd").GetString());
            Assert.Equal(
                source.CanonicalUri.AbsoluteUri,
                body.RootElement.GetProperty("url").GetString());
            Assert.Equal(
                60,
                body.RootElement.GetProperty("max_timeout").GetInt32());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(
                        new
                        {
                            status = "ok",
                            message = "Success",
                            solution = new
                            {
                                url = finalUrl
                                    ?? "https://fbref.com/en/comps/10/playingtime/Championship-Stats",
                                status = 200,
                                cookies = Array.Empty<object>(),
                                userAgent = "test",
                                headers = new { },
                                response = content,
                            },
                            startTimestamp = 1,
                            endTimestamp = 2,
                            version = "2.1.0",
                        }),
                    Encoding.UTF8,
                    "application/json"),
            };
        }
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
