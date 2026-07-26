using System.Net.Http.Json;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Advice;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class OfficialDecisionRoomPreviewTests
{
    private static readonly DateTimeOffset CaptureTime =
        new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Demo_advice_uses_official_identities_photos_and_dossier_links()
    {
        using var files = new TemporaryDatabaseFiles();
        DatabaseOptions options = CreateOptions(files.DatabasePath);
        await new DecisionSnapshotStore(options)
            .MigrateAsync(TestContext.Current.CancellationToken);
        var captureStore = new OfficialFplCaptureStore(options);
        using var httpClient = new HttpClient();
        var importer = new OfficialFplImporter(
            httpClient,
            captureStore,
            TimeProvider.System);
        await importer.ImportCapturedPayloadAsync(
            CreateBootstrap(),
            CreateFixtures(),
            CaptureTime,
            TestContext.Current.CancellationToken);

        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(
                    builder =>
                    {
                        builder.UseSetting("AutoFpl:DatabasePath", files.DatabasePath);
                        builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");
                    });
        using HttpClient client = factory.CreateClient();

        GameweekAdviceDocument? advice = await client.GetFromJsonAsync<GameweekAdviceDocument>(
            "/api/v1/advice/demo",
            TestContext.Current.CancellationToken);

        Assert.NotNull(advice);
        Assert.True(advice.IsSynthetic);
        Assert.Equal("synthetic-forecast-real-identities", advice.EvidenceStatus);
        Assert.Null(advice.SnapshotId);
        Assert.Equal(CaptureTime, advice.DecisionCutoffUtc);
        Assert.Equal(15, advice.Selection.Players.Count);
        Assert.Equal(
            2,
            advice.Selection.Players.Count(player => player.Position == "goalkeeper"));
        Assert.Equal(
            5,
            advice.Selection.Players.Count(player => player.Position == "defender"));
        Assert.Equal(
            5,
            advice.Selection.Players.Count(player => player.Position == "midfielder"));
        Assert.Equal(
            3,
            advice.Selection.Players.Count(player => player.Position == "forward"));
        Assert.All(
            advice.Selection.Players.GroupBy(player => player.ClubShortName),
            club => Assert.True(club.Count() <= 3));
        Assert.All(
            advice.Selection.Players,
            player =>
            {
                Assert.StartsWith(
                    "https://resources.premierleague.com/premierleague/photos/players/110x140/",
                    player.PhotoUrl,
                    StringComparison.Ordinal);
                Assert.Equal(
                    $"/api/v1/data/official-fpl/replays/2026-27/1/players/{player.PlayerId}",
                    player.DossierPath);
                Assert.Contains(
                    player.Reasons,
                    reason => reason.Contains("synthetic", StringComparison.Ordinal));
            });
    }

    private static byte[] CreateBootstrap()
    {
        object[] teams = Enumerable.Range(1, 10)
            .Select(
                id => (object)new
                {
                    id,
                    code = id * 10,
                    name = $"Team {id}",
                    short_name = $"T{id:00}",
                })
            .ToArray();
        int[] positions =
        [
            1, 1,
            2, 2, 2, 2, 2, 2,
            3, 3, 3, 3, 3, 3,
            4, 4, 4, 4,
            1, 2,
        ];
        object[] players = positions
            .Select(
                (position, index) =>
                {
                    int id = index + 1;
                    int code = 1000 + id;
                    return (object)new
                    {
                        id,
                        code,
                        team = (index % 10) + 1,
                        element_type = position,
                        first_name = $"Player{id}",
                        second_name = $"Official{id}",
                        web_name = $"Official{id}",
                        photo = $"{code}.png",
                        now_cost = 45 + id,
                        status = "a",
                        news = string.Empty,
                        news_added = (string?)null,
                        chance_of_playing_next_round = (int?)null,
                        selected_by_percent = (30 - id).ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        total_points = id,
                        minutes = 90,
                        starts = 1,
                    };
                })
            .ToArray();

        return JsonSerializer.SerializeToUtf8Bytes(
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
                teams,
                elements = players,
            });
    }

    private static byte[] CreateFixtures() =>
        JsonSerializer.SerializeToUtf8Bytes(
            Enumerable.Range(1, 5)
                .Select(
                    id => new
                    {
                        id,
                        @event = 1,
                        team_h = id,
                        team_a = id + 5,
                        kickoff_time = $"2026-08-0{id + 1}T14:00:00Z",
                        started = false,
                        finished = false,
                        finished_provisional = false,
                        team_h_score = (int?)null,
                        team_a_score = (int?)null,
                    })
                .ToArray());

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

    private sealed class TemporaryDatabaseFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"autofpl-decision-room-tests-{Guid.NewGuid():N}");

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
