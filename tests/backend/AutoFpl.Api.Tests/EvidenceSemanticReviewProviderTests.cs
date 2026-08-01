using System.Net;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Intelligence;
using AutoFpl.Contracts.Intelligence;

using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class EvidenceSemanticReviewProviderTests
{
    [Fact]
    public void Provider_is_disabled_by_default()
    {
        EvidenceSemanticReviewProviderOptions options =
            EvidenceSemanticReviewProviderOptions.FromConfiguration(
                new ConfigurationBuilder().Build());

        Assert.False(options.Enabled);
        Assert.Null(options.Endpoint);
        Assert.Null(options.Interval);
    }

    [Fact]
    public void Enabled_provider_fails_closed_for_unallowlisted_endpoint()
    {
        IConfiguration configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["Provider"] = "hermes",
                ["Endpoint"] = "http://unexpected.internal/v1/chat/completions",
                ["Model"] = "local-model",
                ["AllowedEndpointHosts"] = "riker",
            });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => EvidenceSemanticReviewProviderOptions.FromConfiguration(configuration));

        Assert.Contains("AllowedEndpointHosts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adapter_sends_secret_only_as_header_and_parses_strict_output()
    {
        EvidenceReviewContextDocument context = CreateContext();
        string providerOutput = JsonSerializer.Serialize(new
        {
            status = EvidenceSemanticReviewStore.StatusComplete,
            reason = (string?)null,
            results = new[]
            {
                new
                {
                    playerId = 7,
                    verdict = "supports-adverse-interpretation",
                    evidenceQuality = "high",
                    timeHorizon = "next gameweek",
                    scenarioKeys = new[] { "availability-stress" },
                    supportingClaimIds = new long[] { 11 },
                    contradictingClaimIds = Array.Empty<long>(),
                    dependentClaimIds = Array.Empty<long>(),
                    corroboratingSourceKeys = new[] { "official-team-news" },
                    contradictingSourceKeys = Array.Empty<string>(),
                    assumptions = Array.Empty<string>(),
                    uncertainties = new[] { "No later update is included." },
                    scope = "Supplied evidence before the cutoff.",
                    rationale = "The direct official claim supports the adverse case.",
                    isAbstained = false,
                },
            },
        });
        string envelope = JsonSerializer.Serialize(new
        {
            model = "gpt-test-returned",
            choices = new[]
            {
                new
                {
                    message = new { content = providerOutput, refusal = (string?)null },
                    finish_reason = "stop",
                },
            },
        });
        var handler = new CapturingHandler(envelope);
        var client = new HttpClient(handler);
        EvidenceSemanticReviewProviderOptions options =
            EvidenceSemanticReviewProviderOptions.CreateForTests(
                "openai",
                new Uri("https://api.openai.com/v1/chat/completions"),
                "gpt-test",
                "top-secret-key");
        var provider = new OpenAiCompatibleEvidenceSemanticReviewProvider(
            client,
            options,
            TimeProvider.System);

        EvidenceSemanticReviewProviderResponse result = await provider.ReviewAsync(
            context,
            TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("top-secret-key", handler.AuthorizationParameter);
        Assert.DoesNotContain("top-secret-key", handler.RequestBody, StringComparison.Ordinal);
        using JsonDocument request = JsonDocument.Parse(handler.RequestBody);
        JsonElement root = request.RootElement;
        Assert.True(root.GetProperty("response_format")
            .GetProperty("json_schema")
            .GetProperty("strict")
            .GetBoolean());
        Assert.Equal(JsonValueKind.False, root.GetProperty("store").ValueKind);
        JsonElement playerEnum = root.GetProperty("response_format")
            .GetProperty("json_schema")
            .GetProperty("schema")
            .GetProperty("properties")
            .GetProperty("results")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("playerId")
            .GetProperty("enum");
        Assert.Equal(7, playerEnum[0].GetInt32());
        Assert.Equal("gpt-test-returned", result.ProviderModel);
        Assert.Single(result.Results);
    }

    private static IConfiguration Configuration(
        IReadOnlyDictionary<string, string?> values)
    {
        Dictionary<string, string?> prefixed = values.ToDictionary(
            pair => $"{EvidenceSemanticReviewProviderOptions.ConfigurationSection}:{pair.Key}",
            pair => pair.Value,
            StringComparer.Ordinal);
        prefixed[$"{EvidenceSemanticReviewProviderOptions.ConfigurationSection}:Enabled"] = "true";
        return new ConfigurationBuilder().AddInMemoryCollection(prefixed).Build();
    }

    private static EvidenceReviewContextDocument CreateContext()
    {
        var player = new EvidenceReviewPlayerDocument(7, "Tester", 1, "Test FC", "MID");
        var claim = new EvidenceReviewClaimDocument(
            11,
            true,
            "official-team-news",
            "https://example.test/news",
            "Coach",
            new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 9, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 9, 5, 0, TimeSpan.Zero),
            "availability",
            "doubtful",
            null,
            null,
            null,
            null,
            "direct",
            "Player is doubtful.",
            null,
            new string('c', 64));
        var target = new EvidenceReviewTargetDocument(
            player,
            ["availability-stress"],
            ["official-team-news"],
            [11],
            ["Alternative"],
            -2m,
            0m,
            [claim]);
        return new(
            "1.0",
            CurrentEvidenceReviewContextStore.ReadyStatus,
            CurrentEvidenceReviewContextStore.ReviewMode,
            false,
            false,
            "2026-27",
            1,
            new DateTimeOffset(2026, 8, 21, 17, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 9, 5, 0, TimeSpan.Zero),
            1,
            2,
            new string('a', 64),
            new string('b', 64),
            new(
                CurrentEvidenceReviewContextStore.PromptVersion,
                CurrentEvidenceReviewContextStore.OutputSchemaVersion,
                [
                    "supports-adverse-interpretation",
                    "contradicts-adverse-interpretation",
                    "mixed-or-time-dependent",
                    "insufficient-evidence",
                ],
                [],
                []),
            new(1, 1, 1, 1, 1),
            [target],
            []);
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
