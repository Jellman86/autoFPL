using System.Text.Json;
using System.Text.Json.Nodes;

using NJsonSchema;

namespace AutoFpl.Domain.Tests.Sources;

public sealed class ManualEvidenceContractTests
{
    private static readonly string ContractDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "contracts",
        "manual-evidence",
        "v1");

    private static readonly EvidenceSemantics ContractMetadata = new(
        "decision-state",
        "contract-metadata");
    private static readonly EvidenceSemantics DecisionState = new(
        "decision-state",
        "decision-state-at-receipt");
    private static readonly EvidenceSemantics ObservedEvent = new(
        "observed-event-evidence",
        "observation-known-at-receipt");
    private static readonly EvidenceSemantics OutcomeEvidence = new(
        "outcome-evidence",
        "outcome-known-at-receipt");

    private static readonly string[] SquadDecisionFields =
    [
        "budgetTenths",
        "players",
        "players[].playerId",
        "players[].clubId",
        "players[].position",
        "players[].priceTenths",
    ];

    private static readonly string[] LineupDecisionFields =
    [
        .. SquadDecisionFields,
        "startingPlayerIds",
        "captainPlayerId",
        "viceCaptainPlayerId",
    ];

    private static readonly string[] SelectionDecisionFields =
    [
        .. LineupDecisionFields,
        "replacementGoalkeeperPlayerId",
        "outfieldSubstitutePlayerIds",
    ];

    private static readonly string[] PersistedSnapshotFields =
    [
        "schemaVersion",
        "seasonCode",
        "gameweek",
        "deadlineUtc",
        "decisionCutoffUtc",
        "budgetTenths",
        "players",
        "players[].playerId",
        "players[].displayName",
        "players[].clubId",
        "players[].position",
        "players[].priceTenths",
        "startingPlayerIds",
        "captainPlayerId",
        "viceCaptainPlayerId",
        "replacementGoalkeeperPlayerId",
        "outfieldSubstitutePlayerIds",
        "observations",
        "observations[].sourceKey",
        "observations[].playerId",
        "observations[].metric",
        "observations[].value",
        "observations[].observedAtUtc",
        "observations[].retrievedAtUtc",
        "observations[].availableAtUtc",
        "observations[].supersedesObservationId",
        "supersedesSnapshotId",
    ];

    private static readonly IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, EvidenceSemantics>> ExpectedSemanticsByRoute =
        new Dictionary<string, IReadOnlyDictionary<string, EvidenceSemantics>>(
            StringComparer.Ordinal)
        {
            ["/api/v1/decision-snapshot-metadata/validation"] = CreateSemantics(
                (["schemaVersion", "sourceType"], ContractMetadata)),
            ["/api/v1/decision-snapshots"] = CreateSemantics(
                (PersistedSnapshotFields, new("schema-validated", "schema-validated"))),
            ["/api/v1/selections/drafts"] = CreateSemantics(
                (["forecastArtifactId"], DecisionState)),
            ["/api/v1/selections/{selectionRevisionId:long:min(1)}/revisions"] =
                CreateSemantics(
                    ([
                        "startingPlayerIds",
                        "captainPlayerId",
                        "viceCaptainPlayerId",
                        "replacementGoalkeeperPlayerId",
                        "outfieldSubstitutePlayerIds",
                    ], DecisionState)),
            ["/api/v1/squads/validation"] = CreateSemantics(
                (SquadDecisionFields, DecisionState)),
            ["/api/v1/lineups/validation"] = CreateSemantics(
                (LineupDecisionFields, DecisionState)),
            ["/api/v1/gameweek-selections/validation"] = CreateSemantics(
                (SelectionDecisionFields, DecisionState)),
            ["/api/v1/gameweek-outcomes/captaincy-resolution"] = CreateSemantics(
                (SelectionDecisionFields, DecisionState),
                (["playerIdsWithMinutes"], ObservedEvent)),
            ["/api/v1/gameweek-outcomes/substitution-resolution"] = CreateSemantics(
                (SelectionDecisionFields, DecisionState),
                (["playerIdsWhoPlayed"], ObservedEvent)),
            ["/api/v1/gameweek-outcomes/effective-resolution"] = CreateSemantics(
                (SelectionDecisionFields, DecisionState),
                (["playerIdsWhoPlayed"], ObservedEvent)),
            ["/api/v1/gameweek-outcomes/effective-score"] = CreateSemantics(
                (SelectionDecisionFields, DecisionState),
                (["playerIdsWhoPlayed"], ObservedEvent),
                (["playerPoints", "playerPoints[].playerId", "playerPoints[].points"],
                    OutcomeEvidence)),
        };

    [Fact]
    public async Task Current_catalog_conforms_to_the_manual_evidence_contract()
    {
        JsonSchema schema = await JsonSchema.FromFileAsync(
            Path.Combine(ContractDirectory, "manual-evidence-catalog.schema.json"),
            TestContext.Current.CancellationToken);
        string catalog = await File.ReadAllTextAsync(
            Path.Combine(ContractDirectory, "current-post-routes.json"),
            TestContext.Current.CancellationToken);

        Assert.Empty(schema.Validate(catalog));
    }

    [Fact]
    public async Task Contract_rejects_mismatched_classification_and_temporal_role()
    {
        JsonSchema schema = await JsonSchema.FromFileAsync(
            Path.Combine(ContractDirectory, "manual-evidence-catalog.schema.json"),
            TestContext.Current.CancellationToken);
        JsonNode catalog = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(ContractDirectory, "current-post-routes.json"),
            TestContext.Current.CancellationToken))!;
        JsonObject observedField = catalog["routes"]!
            .AsArray()
            .SelectMany(route => route!["fields"]!.AsArray())
            .Select(field => field!.AsObject())
            .Single(field => field["jsonPath"]!.GetValue<string>()
                == "playerIdsWithMinutes");
        observedField["temporalRole"] = "decision-state-at-receipt";

        Assert.NotEmpty(schema.Validate(catalog.ToJsonString()));
    }

    [Fact]
    public async Task Catalog_uses_conservative_receipt_time_for_replay()
    {
        using JsonDocument catalog = await LoadCatalogAsync();
        JsonElement timing = catalog.RootElement.GetProperty("timingPolicy");

        Assert.Equal(
            "explicit-on-decision-snapshot-route",
            timing.GetProperty("requestTimestampHandling").GetString());
        Assert.Equal(
            "application-for-persisted-state",
            timing.GetProperty("receiptTimeOwner").GetString());
        Assert.Equal("not-before-receipt", timing.GetProperty("replayAvailabilityPolicy").GetString());
        Assert.Equal("unknown-unless-separately-recorded", timing.GetProperty("observationTimePolicy").GetString());
        Assert.Equal("mixed", catalog.RootElement.GetProperty("persistence").GetString());
        Assert.Equal("disabled", catalog.RootElement.GetProperty("requestBodyLogging").GetString());
        Assert.False(catalog.RootElement.GetProperty("credentialsAccepted").GetBoolean());
        Assert.False(catalog.RootElement.GetProperty("opaqueUploadsAccepted").GetBoolean());
        Assert.False(catalog.RootElement.GetProperty("accountActionsAccepted").GetBoolean());
        Assert.False(catalog.RootElement.GetProperty("derivedRequestValuesAccepted").GetBoolean());
    }

    [Fact]
    public async Task Every_catalog_field_has_explicit_route_local_evidence_semantics()
    {
        using JsonDocument catalog = await LoadCatalogAsync();
        JsonElement[] routes = catalog.RootElement
            .GetProperty("routes")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(
            ExpectedSemanticsByRoute.Keys.Order(),
            routes.Select(route => route.GetProperty("path").GetString()!).Order());

        foreach (JsonElement route in routes)
        {
            string routePath = route.GetProperty("path").GetString()!;
            IReadOnlyDictionary<string, EvidenceSemantics> expectedRoute =
                ExpectedSemanticsByRoute[routePath];
            JsonElement[] fields = route.GetProperty("fields").EnumerateArray().ToArray();

            Assert.Equal(
                expectedRoute.Keys.Order(),
                fields.Select(field => field.GetProperty("jsonPath").GetString()!).Order());

            if (StringComparer.Ordinal.Equals(routePath, "/api/v1/decision-snapshots"))
            {
                continue;
            }

            foreach (JsonElement field in fields)
            {
                string fieldPath = field.GetProperty("jsonPath").GetString()!;
                EvidenceSemantics expected = expectedRoute[fieldPath];

                Assert.Equal(
                    expected.Classification,
                    field.GetProperty("classification").GetString());
                Assert.Equal(
                    expected.TemporalRole,
                    field.GetProperty("temporalRole").GetString());
                Assert.Equal(
                    "not-before-receipt",
                    field.GetProperty("replayAvailabilityPolicy").GetString());
            }
        }
    }

    private static async Task<JsonDocument> LoadCatalogAsync()
    {
        await using FileStream stream = File.OpenRead(
            Path.Combine(ContractDirectory, "current-post-routes.json"));
        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static IReadOnlyDictionary<string, EvidenceSemantics> CreateSemantics(
        params (IEnumerable<string> Fields, EvidenceSemantics Semantics)[] groups) => groups
        .SelectMany(group => group.Fields.Select(field => (field, group.Semantics)))
        .ToDictionary(
            item => item.field,
            item => item.Semantics,
            StringComparer.Ordinal);

    private sealed record EvidenceSemantics(
        string Classification,
        string TemporalRole);
}
