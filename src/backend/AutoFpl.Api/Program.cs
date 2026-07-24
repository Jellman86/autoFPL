using System.Text.Json.Serialization;

using AutoFpl.Api.Errors;
using AutoFpl.Api.Health;
using AutoFpl.Contracts.Snapshots;
using AutoFpl.Domain.Snapshots;

if (args.Length == 1 && StringComparer.Ordinal.Equals(args[0], "--health-check"))
{
    return await HealthProbe.CheckAsync();
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DecisionSnapshotValidationExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options =>
{
    options.ThrowOnBadRequest = false;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
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

app.Run();
return 0;

public sealed record DecisionSnapshotMetadataRequest(
    [property: JsonPropertyName("schemaVersion")] string? SchemaVersion,
    [property: JsonPropertyName("sourceType")] string? SourceType);

public sealed record ProbeResponse([property: JsonPropertyName("status")] string Status);

public partial class Program;
