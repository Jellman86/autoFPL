using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class RequestLoggingTests
{
    private const int SentinelPlayerId = 1_976_543_210;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Manual_request_content_is_not_logged_by_any_post_route()
    {
        var entries = new ConcurrentQueue<string>();
        using var database = new TemporaryDatabase();
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("AutoFpl:DatabasePath", database.Path);
                builder.UseSetting("AutoFpl:SeedDemoSnapshot", "false");
                builder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(new CapturingLoggerProvider(entries));
                });
            });
        using HttpClient client = factory.CreateClient();
        string[] actualPostRoutes = factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata
                .GetMetadata<HttpMethodMetadata>()?
                .HttpMethods
                .Contains("POST", StringComparer.Ordinal) is true)
            .Select(endpoint => endpoint.RoutePattern.RawText!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        IReadOnlyDictionary<string, (string Body, string Sentinel, HttpStatusCode ExpectedStatus)>
            requestsByRoute = BuildRequests();

        Assert.Equal(
            requestsByRoute.Keys.Order(StringComparer.Ordinal),
            actualPostRoutes);

        foreach (KeyValuePair<string, (string Body, string Sentinel, HttpStatusCode ExpectedStatus)>
            request in requestsByRoute)
        {
            using var content = new StringContent(
                request.Value.Body,
                Encoding.UTF8,
                "application/json");
            using var message = new HttpRequestMessage(HttpMethod.Post, request.Key)
            {
                Content = content,
            };
            message.Headers.Accept.ParseAdd("application/json");
            message.Headers.Accept.ParseAdd("text/event-stream");
            using HttpResponseMessage response = await client.SendAsync(
                message,
                TestContext.Current.CancellationToken);

            string responseBody = await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken);
            Assert.True(
                response.StatusCode == request.Value.ExpectedStatus,
                $"{request.Key}: expected {request.Value.ExpectedStatus}, "
                + $"received {response.StatusCode}: {responseBody}");
        }

        foreach ((string Body, string Sentinel, HttpStatusCode ExpectedStatus) request
            in requestsByRoute.Values)
        {
            Assert.DoesNotContain(
                entries,
                entry => entry.Contains(request.Sentinel, StringComparison.Ordinal));
        }
    }

    private static IReadOnlyDictionary<
        string,
        (string Body, string Sentinel, HttpStatusCode ExpectedStatus)> BuildRequests()
    {
        int[] playerIds = Enumerable.Range(SentinelPlayerId, 15).ToArray();
        object[] players =
        [
            Player(playerIds[0], 1, "goalkeeper", 45),
            Player(playerIds[1], 2, "goalkeeper", 45),
            Player(playerIds[2], 1, "defender", 45),
            Player(playerIds[3], 2, "defender", 45),
            Player(playerIds[4], 3, "defender", 45),
            Player(playerIds[5], 4, "defender", 45),
            Player(playerIds[6], 5, "defender", 45),
            Player(playerIds[7], 1, "midfielder", 50),
            Player(playerIds[8], 2, "midfielder", 50),
            Player(playerIds[9], 3, "midfielder", 50),
            Player(playerIds[10], 4, "midfielder", 50),
            Player(playerIds[11], 5, "midfielder", 50),
            Player(playerIds[12], 3, "forward", 60),
            Player(playerIds[13], 4, "forward", 60),
            Player(playerIds[14], 5, "forward", 60),
        ];
        int[] startingPlayerIds =
        [
            playerIds[0],
            playerIds[2],
            playerIds[3],
            playerIds[4],
            playerIds[7],
            playerIds[8],
            playerIds[9],
            playerIds[10],
            playerIds[11],
            playerIds[12],
            playerIds[13],
        ];
        int[] outfieldSubstitutePlayerIds = [playerIds[5], playerIds[6], playerIds[14]];
        string sentinel = SentinelPlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        object selection = new
        {
            budgetTenths = 1_000,
            players,
            startingPlayerIds,
            captainPlayerId = playerIds[7],
            viceCaptainPlayerId = playerIds[12],
            replacementGoalkeeperPlayerId = playerIds[1],
            outfieldSubstitutePlayerIds,
        };
        object[] persistedPlayers = players
            .Select((player, index) =>
            {
                JsonElement value = JsonSerializer.SerializeToElement(player, JsonOptions);
                return new
                {
                    playerId = value.GetProperty("playerId").GetInt32(),
                    displayName = $"Private player {index + 1}",
                    clubId = value.GetProperty("clubId").GetInt32(),
                    position = value.GetProperty("position").GetString(),
                    priceTenths = value.GetProperty("priceTenths").GetInt32(),
                };
            })
            .ToArray();

        return new Dictionary<
            string,
            (string Body, string Sentinel, HttpStatusCode ExpectedStatus)>(StringComparer.Ordinal)
        {
            ["/mcp/"] = (
                """
                {
                  "jsonrpc": "2.0",
                  "id": 1,
                  "method": "initialize",
                  "params": {
                    "protocolVersion": "2025-11-25",
                    "capabilities": {},
                    "clientInfo": {
                      "name": "AUTOFPL_PRIVATE_SENTINEL_MCP",
                      "version": "1.0"
                    }
                  }
                }
                """,
                "AUTOFPL_PRIVATE_SENTINEL_MCP",
                HttpStatusCode.OK),
            ["/api/v1/selections/drafts"] = (
                Serialize(new { forecastArtifactId = SentinelPlayerId }),
                sentinel,
                HttpStatusCode.NotFound),
            ["/api/v1/selections/{selectionRevisionId:long:min(1)}/revisions"] = (
                Serialize(new
                {
                    startingPlayerIds,
                    captainPlayerId = playerIds[7],
                    viceCaptainPlayerId = playerIds[12],
                    replacementGoalkeeperPlayerId = playerIds[1],
                    outfieldSubstitutePlayerIds,
                }),
                sentinel,
                HttpStatusCode.NotFound),
            ["/api/v1/decision-snapshot-metadata/validation"] = (
                """{"schemaVersion":"1.0","sourceType":"AUTOFPL_PRIVATE_SENTINEL_METADATA"}""",
                "AUTOFPL_PRIVATE_SENTINEL_METADATA",
                HttpStatusCode.UnprocessableEntity),
            ["/api/v1/decision-snapshots"] = (
                Serialize(new
                {
                    schemaVersion = "1.0",
                    seasonCode = "2026-27",
                    gameweek = 1,
                    deadlineUtc = "2026-08-15T12:00:00Z",
                    decisionCutoffUtc = "2026-08-15T11:00:00Z",
                    budgetTenths = 1_000,
                    players = persistedPlayers,
                    startingPlayerIds,
                    captainPlayerId = playerIds[7],
                    viceCaptainPlayerId = playerIds[12],
                    replacementGoalkeeperPlayerId = playerIds[1],
                    outfieldSubstitutePlayerIds,
                    observations = new[]
                    {
                        new
                        {
                            sourceKey = "AUTOFPL_PRIVATE_SENTINEL_SOURCE",
                            playerId = playerIds[7],
                            metric = "expected-points",
                            value = 7.2m,
                            observedAtUtc = "2026-08-15T10:30:00Z",
                            retrievedAtUtc = "2026-08-15T10:35:00Z",
                            availableAtUtc = "2026-08-15T10:40:00Z",
                            supersedesObservationId = (long?)null,
                        },
                    },
                    supersedesSnapshotId = (long?)null,
                }),
                sentinel,
                HttpStatusCode.Created),
            ["/api/v1/squads/validation"] = (
                Serialize(new { budgetTenths = 1_000, players }),
                sentinel,
                HttpStatusCode.OK),
            ["/api/v1/lineups/validation"] = (
                Serialize(new
                {
                    budgetTenths = 1_000,
                    players,
                    startingPlayerIds,
                    captainPlayerId = playerIds[7],
                    viceCaptainPlayerId = playerIds[12],
                }),
                sentinel,
                HttpStatusCode.OK),
            ["/api/v1/gameweek-selections/validation"] = (
                Serialize(selection),
                sentinel,
                HttpStatusCode.OK),
            ["/api/v1/gameweek-outcomes/captaincy-resolution"] = (
                Serialize(new
                {
                    budgetTenths = 1_000,
                    players,
                    startingPlayerIds,
                    captainPlayerId = playerIds[7],
                    viceCaptainPlayerId = playerIds[12],
                    replacementGoalkeeperPlayerId = playerIds[1],
                    outfieldSubstitutePlayerIds,
                    playerIdsWithMinutes = startingPlayerIds,
                }),
                sentinel,
                HttpStatusCode.OK),
            ["/api/v1/gameweek-outcomes/substitution-resolution"] = (
                Serialize(new
                {
                    budgetTenths = 1_000,
                    players,
                    startingPlayerIds,
                    captainPlayerId = playerIds[7],
                    viceCaptainPlayerId = playerIds[12],
                    replacementGoalkeeperPlayerId = playerIds[1],
                    outfieldSubstitutePlayerIds,
                    playerIdsWhoPlayed = startingPlayerIds,
                }),
                sentinel,
                HttpStatusCode.OK),
            ["/api/v1/gameweek-outcomes/effective-resolution"] = (
                Serialize(new
                {
                    budgetTenths = 1_000,
                    players,
                    startingPlayerIds,
                    captainPlayerId = playerIds[7],
                    viceCaptainPlayerId = playerIds[12],
                    replacementGoalkeeperPlayerId = playerIds[1],
                    outfieldSubstitutePlayerIds,
                    playerIdsWhoPlayed = startingPlayerIds,
                }),
                sentinel,
                HttpStatusCode.OK),
            ["/api/v1/gameweek-outcomes/effective-score"] = (
                Serialize(new
                {
                    budgetTenths = 1_000,
                    players,
                    startingPlayerIds,
                    captainPlayerId = playerIds[7],
                    viceCaptainPlayerId = playerIds[12],
                    replacementGoalkeeperPlayerId = playerIds[1],
                    outfieldSubstitutePlayerIds,
                    playerIdsWhoPlayed = startingPlayerIds,
                    playerPoints = playerIds
                        .Select(playerId => new
                        {
                            playerId,
                            points = startingPlayerIds.Contains(playerId) ? 1 : 0,
                        })
                        .ToArray(),
                }),
                sentinel,
                HttpStatusCode.OK),
        };
    }

    private static object Player(
        int playerId,
        int clubId,
        string position,
        int priceTenths) => new
        {
            playerId,
            clubId,
            position,
            priceTenths,
        };

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            entries.Enqueue(RenderState(state));
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(RenderState(state));
            entries.Enqueue(formatter(state, exception));
            if (exception is not null)
            {
                entries.Enqueue(exception.ToString());
            }
        }

        private static string RenderState<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                return string.Join(
                    ",",
                    values.Select(pair => $"{pair.Key}={pair.Value}"));
            }

            return state?.ToString() ?? string.Empty;
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"autofpl-request-logging-{Guid.NewGuid():N}");

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(_directory);
        }

        public string Path => System.IO.Path.Combine(_directory, "autofpl.db");

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
